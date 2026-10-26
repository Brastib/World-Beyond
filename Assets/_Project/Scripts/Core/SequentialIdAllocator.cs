namespace WorldBeyond.Core
{
    /// <summary>
    /// Fachliche IDs (Civ, Siedlung, Armee, Krieg, Vertrag): fortlaufend ab 1, nie wiederverwendet, 0 = ungültig
    /// (ARCHITECTURE §11.1 Regel 2). Der Zählerstand gehört zum Spielstand des besitzenden Systems.
    /// </summary>
    public sealed class SequentialIdAllocator
    {
        public int LastIssued { get; private set; }

        public int Next()
        {
            checked { LastIssued++; }
            return LastIssued;
        }

        /// <summary>lastIssued &lt; 0 → Fehler, Zustand bleibt unverändert.</summary>
        public bool TryRestore(int lastIssued, out string error)
        {
            if (lastIssued < 0)
            {
                error = $"SequentialIdAllocator: LastIssued {lastIssued} ist negativ.";
                return false;
            }
            LastIssued = lastIssued;
            error = null;
            return true;
        }
    }
}
