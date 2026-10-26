using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldBeyond.Application.Save;
using static WorldBeyond.Tests.EditMode.Save.SaveCoreTestUtil;

namespace WorldBeyond.Tests.EditMode.Save
{
    // TEST_PLAN T-7-15, T-7-16 (Migrations-Teil)
    public class SaveGameMigratorTests
    {
        private static SaveGameData SampleWithUnitsAt(int version)
        {
            var data = Sample();
            data.Sections["units"].Version = version;
            data.Sections["units"].Data = JToken.Parse("{\"list\":[{\"t\":\"unit.archer\"}]}");
            return data;
        }

        private static readonly ISaveMigration[] UnitChain =
        {
            // v1 → v2: "list" heißt jetzt "units"
            new FuncMigration("units", 1, d => { var o = (JObject)d; o["units"] = o["list"]; o.Remove("list"); return o; }),
            // v2 → v3: neues Feld "hp" mit Standardwert
            new FuncMigration("units", 2, d => { foreach (var u in d["units"]) u["hp"] = 1.0; return d; }),
        };

        [Test]
        public void Chain_MigratesStepByStep_AndLeavesSourceUnchanged()
        {
            var source = SampleWithUnitsAt(1);
            var before = new SaveGameSerializer().Serialize(source);
            var migrator = new SaveGameMigrator(UnitChain);

            Assert.IsTrue(migrator.TryMigrate(source, new Dictionary<string, int> { ["units"] = 3, ["clock"] = 1, ["civs"] = 1 },
                out var migrated, out var error, out var detail), detail);
            Assert.AreEqual(SaveLoadError.None, error);
            Assert.AreEqual(3, migrated.Sections["units"].Version);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse("{\"units\":[{\"t\":\"unit.archer\",\"hp\":1.0}]}"), migrated.Sections["units"].Data));

