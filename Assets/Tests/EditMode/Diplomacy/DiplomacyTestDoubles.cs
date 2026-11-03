using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Diplomacy;

namespace WorldBeyond.Tests.EditMode.Diplomacy
{
    // Testwelt für Phase 4: ersetzt nur den Port, nicht die echten Systeme anderer Phasen.
    public sealed class FakeDiplomacyWorld : IDiplomacyWorldPort
    {
        public sealed class Civ
        {
            public float Strength = 100f;
            public int Population = 1000;
            public float Aggression = 0.5f;
            public float ResourceNeed;
            public int CapitalX, CapitalY;
            public bool Alive = true;
            public float Stock = 1000f;
            public int TerritoryTiles;
            public int ArmyUnits = -1;    // -1 = aus Stärke geschätzt (Stärke / 5)
        }

        public readonly SortedDictionary<int, Civ> Civs = new SortedDictionary<int, Civ>();
        public readonly SortedDictionary<int, SettlementInfo> Settlements = new SortedDictionary<int, SettlementInfo>();
        public readonly Dictionary<(int, int), int> Borders = new Dictionary<(int, int), int>();
        public readonly Dictionary<(int, int), float> Contestion = new Dictionary<(int, int), float>();
        public readonly Dictionary<(int, int), int> DominantAt = new Dictionary<(int, int), int>();
        public readonly List<(int civ, int count)> RecruitRequests = new List<(int, int)>();
        public bool RecruitSucceeds = true;
        public int MaxRecruitBatch = int.MaxValue;   // größere Anforderungen werden abgelehnt (wie zu wenig Ressourcen)
        int _nextSettlement = 1;

        public Civ AddCiv(int id, int capitalX, int capitalY, float strength = 100f, int population = 1000)
        {
            var c = new Civ { CapitalX = capitalX, CapitalY = capitalY, Strength = strength, Population = population };
            Civs[id] = c;
            AddSettlement(id, capitalX, capitalY, population / 2, true);
            return c;
        }

        public int AddSettlement(int civ, int x, int y, int population, bool capital = false)
        {
            int id = _nextSettlement++;
            Settlements[id] = new SettlementInfo(id, civ, population, x, y, capital);
            return id;
        }

        public void SetBorder(int a, int b, int length) { Borders[(a, b)] = length; Borders[(b, a)] = length; }
        public void Kill(int civ) { Civs[civ].Alive = false; }

        public IReadOnlyList<int> CivIds
        {
            get { var l = new List<int>(); foreach (var kv in Civs) if (kv.Value.Alive) l.Add(kv.Key); return l; }
        }
        public bool IsCivAlive(int civId) => Civs.TryGetValue(civId, out var c) && c.Alive;
        public float GetMilitaryStrength(int civId) => Civs.TryGetValue(civId, out var c) ? c.Strength : 0f;
        public int GetArmyUnitCount(int civId) =>
            Civs.TryGetValue(civId, out var c) ? (c.ArmyUnits >= 0 ? c.ArmyUnits : (int)(c.Strength / 5f)) : 0;
        public float UnitStrength = 5f;
        public float GetUnitCombatStrength(int unitTypeId) => UnitStrength;
        public int GetPopulation(int civId) => Civs.TryGetValue(civId, out var c) ? c.Population : 0;
        public int GetTerritoryTiles(int civId) => Civs.TryGetValue(civId, out var c) ? c.TerritoryTiles : 0;
        public float GetAggression(int civId) => Civs.TryGetValue(civId, out var c) ? c.Aggression : 0f;
        public float GetResourceNeed(int civId) => Civs.TryGetValue(civId, out var c) ? c.ResourceNeed : 0f;
        public int GetSharedBorderLength(int a, int b) => Borders.TryGetValue((a, b), out var v) ? v : 0;
        public float GetResourceContestion(int a, int b) => Contestion.TryGetValue((a, b), out var v) ? v : 0f;

        public bool TryGetCapitalPosition(int civId, out int x, out int y)
        {
            x = y = 0;
            if (!Civs.TryGetValue(civId, out var c) || !c.Alive) return false;
            x = c.CapitalX; y = c.CapitalY; return true;
        }

