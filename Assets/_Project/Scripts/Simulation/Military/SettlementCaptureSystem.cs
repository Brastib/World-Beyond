using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Military
{
    /// <summary>
    /// Besetzung von Siedlungen durch Einheiten (M1-Entscheidung von Integration): Halten feindliche Militäreinheiten
    /// (CombatEnvironment.AreHostile zum Besitzer) den Ortskern CaptureHoldSeconds lang, ohne dass eine lebende
    /// Militäreinheit des Besitzers dort steht, wird die Siedlung über ArmyEnvironment.CaptureSettlement übertragen.
    /// Sind mehrere Angreifer-Civs da, zählt die mit den meisten Einheiten (Gleichstand: kleinere Civ-Id).
    /// Verteidiger im Kern, kein Angreifer mehr oder ein Wechsel der führenden Angreifer-Civ setzen den Fortschritt zurück.
    /// Order 120 (nach ArmyAI), jede Spielsekunde.
    /// </summary>
    public sealed class SettlementCaptureSystem : ISimulationSystem, ISaveable
    {
        private const int Interval = 10;

        private readonly IUnitStore _units;
        private readonly MilitaryCatalog _catalog;
        private readonly CombatEnvironment _combatEnv;
        private readonly ArmyEnvironment _env;

        private readonly SortedDictionary<int, Progress> _progress = new SortedDictionary<int, Progress>();
        private readonly SortedDictionary<int, ArmyObjective> _candidates = new SortedDictionary<int, ArmyObjective>();
        private readonly SortedSet<int> _civs = new SortedSet<int>();
        private readonly List<EntityId> _alive = new List<EntityId>();
        private readonly List<(int civ, float x, float z)> _military = new List<(int civ, float x, float z)>();
        private readonly SortedDictionary<int, int> _count = new SortedDictionary<int, int>();
        private readonly List<int> _remove = new List<int>();

        private sealed class Progress { public int AttackerCiv; public long HeldTicks; }

        public SettlementCaptureSystem(IUnitStore units, MilitaryCatalog catalog, CombatEnvironment combatEnv, ArmyEnvironment env)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _combatEnv = combatEnv ?? throw new ArgumentNullException(nameof(combatEnv));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "SettlementCapture";
        public int Order => SystemOrder.AI + 20;
        public int TickInterval => Interval;

        private long HoldTicks => Math.Max(1L, (long)MathF.Round(_env.CaptureHoldSeconds * 10f));

        /// <summary>Für UI/Chronik: läuft eine Besetzung, von wem und wie weit (0..1)?</summary>
        public bool TryGetProgress(int settlementId, out int attackerCiv, out float fraction)
        {
            if (_progress.TryGetValue(settlementId, out var p))
            {
                attackerCiv = p.AttackerCiv;
                fraction = Math.Min(1f, p.HeldTicks / (float)HoldTicks);
                return true;
            }
            attackerCiv = 0; fraction = 0f;
            return false;
        }

        public void Tick(SimContext ctx)
        {
            if (_env.CaptureSettlement == null) return;
            CollectMilitary();
            CollectCandidates();

            float r2 = _env.CaptureRadius * _env.CaptureRadius;
            _remove.Clear();
            foreach (var p in _progress.Keys) if (!_candidates.ContainsKey(p)) _remove.Add(p);
            foreach (var id in _remove) _progress.Remove(id);

            foreach (var kv in _candidates)
            {
                int sid = kv.Key;
                var s = kv.Value;
                bool defended = false;
                _count.Clear();
                foreach (var m in _military)
                {
                    float dx = m.x - s.X, dz = m.z - s.Z;
                    if (dx * dx + dz * dz > r2) continue;
                    if (m.civ == s.OwnerCiv) { defended = true; break; }
                    if (_combatEnv.AreHostile(m.civ, s.OwnerCiv))
                        _count[m.civ] = _count.TryGetValue(m.civ, out int n) ? n + 1 : 1;
                }
                if (defended || _count.Count == 0) { _progress.Remove(sid); continue; }

                int leader = 0, best = -1;
                foreach (var c in _count) if (c.Value > best) { best = c.Value; leader = c.Key; }   // sortiert → Gleichstand: kleinste Id

                if (!_progress.TryGetValue(sid, out var prog) || prog.AttackerCiv != leader)
                    _progress[sid] = prog = new Progress { AttackerCiv = leader, HeldTicks = 0 };
                prog.HeldTicks += Interval;
                if (prog.HeldTicks < HoldTicks) continue;

                _progress.Remove(sid);
                _env.CaptureSettlement(sid, leader);   // false = abgelehnt; Fortschritt beginnt dann neu
            }
        }

        private void CollectMilitary()
        {
            _military.Clear();
            _civs.Clear();
            _units.CopyAliveIds(_alive);
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || !_catalog.TryGet(u.Type, out _)) continue;
                _military.Add((u.CivId, u.X, u.Z));
                _civs.Add(u.CivId);
            }
        }

        private void CollectCandidates()
        {
            _candidates.Clear();
            if (_env.Settlements != null)
            {
                var all = _env.Settlements();
                if (all != null) foreach (var s in all) if (s.SettlementId != 0) _candidates[s.SettlementId] = s;
                return;
            }
            if (_env.Objectives == null) return;
            foreach (int civ in _civs)
            {
                var list = _env.Objectives(civ);
                if (list == null) continue;
                foreach (var o in list)
                    if (o.SettlementId != 0 && o.OwnerCiv != civ && !_candidates.ContainsKey(o.SettlementId))
                        _candidates[o.SettlementId] = o;
            }
        }

        // ---------------- ISaveable ----------------

        public const string Key = "capture";
        public string SaveKey => Key;
        public int LoadOrder => 32;
        public int SectionVersion => 1;
        public Type StateType => typeof(CaptureSaveState);

        public object CaptureState()
        {
            var list = new List<CaptureRecord>();
            foreach (var kv in _progress)
                list.Add(new CaptureRecord { SettlementId = kv.Key, AttackerCiv = kv.Value.AttackerCiv, HeldTicks = kv.Value.HeldTicks });
            return new CaptureSaveState { Records = list.ToArray() };
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is CaptureSaveState s) || s.Records == null) { error = "capture: Zustand fehlt oder hat falschen Typ."; return false; }
            _progress.Clear();
            foreach (var r in s.Records)
            {
                if (r == null) continue;
                if (r.SettlementId == 0 || r.HeldTicks < 0 || _progress.ContainsKey(r.SettlementId))
                { error = $"capture: ungültiger Eintrag für Siedlung {r.SettlementId}."; return false; }
                _progress[r.SettlementId] = new Progress { AttackerCiv = r.AttackerCiv, HeldTicks = r.HeldTicks };
            }
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class CaptureSaveState { public CaptureRecord[] Records; }

    [Serializable]
    public sealed class CaptureRecord { public int SettlementId; public int AttackerCiv; public long HeldTicks; }
}
