using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace WorldBeyond.Application.Save
{
    // Ein Migrationsschritt v → v+1. Die Fachsysteme schreiben ihre Schritte selbst.
    public interface ISaveMigration
    {
        string SectionKey { get; }      // SaveKey der Sektion, oder SaveGameMigrator.FrameKey für den Rahmen
        int FromVersion { get; }        // ToVersion = FromVersion + 1
        JToken Migrate(JToken data);    // bekommt eine Kopie; darf sie verändern und zurückgeben
    }

    // Migriert Rahmen und Sektionen schrittweise auf die laufende Version (ARCHITECTURE §11.5 Schritt 2–3).
    // Arbeitet ausschließlich auf Kopien; die Eingabe bleibt unverändert.
    public sealed class SaveGameMigrator
    {
        public const string FrameKey = "$frame";

        private readonly Dictionary<(string key, int from), ISaveMigration> _steps = new Dictionary<(string, int), ISaveMigration>();

        public SaveGameMigrator(IEnumerable<ISaveMigration> migrations)
        {
            if (migrations == null) return;
            foreach (var m in migrations)
            {
                if (m == null) throw new ArgumentException("Migration ist null.");
                if (string.IsNullOrEmpty(m.SectionKey)) throw new ArgumentException("Migration ohne SectionKey.");
                if (m.FromVersion < 1) throw new ArgumentException("Migration " + m.SectionKey + " hat FromVersion < 1.");
                var id = (m.SectionKey, m.FromVersion);
                if (_steps.ContainsKey(id)) throw new ArgumentException("Doppelte Migration " + m.SectionKey + " v" + m.FromVersion + ".");
                _steps.Add(id, m);
            }
        }

        // Rahmen-Migration auf dem rohen JSON, vor SaveGameSerializer.TryMap.
        public bool TryMigrateFrame(JObject root, out JObject migrated, out SaveLoadError error, out string detail)
        {
            migrated = null;
            if (root == null) return Fail(SaveLoadError.Corrupted, "Kein Rahmen.", out error, out detail);
            var versionToken = root["formatVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                return Fail(SaveLoadError.Corrupted, "formatVersion fehlt.", out error, out detail);

            long version = (long)versionToken;
            if (version < 1) return Fail(SaveLoadError.Corrupted, "Ungültige formatVersion " + version + ".", out error, out detail);
            if (version > SaveGameData.CurrentFormatVersion)
                return Fail(SaveLoadError.TooNew, "formatVersion " + version + " ist neuer als " + SaveGameData.CurrentFormatVersion + ".", out error, out detail);

            JToken current = root.DeepClone();
            if (!TryRunChain(FrameKey, (int)version,SaveGameData.CurrentFormatVersion, ref current, out error, out detail)) return false;
            if (!(current is JObject obj)) return Fail(SaveLoadError.Corrupted, "Rahmen-Migration lieferte kein Objekt.", out error, out detail);

            obj["formatVersion"] = SaveGameData.CurrentFormatVersion;
            migrated = obj;
            return true;
        }

        // currentVersions: SectionVersion je SaveKey der laufenden Spielversion.
        // Sektionen ohne Eintrag in currentVersions bleiben unverändert (der Service meldet sie als unbekannt).
        public bool TryMigrate(SaveGameData source, IReadOnlyDictionary<string, int> currentVersions,
                               out SaveGameData migrated, out SaveLoadError error, out string detail)
        {
            migrated = null;
            if (source == null) return Fail(SaveLoadError.Corrupted, "Kein Spielstand.", out error, out detail);
            if (currentVersions == null) throw new ArgumentNullException(nameof(currentVersions));
            if (source.FormatVersion != SaveGameData.CurrentFormatVersion)
                return Fail(SaveLoadError.Unsupported, "Rahmen ist nicht migriert (formatVersion " + source.FormatVersion + ").", out error, out detail);

            var copy = source.DeepCopy();
            if (copy.Sections == null) return Fail(SaveLoadError.Corrupted, "Sektionen fehlen.", out error, out detail);

            foreach (var pair in copy.Sections)
            {
                var section = pair.Value;
                if (section == null) return Fail(SaveLoadError.Corrupted, "Sektion '" + pair.Key + "' ist leer.", out error, out detail);
                if (!currentVersions.TryGetValue(pair.Key, out int target)) continue;
                if (section.Version < 1)
                    return Fail(SaveLoadError.Corrupted, "Sektion '" + pair.Key + "' hat ungültige Version " + section.Version + ".", out error, out detail);
                if (section.Version > target)
                    return Fail(SaveLoadError.TooNew, "Sektion '" + pair.Key + "' v" + section.Version + " ist neuer als v" + target + ".", out error, out detail);

                var data = section.Data;
                if (!TryRunChain(pair.Key, section.Version, target, ref data, out error, out detail)) return false;
                section.Data = data;
                section.Version = target;
            }

            migrated = copy;
            error = SaveLoadError.None;
            detail = null;
            return true;
        }

        public bool HasCompleteChain(string key, int fromVersion, int toVersion)
        {
            for (int v = fromVersion; v < toVersion; v++)
                if (!_steps.ContainsKey((key, v))) return false;
            return true;
        }

        private bool TryRunChain(string key, int from, int to, ref JToken data, out SaveLoadError error, out string detail)
        {
            // Erst die ganze Kette prüfen, damit eine Lücke nie zu einem halb migrierten Zustand führt.
            for (int v = from; v < to; v++)
            {
                if (!_steps.ContainsKey((key, v)))
                    return Fail(SaveLoadError.Unsupported, "Keine Migration für '" + key + "' von v" + v + " nach v" + (v + 1) + ".", out error, out detail);
            }

            for (int v = from; v < to; v++)
            {
                JToken result;
                try
                {
                    result = _steps[(key, v)].Migrate(data == null ? null : data.DeepClone());
                }
                catch (Exception ex)
                {
                    return Fail(SaveLoadError.Corrupted, "Migration '" + key + "' v" + v + " fehlgeschlagen: " + ex.Message, out error, out detail);
                }
                if (result == null)
                    return Fail(SaveLoadError.Corrupted, "Migration '" + key + "' v" + v + " lieferte keine Daten.", out error, out detail);
                data = result;
            }

            error = SaveLoadError.None;
            detail = null;
            return true;
        }

        private static bool Fail(SaveLoadError e, string d, out SaveLoadError error, out string detail)
        {
            error = e;
            detail = d;
            return false;
        }
    }
}
