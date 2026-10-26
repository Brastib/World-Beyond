using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using WorldBeyond.Application.Save;

namespace WorldBeyond.Tests.EditMode.Save
{
    internal static class SaveCoreTestUtil
    {
        public static SaveGameData Sample()
        {
            return new SaveGameData
            {
                Format = SaveGameData.FormatName,
                FormatVersion = SaveGameData.CurrentFormatVersion,
                GameVersion = "0.1.0-test",
                CreatedUtc = "2026-10-26T01:00:00Z",
                Header = new SaveHeader { Name = "Test", WorldSeed = 12345, WorldWidth = 256, WorldHeight = 256, SimTick = 184200, CivCount = 2, UnitCount = 3 },
                Settings = new SimSettingsDto { GameSpeed = 2, Paused = true },
                Sections = new Dictionary<string, SaveSection>
                {
                    ["clock"] = new SaveSection { Version = 1, Data = JToken.Parse("{\"tick\":184200}") },
                    ["units"] = new SaveSection
                    {
                        Version = 1,
                        Data = JToken.Parse("{\"generations\":[0,3,1],\"alive\":[false,true,true],\"freeList\":[],"
                                            + "\"units\":[{\"index\":1,\"gen\":3,\"type\":\"unit.archer\",\"x\":12.375,\"z\":-0.1,\"hp\":0.30000000000000004,"
                                            + "\"spawnedUtc\":\"2026-10-26T00:59:00Z\"}]}")
                    },
                    ["civs"] = new SaveSection { Version = 1, Data = JToken.Parse("{\"lastIssued\":2,\"civs\":[{\"id\":1,\"name\":\"Ostreich\"},{\"id\":2,\"name\":\"Westmark\"}]}") }
                }
            };
        }

        public static string SampleJson() => new SaveGameSerializer().Serialize(Sample());

        // Ändert ein Feld im JSON und berechnet die Prüfsumme neu (für Tests, die gezielt eine Prüfung erreichen wollen).
        public static string Resign(JObject root)
        {
            root["checksum"] = SaveGameSerializer.ComputeChecksum(root["sections"]);
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public sealed class FuncMigration : ISaveMigration
        {
            private readonly System.Func<JToken, JToken> _f;
            public FuncMigration(string key, int from, System.Func<JToken, JToken> f) { SectionKey = key; FromVersion = from; _f = f; }
            public string SectionKey { get; }
            public int FromVersion { get; }
            public JToken Migrate(JToken data) => _f(data);
        }
    }
}
