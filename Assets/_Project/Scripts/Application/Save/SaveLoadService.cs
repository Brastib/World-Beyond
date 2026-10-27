using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldBeyond.Simulation;

namespace WorldBeyond.Application.Save
{
    public readonly struct SaveResult
    {
        public readonly bool Success;
        public readonly SaveLoadError Error;
        public readonly string UserMessage;
        public readonly string Detail;

        public SaveResult(SaveLoadError error, string detail)
        {
            Success = error == SaveLoadError.None;
            Error = error;
            UserMessage = SaveMessages.ForSave(error);
            Detail = detail;
        }
    }

    public readonly struct LoadResult
    {
        public readonly bool Success;
        public readonly SaveLoadError Error;
        public readonly string UserMessage;
        public readonly string Detail;
        public readonly LoadReport Report;

        public LoadResult(SaveLoadError error, string detail, LoadReport report)
        {
            Success = error == SaveLoadError.None;
            Error = error;
            UserMessage = SaveMessages.ForLoad(error);
            Detail = detail;
            Report = report ?? new LoadReport();
        }
    }

    public sealed class SaveSlotInfo
    {
        public string Slot;
        public bool Readable;            // Rahmen gültig (Format, Version, Prüfsumme)
        public SaveLoadError Error;      // Grund, falls nicht lesbar
        public string Name;
        public string CreatedUtc;
        public string GameVersion;
        public int FormatVersion;
        public long SimTick;
    }

    public interface ISaveLoadService
    {
        SaveResult Save(string slotName);
        LoadResult Load(string slotName);
        IReadOnlyList<SaveSlotInfo> ListSlots();
    }

    /// <summary>
    /// Einziger SaveLoadService (INTEGRATION.md §3). Ablauf: ARCHITECTURE §11.4 (Speichern) und §11.5 (Laden).
    /// Kennt keine Fachsysteme: Er liest nur ctx.Saveables und die optionalen Prüfungen ISaveContentCheck/ISaveReferenceCheck.
    /// Unity-frei; den Speicherordner und den Austausch der Komposition liefert der Aufrufer.
    /// </summary>
    public sealed class SaveLoadService : ISaveLoadService
    {
        private readonly SaveFileStore _store;
        private readonly Func<GameStartSettings, GameComposition> _createComposition;
        private readonly Func<GameComposition> _current;
        private readonly Action<GameComposition> _replace;
        private readonly SaveGameMigrator _migrator;
        private readonly SaveGameSerializer _serializer = new SaveGameSerializer();
        private readonly string _gameVersion;
        private readonly Func<DateTime> _utcNow;

        // Slots, die zuletzt aus einer älteren Version geladen wurden → Original beim nächsten Speichern als .vN sichern.
        private readonly Dictionary<string, int> _migratedFrom = new Dictionary<string, int>(StringComparer.Ordinal);

