using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>Lesesicht eines Gebäudes.</summary>
    public readonly struct BuildingView
    {
        public readonly EntityId Id;
        public readonly BuildingTypeId Type;
        public readonly string TypeKey;
        public readonly int SettlementId;
        public readonly int X, Y, SizeX, SizeY;
        public readonly float Construction01;     // 1 = fertig
        public readonly bool Completed;
        public readonly bool Idle;                // fertig, aber ohne Rohstoff/Wasser im Einzugsbereich
        public readonly int Workers;

        public BuildingView(EntityId id, BuildingTypeId type, string key, int settlement, int x, int y, int sx, int sy, float c, bool done, bool idle, int workers)
        {
            Id = id; Type = type; TypeKey = key; SettlementId = settlement; X = x; Y = y; SizeX = sx; SizeY = sy;
            Construction01 = c; Completed = done; Idle = idle; Workers = workers;
        }
    }

    public interface IBuildingQuery
    {
        bool Exists(EntityId id);
        bool TryGet(EntityId id, out BuildingView view);
        void CopyAliveIds(List<EntityId> results);
    }

    /// <summary>
    /// Alle Gebäude aller Zivilisationen. IDs als EntityId über einen EntityStore (ARCHITECTURE §11.1 Regel 1).
    /// Belegt Kacheln ausschließlich über IWorldGrid.SetBuilding/ClearBuilding. SaveKey "buildings".
    /// </summary>
    public sealed class BuildingStore : IBuildingQuery
    {
        internal sealed class Record
        {
            public BuildingTypeId Type;
            public int SettlementId;
            public int X, Y, SizeX, SizeY;
            public double Progress;            // Arbeiterjahre geleistet
            public bool Completed;
            public bool Idle;
            public int Workers;
            public double OutputCarry;          // angesammelter, noch nicht abgebauter Bruchteil
            public int LastWorkX = -1, LastWorkY = -1;   // zuletzt abgebaute Kachel (Darstellung der Arbeiter)
        }

        private readonly EntityStore _ids = new EntityStore(256);
        private Record[] _records = new Record[257];
        private readonly BuildingTypeRegistry _types;

        public BuildingStore(BuildingTypeRegistry types) => _types = types ?? throw new ArgumentNullException(nameof(types));

        public int Count => _ids.AliveCount;
        public bool Exists(EntityId id) => _ids.Exists(id);

        internal EntityId Create(BuildingTypeId type, int settlementId, int x, int y)
        {
            var d = _types.Get(type);
            var id = _ids.Create();
            if (id.Index >= _records.Length) Array.Resize(ref _records, Math.Max(id.Index + 1, _records.Length * 2));
            _records[id.Index] = new Record { Type = type, SettlementId = settlementId, X = x, Y = y, SizeX = d.SizeX, SizeY = d.SizeY };
            return id;
        }

        internal bool Destroy(EntityId id)
        {
            if (!_ids.Exists(id)) return false;
            _records[id.Index] = null;
            return _ids.Destroy(id);
        }

        internal Record GetRecord(EntityId id) => _ids.Exists(id) ? _records[id.Index] : null;

        public bool TryGet(EntityId id, out BuildingView view)
        {
            var r = GetRecord(id);
            if (r == null) { view = default; return false; }
            var d = _types.Get(r.Type);
            float c = r.Completed ? 1f : (float)Math.Min(1.0, r.Progress / d.BuildWorkYears);
            view = new BuildingView(id, r.Type, d.Key, r.SettlementId, r.X, r.Y, r.SizeX, r.SizeY, c, r.Completed, r.Idle, r.Workers);
            return true;
        }

        public void CopyAliveIds(List<EntityId> results)
        {
            results.Clear();
            for (int i = 1; i < _ids.IndexLimit; i++)
            {
                var id = _ids.IdAt(i);
                if (id.IsValid) results.Add(id);
            }
        }

        // ---------------- Speichern ----------------

        internal BuildingStoreState Capture()
        {
            var list = new List<BuildingRecordDto>(_ids.AliveCount);
            for (int i = 1; i < _ids.IndexLimit; i++)
            {
                if (!_ids.IdAt(i).IsValid) continue;
                var r = _records[i];
                list.Add(new BuildingRecordDto
                {
                    Index = i, TypeKey = _types.KeyOf(r.Type), SettlementId = r.SettlementId, X = r.X, Y = r.Y,
                    Progress = r.Progress, Completed = r.Completed, Idle = r.Idle, Workers = r.Workers, OutputCarry = r.OutputCarry,
                    LastWorkX = r.LastWorkX, LastWorkY = r.LastWorkY,
                });
            }
            return new BuildingStoreState { Entities = _ids.CaptureState(), Buildings = list.ToArray() };
        }

        internal bool TryRestore(BuildingStoreState s, out string error)
        {
            if (s == null || s.Entities == null || s.Buildings == null) { error = "buildings: Zustand fehlt."; return false; }
            var probe = new EntityStore(1);
            if (!probe.TryRestoreState(s.Entities, out error)) { error = "buildings: " + error; return false; }
            int len = s.Entities.Generations.Length;
            var records = new Record[Math.Max(len, 2)];
            foreach (var b in s.Buildings)
            {
                if (b == null || b.Index <= 0 || b.Index >= len || !s.Entities.Alive[b.Index]) { error = $"buildings: Datensatz verweist auf keinen lebenden Index ({b?.Index})."; return false; }
                if (records[b.Index] != null) { error = $"buildings: Index {b.Index} doppelt."; return false; }
                if (!_types.TryGetByKey(b.TypeKey, out var t)) { error = $"buildings: unbekannter Gebäudetyp '{b.TypeKey}'."; return false; }
                var d = _types.Get(t);
                records[b.Index] = new Record
                {
                    Type = t, SettlementId = b.SettlementId, X = b.X, Y = b.Y, SizeX = d.SizeX, SizeY = d.SizeY,
                    Progress = b.Progress, Completed = b.Completed, Idle = b.Idle, Workers = b.Workers, OutputCarry = b.OutputCarry,
                    LastWorkX = b.LastWorkX, LastWorkY = b.LastWorkY,
                };
            }
            for (int i = 1; i < len; i++)
                if (s.Entities.Alive[i] && records[i] == null) { error = $"buildings: lebender Index {i} ohne Datensatz."; return false; }
            _ids.TryRestoreState(s.Entities, out _);
            _records = records;
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class BuildingStoreState
    {
        public EntityStoreState Entities;
        public BuildingRecordDto[] Buildings;
    }

    [Serializable]
    public sealed class BuildingRecordDto
    {
        public int Index;
        public string TypeKey;
        public int SettlementId;
        public int X, Y;
        public double Progress;
        public bool Completed;
        public bool Idle;
        public int Workers;
        public double OutputCarry;
        public int LastWorkX, LastWorkY;
    }
}
