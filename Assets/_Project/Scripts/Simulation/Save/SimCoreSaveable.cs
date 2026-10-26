using System;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Save
{
    /// <summary>Speichert Simulationsuhr und Zufallsgenerator. SaveKey "core", wird als Erstes eingespielt.</summary>
    public sealed class SimCoreSaveable : ISaveable
    {
        public const string Key = "core";
        private readonly SimClock _clock;
        private readonly SimRandom _random;

        public SimCoreSaveable(SimClock clock, SimRandom random)
        {
            _clock = clock;
            _random = random;
        }

        public string SaveKey => Key;
        public int LoadOrder => -100;
        public int SectionVersion => 1;
        public Type StateType => typeof(SimCoreState);

        public object CaptureState() => new SimCoreState { Tick = _clock.Tick, RandomState = _random.State };

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is SimCoreState s)) { error = $"core: erwartet {nameof(SimCoreState)}, erhalten {dto?.GetType().Name ?? "null"}."; return false; }
            if (s.Tick < 0) { error = $"core: Tick {s.Tick} ist negativ."; return false; }
            if (s.RandomState == 0) { error = "core: RandomState ist 0."; return false; }
            _clock.Restore(s.Tick);
            _random.State = s.RandomState;
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class SimCoreState
    {
        public long Tick;
        public ulong RandomState;
    }
}
