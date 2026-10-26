using System;

namespace WorldBeyond.Simulation
{
    /// <summary>
    /// Vertrag für speicherbare Systemdaten (SYSTEM_INTERFACES §11, ARCHITECTURE §11).
    /// Der SaveLoadService (Phase 7) wandelt JSON in StateType um und migriert vorher auf SectionVersion.
    /// Simulation referenziert kein Newtonsoft.
    /// </summary>
    public interface ISaveable
    {
        string SaveKey { get; }           // eindeutig, z. B. "world", "units", "civs"
        int LoadOrder { get; }            // aufsteigend eingespielt: core vor world vor units vor civs …
        int SectionVersion { get; }       // Version dieser Sektion
        Type StateType { get; }           // DTO-Typ, den CaptureState liefert und TryRestoreState erwartet
        object CaptureState();            // reines DTO vom Typ StateType, keine Unity-Objekte, Querverweise nur über IDs
        bool TryRestoreState(object dto, out string error);  // dto bereits migriert; false = harte Inkonsistenz → Corrupted
    }
}
