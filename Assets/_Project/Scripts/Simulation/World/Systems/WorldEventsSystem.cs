namespace WorldBeyond.Simulation.World
{
    // Sendet die gebündelten Welt-Events eines Ticks (OwnershipChangedEvent) vor dem Event-Flush des Runners.
    public sealed class WorldEventsSystem : ISimulationSystem
    {
        public const int OrderValue = 850;

        readonly WorldGrid _grid;

        public WorldEventsSystem(WorldGrid grid)
        {
            _grid = grid ?? throw new System.ArgumentNullException(nameof(grid));
        }

        public string Name => "WorldEvents";
        public int Order => OrderValue;
        public int TickInterval => 1;

        public void Tick(SimContext ctx) => _grid.FlushPendingEvents();
    }
}