        public IReadOnlyList<int> GetSettlements(int civId)
        {
            var l = new List<int>();
            foreach (var kv in Settlements) if (kv.Value.OwnerCiv == civId) l.Add(kv.Key);
            return l;
        }

        public bool TryGetSettlement(int id, out SettlementInfo info) => Settlements.TryGetValue(id, out info);

        public bool TransferSettlement(int id, int newCiv)
        {
            if (!Settlements.TryGetValue(id, out var s)) return false;
            Settlements[id] = new SettlementInfo(id, newCiv, s.Population, s.X, s.Y, false);
            return true;
        }

        public float TransferTribute(int from, int to, float fraction)
        {
            float amount = Civs[from].Stock * fraction;
            Civs[from].Stock -= amount;
            Civs[to].Stock += amount;
            return amount;
        }

        public bool RequestMilitaryBuildup(int civId, int count, out string reason)
        {
            RecruitRequests.Add((civId, count));
            bool ok = RecruitSucceeds && count <= MaxRecruitBatch;
            reason = ok ? null : "NotEnoughResources";
            return ok;
        }

        public int GetDominantCivNear(int x, int y, int radius) => DominantAt.TryGetValue((x, y), out var c) ? c : 0;
    }

    // Sammelt veröffentlichte Events (Ersatz für den echten Bus nur in Tests).
    public sealed class RecordingEventBus : ISimEventBus
    {
        public readonly List<object> Published = new List<object>();
        public void Publish<T>(in T evt) where T : struct, ISimEvent => Published.Add(evt);
        public void Subscribe<T>(Action<T> handler) where T : struct, ISimEvent { }
        public void Unsubscribe<T>(Action<T> handler) where T : struct, ISimEvent { }
        public void Flush() { }

        public List<T> Of<T>() where T : struct
        {
            var l = new List<T>();
            foreach (var e in Published) if (e is T t) l.Add(t);
            return l;
        }
    }

    // Kleine Testumgebung: Welt, Bus, Uhr, Service, KI.
    public sealed class DiplomacyTestRig
    {
        public readonly FakeDiplomacyWorld World = new FakeDiplomacyWorld();
        public readonly RecordingEventBus Bus = new RecordingEventBus();
        public long Tick;
        public readonly DiplomacyService Service;
        public readonly WarStrategyEvaluator Ai;

        public DiplomacyTestRig()
        {
            Service = new DiplomacyService(World, Bus, () => Tick);
            Ai = new WarStrategyEvaluator(Service);
        }

        // Simuliert wie im Spiel: Diplomatie jede Sekunde, Civ-KI alle 5 Sekunden.
        public void RunSeconds(int seconds)
        {
            for (int s = 0; s < seconds; s++)
            {
                Tick += 10;
                Service.Update();
                if (Tick % 50 == 0) foreach (var civ in World.CivIds) Ai.EvaluateAndAct(civ);
            }
        }
    }
}

namespace WorldBeyond.Tests.EditMode.Diplomacy
{
    // Kanonischer Text aller öffentlichen Felder/Eigenschaften (rekursiv), um Zustände ohne JSON-Bibliothek zu vergleichen.
    public static class StateDump
    {
        public static string Of(object o)
        {
            var sb = new System.Text.StringBuilder();
            Write(sb, o, 0);
            return sb.ToString();
        }

        static void Write(System.Text.StringBuilder sb, object o, int depth)
        {
            if (depth > 12) { sb.Append("…"); return; }
            if (o == null) { sb.Append("null"); return; }
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || o is string || o is decimal)
            {
                sb.Append(System.Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (o is System.Collections.IEnumerable list)
            {
                sb.Append('[');
                foreach (var item in list) { Write(sb, item, depth + 1); sb.Append(','); }
                sb.Append(']');
                return;
            }
            sb.Append('{');
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            { sb.Append(f.Name).Append('='); Write(sb, f.GetValue(o), depth + 1); sb.Append(';'); }
            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                sb.Append(p.Name).Append('='); Write(sb, p.GetValue(o), depth + 1); sb.Append(';');
            }
            sb.Append('}');
        }
    }
}