        public SaveLoadService(SaveFileStore store,
                               Func<GameStartSettings, GameComposition> createComposition,
                               Func<GameComposition> current,
                               Action<GameComposition> replace,
                               IEnumerable<ISaveMigration> migrations,
                               string gameVersion,
                               Func<DateTime> utcNow = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _createComposition = createComposition ?? throw new ArgumentNullException(nameof(createComposition));
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _replace = replace ?? throw new ArgumentNullException(nameof(replace));
            _migrator = new SaveGameMigrator(migrations);
            _gameVersion = gameVersion ?? "unbekannt";
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        // ---------------- Speichern ----------------

        public SaveResult Save(string slotName)
        {
            if (!SaveFileStore.IsValidSlotName(slotName)) return new SaveResult(SaveLoadError.IoError, "Ungültiger Slot-Name.");
            var comp = _current();
            if (comp == null) return new SaveResult(SaveLoadError.CaptureFailed, "Keine laufende Simulation.");
            if (comp.Runner.IsTicking) return new SaveResult(SaveLoadError.CaptureFailed, "Speichern nur an der Tick-Grenze.");

            if (!TryCapture(comp, slotName, out var data, out var detail)) return new SaveResult(SaveLoadError.CaptureFailed, detail);

            string json = _serializer.Serialize(data);
            // Selbstprüfung: Nie einen Spielstand schreiben, den das Laden als beschädigt ablehnen würde.
            if (!_serializer.TryParseFrame(json, out _, out var verifyError, out var verifyDetail))
                return new SaveResult(SaveLoadError.CaptureFailed, $"Selbstprüfung nach dem Serialisieren fehlgeschlagen ({verifyError}): {verifyDetail}");
            int? preserve = _migratedFrom.TryGetValue(slotName, out int oldVersion) ? oldVersion : (int?)null;
            if (!_store.TryWrite(slotName, json, preserve, out var error, out detail)) return new SaveResult(error, detail);
            _migratedFrom.Remove(slotName);
            return new SaveResult(SaveLoadError.None, null);
        }

        private bool TryCapture(GameComposition comp, string slotName, out SaveGameData data, out string detail)
        {
            var ctx = comp.Context;
            var json = SaveGameSerializer.CreateJsonSerializer();
            data = new SaveGameData
            {
                Format = SaveGameData.FormatName,
                FormatVersion = SaveGameData.CurrentFormatVersion,
                GameVersion = _gameVersion,
                CreatedUtc = _utcNow().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Header = new SaveHeader
                {
                    Name = slotName,
                    WorldSeed = comp.Settings.Seed,
                    WorldWidth = comp.Settings.WorldWidth,
                    WorldHeight = comp.Settings.WorldHeight,
                    SimTick = ctx.Clock.Tick,
                    CivCount = 0,                    // TODO(Phase 3): aus ICivilizationQuery, sobald vorhanden
                    UnitCount = ctx.Units.Count,
                },
                Start = new StartSettingsDto
                {
                    Seed = comp.Settings.Seed,
                    WorldWidth = comp.Settings.WorldWidth,
                    WorldHeight = comp.Settings.WorldHeight,
                    CivilizationCount = comp.Settings.CivilizationCount,
                },
                Settings = new SimSettingsDto { GameSpeed = (int)comp.Runner.Speed, Paused = comp.Runner.Speed == GameSpeed.Paused },
            };

            foreach (var s in ctx.Saveables.InLoadOrder())
            {
                object dto;
                try
                {
                    dto = s.CaptureState();
                }
                catch (Exception ex)
                {
                    detail = $"'{s.SaveKey}'.CaptureState warf {ex.GetType().Name}: {ex.Message}";
                    return false;
                }
                if (dto == null || dto.GetType() != s.StateType)
                {
                    detail = $"'{s.SaveKey}' lieferte {dto?.GetType().Name ?? "null"} statt {s.StateType.Name}.";
                    return false;
                }
                try
                {
                    data.Sections[s.SaveKey] = new SaveSection { Version = s.SectionVersion, Data = JToken.FromObject(dto, json) };
                }
                catch (JsonException ex)
                {
                    detail = $"'{s.SaveKey}' ist nicht serialisierbar: {ex.Message}";
                    return false;
                }
            }

            detail = null;
            return true;
        }

        // ---------------- Laden ----------------

        /// <summary>Dauer der Ladeschritte des letzten Load-Aufrufs in ms (Leistungsanzeige, Messläufe).</summary>
        public IReadOnlyList<(string Stage, double Ms)> LastLoadTimings => _loadTimings;
        private readonly List<(string Stage, double Ms)> _loadTimings = new List<(string, double)>();

        private void Mark(System.Diagnostics.Stopwatch sw, string stage)
        {
            _loadTimings.Add((stage, sw.Elapsed.TotalMilliseconds));
            sw.Restart();
        }

        public LoadResult Load(string slotName)
        {
            var report = new LoadReport();
            _loadTimings.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_store.TryRead(slotName, out var json, out var error, out var detail)) return new LoadResult(error, detail, report);
            Mark(sw, "read");

            // Schritt 1–2: Datei, Metadaten, Formatversion, Prüfsumme.
            if (!_serializer.TryParseFrame(json, out var root, out error, out detail)) return new LoadResult(error, detail, report);
            int originalFormatVersion = (int)root["formatVersion"];

            // Schritt 3a: Rahmen migrieren, dann auf das aktuelle Modell abbilden.
            if (!_migrator.TryMigrateFrame(root, out var migratedRoot, out error, out detail)) return new LoadResult(error, detail, report);
            if (!_serializer.TryMap(migratedRoot, out var data, out error, out detail)) return new LoadResult(error, detail, report);
            if (data.Start == null) return new LoadResult(SaveLoadError.Corrupted, "Startparameter ('start') fehlen.", report);
            Mark(sw, "parse");

            // Leere Simulation mit denselben Startparametern. Der laufende Zustand bleibt bis Schritt 6 unberührt.
            GameComposition fresh;
            try
            {
                fresh = _createComposition(new GameStartSettings
                {
                    Seed = data.Start.Seed,
                    WorldWidth = data.Start.WorldWidth,
                    WorldHeight = data.Start.WorldHeight,
                    CivilizationCount = data.Start.CivilizationCount,
                });
            }
            catch (Exception ex)
            {
                return new LoadResult(SaveLoadError.Corrupted, "Simulation mit den gespeicherten Startparametern nicht erzeugbar: " + ex.Message, report);
            }
            var saveables = fresh.Context.Saveables.InLoadOrder();
            Mark(sw, "create");

            // Schritt 3b: Sektionen migrieren.
            var currentVersions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in saveables) currentVersions[s.SaveKey] = s.SectionVersion;
            if (!_migrator.TryMigrate(data, currentVersions, out var migrated, out error, out detail)) return new LoadResult(error, detail, report);
            bool sectionsMigrated = false;
            foreach (var pair in data.Sections)
                if (currentVersions.TryGetValue(pair.Key, out int v) && pair.Value.Version != v) sectionsMigrated = true;

