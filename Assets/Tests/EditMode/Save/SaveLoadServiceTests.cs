using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Application.Save;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests.EditMode.Save
{
    [Serializable]
    public sealed class ExtraState
    {
        public int Value;
        public string[] UnitKeys = new string[0];
        public int[] CivRefs = new int[0];
    }

    /// <summary>Test-Saveable mit konfigurierbarer Version, Inhalts- und Referenzprüfung.</summary>
    internal sealed class ExtraSaveable : ISaveable, ISaveContentCheck, ISaveReferenceCheck
    {
        public int Version = 1;
        public ExtraState State = new ExtraState();
        public Func<object> CaptureOverride;
        public Action<SimContext, LoadReport> ReferenceCheck;
        public string SaveKey => "test.extra";
        public int LoadOrder => 50;
        public int SectionVersion => Version;
        public Type StateType => typeof(ExtraState);
        public object CaptureState() => CaptureOverride != null ? CaptureOverride() : State;

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is ExtraState s)) { error = "falscher Typ"; return false; }
            if (s.Value < 0) { error = "Value negativ"; return false; }
            State = s;
            error = null;
            return true;
        }

        public void CollectContentKeys(object dto, ICollection<(ContentKind kind, string key)> keys)
        {
            foreach (var k in ((ExtraState)dto).UnitKeys) keys.Add((ContentKind.Unit, k));
        }

        public void CheckReferences(SimContext ctx, LoadReport report) => ReferenceCheck?.Invoke(ctx, report);
    }

    /// <summary>Nutzt Zufall, spawnt und entfernt Einheiten und bewegt sie: macht Determinismusfehler sichtbar.</summary>
    internal sealed class ChurnSystem : ISimulationSystem
    {
        private readonly List<EntityId> _ids = new List<EntityId>();
        public string Name => "test.churn";
        public int Order => 300;
        public int TickInterval => 1;

        public void Tick(SimContext ctx)
        {
            ctx.UnitTypes.TryGetByKey("unit.settler", out var settler);
            int roll = ctx.Random.Range(0, 100);
            if (roll < 40) ctx.Units.Spawn(settler, 1 + roll % 2, ctx.Random.Range(0f, 256f), ctx.Random.Range(0f, 256f));
            ctx.Units.CopyAliveIds(_ids);
            if (_ids.Count > 0 && roll >= 70) ctx.Units.Despawn(_ids[ctx.Random.Range(0, _ids.Count)]);
            foreach (var id in _ids)
            {
                if (!ctx.Units.TryGet(id, out var u)) continue;
                ctx.Units.SetPosition(id, u.X + ctx.Random.Range(-1f, 1f), u.Z + 0.25f, u.Heading + 3f, 1.5f);
            }
        }
    }

    // TEST_PLAN T-7-01, T-7-02, T-7-03, T-7-08, T-7-09, T-7-12, T-7-13, T-7-14, T-7-15, T-7-18, T-7-19, T-7-20 (ohne Unity)
    public class SaveLoadServiceTests
    {
        private string _dir;
        private GameComposition _current;
        private int _replaceCount;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wb-service-tests-" + Guid.NewGuid().ToString("N"));
            _current = null;
            _replaceCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static readonly string[] DefaultTypes = { "unit.settler", "unit.rifleman" };

        private static GameComposition Create(GameStartSettings s, string[] types = null, ExtraSaveable extra = null, bool churn = false)
        {
            var comp = GameComposition.Create(s, ctx =>
            {
                foreach (var key in types ?? DefaultTypes)
                    ctx.UnitTypes.Register(new UnitTypeData { Key = key, DisplayName = key, MaxHp = 100f });
                if (extra != null) ctx.Saveables.Add(extra);
            });
            if (churn) comp.Runner.Register(new ChurnSystem());
            return comp;
        }

        // Neue Service-Instanz = Neustart der Anwendung: nichts im Speicher außer den Dateien.
        private SaveLoadService Service(Func<GameStartSettings, GameComposition> factory, IEnumerable<ISaveMigration> migrations = null)
        {
            return new SaveLoadService(new SaveFileStore(_dir), factory, () => _current, c => { _current = c; _replaceCount++; },
                                       migrations, "0.1.0-test", () => new DateTime(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc));
        }

        private static EntityId SpawnType(GameComposition c, string key, int civ, float x, float z)
        {
            Assert.IsTrue(c.Context.UnitTypes.TryGetByKey(key, out var t), key);
            return c.Context.Units.Spawn(t, civ, x, z);
        }

        private static string KeyOf(GameComposition c, EntityId id)
        {
            Assert.IsTrue(c.Context.Units.TryGet(id, out var v), "Einheit fehlt: " + id);
            return c.Context.Content.GetKey(ContentKind.Unit, v.Type.Value);
        }

        private string FilePath(string slot) => new SaveFileStore(_dir).PathFor(slot);

        private string SectionsChecksum(string slot)
        {
            var root = JObject.Parse(File.ReadAllText(FilePath(slot)));
            return SaveGameSerializer.ComputeChecksum(root["sections"]);
        }

        private void Rewrite(string slot, Action<JObject> edit)
        {
            var root = JObject.Parse(File.ReadAllText(FilePath(slot)));
            edit(root);
            File.WriteAllText(FilePath(slot), SaveCoreTestUtil.Resign(root));
        }

        [Test]
        public void Roundtrip_AfterRestart_RestoresStateGenerationsAndSettings()
        {
            var settings = new GameStartSettings { Seed = 777, WorldWidth = 512, WorldHeight = 384, CivilizationCount = 3 };
            _current = Create(settings);
            var a = SpawnType(_current, "unit.settler", 1, 10.5f, 20.25f);
            var b = SpawnType(_current, "unit.rifleman", 2, -3f, 4f);
            var c = SpawnType(_current, "unit.rifleman", 2, 7f, 8f);
            _current.Context.Units.SetMoveTarget(b, 100f, 200f);
            _current.Context.Units.SetTask(b, new UnitTask { Kind = UnitTaskKind.Attack, TargetEntity = c, TargetX = 7f, TargetZ = 8f });
            _current.Context.Units.SetHp(b, 42.5f);
            _current.Context.Units.Despawn(a);
            var reused = SpawnType(_current, "unit.settler", 1, 0f, 0f);     // nutzt Index von a mit neuer Generation
            Assert.AreEqual(a.Index, reused.Index, "Testannahme: Index wird wiederverwendet");
            for (int i = 0; i < 7; i++) _current.Runner.StepOnce();
            _current.Context.Random.Range(0, 1000);
            _current.Runner.Speed = GameSpeed.x5;
            var original = _current;

            var save = Service(s => Create(s)).Save("slot1");
            Assert.IsTrue(save.Success, save.Detail);

            _current = null;
            var load = Service(s => Create(s)).Load("slot1");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual(1, _replaceCount);
            var loaded = _current;
            Assert.AreNotSame(original, loaded);

            Assert.AreEqual(original.Context.Clock.Tick, loaded.Context.Clock.Tick);
            Assert.AreEqual(original.Context.Random.State, loaded.Context.Random.State);
            Assert.AreEqual(GameSpeed.x5, loaded.Runner.Speed);
            Assert.AreEqual(777UL, loaded.Settings.Seed);
            Assert.AreEqual(512, loaded.Settings.WorldWidth);
            Assert.AreEqual(384, loaded.Settings.WorldHeight);
            Assert.AreEqual(3, loaded.Settings.CivilizationCount);

            Assert.AreEqual(original.Context.Units.Count, loaded.Context.Units.Count);
            Assert.IsFalse(loaded.Context.Units.Exists(a), "Veraltete ID muss nach dem Laden ungültig bleiben");
            Assert.IsTrue(loaded.Context.Units.Exists(reused));
            Assert.IsFalse(loaded.Context.Units.Exists(EntityId.None));
            Assert.IsFalse(loaded.Context.Units.Exists(new EntityId(9999, 1)));
            Assert.IsFalse(loaded.Context.Units.Exists(new EntityId(-1, 1)));

            Assert.IsTrue(loaded.Context.Units.TryGet(b, out var vb));
            Assert.IsTrue(original.Context.Units.TryGet(b, out var ob));
            Assert.AreEqual(ob.X, vb.X); Assert.AreEqual(ob.Z, vb.Z);
            Assert.AreEqual(42.5f, vb.Hp);
            Assert.IsTrue(vb.HasMoveTarget);
            Assert.AreEqual(100f, vb.MoveTargetX); Assert.AreEqual(200f, vb.MoveTargetZ);
            Assert.AreEqual(UnitTaskKind.Attack, vb.Task.Kind);
            Assert.AreEqual(c, vb.Task.TargetEntity, "EntityId im Auftrag muss erhalten bleiben");
            Assert.AreEqual("unit.rifleman", KeyOf(loaded, b));

            // Nächste vergebene ID ist identisch wie ohne Unterbrechung (Freiliste + Generationen exakt).
            var nextOriginal = SpawnType(original, "unit.settler", 1, 0f, 0f);
            var nextLoaded = SpawnType(loaded, "unit.settler", 1, 0f, 0f);
            Assert.AreEqual(nextOriginal, nextLoaded);
        }

        [Test]
        public void RandomState_AboveLongMax_Roundtrips()
        {
            _current = Create(new GameStartSettings { Seed = ulong.MaxValue });
            _current.Context.Random.State = ulong.MaxValue - 5;
            Assert.IsTrue(Service(s => Create(s)).Save("big").Success);
            var load = Service(s => Create(s)).Load("big");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual(ulong.MaxValue - 5, _current.Context.Random.State);
            Assert.AreEqual(ulong.MaxValue, _current.Settings.Seed);
        }

        [TestCase(40, 60)]
        [TestCase(1, 150)]
        public void Determinism_SaveLoadContinue_EqualsUninterrupted(int n, int m)
        {
            var settings = new GameStartSettings { Seed = 4242 };
            var uninterrupted = Create(settings, churn: true);
            for (int i = 0; i < n + m; i++) uninterrupted.Runner.StepOnce();

            _current = Create(settings, churn: true);
            for (int i = 0; i < n; i++) _current.Runner.StepOnce();
            Assert.IsTrue(Service(s => Create(s, churn: true)).Save("mid").Success);
            var load = Service(s => Create(s, churn: true)).Load("mid");
            Assert.IsTrue(load.Success, load.Detail);
            for (int i = 0; i < m; i++) _current.Runner.StepOnce();
            Assert.Greater(_current.Context.Units.Count, 0, "Testannahme: Simulation hat Einheiten erzeugt");
            Assert.IsTrue(Service(s => Create(s, churn: true)).Save("continued").Success);

            _current = uninterrupted;
            Assert.IsTrue(Service(s => Create(s, churn: true)).Save("straight").Success);

            Assert.AreEqual(SectionsChecksum("straight"), SectionsChecksum("continued"));
        }

        [Test]
        public void ContentOrderChanged_UnitsKeepTheirTypes()
        {
            _current = Create(new GameStartSettings(), new[] { "unit.settler", "unit.rifleman" });
            var s1 = SpawnType(_current, "unit.settler", 1, 0, 0);
            var r1 = SpawnType(_current, "unit.rifleman", 1, 1, 1);
            Assert.IsTrue(Service(s => Create(s)).Save("order").Success);

            var reordered = new[] { "unit.medic", "unit.rifleman", "unit.tank", "unit.settler" };
            var load = Service(s => Create(s, reordered)).Load("order");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual("unit.settler", KeyOf(_current, s1));
            Assert.AreEqual("unit.rifleman", KeyOf(_current, r1));
        }

        [Test]
        public void MissingContentKey_IsMissingContent_AndKeepsCurrentState()
        {
            var extra = new ExtraSaveable { State = new ExtraState { Value = 1, UnitKeys = new[] { "unit.settler", "unit.removed" } } };
            _current = Create(new GameStartSettings(), extra: extra);
            Assert.IsTrue(Service(s => Create(s, extra: new ExtraSaveable())).Save("mc").Success);
            var before = _current;

            var load = Service(s => Create(s, extra: new ExtraSaveable())).Load("mc");
            Assert.AreEqual(SaveLoadError.MissingContent, load.Error);
            StringAssert.Contains("unit.removed", load.Detail);
            StringAssert.DoesNotContain("unit.settler", load.Detail);
            Assert.IsFalse(string.IsNullOrEmpty(load.UserMessage));
            Assert.AreSame(before, _current);
            Assert.AreEqual(0, _replaceCount);
        }

        [Test]
        public void SoftReferenceProblem_IsWarning_HardIsInvalidReferences()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable());
            Assert.IsTrue(Service(s => Create(s)).Save("refs").Success);

            var soft = Service(s => Create(s, extra: new ExtraSaveable { ReferenceCheck = (c, r) => r.Warn("Auftrag auf tote Einheit → Idle") })).Load("refs");
            Assert.IsTrue(soft.Success, soft.Detail);
            CollectionAssert.Contains(soft.Report.Warnings, "Auftrag auf tote Einheit → Idle");
            var afterSoft = _current;

            var hard = Service(s => Create(s, extra: new ExtraSaveable { ReferenceCheck = (c, r) => r.Fail("Siedlung gehört Civ 99, die es nicht gibt") })).Load("refs");
            Assert.AreEqual(SaveLoadError.InvalidReferences, hard.Error);
            StringAssert.Contains("Civ 99", hard.Detail);
            Assert.AreSame(afterSoft, _current, "Bei Fehler darf der laufende Zustand nicht ersetzt werden");

            var throwing = Service(s => Create(s, extra: new ExtraSaveable { ReferenceCheck = (c, r) => throw new NullReferenceException("x") })).Load("refs");
            Assert.AreEqual(SaveLoadError.InvalidReferences, throwing.Error);
        }

        [Test]
        public void CorruptedOrTooNewFile_KeepsCurrentState()
        {
            _current = Create(new GameStartSettings());
            Assert.IsTrue(Service(s => Create(s)).Save("c").Success);
            var before = _current;

            var bytes = File.ReadAllBytes(FilePath("c"));
            File.WriteAllBytes(FilePath("c"), new ArraySegment<byte>(bytes, 0, bytes.Length / 2).ToArray());
            var corrupted = Service(s => Create(s)).Load("c");
            Assert.AreEqual(SaveLoadError.Corrupted, corrupted.Error);
            Assert.AreEqual("Der Spielstand ist beschädigt und kann nicht geladen werden.", corrupted.UserMessage);

            File.WriteAllBytes(FilePath("c"), bytes);
            Rewrite("c", r => r["formatVersion"] = 99);
            var tooNew = Service(s => Create(s)).Load("c");
            Assert.AreEqual(SaveLoadError.TooNew, tooNew.Error);

            Assert.AreSame(before, _current);
            Assert.AreEqual(0, _replaceCount);
        }

        [Test]
        public void MissingRequiredSection_IsCorrupted()
        {
            _current = Create(new GameStartSettings());
            Service(s => Create(s)).Save("m");
            Rewrite("m", r => ((JObject)r["sections"]).Remove("units"));
            var load = Service(s => Create(s)).Load("m");
            Assert.AreEqual(SaveLoadError.Corrupted, load.Error);
            StringAssert.Contains("units", load.Detail);
        }

        [Test]
        public void MissingStartSettings_IsCorrupted()
        {
            _current = Create(new GameStartSettings());
            Service(s => Create(s)).Save("m");
            Rewrite("m", r => r.Remove("start"));
            Assert.AreEqual(SaveLoadError.Corrupted, Service(s => Create(s)).Load("m").Error);
        }

        [Test]
        public void UnknownSection_IsIgnoredWithWarning()
        {
            _current = Create(new GameStartSettings());
            Service(s => Create(s)).Save("u");
            Rewrite("u", r => r["sections"]["mod.unknown"] = JObject.Parse("{\"v\":1,\"data\":{}}"));
            var load = Service(s => Create(s)).Load("u");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.IsTrue(load.Report.Warnings.Exists(w => w.Contains("mod.unknown")));
        }

        [Test]
        public void InconsistentUnitData_IsCorrupted_AndKeepsCurrentState()
        {
            _current = Create(new GameStartSettings());
            SpawnType(_current, "unit.settler", 1, 0, 0);
            SpawnType(_current, "unit.settler", 1, 0, 0);
            Service(s => Create(s)).Save("x");
            var before = _current;
            // Zweiter Datensatz zeigt auf denselben Index wie der erste.
            Rewrite("x", r => r["sections"]["units"]["data"]["Units"][1]["Index"] = r["sections"]["units"]["data"]["Units"][0]["Index"]);
            var load = Service(s => Create(s)).Load("x");
            Assert.AreEqual(SaveLoadError.Corrupted, load.Error);
            StringAssert.Contains("units", load.Detail);
            Assert.AreSame(before, _current);
        }

        [Test]
        public void WrongTypeInSection_IsCorrupted()
        {
            _current = Create(new GameStartSettings());
            Service(s => Create(s)).Save("t");
            Rewrite("t", r => r["sections"]["core"]["data"]["Tick"] = "nicht-zahl");
            Assert.AreEqual(SaveLoadError.Corrupted, Service(s => Create(s)).Load("t").Error);
        }

        [Test]
        public void UnknownGameSpeed_LoadsPausedWithWarning()
        {
            _current = Create(new GameStartSettings());
            Service(s => Create(s)).Save("g");
            Rewrite("g", r => r["settings"]["gameSpeed"] = 3);
            var load = Service(s => Create(s)).Load("g");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual(GameSpeed.Paused, _current.Runner.Speed);
            Assert.IsNotEmpty(load.Report.Warnings);
        }

        [Test]
        public void Migration_OlderSection_LoadsAndPreservesOriginalOnNextSave()
        {
            // Spielstand mit test.extra v1 (Feld "old") erzeugen.
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { Version = 1 });
            Service(s => Create(s)).Save("mig");
            Rewrite("mig", r => r["sections"]["test.extra"]["data"] = JObject.Parse("{\"old\":7}"));
            var originalBytes = File.ReadAllBytes(FilePath("mig"));

            var migration = new SaveCoreTestUtil.FuncMigration("test.extra", 1, d => new JObject { ["Value"] = (int)d["old"] * 10 });
            ExtraSaveable loadedExtra = null;
            var service = Service(s => Create(s, extra: loadedExtra = new ExtraSaveable { Version = 2 }), new[] { migration });

            var load = service.Load("mig");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual(70, loadedExtra.State.Value);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(FilePath("mig")), "Laden darf die Quelldatei nicht verändern");

            Assert.IsTrue(service.Save("mig").Success);
            var store = new SaveFileStore(_dir);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.VersionBackupPathFor("mig", 1)), "Original muss als .v1 erhalten bleiben");
            var saved = JObject.Parse(File.ReadAllText(FilePath("mig")));
            Assert.AreEqual(2, (int)saved["sections"]["test.extra"]["v"]);
        }

        [Test]
        public void Migration_Missing_IsUnsupported()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { Version = 1 });
            Service(s => Create(s)).Save("nomig");
            var load = Service(s => Create(s, extra: new ExtraSaveable { Version = 3 })).Load("nomig");
            Assert.AreEqual(SaveLoadError.Unsupported, load.Error);
            Assert.AreEqual(0, _replaceCount);
        }

        [Test]
        public void SectionNewerThanGame_IsTooNew()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { Version = 4 });
            Service(s => Create(s)).Save("new");
            Assert.AreEqual(SaveLoadError.TooNew, Service(s => Create(s, extra: new ExtraSaveable { Version = 2 })).Load("new").Error);
        }

        [Test]
        public void RestoreRejectsValue_IsCorrupted()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { State = new ExtraState { Value = -5 } });
            Service(s => Create(s)).Save("neg");
            var load = Service(s => Create(s, extra: new ExtraSaveable())).Load("neg");
            Assert.AreEqual(SaveLoadError.Corrupted, load.Error);
            StringAssert.Contains("test.extra", load.Detail);
        }

        [Test]
        public void Save_WrongDtoType_IsCaptureFailed_AndWritesNothing()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { CaptureOverride = () => "kein DTO" });
            var save = Service(s => Create(s)).Save("bad");
            Assert.AreEqual(SaveLoadError.CaptureFailed, save.Error);
            Assert.IsFalse(File.Exists(FilePath("bad")));
        }

        [Test]
        public void Save_ThrowingCapture_IsCaptureFailed()
        {
            _current = Create(new GameStartSettings(), extra: new ExtraSaveable { CaptureOverride = () => throw new InvalidOperationException("boom") });
            var save = Service(s => Create(s)).Save("bad");
            Assert.AreEqual(SaveLoadError.CaptureFailed, save.Error);
            StringAssert.Contains("boom", save.Detail);
        }

        private sealed class SaveDuringTickSystem : ISimulationSystem
        {
            public SaveLoadService Service;
            public SaveResult Result;
            public string Name => "test.save-in-tick";
            public int Order => 100;
            public int TickInterval => 1;
            public void Tick(SimContext ctx) => Result = Service.Save("tick");
        }

        [Test]
        public void Save_DuringTick_IsRejected()
        {
            _current = Create(new GameStartSettings());
            var sys = new SaveDuringTickSystem { Service = Service(s => Create(s)) };
            _current.Runner.Register(sys);
            _current.Runner.StepOnce();
            Assert.AreEqual(SaveLoadError.CaptureFailed, sys.Result.Error);
            Assert.IsFalse(File.Exists(FilePath("tick")));
        }

        [Test]
        public void Save_WithoutGame_IsCaptureFailed()
        {
            Assert.AreEqual(SaveLoadError.CaptureFailed, Service(s => Create(s)).Save("x").Error);
        }

        [Test]
        public void Load_MissingSlot_IsNotFound()
        {
            var load = Service(s => Create(s)).Load("nichtda");
            Assert.AreEqual(SaveLoadError.NotFound, load.Error);
            Assert.AreEqual("Dieser Spielstand wurde nicht gefunden.", load.UserMessage);
        }

        [Test]
        public void ListSlots_ShowsReadableAndBrokenSaves()
        {
            _current = Create(new GameStartSettings());
            for (int i = 0; i < 3; i++) _current.Runner.StepOnce();
            var service = Service(s => Create(s));
            service.Save("gut");
            service.Save("kaputt");
            File.WriteAllText(FilePath("kaputt"), "{");

            var slots = service.ListSlots();
            Assert.AreEqual(2, slots.Count);
            var gut = slots[0].Slot == "gut" ? slots[0] : slots[1];
            var kaputt = slots[0].Slot == "kaputt" ? slots[0] : slots[1];
            Assert.IsTrue(gut.Readable);
            Assert.AreEqual(3, gut.SimTick);
            Assert.AreEqual("gut", gut.Name);
            Assert.AreEqual("2026-10-26T01:00:00Z", gut.CreatedUtc);
            Assert.IsFalse(kaputt.Readable);
            Assert.AreEqual(SaveLoadError.Corrupted, kaputt.Error);
        }
    }
}
