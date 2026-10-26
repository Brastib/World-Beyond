using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WorldBeyond.Application.Save
{
    // Dateizugriff für Spielstände (ARCHITECTURE §11.4/§11.5). Keine Unity-Abhängigkeit:
    // der SaveLoadService übergibt Application.persistentDataPath + "/Saves".
    // Atomar: <slot>.wbsave.tmp schreiben → zurücklesen → alte Datei nach <slot>.wbsave.bak → .tmp umbenennen.
    public sealed class SaveFileStore
    {
        public const string Extension = ".wbsave";
        private static readonly Regex SlotPattern = new Regex("^[A-Za-z0-9_-]{1,64}$");
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string _directory;
        private readonly Action<string> _faultInjection;   // nur für Tests: wird an benannten Stellen aufgerufen und darf werfen

        public SaveFileStore(string directory, Action<string> faultInjection = null)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Verzeichnis fehlt.", nameof(directory));
            _directory = directory;
            _faultInjection = faultInjection;
        }

        public string Directory => _directory;

        public static bool IsValidSlotName(string slot) => slot != null && SlotPattern.IsMatch(slot);

        public string PathFor(string slot) => Path.Combine(_directory, slot + Extension);
        public string TempPathFor(string slot) => PathFor(slot) + ".tmp";
        public string BackupPathFor(string slot) => PathFor(slot) + ".bak";
        public string VersionBackupPathFor(string slot, int version) => Path.Combine(_directory, slot + ".v" + version + Extension);

        // preserveVersion: wenn gesetzt und die bestehende Datei noch nicht gesichert wurde, bleibt sie dauerhaft
        // als <slot>.v<preserveVersion>.wbsave erhalten (für migrierte Spielstände, ARCHITECTURE §11.5).
        public bool TryWrite(string slot, string content, int? preserveVersion, out SaveLoadError error, out string detail)
        {
            if (!IsValidSlotName(slot)) return Fail(SaveLoadError.IoError, "Ungültiger Slot-Name.", out error, out detail);
            if (content == null) throw new ArgumentNullException(nameof(content));

            string final = PathFor(slot), tmp = TempPathFor(slot), bak = BackupPathFor(slot);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);

                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = Utf8NoBom.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                _faultInjection?.Invoke("afterTempWrite");

                if (File.ReadAllText(tmp, Utf8NoBom) != content)
                    return Fail(SaveLoadError.IoError, "Zurückgelesene Datei weicht ab.", out error, out detail);

                if (File.Exists(final))
                {
                    if (preserveVersion.HasValue)
                    {
                        var versioned = VersionBackupPathFor(slot, preserveVersion.Value);
                        if (!File.Exists(versioned)) File.Copy(final, versioned);
                    }
                    _faultInjection?.Invoke("beforeReplace");
                    File.Replace(tmp, final, bak);
                }
                else
                {
                    _faultInjection?.Invoke("beforeReplace");
                    File.Move(tmp, final);
                }
                _faultInjection?.Invoke("afterReplace");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InjectedFaultException)
            {
                return Fail(SaveLoadError.IoError, ex.Message, out error, out detail);
            }

            error = SaveLoadError.None;
            detail = null;
            return true;
        }

        public bool TryRead(string slot, out string content, out SaveLoadError error, out string detail)
            => TryReadPath(slot, PathFor(slot), out content, out error, out detail);

        public bool TryReadBackup(string slot, out string content, out SaveLoadError error, out string detail)
            => TryReadPath(slot, BackupPathFor(slot), out content, out error, out detail);

        public IReadOnlyList<string> ListSlots()
        {
            var result = new List<string>();
            if (!System.IO.Directory.Exists(_directory)) return result;
            foreach (var file in System.IO.Directory.GetFiles(_directory, "*" + Extension))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsValidSlotName(name)) result.Add(name);   // schließt <slot>.vN-Sicherungen aus (enthalten einen Punkt)
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private bool TryReadPath(string slot, string path, out string content, out SaveLoadError error, out string detail)
        {
            content = null;
            if (!IsValidSlotName(slot)) return Fail(SaveLoadError.IoError, "Ungültiger Slot-Name.", out error, out detail);
            try
            {
                if (!File.Exists(path)) return Fail(SaveLoadError.NotFound, path, out error, out detail);
                content = File.ReadAllText(path, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail(SaveLoadError.IoError, ex.Message, out error, out detail);
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

    // Von Tests über faultInjection geworfen, um einen Abbruch beim Speichern zu simulieren.
    public sealed class InjectedFaultException : Exception
    {
        public InjectedFaultException(string stage) : base("Simulierter Abbruch: " + stage) { }
    }
}
