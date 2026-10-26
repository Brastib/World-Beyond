using NUnit.Framework;
using WorldBeyond.Simulation.Civilizations;

namespace WorldBeyond.Tests.Civilizations
{
    // Realistisches Tempo (VISION V7, Nutzervorgabe 2026-10-26): 1 Simulationsjahr = 1 Weltjahr; Zeitalter wechseln
    // über Jahrhunderte, nicht Jahrzehnte. Befund vorher: Industriezeitalter nach 75 Jahren (Unity-Lauf, Seed 20261026).
    // Gemessen (128er Welt, 2 Civs): Frühzeit 700–1000+ Jahre, Antike ab Jahr ~700–900, 10 → 1 000–3 400 Einwohner in 1 000 Jahren.
    public class CivTechPaceTests
    {
        [Test]
        public void ThreeCenturies_StayInEarlyAge_ButGrow([Values(1UL, 20261026UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            r.Years(300);
            TestContext.WriteLine(r.Describe());
            foreach (int id in r.Civs.AllCivIds)
            {
                var c = r.Civs.Get(id);
                Assert.AreEqual(Epoch.Primitive, c.Epoch, $"{c.Name}: nach 300 Jahren noch Frühzeit");
                Assert.GreaterOrEqual(c.Population, 30, $"{c.Name}: wächst trotzdem");
                Assert.Greater(c.SettlementCount, 1, $"{c.Name}: gründet Tochtersiedlungen");
            }
        }

        [Test, Explicit("Langlauf (~1 min je Seed): Zeitalter über ein Jahrtausend")]
        public void Millennium_ReachesAtMostTheMiddleAges([Values(1UL, 20261026UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            string log = System.Environment.GetEnvironmentVariable("WB_PACE_LOG");
            for (int i = 0; i < 10; i++)
            {
                r.Years(100);
                TestContext.WriteLine(r.Describe());
                if (!string.IsNullOrEmpty(log)) System.IO.File.AppendAllText(log, $"{seed} {r.Describe()}\n");
            }
            foreach (int id in r.Civs.AllCivIds)
                Assert.LessOrEqual((int)r.Civs.Get(id).Epoch, (int)Epoch.Medieval, "Nach 1 000 Jahren höchstens Mittelalter");
        }
    }
}
