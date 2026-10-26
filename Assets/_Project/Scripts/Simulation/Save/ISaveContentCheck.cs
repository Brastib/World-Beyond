using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation
{
    /// <summary>
    /// Optional für ISaveable mit Inhaltsschlüsseln (Einheiten-, Gebäude-, Ressourcentypen …).
    /// Der SaveLoadService prüft alle Schlüssel gegen die ContentRegistry, bevor er irgendetwas einspielt,
    /// und meldet fehlende als MissingContent statt als beschädigten Spielstand (ARCHITECTURE §11.5 Schritt 4).
    /// </summary>
    public interface ISaveContentCheck
    {
        /// <param name="dto">migriertes DTO vom Typ StateType</param>
        void CollectContentKeys(object dto, ICollection<(ContentKind kind, string key)> keys);
    }
}