            Assert.AreEqual(before, new SaveGameSerializer().Serialize(source), "Quelle darf nicht verändert werden");
            Assert.AreEqual(1, source.Sections["units"].Version);
        }

        [Test]
        public void Chain_FromMiddleVersion_RunsOnlyRemainingSteps()
        {
            var source = SampleWithUnitsAt(2);
            source.Sections["units"].Data = JToken.Parse("{\"units\":[{\"t\":\"unit.archer\"}]}");
            Assert.IsTrue(new SaveGameMigrator(UnitChain).TryMigrate(source, new Dictionary<string, int> { ["units"] = 3 }, out var migrated, out _, out var detail), detail);
            Assert.AreEqual(1.0, (double)migrated.Sections["units"].Data["units"][0]["hp"]);
        }

        [Test]
        public void CurrentVersion_IsUnchanged()
        {
            var source = Sample();
            Assert.IsTrue(new SaveGameMigrator(null).TryMigrate(source, new Dictionary<string, int> { ["units"] = 1, ["clock"] = 1, ["civs"] = 1 }, out var migrated, out _, out _));
            Assert.AreEqual(new SaveGameSerializer().Serialize(source), new SaveGameSerializer().Serialize(migrated));
        }

        [Test]
        public void GapInChain_IsUnsupported_AndNoStepRuns()
        {
            int calls = 0;
            var migrator = new SaveGameMigrator(new ISaveMigration[]
            {
                new FuncMigration("units", 1, d => { calls++; return d; }),
                // v2 → v3 fehlt
                new FuncMigration("units", 3, d => { calls++; return d; }),
            });
            Assert.IsFalse(migrator.TryMigrate(SampleWithUnitsAt(1), new Dictionary<string, int> { ["units"] = 4 }, out var migrated, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Unsupported, error);
            StringAssert.Contains("v2", detail);
            Assert.IsNull(migrated);
            Assert.AreEqual(0, calls, "Bei einer Lücke darf kein Schritt laufen");
        }

        [Test]
        public void NoMigrationAtAll_ForOlderSection_IsUnsupported()
        {
            Assert.IsFalse(new SaveGameMigrator(null).TryMigrate(SampleWithUnitsAt(1), new Dictionary<string, int> { ["units"] = 2 }, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Unsupported, error);
        }

        [Test]
        public void NewerSection_IsTooNew()
        {
            Assert.IsFalse(new SaveGameMigrator(UnitChain).TryMigrate(SampleWithUnitsAt(5), new Dictionary<string, int> { ["units"] = 3 }, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.TooNew, error);
        }

        [Test]
        public void InvalidSectionVersion_IsCorrupted()
        {
            Assert.IsFalse(new SaveGameMigrator(UnitChain).TryMigrate(SampleWithUnitsAt(0), new Dictionary<string, int> { ["units"] = 3 }, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
        }

        [Test]
        public void UnknownSection_IsLeftUntouched()
        {
            var source = Sample();
            source.Sections["mod.foo"] = new SaveSection { Version = 9, Data = JToken.Parse("{\"x\":1}") };
            Assert.IsTrue(new SaveGameMigrator(null).TryMigrate(source, new Dictionary<string, int> { ["units"] = 1 }, out var migrated, out _, out _));
            Assert.AreEqual(9, migrated.Sections["mod.foo"].Version);
        }

        [Test]
        public void ThrowingStep_IsCorrupted()
        {
            var migrator = new SaveGameMigrator(new ISaveMigration[] { new FuncMigration("units", 1, d => throw new InvalidOperationException("kaputt")) });
            Assert.IsFalse(migrator.TryMigrate(SampleWithUnitsAt(1), new Dictionary<string, int> { ["units"] = 2 }, out _, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
            StringAssert.Contains("kaputt", detail);
        }

        [Test]
        public void NullResult_IsCorrupted()
        {
            var migrator = new SaveGameMigrator(new ISaveMigration[] { new FuncMigration("units", 1, d => null) });
            Assert.IsFalse(migrator.TryMigrate(SampleWithUnitsAt(1), new Dictionary<string, int> { ["units"] = 2 }, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
        }

        [Test]
        public void StepCannotMutateSourceThroughReference()
        {
            var source = SampleWithUnitsAt(1);
            var migrator = new SaveGameMigrator(new ISaveMigration[] { new FuncMigration("units", 1, d => { ((JObject)d)["evil"] = 1; return d; }) });
            Assert.IsTrue(migrator.TryMigrate(source, new Dictionary<string, int> { ["units"] = 2 }, out _, out _, out _));
            Assert.IsNull(source.Sections["units"].Data["evil"]);
        }

        [Test]
        public void DuplicateStep_IsRejectedAtStartup()
        {
            Assert.Throws<ArgumentException>(() => new SaveGameMigrator(new ISaveMigration[]
            {
                new FuncMigration("units", 1, d => d),
                new FuncMigration("units", 1, d => d),
            }));
        }

        [Test]
        public void Frame_CurrentVersion_IsNoOp()
        {
            var root = JObject.Parse(SampleJson());
            Assert.IsTrue(new SaveGameMigrator(null).TryMigrateFrame(root, out var migrated, out _, out _));
            Assert.IsTrue(JToken.DeepEquals(root, migrated));
            Assert.AreNotSame(root, migrated);
        }

        [Test]
        public void Frame_NewerVersion_IsTooNew()
        {
            var root = JObject.Parse(SampleJson());
            root["formatVersion"] = SaveGameData.CurrentFormatVersion + 1;
            Assert.IsFalse(new SaveGameMigrator(null).TryMigrateFrame(root, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.TooNew, error);
        }

        [Test]
        public void UnmigratedFrame_IsNeverReadAsCurrent()
        {
            var source = Sample();
            source.FormatVersion = 0;
            Assert.IsFalse(new SaveGameMigrator(null).TryMigrate(source, new Dictionary<string, int>(), out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Unsupported, error);
        }
    }
}
