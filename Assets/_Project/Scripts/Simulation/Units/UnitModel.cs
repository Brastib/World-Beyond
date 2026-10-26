using System;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Units
{
    // Explizite Werte, nur anhängen.
    public enum UnitState : byte
    {
        Idle = 0,
        Moving = 1,
        Working = 2,
        Fighting = 3,
        Fleeing = 4,
        Dead = 5,
    }

    // Explizite Werte, nur anhängen. Aufträge sind Daten, keine Coroutines (ARCHITECTURE §11.1).
    public enum UnitTaskKind : byte
    {
        None = 0,
        MoveTo = 1,
        Gather = 2,
        Build = 3,
        Attack = 4,
        Settle = 5,
        Garrison = 6,
        Flee = 7,
    }

    /// <summary>
    /// Auftrag einer Einheit. TargetEntity: Laufzeit-Entity (Einheit/Gebäude), vor Nutzung mit Exists prüfen.
    /// TargetRef: fachliche ID (Siedlung, Armee …) oder 0. Keine ContentRegistry-Laufzeit-ints hier speichern.
    /// </summary>
    [Serializable]
    public struct UnitTask
    {
        public UnitTaskKind Kind;
        public EntityId TargetEntity;
        public int TargetRef;
        public float TargetX;
        public float TargetZ;

        public static UnitTask None => default;
        public static UnitTask MoveTo(float x, float z) => new UnitTask { Kind = UnitTaskKind.MoveTo, TargetX = x, TargetZ = z };
    }

    /// <summary>Snapshot für UI/Presentation. Kopie, keine Referenz in den Store.</summary>
    public readonly struct UnitView
    {
        public readonly EntityId Id;
        public readonly UnitTypeId Type;
        public readonly int CivId;
        public readonly float X, Z;
        public readonly float Heading;     // Grad, 0 = +Z
        public readonly float Speed;
        public readonly float Hp, MaxHp;
        public readonly UnitState State;
        public readonly UnitTask Task;
        public readonly bool HasMoveTarget;
        public readonly float MoveTargetX, MoveTargetZ;

        public UnitView(EntityId id, UnitTypeId type, int civId, float x, float z, float heading, float speed,
            float hp, float maxHp, UnitState state, UnitTask task, bool hasMoveTarget, float mtx, float mtz)
        {
            Id = id; Type = type; CivId = civId; X = x; Z = z; Heading = heading; Speed = speed;
            Hp = hp; MaxHp = maxHp; State = state; Task = task;
            HasMoveTarget = hasMoveTarget; MoveTargetX = mtx; MoveTargetZ = mtz;
        }
    }

    public struct UnitSpawnedEvent : ISimEvent
    {
        public EntityId Unit;
        public UnitTypeId UnitType;
        public int CivId;
        public float X, Z;
    }

    /// <summary>Einziges Event für den Tod einer Einheit (INTEGRATION.md §5). Killer kann bereits ungültig sein.</summary>
    public struct UnitDiedEvent : ISimEvent
    {
        public EntityId Unit;
        public EntityId Killer;
        public int CivId;
        public int KillerCivId;
        public UnitTypeId UnitType;
        public float X, Z;
    }
}
