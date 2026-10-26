using System.Collections.Generic;

using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World
{
    // Räumliche Abfragen für Entities (Auswahl, Reichweite, Abstand halten). Vertrag: SYSTEM_INTERFACES §5.
    public interface ISpatialIndex
    {
        void Insert(EntityId id, float x, float z);
        void Move(EntityId id, float x, float z);
        void Remove(EntityId id);
        bool Contains(EntityId id);

        // Füllen die Ergebnisliste (vorher geleert), keine Allokation pro Aufruf.
        void QueryRadius(float x, float z, float radius, List<EntityId> results);
        void QueryRect(float minX, float minZ, float maxX, float maxZ, List<EntityId> results);
    }
}
