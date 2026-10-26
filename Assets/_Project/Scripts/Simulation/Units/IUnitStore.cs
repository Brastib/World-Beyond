using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Units
{
    public interface IUnitQuery
    {
        int Count { get; }
        bool Exists(EntityId id);
        bool TryGet(EntityId id, out UnitView view);   // false bei ungültiger ID (Regel 6: kein Throw)
        UnitView Get(EntityId id);                     // ungültige ID → default(UnitView); bevorzugt TryGet
        void ForEachAlive(System.Action<EntityId> visitor);
        void CopyAliveIds(List<EntityId> results);     // ohne Allokation pro Aufruf
    }

    /// <summary>
    /// Einziger Einheiten-Speicher für zivile und militärische Einheiten (INTEGRATION.md §3).
    /// Schreibrechte: Spawn/Despawn über Produktion (Civ/IRecruitment), God-Commands und ICombatService;
    /// SetPosition nur MovementSystem (World); SetHp nur ICombatService (Military).
    /// Ungültige IDs werden ignoriert und liefern false (ARCHITECTURE §11.1 Regel 6).
    /// </summary>
    public interface IUnitStore : IUnitQuery
    {
        EntityId Spawn(UnitTypeId type, int civId, float x, float z);   // None bei unbekanntem Typ
        bool Despawn(EntityId id, EntityId killer = default, int killerCivId = 0);  // löst UnitDiedEvent aus
        bool SetMoveTarget(EntityId id, float x, float z);
        bool ClearMoveTarget(EntityId id);
        bool SetTask(EntityId id, UnitTask task);
        bool SetState(EntityId id, UnitState state);
        bool SetPosition(EntityId id, float x, float z, float heading, float speed);
        bool SetHp(EntityId id, float hp);
    }
}