            foreach (var key in migrated.Sections.Keys)
                if (!currentVersions.ContainsKey(key)) report.Warn($"Unbekannte Sektion '{key}' wird ignoriert.");

            // DTOs bilden; fehlende Pflichtsektion = beschädigt.
            var serializer = SaveGameSerializer.CreateJsonSerializer();
            var dtos = new object[saveables.Count];
            for (int i = 0; i < saveables.Count; i++)
            {
                var s = saveables[i];
                if (!migrated.Sections.TryGetValue(s.SaveKey, out var section) || section?.Data == null || section.Data.Type == JTokenType.Null)
                    return new LoadResult(SaveLoadError.Corrupted, $"Pflichtsektion '{s.SaveKey}' fehlt.", report);
                try
                {
                    dtos[i] = section.Data.ToObject(s.StateType, serializer);
                }
                catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidCastException)
                {
                    return new LoadResult(SaveLoadError.Corrupted, $"Sektion '{s.SaveKey}' nicht lesbar: {ex.Message}", report);
                }
                if (dtos[i] == null) return new LoadResult(SaveLoadError.Corrupted, $"Sektion '{s.SaveKey}' ist leer.", report);
            }

            Mark(sw, "dto");

            // Schritt 4: Inhaltsschlüssel prüfen, bevor irgendetwas eingespielt wird.
            var keys = new List<(Core.ContentKind kind, string key)>();
            var missing = new List<string>();
            for (int i = 0; i < saveables.Count; i++)
            {
                if (!(saveables[i] is ISaveContentCheck check)) continue;
                keys.Clear();
                try
                {
                    check.CollectContentKeys(dtos[i], keys);
                }
                catch (Exception ex)
                {
                    return new LoadResult(SaveLoadError.Corrupted, $"'{saveables[i].SaveKey}': Inhaltsprüfung warf {ex.GetType().Name}: {ex.Message}", report);
                }
                foreach (var (kind, key) in keys)
                {
                    if (!fresh.Context.Content.Contains(kind, key))
                    {
                        var entry = $"{kind}:{key}";
                        if (!missing.Contains(entry)) missing.Add(entry);
                    }
                }
            }
            if (missing.Count > 0)
                return new LoadResult(SaveLoadError.MissingContent, "Fehlende Inhalte: " + string.Join(", ", missing), report);

