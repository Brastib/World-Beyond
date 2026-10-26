using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldBeyond.Application.Save
{
    // JSON ↔ SaveGameData. Wirft nach außen keine Exceptions beim Lesen, sondern meldet SaveLoadError.
    // Ablauf beim Laden: TryParseFrame (Datei, Format, Version, Prüfsumme) → ggf. Rahmen-Migration → TryMap.
    public sealed class SaveGameSerializer
    {
        public static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                // Strings wie "2026-10-25T20:00:00Z" bleiben Strings; sonst ändert sich die kanonische Form und damit die Prüfsumme.
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None
            };
        }

        public static JsonSerializer CreateJsonSerializer() => JsonSerializer.Create(CreateSettings());

        public string Serialize(SaveGameData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var root = JObject.FromObject(data, CreateJsonSerializer());
            var checksum = ComputeChecksum(root["sections"]);
            root["checksum"] = checksum;
            data.Checksum = checksum;
            return root.ToString(Formatting.None);
        }

        public bool TryDeserialize(string json, out SaveGameData data, out SaveLoadError error, out string detail)
        {
            data = null;
            if (!TryParseFrame(json, out var root, out error, out detail)) return false;
            return TryMap(root, out data, out error, out detail);
        }

        // Schritt 1 und 2 aus ARCHITECTURE §11.5: Datei/Metadaten und Formatversion.
        // TooNew wird vor der Prüfsumme gemeldet, weil eine neuere Version die Prüfsumme anders bilden könnte.
        public bool TryParseFrame(string json, out JObject root, out SaveLoadError error, out string detail)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json)) return Fail(SaveLoadError.Corrupted, "Datei ist leer.", out error, out detail);

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                {
                    var token = JToken.ReadFrom(reader);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                            return Fail(SaveLoadError.Corrupted, "Zusätzliche Daten nach dem JSON-Objekt.", out error, out detail);
                    }
                    root = token as JObject;
                }
            }
            catch (JsonException ex)
            {
                return Fail(SaveLoadError.Corrupted, "Kein gültiges JSON: " + ex.Message, out error, out detail);
            }

            if (root == null) return Fail(SaveLoadError.Corrupted, "Wurzel ist kein JSON-Objekt.", out error, out detail);

            if (root["format"]?.Type != JTokenType.String || (string)root["format"] != SaveGameData.FormatName)
                return Fail(SaveLoadError.Corrupted, "Feld 'format' fehlt oder ist kein World-Beyond-Spielstand.", out error, out detail);

            var versionToken = root["formatVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                return Fail(SaveLoadError.Corrupted, "Feld 'formatVersion' fehlt oder ist keine Ganzzahl.", out error, out detail);
            long version = (long)versionToken;
            if (version < 1) return Fail(SaveLoadError.Corrupted, "Ungültige formatVersion " + version + ".", out error, out detail);
            if (version > SaveGameData.CurrentFormatVersion)
                return Fail(SaveLoadError.TooNew, "formatVersion " + version + " ist neuer als die unterstützte Version " + SaveGameData.CurrentFormatVersion + ".", out error, out detail);

            var sections = root["sections"];
            if (sections == null || sections.Type != JTokenType.Object)
                return Fail(SaveLoadError.Corrupted, "Feld 'sections' fehlt.", out error, out detail);

            var checksumToken = root["checksum"];
            if (checksumToken == null || checksumToken.Type != JTokenType.String)
                return Fail(SaveLoadError.Corrupted, "Prüfsumme fehlt.", out error, out detail);
            if (!string.Equals((string)checksumToken, ComputeChecksum(sections), StringComparison.Ordinal))
                return Fail(SaveLoadError.Corrupted, "Prüfsumme stimmt nicht. Die Datei wurde verändert oder ist beschädigt.", out error, out detail);

            error = SaveLoadError.None;
            detail = null;
            return true;
        }

        // Rahmen der aktuellen formatVersion → SaveGameData. Prüft die Struktur jeder Sektion.
        public bool TryMap(JObject root, out SaveGameData data, out SaveLoadError error, out string detail)
        {
            data = null;
            if (root == null) return Fail(SaveLoadError.Corrupted, "Kein Rahmen.", out error, out detail);

            foreach (var property in ((JObject)root["sections"]).Properties())
            {
                if (string.IsNullOrEmpty(property.Name))
                    return Fail(SaveLoadError.Corrupted, "Sektion ohne Namen.", out error, out detail);
                if (!(property.Value is JObject section))
                    return Fail(SaveLoadError.Corrupted, "Sektion '" + property.Name + "' ist kein Objekt.", out error, out detail);
                var v = section["v"];
                if (v == null || v.Type != JTokenType.Integer || (long)v < 1 || (long)v > int.MaxValue)
                    return Fail(SaveLoadError.Corrupted, "Sektion '" + property.Name + "' hat keine gültige Version.", out error, out detail);
                if (section["data"] == null)
                    return Fail(SaveLoadError.Corrupted, "Sektion '" + property.Name + "' hat keine Daten.", out error, out detail);
            }

            try
            {
                data = root.ToObject<SaveGameData>(CreateJsonSerializer());
            }
            catch (JsonException ex)
            {
                return Fail(SaveLoadError.Corrupted, "Rahmen nicht lesbar: " + ex.Message, out error, out detail);
            }

            error = SaveLoadError.None;
            detail = null;
            return true;
        }

        public static string ComputeChecksum(SaveGameData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return ComputeChecksum(JObject.FromObject(data, CreateJsonSerializer())["sections"]);
        }

        // SHA-256 über die kanonische Form (Objekteigenschaften rekursiv nach Ordinal sortiert, ohne Leerraum).
        public static string ComputeChecksum(JToken sections)
        {
            var canonical = Canonicalize(sections ?? JValue.CreateNull()).ToString(Formatting.None);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    var sorted = new JObject();
                    foreach (var p in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                        sorted.Add(p.Name, Canonicalize(p.Value));
                    return sorted;
                case JArray arr:
                    return new JArray(arr.Select(Canonicalize));
                default:
                    return token.DeepClone();
            }
        }

        private static bool Fail(SaveLoadError e, string d, out SaveLoadError error, out string detail)
        {
            error = e;
            detail = d;
            return false;
        }
    }
}
