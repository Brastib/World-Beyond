using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Prüf-Befund (Nachtprüfung): Karawanen zu unerreichbaren Siedlungen wurden alle 10 s neu geplant und scheiterten sofort.
    public class CivTransportTests
    {
        [Test]
        public void FailedCaravanTrip_BlocksRoute_ForCooldown()
        {
            var r = CivRun.Create(12345UL);
            var transport = r.Ctx.Get<TransportSystem>();
            var caravanType = r.Ctx.UnitTypes.TryGetByKey(CivContent.UnitCaravan, out var ct) ? ct : default;
            var units = new List<EntityId>();
            EntityId caravan = default;
            for (int i = 0; i < 400 && caravan == default; i++)
            {
                r.Years(1);
                r.Ctx.Units.CopyAliveIds(units);
                foreach (var u in units)
                    if (r.Ctx.Units.Get(u).Type.Equals(caravanType)) { caravan = u; break; }
            }
            Assert.AreNotEqual(default(EntityId), caravan, "In 400 Jahren keine Karawane unterwegs");
            int target = r.Ctx.Units.Get(caravan).Task.TargetRef;
            int before = transport.BlockedRouteCount;

            r.Ctx.Events.Publish(new UnitMoveFailedEvent { Unit = caravan, Reason = PathStatus.NotFound });
            r.Years(0.01);
            Assert.AreEqual(before + 1, transport.BlockedRouteCount, "Gescheiterte Fahrt sperrt die Strecke");
            Assert.IsTrue(BlockedTo(r, transport, target), "Strecke zum Ziel gesperrt");
            Assert.AreNotEqual(target, r.Ctx.Units.TryGet(caravan, out var back) ? back.Task.TargetRef : -1, "Karawane kehrt heim");

            r.Years(TransportSystem.RouteCooldownTicks / (double)CivTime.TicksPerYear + 0.5);
            Assert.IsFalse(BlockedTo(r, transport, target) && transport.BlockedRouteCount > before + 5,
                "Sperren laufen ab und häufen sich nicht");
        }

        [Test]
        public void LongRun_CaravansRarelyFail([Values(1UL, 12345UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            var caravanType = r.Ctx.UnitTypes.TryGetByKey(CivContent.UnitCaravan, out var ct) ? ct : default;
            int failed = 0;
            var caravans = new HashSet<EntityId>();
            r.Ctx.Events.Subscribe<UnitSpawnedEvent>(e => { if (e.UnitType.Equals(caravanType)) caravans.Add(e.Unit); });
            r.Ctx.Events.Subscribe<UnitMoveFailedEvent>(e => { if (caravans.Contains(e.Unit)) failed++; });
            r.Years(400);
            int sent = caravans.Count;
            TestContext.WriteLine($"Karawanen: {sent} losgeschickt, {failed} Fehlschläge");
            Assert.Greater(sent, 0);
            Assert.LessOrEqual(failed, sent / 20 + 5, "Höchstens 5 % der Karawanen scheitern am Weg");
        }

        static bool BlockedTo(CivRun r, TransportSystem t, int target)
        {
            foreach (int sid in r.Settlements.AllSettlementIds)
                if (t.IsBlocked(sid, target)) return true;
            return false;
        }
    }

    public class TerritorySaveTests
    {
        [Test]
        public void CaptureWithoutDirtyArea_StoresZeros()
        {
            var r = CivRun.Create(3UL);
            r.Years(3);
            r.Years(0.2);   // TerritorySystem hat die offene Neuberechnung erledigt
            var civs = FindSection(r, "civs");
            var st = (CivSaveState)civs.CaptureState();
            if (st.Territory.Dirty) { r.Years(0.5); st = (CivSaveState)civs.CaptureState(); }
            Assert.IsFalse(st.Territory.Dirty, "nach Neuberechnung nichts offen");
            Assert.AreEqual(0, st.Territory.MinX);
            Assert.AreEqual(0, st.Territory.MinY);
            Assert.AreEqual(0, st.Territory.MaxX);
            Assert.AreEqual(0, st.Territory.MaxY);

            r.Ctx.Get<ITerritoryService>().MarkDirty(r.Settlements.AllSettlementIds[0]);
            st = (CivSaveState)civs.CaptureState();
            Assert.IsTrue(st.Territory.Dirty);
            Assert.Greater(st.Territory.MaxX, st.Territory.MinX, "offener Bereich wird gespeichert");
        }

        static ISaveable FindSection(CivRun r, string key)
        {
            foreach (var s in r.Ctx.Saveables.InLoadOrder()) if (s.SaveKey == key) return s;
            Assert.Fail($"Sektion {key} fehlt");
            return null;
        }
    }
}
