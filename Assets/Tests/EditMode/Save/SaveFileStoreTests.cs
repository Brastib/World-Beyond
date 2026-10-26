using System;
using System.IO;
using NUnit.Framework;
using WorldBeyond.Application.Save;

namespace WorldBeyond.Tests.EditMode.Save
{
    // TEST_PLAN T-7-04, T-7-15 (Quelldatei unverändert), T-7-18, Teil von T-7-20 (Datei überlebt neue Instanz)
    public class SaveFileStoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wb-save-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static void Throw(string stage, string at)
        {
            if (stage == at) throw new InjectedFaultException(stage);
        }

        [Test]
        public void WriteThenRead_WithNewInstance_ReturnsSameContent()
        {
            Assert.IsTrue(new SaveFileStore(_dir).TryWrite("slot1", "inhalt-ä", null, out _, out var d), d);
            Assert.IsTrue(new SaveFileStore(_dir).TryRead("slot1", out var content, out _, out _));
            Assert.AreEqual("inhalt-ä", content);
            Assert.IsFalse(File.Exists(new SaveFileStore(_dir).TempPathFor("slot1")), ".tmp muss nach Erfolg weg sein");
        }

        [Test]
        public void SecondWrite_KeepsPreviousAsBak()
        {
            var store = new SaveFileStore(_dir);
            store.TryWrite("s", "alt", null, out _, out _);
            store.TryWrite("s", "neu", null, out _, out _);
            Assert.AreEqual("neu", File.ReadAllText(store.PathFor("s")));
            Assert.IsTrue(store.TryReadBackup("s", out var bak, out _, out _));
            Assert.AreEqual("alt", bak);
        }

        [TestCase("afterTempWrite")]
        [TestCase("beforeReplace")]
        public void AbortDuringSave_LeavesExistingSaveIntact(string stage)
        {
            new SaveFileStore(_dir).TryWrite("s", "gueltig", null, out _, out _);
            var faulty = new SaveFileStore(_dir, st => Throw(st, stage));
            Assert.IsFalse(faulty.TryWrite("s", "halb", null, out var error, out _));
            Assert.AreEqual(SaveLoadError.IoError, error);
            Assert.IsTrue(new SaveFileStore(_dir).TryRead("s", out var content, out _, out _));
            Assert.AreEqual("gueltig", content);
        }

        [Test]
        public void AbortOnFirstSave_LeavesNoFinalFile()
        {
            var faulty = new SaveFileStore(_dir, st => Throw(st, "beforeReplace"));
            Assert.IsFalse(faulty.TryWrite("s", "x", null, out _, out _));
            Assert.IsFalse(new SaveFileStore(_dir).TryRead("s", out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.NotFound, error);
        }

        [Test]
        public void LeftoverTmpFromCrash_DoesNotBreakNextSave()
        {
            var store = new SaveFileStore(_dir);
            Directory.CreateDirectory(_dir);
            File.WriteAllText(store.TempPathFor("s"), "reste");
            Assert.IsTrue(store.TryWrite("s", "neu", null, out _, out var d), d);
            Assert.AreEqual("neu", File.ReadAllText(store.PathFor("s")));
        }

        [Test]
        public void PreserveVersion_KeepsOriginalOnce()
        {
            var store = new SaveFileStore(_dir);
            store.TryWrite("s", "version1-original", null, out _, out _);
            var originalBytes = File.ReadAllBytes(store.PathFor("s"));

            Assert.IsTrue(store.TryWrite("s", "migriert-a", 1, out _, out _));
            Assert.IsTrue(store.TryWrite("s", "migriert-b", 1, out _, out _));

            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.VersionBackupPathFor("s", 1)), "v1-Sicherung muss das Original bleiben");
            Assert.AreEqual("migriert-b", File.ReadAllText(store.PathFor("s")));
        }

        [Test]
        public void Read_DoesNotModifyFile()
        {
            var store = new SaveFileStore(_dir);
            store.TryWrite("s", SaveCoreTestUtil.SampleJson(), null, out _, out _);
            var path = store.PathFor("s");
            var bytes = File.ReadAllBytes(path);
            var time = File.GetLastWriteTimeUtc(path);

            store.TryRead("s", out var json, out _, out _);
            new SaveGameSerializer().TryDeserialize(json, out _, out _, out _);

            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            Assert.AreEqual(time, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void MissingSlot_IsNotFound()
        {
            Assert.IsFalse(new SaveFileStore(_dir).TryRead("gibtsnicht", out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.NotFound, error);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("../boese")]
        [TestCase("a/b")]
        [TestCase("a.b")]
        [TestCase("C:\\x")]
        [TestCase("12345678901234567890123456789012345678901234567890123456789012345")]
        public void InvalidSlotName_IsRejected(string slot)
        {
            var store = new SaveFileStore(_dir);
            Assert.IsFalse(store.TryWrite(slot, "x", null, out var error, out _));
            Assert.AreEqual(SaveLoadError.IoError, error);
            Assert.IsFalse(store.TryRead(slot, out _, out _, out _));
        }

        [Test]
        public void ListSlots_ExcludesBackups()
        {
            var store = new SaveFileStore(_dir);
            store.TryWrite("b", "1", null, out _, out _);
            store.TryWrite("a", "1", null, out _, out _);
            store.TryWrite("a", "2", 1, out _, out _);
            CollectionAssert.AreEqual(new[] { "a", "b" }, store.ListSlots());
        }
    }
}
