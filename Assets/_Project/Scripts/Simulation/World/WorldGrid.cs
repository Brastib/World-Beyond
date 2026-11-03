using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Simulation.World
{
    // Die einzige Instanz der Spielwelt (INTEGRATION §3). Kacheldaten liegen in einem dichten Array, Index = y * Width + x.
    // Schreibzugriffe laufen ausschließlich über die IWorldGrid-Methoden, damit Events, Änderungsmarkierungen
    // (für den Spielstand) und die Versionsnummer (für Caches) konsistent bleiben.
    public sealed partial class WorldGrid : IWorldGrid
    {
        TileData[] _tiles;
        // Kacheln, die sich seit der Generierung geändert haben (Spielstand speichert nur diese, ARCHITECTURE §11).
        bool[] _changedSinceGeneration;
        // Generierter Ursprungswert jeder geänderten Kachel. Damit kann das Laden die frisch generierte Welt
        // wiederherstellen, ohne neu zu generieren.
        Dictionary<int, TileData> _original = new Dictionary<int, TileData>();
        int _changedCount;

        ISimEventBus _events;

        bool _ownershipDirty;
        int _ownMinX, _ownMinY, _ownMaxX, _ownMaxY;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public ulong Seed => GenSettings.Seed;
        public float SeaLevel => GenSettings.SeaLevel;
        public int Version { get; private set; }
        public int NavigationVersion { get; private set; }

        // Einstellungen, mit denen diese Welt erzeugt wurde; Grundlage für den Neuaufbau beim Laden.
        public WorldGenSettings GenSettings { get; private set; }

        public int ChangedTileCount => _changedCount;

        public WorldGrid(WorldGenSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.Validate(out string error))
                throw new ArgumentException(error, nameof(settings));
            Allocate(settings.Clone());
        }

        void Allocate(WorldGenSettings settings)
        {
            GenSettings = settings;
            Width = settings.Width;
            Height = settings.Height;
            _tiles = new TileData[Width * Height];
            _changedSinceGeneration = new bool[Width * Height];
            _changedCount = 0;
            _original = new Dictionary<int, TileData>();
            _ownershipDirty = false;
            Version++;
            NavigationVersion++;
        }

        // Verbindet die Welt mit dem Event-Bus der Simulation. Ohne Bus (z. B. während der Generierung) werden keine Events gesendet.
        public void AttachEvents(ISimEventBus events) => _events = events;

        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public ref readonly TileData Get(int x, int y)
        {
            if (!InBounds(x, y))
                throw new ArgumentOutOfRangeException($"Kachel ({x}, {y}) liegt außerhalb der Welt {Width}×{Height}.");
            return ref _tiles[y * Width + x];
        }

        // Nur für Generator und Laden: direkter Schreibzugriff ohne Events und ohne Änderungsmarkierung.
        internal ref TileData RawRef(int index) => ref _tiles[index];

        internal TileData[] RawTiles => _tiles;

        // ---- Schreibzugriffe ----

        public void Set(int x, int y, in TileData tile)
        {
            int i = CheckedIndex(x, y);
            int oldOwner = _tiles[i].OwnerCivId;
            Remember(i);
            _tiles[i] = tile;
            if (tile.ResourceAmount == 0)
                _tiles[i].Resource = ResourceType.None;
            MarkChanged(i, x, y, TileChangeKind.Full);
            if (oldOwner != tile.OwnerCivId)
                MarkOwnership(x, y);
        }

        public void SetHeight(int x, int y, float height)
        {
            int i = CheckedIndex(x, y);
            if (float.IsNaN(height) || float.IsInfinity(height))
                throw new ArgumentException("Höhe muss eine endliche Zahl sein.", nameof(height));
            if (_tiles[i].Height == height)
                return;
            Remember(i);
            _tiles[i].Height = height;
            MarkChanged(i, x, y, TileChangeKind.Height);
        }

        public void SetTerrain(int x, int y, TerrainType terrain)
        {
            int i = CheckedIndex(x, y);
            if (_tiles[i].Terrain == terrain)
                return;
            Remember(i);
            _tiles[i].Terrain = terrain;
            // Wasser braucht eine Oberfläche, Land darf keine über dem Gelände haben.
            if (MovementRules.IsWaterTerrain(terrain))
            {
                if (_tiles[i].WaterLevel <= _tiles[i].Height)
                    _tiles[i].WaterLevel = Math.Max(SeaLevel, _tiles[i].Height + 1f);
            }
            else
            {
                _tiles[i].WaterLevel = 0f;
                _tiles[i].Flags &= ~TileFlags.Lake;
            }
            MarkChanged(i, x, y, TileChangeKind.Terrain);
        }

        public void SetFlags(int x, int y, TileFlags set, TileFlags clear)
        {
            int i = CheckedIndex(x, y);
            TileFlags old = _tiles[i].Flags;
            TileFlags now = (old & ~clear) | set;
            if (now == old)
                return;
            Remember(i);
            _tiles[i].Flags = now;
            MarkChanged(i, x, y, TileChangeKind.Flags);
        }

        public bool TryHarvest(int x, int y, int amount, out int taken)
        {
            taken = 0;
            if (!InBounds(x, y) || amount <= 0)
                return false;
            int i = y * Width + x;
            ref TileData t = ref _tiles[i];
            if (t.Resource == ResourceType.None || t.ResourceAmount == 0)
                return false;
            Remember(i);

            taken = Math.Min(amount, t.ResourceAmount);
            t.ResourceAmount = (ushort)(t.ResourceAmount - taken);
            ResourceType harvested = t.Resource;
            if (t.ResourceAmount == 0)
            {
                t.Resource = ResourceType.None;
                // Abgeholzter Wald wird zu Grasland.
                if (harvested == ResourceType.Wood && t.Terrain == TerrainType.Forest)
                {
                    t.Terrain = TerrainType.Grass;
                    MarkChanged(i, x, y, TileChangeKind.Terrain);
                }
                else
                {
                    MarkChanged(i, x, y, TileChangeKind.Resource);
                }
                _events?.Publish(new ResourceDepletedEvent { X = x, Y = y, Resource = harvested });
            }
            else
            {
                MarkChanged(i, x, y, TileChangeKind.Resource);
            }
            return true;
        }

        public void SetResource(int x, int y, ResourceType type, ushort amount)
        {
            int i = CheckedIndex(x, y);
            if (type == ResourceType.None || amount == 0)
            {
                type = ResourceType.None;
                amount = 0;
            }
            if (_tiles[i].Resource == type && _tiles[i].ResourceAmount == amount)
                return;
            Remember(i);
            _tiles[i].Resource = type;
            _tiles[i].ResourceAmount = amount;
            MarkChanged(i, x, y, TileChangeKind.Resource);
        }

        public void SetOwner(int x, int y, int civId)
        {
            int i = CheckedIndex(x, y);
            if (civId < 0)
                throw new ArgumentOutOfRangeException(nameof(civId), "Zivilisations-ID darf nicht negativ sein.");
            if (_tiles[i].OwnerCivId == civId)
                return;
            Remember(i);
            _tiles[i].OwnerCivId = civId;
            // Kein TileChangedEvent pro Kachel: Territorien ändern viele Kacheln auf einmal, gemeldet wird gebündelt.
            MarkChangedSilently(i);
            MarkOwnership(x, y);
        }

        public bool SetBuilding(int x, int y, EntityId building)
        {
            if (!InBounds(x, y) || !building.IsValid)
                return false;
            int i = y * Width + x;
            ref TileData t = ref _tiles[i];
            if (t.Building.IsValid)
                return t.Building.Equals(building);
            if (!IsBuildable(x, y))
                return false;
            Remember(i);
            t.Building = building;
            MarkChanged(i, x, y, TileChangeKind.Building);
            return true;
        }

        public void ClearBuilding(int x, int y)
        {
            int i = CheckedIndex(x, y);
            if (!_tiles[i].Building.IsValid)
                return;
            Remember(i);
            _tiles[i].Building = EntityId.None;
            MarkChanged(i, x, y, TileChangeKind.Building);
        }

        // Sendet die gebündelten Besitzänderungen dieses Ticks. Aufgerufen von WorldTickSystem vor EventBus.Flush.
        public void FlushPendingEvents()
        {
            if (!_ownershipDirty)
                return;
            _ownershipDirty = false;
            _events?.Publish(new OwnershipChangedEvent { MinX = _ownMinX, MinY = _ownMinY, MaxX = _ownMaxX, MaxY = _ownMaxY });
        }

        // ---- intern ----

        int CheckedIndex(int x, int y)
        {
            if (!InBounds(x, y))
                throw new ArgumentOutOfRangeException($"Kachel ({x}, {y}) liegt außerhalb der Welt {Width}×{Height}.");
            return y * Width + x;
        }

        // Vor jeder ersten Änderung einer Kachel aufrufen: sichert den generierten Wert.
        void Remember(int index)
        {
            if (!_changedSinceGeneration[index])
                _original[index] = _tiles[index];
        }

        void MarkChanged(int index, int x, int y, TileChangeKind kind)
        {
            MarkChangedSilently(index);
            if (kind != TileChangeKind.Resource && kind != TileChangeKind.Owner)
                NavigationVersion++;
            _events?.Publish(new TileChangedEvent { X = x, Y = y, Kind = kind });
        }

        void MarkChangedSilently(int index)
        {
            if (!_changedSinceGeneration[index])
            {
                _changedSinceGeneration[index] = true;
                _changedCount++;
            }
            Version++;
        }

        void MarkOwnership(int x, int y)
        {
            if (!_ownershipDirty)
            {
                _ownershipDirty = true;
                _ownMinX = _ownMaxX = x;
                _ownMinY = _ownMaxY = y;
                return;
            }
            if (x < _ownMinX) _ownMinX = x;
            if (x > _ownMaxX) _ownMaxX = x;
            if (y < _ownMinY) _ownMinY = y;
            if (y > _ownMaxY) _ownMaxY = y;
        }

        // Übernimmt den kompletten Zustand einer anderen Welt (Laden: frisch generieren, dann einspielen).
        internal void CopyFrom(WorldGrid source)
        {
            if (Width != source.Width || Height != source.Height)
                Allocate(source.GenSettings.Clone());
            else
                GenSettings = source.GenSettings.Clone();
            Array.Copy(source._tiles, _tiles, _tiles.Length);
            Array.Copy(source._changedSinceGeneration, _changedSinceGeneration, _tiles.Length);
            _changedCount = source._changedCount;
            _original = new Dictionary<int, TileData>(source._original);
            _ownershipDirty = false;
            Version++;
            NavigationVersion++;
        }

        internal bool IsChangedSinceGeneration(int index) => _changedSinceGeneration[index];

        // Beim Laden: Kachel mit gespeichertem Wert überschreiben, generierten Wert als Ursprung sichern.
        internal void ApplyLoadedDelta(int index, in TileData tile)
        {
            Remember(index);
            _tiles[index] = tile;
            MarkChangedSilently(index);
        }

        // Kopie der Welt im Zustand direkt nach der Generierung, ohne neu zu generieren.
        public WorldGrid CloneGenerated()
        {
            var generated = new WorldGrid(GenSettings);
            Array.Copy(_tiles, generated._tiles, _tiles.Length);
            foreach (var kv in _original)
                generated._tiles[kv.Key] = kv.Value;
            return generated;
        }

        internal void ResetChangeTracking()
        {
            Array.Clear(_changedSinceGeneration, 0, _changedSinceGeneration.Length);
            _changedCount = 0;
            _original.Clear();
        }
    }
}
