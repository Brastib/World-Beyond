using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldBeyond.Application.Save
{
    // Wurzel-Datenmodell eines Spielstands (ARCHITECTURE §11.3, SYSTEM_INTERFACES §11).
    public sealed class SaveGameData
    {
        public const string FormatName = "WorldBeyondSave";
        public const int CurrentFormatVersion = 1;

        [JsonProperty("format")] public string Format;
        [JsonProperty("formatVersion")] public int FormatVersion;
        [JsonProperty("gameVersion")] public string GameVersion;
        [JsonProperty("createdUtc")] public string CreatedUtc;
        [JsonProperty("header")] public SaveHeader Header;
        [JsonProperty("start")] public StartSettingsDto Start;      // zum Neuaufbau der leeren Simulation beim Laden
        [JsonProperty("settings")] public SimSettingsDto Settings;
        [JsonProperty("sections")] public Dictionary<string, SaveSection> Sections = new Dictionary<string, SaveSection>();
        [JsonProperty("checksum")] public string Checksum;

        // Neuer Rahmen und neue SaveSection-Objekte; die JSON-Daten der Sektionen werden geteilt, nicht kopiert.
        public SaveGameData ShallowCopyWithSections()
        {
            var copy = (SaveGameData)MemberwiseClone();
            if (Sections != null)
            {
                copy.Sections = new Dictionary<string, SaveSection>(Sections.Count);
                foreach (var pair in Sections)
                    copy.Sections[pair.Key] = pair.Value == null ? null : new SaveSection { Version = pair.Value.Version, Data = pair.Value.Data };
            }
            return copy;
        }

        public SaveGameData DeepCopy()
        {
            return JObject.FromObject(this, SaveGameSerializer.CreateJsonSerializer()).ToObject<SaveGameData>(SaveGameSerializer.CreateJsonSerializer());
        }
    }

    public sealed class SaveSection
    {
        [JsonProperty("v")] public int Version;      // = ISaveable.SectionVersion beim Speichern
        [JsonProperty("data")] public JToken Data;   // roh, damit Migrationen ohne DTO-Typ arbeiten können
    }

    // Für die Ladeliste; wird gelesen, ohne die Sektionen auszuwerten.
    public sealed class SaveHeader
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("worldSeed")] public ulong WorldSeed;
        [JsonProperty("worldWidth")] public int WorldWidth;
        [JsonProperty("worldHeight")] public int WorldHeight;
        [JsonProperty("simTick")] public long SimTick;
        [JsonProperty("civCount")] public int CivCount;
        [JsonProperty("unitCount")] public int UnitCount;
    }

    // Spiegel von GameStartSettings ohne Abhängigkeit von dessen Feldnamen.
    public sealed class StartSettingsDto
    {
        [JsonProperty("seed")] public ulong Seed;
        [JsonProperty("worldWidth")] public int WorldWidth;
        [JsonProperty("worldHeight")] public int WorldHeight;
        [JsonProperty("civilizationCount")] public int CivilizationCount;
    }

    public sealed class SimSettingsDto
    {
        [JsonProperty("gameSpeed")] public int GameSpeed;   // Wert von GameSpeed (explizite Enum-Werte)
        [JsonProperty("paused")] public bool Paused;
        // Weitere simulationsrelevante Einstellungen nur anhängen.
    }
}
