namespace WorldBeyond.Application.Save
{
    // Explizite Werte, nur anhängen (ARCHITECTURE §11.7).
    public enum SaveLoadError
    {
        None = 0,
        NotFound = 1,
        IoError = 2,
        Corrupted = 3,
        TooNew = 4,
        Unsupported = 5,
        MissingContent = 6,
        InvalidReferences = 7,
        CaptureFailed = 8       // Speichern: ein System lieferte keinen gültigen Zustand oder es lief gerade ein Tick
    }

    // Verständliche Meldungen für den Spieler. Technische Details stehen separat im Ergebnis bzw. im Log.
    public static class SaveMessages
    {
        public static string ForLoad(SaveLoadError error)
        {
            switch (error)
            {
                case SaveLoadError.None: return "Spielstand geladen.";
                case SaveLoadError.NotFound: return "Dieser Spielstand wurde nicht gefunden.";
                case SaveLoadError.IoError: return "Der Spielstand konnte nicht gelesen werden. Bitte prüfe, ob die Datei geöffnet oder schreibgeschützt ist.";
                case SaveLoadError.Corrupted: return "Der Spielstand ist beschädigt und kann nicht geladen werden.";
                case SaveLoadError.TooNew: return "Dieser Spielstand wurde mit einer neueren Version von World Beyond erstellt und kann nicht geladen werden.";
                case SaveLoadError.Unsupported: return "Dieser Spielstand stammt aus einer älteren Version, die nicht mehr unterstützt wird.";
                case SaveLoadError.MissingContent: return "Der Spielstand enthält Inhalte (z. B. Einheiten- oder Gebäudetypen), die es in dieser Version nicht mehr gibt.";
                case SaveLoadError.InvalidReferences: return "Der Spielstand enthält widersprüchliche Verweise und kann nicht sicher geladen werden.";
                default: return "Unbekannter Fehler beim Laden.";
            }
        }

        public static string ForSave(SaveLoadError error)
        {
            switch (error)
            {
                case SaveLoadError.None: return "Spiel gespeichert.";
                case SaveLoadError.IoError: return "Das Spiel konnte nicht gespeichert werden. Bitte prüfe freien Speicherplatz und Schreibrechte.";
                case SaveLoadError.CaptureFailed: return "Das Spiel konnte gerade nicht gespeichert werden. Bitte versuche es gleich noch einmal.";
                default: return "Das Spiel konnte nicht gespeichert werden.";
            }
        }
    }
}
