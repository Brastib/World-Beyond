using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldBeyond.Application.Save;
using static WorldBeyond.Tests.EditMode.Save.SaveCoreTestUtil;

namespace WorldBeyond.Tests.EditMode.Save
{
    // TEST_PLAN T-7-01 (Rahmen-Teil), T-7-02, T-7-03, T-7-16, T-7-17
    public class SaveGameSerializerTests
    {
        private readonly SaveGameSerializer _serializer = new SaveGameSerializer();

        [Test]
        public void Roundtrip_KeepsAllFieldsAndChecksum()
        {
            var json = SampleJson();
            Assert.IsTrue(_serializer.TryDeserialize(json, out var data, out var error, out var detail), detail);
            Assert.AreEqual(SaveLoadError.None, error);
            Assert.AreEqual(12345, data.Header.WorldSeed);
            Assert.AreEqual(184200, data.Header.SimTick);
            Assert.AreEqual(2, data.Settings.GameSpeed);
            Assert.IsTrue(data.Settings.Paused);
            Assert.AreEqual(3, data.Sections.Count);
            Assert.AreEqual("unit.archer", (string)data.Sections["units"].Data["units"][0]["type"]);
            Assert.AreEqual(0.30000000000000004, (double)data.Sections["units"].Data["units"][0]["hp"]);
            Assert.AreEqual(JTokenType.String, data.Sections["units"].Data["units"][0]["spawnedUtc"].Type, "Datumsstrings dürfen nicht in DateTime umgewandelt werden");

            var again = _serializer.Serialize(data);
            Assert.AreEqual(json, again, "Zweites Serialisieren muss identisch sein");
        }

        [Test]
        public void Checksum_IndependentOfPropertyOrder()
        {
            var a = JToken.Parse("{\"b\":{\"y\":1,\"x\":[1,{\"q\":2,\"p\":3}]},\"a\":1.5}");
            var b = JToken.Parse("{\"a\":1.5,\"b\":{\"x\":[1,{\"p\":3,\"q\":2}],\"y\":1}}");
            Assert.AreEqual(SaveGameSerializer.ComputeChecksum(a), SaveGameSerializer.ComputeChecksum(b));
        }

        // Die Prüfsumme wird seit Phase 7 (Performance) ohne Baumkopie geschrieben. Sie muss mit der ursprünglichen Bildung
        // (sortierter Klon, ToString ohne Leerraum, SHA-256) übereinstimmen, sonst wären bestehende Spielstände "beschädigt".
        [TestCase("{\"b\":{\"y\":1,\"x\":[1,{\"q\":2,\"p\":3}]},\"a\":1.5}")]
        [TestCase("{\"s\":\"äöü \\\"x\\\" \\u0001 </tag> 漢字\",\"n\":null,\"t\":true,\"f\":-0.30000000000000004,\"e\":1e300,\"i\":-9223372036854775808,\"u\":18446744073709551615}")]
        [TestCase("[[],{},[{}],\"2026-10-25T20:00:00Z\",0.1,1.0,100]")]
        [TestCase("{\"B\":1,\"a\":2,\"_\":3,\"Ä\":4,\"aa\":5,\"\":6}")]
        public void Checksum_MatchesOriginalCanonicalForm(string json)
        {
            var token = Parse(json);
            Assert.AreEqual(ReferenceChecksum(token), SaveGameSerializer.ComputeChecksum(token));
        }

        [Test]
        public void FileChecksum_MatchesOriginalCanonicalForm()
        {
            var root = (JObject)Parse(SampleJson());
            var withoutChecksum = (JObject)root.DeepClone();
            withoutChecksum.Remove("checksum");
            Assert.AreEqual(ReferenceChecksum(withoutChecksum), SaveGameSerializer.ComputeFileChecksum(root));
            Assert.AreEqual((string)root["checksum"], SaveGameSerializer.ComputeFileChecksum(root));
        }

        private static JToken Parse(string json)
        {
            using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                return JToken.ReadFrom(reader);
        }

        private static string ReferenceChecksum(JToken token)
        {
            JToken Canon(JToken t)
            {
                if (t is JObject o)
                {
                    var sorted = new JObject();
                    foreach (var p in System.Linq.Enumerable.OrderBy(o.Properties(), p => p.Name, System.StringComparer.Ordinal)) sorted.Add(p.Name, Canon(p.Value));
                    return sorted;
                }
                if (t is JArray a) return new JArray(System.Linq.Enumerable.Select(a, Canon));
                return t.DeepClone();
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Canon(token).ToString(Newtonsoft.Json.Formatting.None)));
                return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        [Test]
        public void Checksum_ChangesWhenArrayOrderChanges()
        {
            Assert.AreNotEqual(SaveGameSerializer.ComputeChecksum(JToken.Parse("[1,2]")), SaveGameSerializer.ComputeChecksum(JToken.Parse("[2,1]")));
        }

