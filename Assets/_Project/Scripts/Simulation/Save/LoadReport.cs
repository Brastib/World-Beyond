using System.Collections.Generic;

namespace WorldBeyond.Simulation
{
    /// <summary>
    /// Optional für Systeme mit Querverweisen. Der SaveLoadService ruft es nach dem Einspielen ALLER Sektionen auf.
    /// Weiche Fehler: Rückfall anwenden (ARCHITECTURE §11.1 Regel 6) und mit Warn melden; harte mit Fail.
    /// </summary>
    public interface ISaveReferenceCheck
    {
        void CheckReferences(SimContext ctx, LoadReport report);
    }

    /// <summary>Ergebnis der Referenzprüfung beim Laden. Errors nicht leer → Laden schlägt fehl (InvalidReferences).</summary>
    public sealed class LoadReport
    {
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();

        public void Warn(string message) => Warnings.Add(message);
        public void Fail(string message) => Errors.Add(message);
        public bool HasErrors => Errors.Count > 0;
    }
}
