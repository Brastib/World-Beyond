using System.Numerics;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World
{
    // Explizite Werte, nur anhängen (ARCHITECTURE §11.1 Regel 4). Im Spielstand als String-Schlüssel (WorldContentKeys).
    public enum TerrainType : byte
    {
        DeepWater = 0,
        ShallowWater = 1,
        Beach = 2,
        Grass = 3,
        Forest = 4,
        Rock = 5,
        Mountain = 6,
        Snow = 7,
        Desert = 8,
        Swamp = 9,
        River = 10,
    }

    public enum BiomeType : byte
    {
        Ocean = 0,
        Coast = 1,
        Temperate = 2,
        Boreal = 3,
        Tundra = 4,
        Desert = 5,
        Savanna = 6,
        Wetland = 7,
        Alpine = 8,
        TropicalForest = 9,   // heiß und feucht, dichter Wald
        Mediterranean = 10,   // warm, trockene Sommer, lichte Wälder
    }

    public enum ResourceType : byte
    {
        None = 0,
        Food = 1,
        Wood = 2,
        Stone = 3,
        Ore = 4,
        Oil = 5,
        Gold = 6,
        Metal = 7,      // nur Lagergut (Civ), kommt nicht auf Kacheln vor
        Tools = 8,      // nur Lagergut (Civ), kommt nicht auf Kacheln vor
    }

    [System.Flags]
    public enum TileFlags : ushort
    {
        None = 0,
        Burning = 1,
        Scorched = 2,
        Road = 4,       // auf Wasser/Fluss = Brücke
        Crater = 8,
        RiverBank = 16,
        Lake = 32,
        Wreck = 64,
    }

    public enum TileChangeKind : byte
    {
        Height = 0,
        Terrain = 1,
        Resource = 2,
        Building = 3,
        Flags = 4,
        Full = 5,
        Owner = 6,
    }

    public enum PlacementFailure : byte
    {
        None = 0,
        OutOfBounds = 1,
        Water = 2,
        TooSteep = 3,
        Occupied = 4,
        Blocked = 5,
    }

    public struct TileData
    {
        public float Height;           // Geländehöhe in m
        public float WaterLevel;       // Wasseroberfläche in m; nur relevant, wenn > Height (Meer, See, Fluss)
        public TerrainType Terrain;
        public BiomeType Biome;
        public ResourceType Resource;
        public ushort ResourceAmount;
        public byte Fertility;         // 0..255, Nahrungsertrag
        public byte Moisture;          // 0..255
        public int OwnerCivId;         // 0 = niemand; schreiben nur über IWorldGrid.SetOwner
        public EntityId Building;      // EntityId.None = frei; schreiben nur über SetBuilding/ClearBuilding
        public TileFlags Flags;

        public bool HasFlag(TileFlags flag) => (Flags & flag) == flag;
    }

    public delegate void TileVisitor(int x, int y, in TileData tile);

    // Rückgabe <= 0 bedeutet ungeeignet.
    public delegate float TileScorer(int x, int y, in TileData tile);

    public struct TileScore
    {
        public int X;
        public int Y;
        public float Score;

        public TileScore(int x, int y, float score)
        {
            X = x;
            Y = y;
            Score = score;
        }
    }

    // Strahl für Picking. System.Numerics statt UnityEngine, damit die Simulation ohne Unity kompiliert (Tools/SimTests).
    public struct WorldRay
    {
        public Vector3 Origin;
        public Vector3 Direction;

        public WorldRay(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }

    public struct TileChangedEvent : ISimEvent
    {
        public int X;
        public int Y;
        public TileChangeKind Kind;
    }

    public struct ResourceDepletedEvent : ISimEvent
    {
        public int X;
        public int Y;
        public ResourceType Resource;
    }

    // Ein Event pro Tick mit dem Rechteck aller Besitzänderungen (inklusive Grenzen).
    public struct OwnershipChangedEvent : ISimEvent
    {
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
    }
}
