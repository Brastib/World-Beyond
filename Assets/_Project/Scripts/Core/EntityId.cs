using System;

namespace WorldBeyond.Core
{
    /// <summary>
    /// Stabile Laufzeit-ID für Einheiten, Gebäude und Geschosse (ARCHITECTURE §11.1 Regel 1).
    /// Index 0 ist reserviert und bedeutet "ungültig". Die Generation macht wiederverwendete
    /// Indizes erkennbar: Eine alte ID mit veralteter Generation ist nie mehr gültig.
    /// </summary>
    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly int Generation;

        public static readonly EntityId None = default;

        public EntityId(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>Formal gültig (Index &gt; 0, Generation &gt; 0). Ob das Objekt noch lebt, prüft der zuständige Store mit Exists.</summary>
        public bool IsValid => Index > 0 && Generation > 0;

        public bool Equals(EntityId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ Generation;
        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"#{Index}.{Generation}" : "#none";
    }
}
