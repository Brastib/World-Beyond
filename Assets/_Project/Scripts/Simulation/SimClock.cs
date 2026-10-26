using System;

namespace WorldBeyond.Simulation
{
    /// <summary>Einzige Simulationsuhr. Nur der SimulationRunner rückt sie vor.</summary>
    public sealed class SimClock
    {
        public const int TicksPerSecond = 10;

        public long Tick { get; private set; }
        public float DeltaSeconds => 1f / TicksPerSecond;
        public double ElapsedSeconds => (double)Tick / TicksPerSecond;

        public void Advance() => Tick++;

        public void Restore(long tick)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick), "Tick darf nicht negativ sein.");
            Tick = tick;
        }
    }
}