            // Schritt 5: In LoadOrder einspielen, danach Querverweise prüfen.
            for (int i = 0; i < saveables.Count; i++)
            {
                string restoreError;
                bool ok;
                try
                {
                    ok = saveables[i].TryRestoreState(dtos[i], out restoreError);
                }
                catch (Exception ex)
                {
                    ok = false;
                    restoreError = $"{ex.GetType().Name}: {ex.Message}";
                }
                if (!ok) return new LoadResult(SaveLoadError.Corrupted, $"'{saveables[i].SaveKey}': {restoreError}", report);
                Mark(sw, "restore:" + saveables[i].SaveKey);
            }

            foreach (var s in saveables)
            {
                if (!(s is ISaveReferenceCheck check)) continue;
                try
                {
                    check.CheckReferences(fresh.Context, report);
                }
                catch (Exception ex)
                {
                    report.Fail($"'{s.SaveKey}': Referenzprüfung warf {ex.GetType().Name}: {ex.Message}");
                }
            }
            Mark(sw, "references");
            if (report.HasErrors) return new LoadResult(SaveLoadError.InvalidReferences, string.Join(" | ", report.Errors), report);

            // Einstellungen übernehmen; unbekannter Geschwindigkeitswert → pausiert (Regel 6).
            var speed = GameSpeed.Paused;
            if (migrated.Settings != null && Enum.IsDefined(typeof(GameSpeed), migrated.Settings.GameSpeed))
                speed = (GameSpeed)migrated.Settings.GameSpeed;
            else
                report.Warn("Simulationsgeschwindigkeit fehlt oder ist unbekannt; Spiel startet pausiert.");
            fresh.Runner.Speed = speed;

            // Beim Aufbau der frischen Komposition veröffentlichte Events (Civ-/Siedlungsgründungen der Startwelt) gehören
            // nicht zum geladenen Stand; sonst meldet der erste Tick nach dem Laden sie erneut (Unity-Lauf 4, Ereignis-Feed).
            if (fresh.Context.Events is WorldBeyond.Core.SimEventBus bus) bus.ClearPending();

            // Schritt 6: erst jetzt austauschen.
            _replace(fresh);
            if (originalFormatVersion != SaveGameData.CurrentFormatVersion || sectionsMigrated) _migratedFrom[slotName] = originalFormatVersion;
            else _migratedFrom.Remove(slotName);
            return new LoadResult(SaveLoadError.None, null, report);
        }

        // ---------------- Liste ----------------

        public IReadOnlyList<SaveSlotInfo> ListSlots()
        {
            var result = new List<SaveSlotInfo>();
            foreach (var slot in _store.ListSlots())
            {
                var info = new SaveSlotInfo { Slot = slot };
                if (!_store.TryRead(slot, out var json, out var error, out _)) { info.Error = error; result.Add(info); continue; }
                if (!_serializer.TryParseFrame(json, out var root, out error, out _)) { info.Error = error; result.Add(info); continue; }
                info.Readable = true;
                info.FormatVersion = (int)root["formatVersion"];
                info.GameVersion = root["gameVersion"]?.Type == JTokenType.String ? (string)root["gameVersion"] : null;
                info.CreatedUtc = root["createdUtc"]?.Type == JTokenType.String ? (string)root["createdUtc"] : null;
                if (root["header"] is JObject header)
                {
                    info.Name = header["name"]?.Type == JTokenType.String ? (string)header["name"] : slot;
                    info.SimTick = header["simTick"]?.Type == JTokenType.Integer ? (long)header["simTick"] : 0;
                }
                result.Add(info);
            }
            return result;
        }
    }
}
