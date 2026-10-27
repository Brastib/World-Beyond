using System.Collections.Generic;

using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World
{
    public enum PathStatus
    {
        Pending = 0,
        Found = 1,
        NotFound = 2,
        Invalid = 3,
    }

    // Pfadsuche auf dem Kachelraster. Vertrag: SYSTEM_INTERFACES §5.
    public interface IPathfinder
    {
        // Asynchron über ein Budget pro Tick: Anfrage stellen, später Ergebnis abholen.
        int RequestPath(int fromX, int fromY, int toX, int toY, MovementClass mc);

        // Found: path enthält die geglätteten Wegpunkte (Kacheln) inklusive Start und Ziel; die Anfrage ist danach erledigt.
        PathStatus TryGetPath(int requestId, List<(int x, int y)> path);

        void Cancel(int requestId);

        // Synchron und günstig, für Standortwahl und KI-Bewertung.
        bool AreConnected(int ax, int ay, int bx, int by, MovementClass mc);

        // Schätzung der Wegkosten; float.PositiveInfinity, wenn nicht verbunden.
        float EstimateCost(int fromX, int fromY, int toX, int toY, MovementClass mc);
    }
}