        [TestCase("", TestName = "Corrupted_Empty")]
        [TestCase("   \n ", TestName = "Corrupted_Whitespace")]
        [TestCase("not json", TestName = "Corrupted_NotJson")]
        [TestCase("[1,2,3]", TestName = "Corrupted_ArrayRoot")]
        [TestCase("{\"format\":\"Other\",\"formatVersion\":1,\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_WrongFormat")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_MissingVersion")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"formatVersion\":\"1\",\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_VersionIsString")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"formatVersion\":0,\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_VersionZero")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"formatVersion\":-3,\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_VersionNegative")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"formatVersion\":1.5,\"sections\":{},\"checksum\":\"x\"}", TestName = "Corrupted_VersionFloat")]
        [TestCase("{\"format\":\"WorldBeyondSave\",\"formatVersion\":1,\"checksum\":\"x\"}", TestName = "Corrupted_MissingSections")]
        public void InvalidFrame_IsCorrupted(string json)
        {
            Assert.IsFalse(_serializer.TryDeserialize(json, out var data, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Corrupted, error, detail);
            Assert.IsNull(data);
            Assert.IsFalse(string.IsNullOrEmpty(detail));
        }

        [Test]
        public void Truncated_IsCorrupted()
        {
            var json = SampleJson();
            foreach (var len in new[] { 1, json.Length / 3, json.Length / 2, json.Length - 1 })
            {
                Assert.IsFalse(_serializer.TryDeserialize(json.Substring(0, len), out _, out var error, out _), "Länge " + len);
                Assert.AreEqual(SaveLoadError.Corrupted, error, "Länge " + len);
            }
        }

        [Test]
        public void TrailingGarbage_IsCorrupted()
        {
            Assert.IsFalse(_serializer.TryDeserialize(SampleJson() + "{}", out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
        }

        [Test]
        public void TamperedValue_FailsChecksum()
        {
            var json = SampleJson().Replace("Ostreich", "Ostreick");
            Assert.IsFalse(_serializer.TryDeserialize(json, out _, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
            StringAssert.Contains("Prüfsumme", detail);
        }

        [TestCase("header", "worldSeed", 999)]
        [TestCase("header", "simTick", 1)]
        [TestCase("settings", "gameSpeed", 10)]
        [TestCase("settings", "paused", false)]
        public void TamperedFrameField_FailsChecksum(string obj, string field, object value)
        {
            var root = JObject.Parse(SampleJson());
            root[obj][field] = JToken.FromObject(value);
            Assert.IsFalse(_serializer.TryDeserialize(root.ToString(), out _, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Corrupted, error, detail);
        }

        [TestCase("null", TestName = "Map_SectionsNull")]
        [TestCase("[]", TestName = "Map_SectionsArray")]
        [TestCase("5", TestName = "Map_SectionsNumber")]
        public void TryMap_BrokenSectionsAfterFrameMigration_IsCorrupted(string sectionsJson)
        {
            // Simuliert eine fehlerhafte $frame-Migration: TryMap bekommt einen ungültigen Rahmen.
            var root = JObject.Parse(SampleJson());
            root["sections"] = JToken.Parse(sectionsJson);
            Assert.IsFalse(_serializer.TryMap(root, out var data, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
            Assert.IsNull(data);
        }

        [Test]
        public void TryMap_MissingSections_IsCorrupted()
        {
            var root = JObject.Parse(SampleJson());
            root.Remove("sections");
            Assert.IsFalse(_serializer.TryMap(root, out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
        }

        [Test]
        public void MissingChecksum_IsCorrupted()
        {
            var root = JObject.Parse(SampleJson());
            root.Remove("checksum");
            Assert.IsFalse(_serializer.TryDeserialize(root.ToString(), out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.Corrupted, error);
        }

        [Test]
        public void NewerFormatVersion_IsTooNew_EvenWithForeignChecksum()
        {
            var root = JObject.Parse(SampleJson());
            root["formatVersion"] = SaveGameData.CurrentFormatVersion + 1;
            root["checksum"] = "anders-berechnet";
            Assert.IsFalse(_serializer.TryDeserialize(root.ToString(), out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.TooNew, error);
        }

        [Test]
        public void HugeFormatVersion_IsTooNew()
        {
            var root = JObject.Parse(SampleJson());
            root["formatVersion"] = long.MaxValue;
            Assert.IsFalse(_serializer.TryDeserialize(root.ToString(), out _, out var error, out _));
            Assert.AreEqual(SaveLoadError.TooNew, error);
        }

        [TestCase("{\"data\":{}}", TestName = "Section_MissingVersion")]
        [TestCase("{\"v\":0,\"data\":{}}", TestName = "Section_VersionZero")]
        [TestCase("{\"v\":\"1\",\"data\":{}}", TestName = "Section_VersionString")]
        [TestCase("{\"v\":1}", TestName = "Section_MissingData")]
        [TestCase("42", TestName = "Section_NotObject")]
        public void InvalidSection_IsCorrupted(string sectionJson)
        {
            var root = JObject.Parse(SampleJson());
            root["sections"]["broken"] = JToken.Parse(sectionJson);
            Assert.IsFalse(_serializer.TryDeserialize(Resign(root), out _, out var error, out var detail));
            Assert.AreEqual(SaveLoadError.Corrupted, error, detail);
            StringAssert.Contains("broken", detail);
        }
    }
}
