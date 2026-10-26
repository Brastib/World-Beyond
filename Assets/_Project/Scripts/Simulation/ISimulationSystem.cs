namespace WorldBeyond.Simulation
{
    /// <summary>
    /// Ein System der Simulation. Wird nur in der Composition Root (GameComposition) erzeugt und registriert.
    /// Läuft, wenn Tick % TickInterval == 0. Feste Order-Werte: SYSTEM_INTERFACES §2.
    /// </summary>
    public interface ISimulationSystem
    {
        string Name { get; }
        int Order { get; }                 // feste Reihenfolge, siehe SystemOrder
        int TickInterval { get; }          // 1 = jeden Tick, 50 = alle 5 s
        void Tick(SimContext ctx);
    }

    /// <summary>Verbindliche Order-Werte (SYSTEM_INTERFACES §2). Der Event-Flush (900) macht der Runner selbst.</summary>
    public static class SystemOrder
    {
        public const int Commands = 0;
        public const int AI = 100;
        public const int Economy = 200;
        public const int Movement = 300;
        public const int Combat = 400;
        public const int Projectiles = 500;
        public const int Cleanup = 600;
        public const int Settlements = 700;
        public const int Diplomacy = 800;
    }
}
