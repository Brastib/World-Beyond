namespace WorldBeyond.Simulation.Diplomacy
{
    // Takt der Diplomatie (Order 800 laut SYSTEM_INTERFACES §2), jede Spielsekunde.
    // Die KI-Entscheidungen laufen nicht hier, sondern im CivAISystem über WarStrategyEvaluator.
    public sealed class DiplomacySystem : ISimulationSystem
    {
        readonly DiplomacyService _diplomacy;

        public DiplomacySystem(DiplomacyService diplomacy) { _diplomacy = diplomacy; }

        public string Name => "Diplomacy";
        public int Order => SystemOrder.Diplomacy;
        public int TickInterval => SimClock.TicksPerSecond;
        public void Tick(SimContext ctx) => _diplomacy.Update();
    }
}
