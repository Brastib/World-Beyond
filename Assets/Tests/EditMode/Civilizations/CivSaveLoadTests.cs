using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Speichern/Laden mitten im Wachstum: Alle Sektionen (core, world, units, civs, …) werden erfasst, in eine frische
    // Komposition eingespielt und beide Läufe danach weitergerechnet. Ergebnis muss exakt gleich sein.
    public class CivSaveLoadTests
    {
        static object RoundTrip(object dto, System.Type type)
        {
#if !UNITY_5_3_OR_NEWER
            // Headless: echter JSON-Umweg wie im SaveLoadService (Newtonsoft), deckt nicht serialisierbare Felder auf.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);
            return Newtonsoft.Json.JsonConvert.DeserializeObject(json, type);
#else
            return dto;
#endif
        }

        static void Transfer(CivRun from, CivRun into)
        {
            var captured = new Dictionary<string, object>();
            foreach (var s in from.Ctx.Saveables.InLoadOrder()) captured[s.SaveKey] = RoundTrip(s.CaptureState(), s.StateType);
            foreach (var s in into.Ctx.Saveables.InLoadOrder())
            {
                Assert.IsTrue(captured.ContainsKey(s.SaveKey), $"Sektion {s.SaveKey} fehlt");
                Assert.IsTrue(s.TryRestoreState(captured[s.SaveKey], out string err), $"{s.SaveKey}: {err}");
            }
            var report = new LoadReport();
            foreach (var s in into.Ctx.Saveables.InLoadOrder())
                if (s is ISaveReferenceCheck check) check.CheckReferences(into.Ctx, report);
            Assert.IsFalse(report.HasErrors, string.Join("; ", report.Errors));
        }

        [Test]
        public void SaveMidGrowth_LoadIntoFreshGame_ContinuesIdentically()
        {
            var a = CivRun.Create(21);
            a.Years(15);
            var b = CivRun.Create(999);   // anderer Seed: alles muss aus dem Spielstand kommen
            Transfer(a, b);
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint(), "Zustand direkt nach dem Laden");

            a.Years(10);
            b.Years(10);
            TestContext.WriteLine(a.Describe());
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint(), "Deterministische Fortsetzung nach dem Laden");
        }

        [Test]
        public void CivSection_RejectsBrokenState_AndKeepsCurrent()
        {
            var a = CivRun.Create(4);
            a.Years(2);
            var civs = FindSection(a, "civs");
            var state = (CivSaveState)RoundTrip(civs.CaptureState(), civs.StateType);
            state.Settlements[0].CivId = 999;   // verweist auf unbekannte Civ
            string before = a.Fingerprint();
            Assert.IsFalse(civs.TryRestoreState(state, out string err));
            TestContext.WriteLine(err);
            Assert.AreEqual(before, a.Fingerprint(), "Fehlgeschlagenes Laden verändert nichts");
        }

        static ISaveable FindSection(CivRun r, string key)
        {
            foreach (var s in r.Ctx.Saveables.InLoadOrder()) if (s.SaveKey == key) return s;
            Assert.Fail($"Sektion {key} fehlt");
            return null;
        }
    }
}
