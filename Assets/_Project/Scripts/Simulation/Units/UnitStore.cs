using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Units
{
    /// <summary>
    /// Dichte Arrays, indiziert über EntityId.Index. Speichert sich selbst als Sektion "units";
    /// Einheitentypen stehen im Spielstand als Key, nie als Laufzeit-int.
    /// </summary>
    public sealed class UnitStore : IUnitStore, ISaveable
    {
        public const string Key = "units";

        private readonly EntityStore _ids;
        private readonly UnitTypeRegistry _types;
        private readonly ISimEventBus _events;

        private int[] _type;
        private int[] _civ;
        private float[] _x, _z, _heading, _speed, _hp;
        private bool[] _hasTarget;
        private float[] _tx, _tz;
        private UnitState[] _state;
        private UnitTask[] _task;

        public UnitStore(UnitTypeRegistry types, ISimEventBus events, int initialCapacity = 1024)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _ids = new EntityStore(initialCapacity);
            Allocate(initialCapacity + 1);
        }

        public int Count => _ids.AliveCount;
        public bool Exists(EntityId id) => _ids.Exists(id);

        public EntityId Spawn(UnitTypeId type, int civId, float x, float z)
        {
            if (!_types.TryGet(type, out var data)) return EntityId.None;
            var id = _ids.Create();
            EnsureCapacity(id.Index + 1);
            int i = id.Index;
            _type[i] = type.Value;
            _civ[i] = civId;
            _x[i] = x; _z[i] = z;
            _heading[i] = 0f; _speed[i] = 0f;
            _hp[i] = data.MaxHp;
            _hasTarget[i] = false; _tx[i] = 0f; _tz[i] = 0f;
            _state[i] = UnitState.Idle;
            _task[i] = UnitTask.None;
            _events.Publish(new UnitSpawnedEvent { Unit = id, UnitType = type, CivId = civId, X = x, Z = z });
            return id;
        }

        public bool Despawn(EntityId id, EntityId killer = default, int killerCivId = 0)
        {
            if (!_ids.Exists(id)) return false;
            int i = id.Index;
            var evt = new UnitDiedEvent
            {
                Unit = id, Killer = killer, CivId = _civ[i], KillerCivId = killerCivId,
                UnitType = new UnitTypeId(_type[i]), X = _x[i], Z = _z[i],
            };
            _state[i] = UnitState.Dead;
            _ids.Destroy(id);
            _events.Publish(evt);
            return true;
        }

        public bool TryGet(EntityId id, out UnitView view)
        {
            if (!_ids.Exists(id)) { view = default; return false; }
            int i = id.Index;
            float maxHp = _types.TryGet(new UnitTypeId(_type[i]), out var d) ? d.MaxHp : 0f;
            view = new UnitView(id, new UnitTypeId(_type[i]), _civ[i], _x[i], _z[i], _heading[i], _speed[i],
                _hp[i], maxHp, _state[i], _task[i], _hasTarget[i], _tx[i], _tz[i]);
            return true;
        }

        public UnitView Get(EntityId id) => TryGet(id, out var v) ? v : default;

        public void ForEachAlive(Action<EntityId> visitor)
        {
            int limit = _ids.IndexLimit;
            for (int i = 1; i < limit; i++)
            {
                var id = _ids.IdAt(i);
                if (id.IsValid) visitor(id);
            }
        }

        public void CopyAliveIds(List<EntityId> results)
        {
            results.Clear();
            int limit = _ids.IndexLimit;
            for (int i = 1; i < limit; i++)
            {
                var id = _ids.IdAt(i);
                if (id.IsValid) results.Add(id);
            }
        }

        public bool SetMoveTarget(EntityId id, float x, float z)
        {
            if (!_ids.Exists(id)) return false;
            int i = id.Index;
            _hasTarget[i] = true; _tx[i] = x; _tz[i] = z;
            if (_state[i] == UnitState.Idle) _state[i] = UnitState.Moving;
            return true;
        }

        public bool ClearMoveTarget(EntityId id)
        {
            if (!_ids.Exists(id)) return false;
            int i = id.Index;
            _hasTarget[i] = false;
            _speed[i] = 0f;
            if (_state[i] == UnitState.Moving) _state[i] = UnitState.Idle;
            return true;
        }

        public bool SetTask(EntityId id, UnitTask task)
        {
            if (!_ids.Exists(id)) return false;
            _task[id.Index] = task;
            return true;
        }

        public bool SetState(EntityId id, UnitState state)
        {
            if (!_ids.Exists(id) || state == UnitState.Dead) return false;  // Tod nur über Despawn
            _state[id.Index] = state;
            return true;
        }

        public bool SetPosition(EntityId id, float x, float z, float heading, float speed)
        {
            if (!_ids.Exists(id)) return false;
            int i = id.Index;
            _x[i] = x; _z[i] = z; _heading[i] = heading; _speed[i] = speed;
            return true;
        }

        public bool SetHp(EntityId id, float hp)
        {
            if (!_ids.Exists(id)) return false;
            int i = id.Index;
            float max = _types.TryGet(new UnitTypeId(_type[i]), out var d) ? d.MaxHp : hp;
            _hp[i] = Math.Max(0f, Math.Min(hp, max));
            return true;
        }

        // ---------------- ISaveable ----------------

        public string SaveKey => Key;
        public int LoadOrder => 10;
        public int SectionVersion => 1;
        public Type StateType => typeof(UnitStoreState);

        public object CaptureState()
        {
            var state = new UnitStoreState { Entities = _ids.CaptureState() };
            var units = new List<UnitRecord>(_ids.AliveCount);
            int limit = _ids.IndexLimit;
            for (int i = 1; i < limit; i++)
            {
                if (!_ids.IdAt(i).IsValid) continue;
                units.Add(new UnitRecord
                {
                    Index = i,
                    TypeKey = _types.KeyOf(new UnitTypeId(_type[i])),
                    CivId = _civ[i],
                    X = _x[i], Z = _z[i], Heading = _heading[i], Speed = _speed[i], Hp = _hp[i],
                    HasMoveTarget = _hasTarget[i], MoveTargetX = _tx[i], MoveTargetZ = _tz[i],
                    State = _state[i], Task = _task[i],
                });
            }
            state.Units = units.ToArray();
            return state;
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is UnitStoreState s) || s.Entities == null || s.Units == null)
            {
                error = "units: Zustand fehlt oder hat falschen Typ.";
                return false;
            }

            // Erst vollständig in einen Prüf-Store einspielen; der echte Zustand ändert sich nur bei Erfolg.
            var probe = new EntityStore(1);
            if (!probe.TryRestoreState(s.Entities, out error)) { error = "units: " + error; return false; }

            int len = s.Entities.Generations.Length;
            var covered = new bool[len];
            var typeIds = new int[s.Units.Length];
            for (int u = 0; u < s.Units.Length; u++)
            {
                var r = s.Units[u];
                if (r == null || r.Index <= 0 || r.Index >= len || !s.Entities.Alive[r.Index])
                {
                    error = $"units: Datensatz {u} verweist auf keinen lebenden Index ({r?.Index}).";
                    return false;
                }
                if (covered[r.Index]) { error = $"units: Index {r.Index} doppelt."; return false; }
                covered[r.Index] = true;
                if (!_types.TryGetByKey(r.TypeKey, out var t)) { error = $"units: unbekannter Einheitentyp '{r.TypeKey}'."; return false; }
                if (r.State == UnitState.Dead) { error = $"units: Index {r.Index} ist als Dead gespeichert, lebt aber."; return false; }
                typeIds[u] = t.Value;
            }
            for (int i = 1; i < len; i++)
            {
                if (s.Entities.Alive[i] && !covered[i]) { error = $"units: lebender Index {i} hat keinen Datensatz."; return false; }
            }

            _ids.TryRestoreState(s.Entities, out _);
            Allocate(Math.Max(len, 2));
            for (int u = 0; u < s.Units.Length; u++)
            {
                var r = s.Units[u];
                int i = r.Index;
                _type[i] = typeIds[u];
                _civ[i] = r.CivId;
                _x[i] = r.X; _z[i] = r.Z; _heading[i] = r.Heading; _speed[i] = r.Speed; _hp[i] = r.Hp;
                _hasTarget[i] = r.HasMoveTarget; _tx[i] = r.MoveTargetX; _tz[i] = r.MoveTargetZ;
                _state[i] = r.State; _task[i] = r.Task;
            }
            error = null;
            return true;
        }

        // ---------------- intern ----------------

        private void Allocate(int capacity)
        {
            _type = new int[capacity];
            _civ = new int[capacity];
            _x = new float[capacity]; _z = new float[capacity];
            _heading = new float[capacity]; _speed = new float[capacity]; _hp = new float[capacity];
            _hasTarget = new bool[capacity]; _tx = new float[capacity]; _tz = new float[capacity];
            _state = new UnitState[capacity];
            _task = new UnitTask[capacity];
        }

        private void EnsureCapacity(int needed)
        {
            if (needed <= _type.Length) return;
            int cap = Math.Max(needed, _type.Length * 2);
            Array.Resize(ref _type, cap);
            Array.Resize(ref _civ, cap);
            Array.Resize(ref _x, cap); Array.Resize(ref _z, cap);
            Array.Resize(ref _heading, cap); Array.Resize(ref _speed, cap); Array.Resize(ref _hp, cap);
            Array.Resize(ref _hasTarget, cap); Array.Resize(ref _tx, cap); Array.Resize(ref _tz, cap);
            Array.Resize(ref _state, cap);
            Array.Resize(ref _task, cap);
        }
    }

    [Serializable]
    public sealed class UnitStoreState
    {
        public EntityStoreState Entities;
        public UnitRecord[] Units;
    }

    [Serializable]
    public sealed class UnitRecord
    {
        public int Index;
        public string TypeKey;
        public int CivId;
        public float X, Z, Heading, Speed, Hp;
        public bool HasMoveTarget;
        public float MoveTargetX, MoveTargetZ;
        public UnitState State;
        public UnitTask Task;
    }
}
