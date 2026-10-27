using System;
using System.Collections.Generic;
using WorldBeyond.Simulation;

namespace WorldBeyond.Application
{
    // Explizite Werte = Ticks-Multiplikator.
    public enum GameSpeed
    {
        Paused = 0,
        x1 = 1,
        x2 = 2,
        x5 = 5,
        x10 = 10,
    }

    public interface ISimulationRunner
    {
        GameSpeed Speed { get; set; }
        float InterpolationAlpha { get; }  // für flüssige Darstellung zwischen Ticks
        bool IsTicking { get; }            // true während eines Ticks; Speichern nur bei false
        void Register(ISimulationSystem system);
        int Advance(float realDeltaSeconds);
        void StepOnce();
        event Action<long> TickCompleted;
    }

    /// <summary>
    /// Einzige Tick-Schleife. Pro Tick: Systeme nach Order (bei Gleichstand Registrierungsreihenfolge),
    /// jeweils wenn Tick % TickInterval == 0; danach Event-Flush (Order 900); danach Uhr vorrücken.
    /// </summary>
    public sealed class SimulationRunner : ISimulationRunner
    {
        private readonly SimContext _ctx;
        private readonly List<(ISimulationSystem system, int seq)> _systems = new List<(ISimulationSystem, int)>();
        private ISimulationSystem[] _ordered = Array.Empty<ISimulationSystem>();
        private double _accumulator;
        private int _seq;

        public SimulationRunner(SimContext ctx, int maxTicksPerAdvance = 20)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            if (maxTicksPerAdvance < 1) throw new ArgumentOutOfRangeException(nameof(maxTicksPerAdvance));
            MaxTicksPerAdvance = maxTicksPerAdvance;
        }

        public GameSpeed Speed { get; set; } = GameSpeed.x1;
        public int MaxTicksPerAdvance { get; }
        // Exakt in double rechnen; 0.1f als float würde nach 10 × 0.1 s einen Tick verlieren.
        private const double TickSeconds = 1.0 / SimClock.TicksPerSecond;
        private const double Epsilon = 1e-9;

        public float InterpolationAlpha => (float)(_accumulator / TickSeconds);
        public bool IsTicking { get; private set; }
        public IReadOnlyList<ISimulationSystem> Systems => _ordered;

        /// <summary>Wie oft Advance Ticks verwerfen musste, weil MaxTicksPerAdvance erreicht war (Leistungsanzeige).</summary>
        public int DroppedBacklogCount { get; private set; }

        public event Action<long> TickCompleted;

        /// <summary>
        /// Exception in einem System oder Event-Handler. Der Tick wird trotzdem abgeschlossen und die Uhr rückt vor,
        /// damit nie ein Tick doppelt läuft (Sandbox läuft ohne Eingriff weiter). Parameter: Tick, Quelle, Exception.
        /// </summary>
        public event Action<long, string, Exception> SystemFailed;

        /// <summary>Zahl der gemeldeten Fehler seit Start (für Debug-Overlay und Tests).</summary>
        public int FaultCount { get; private set; }

        public void Register(ISimulationSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (IsTicking) throw new InvalidOperationException("Systeme dürfen nicht während eines Ticks registriert werden.");
            if (system.TickInterval < 1) throw new ArgumentException($"System '{system.Name}': TickInterval muss >= 1 sein.");
            foreach (var s in _systems)
                if (ReferenceEquals(s.system, system)) throw new InvalidOperationException($"System '{system.Name}' ist bereits registriert.");
            _systems.Add((system, _seq++));
            _systems.Sort((a, b) => a.system.Order != b.system.Order ? a.system.Order.CompareTo(b.system.Order) : a.seq.CompareTo(b.seq));
            _ordered = new ISimulationSystem[_systems.Count];
            for (int i = 0; i < _systems.Count; i++) _ordered[i] = _systems[i].system;
        }

        /// <summary>Echtzeit vorrücken. Liefert die Zahl ausgeführter Ticks.</summary>
        public int Advance(float realDeltaSeconds)
        {
            if (Speed == GameSpeed.Paused || realDeltaSeconds <= 0f) return 0;
            const double dt = TickSeconds;
            _accumulator += (double)realDeltaSeconds * (int)Speed;
            int ran = 0;
            while (_accumulator + Epsilon >= dt && ran < MaxTicksPerAdvance)
            {
                RunTick();
                _accumulator -= dt;
                ran++;
            }
            if (_accumulator < 0) _accumulator = 0;
            if (_accumulator + Epsilon >= dt)
            {
                // Rückstand verwerfen statt Todesspirale; Simulation läuft dann langsamer als gewünscht.
                _accumulator %= dt;
                DroppedBacklogCount++;
            }
            return ran;
        }

        /// <summary>Genau einen Tick ausführen, auch bei Pause (Einzelschritt, Tests).</summary>
        public void StepOnce() => RunTick();

        private void RunTick()
        {
            if (IsTicking) throw new InvalidOperationException("Tick ist nicht reentrant.");
            IsTicking = true;
            try
            {
                long tick = _ctx.Clock.Tick;
                var systems = _ordered;
                for (int i = 0; i < systems.Length; i++)
                {
                    var s = systems[i];
                    if (tick % s.TickInterval != 0) continue;
                    try { s.Tick(_ctx); }
                    catch (Exception ex) { ReportFault(tick, s.Name, ex); }
                }
                try { _ctx.Events.Flush(); }
                catch (Exception ex) { ReportFault(tick, "EventBus", ex); }
                _ctx.Clock.Advance();
            }
            finally
            {
                IsTicking = false;
            }
            TickCompleted?.Invoke(_ctx.Clock.Tick);
        }

        /// <summary>Meldet einen Fehler; GameComposition leitet auch Handler-Fehler des Event-Bus hierher.</summary>
        public void ReportFault(long tick, string source, Exception ex)
        {
            FaultCount++;
            SystemFailed?.Invoke(tick, source, ex);
        }
    }
}
