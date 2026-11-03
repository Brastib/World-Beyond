using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Presentation.WorldObjects
{
    public struct UnitSample
    {
        public EntityId Id;
        public int CivId;
        public UnitRole Role;
        public float Radius;
        public float PrevX, PrevZ, PrevHeading;
        public float X, Z, Heading;
        public bool Moving;
    }

    /// <summary>
    /// Unity-frei: hält für jede lebende Einheit die Position des vorletzten und letzten Ticks, damit die Darstellung
    /// zwischen den Ticks mit SimulationRunner.InterpolationAlpha glätten kann. Capture nur aufrufen, wenn der Tick
    /// sich geändert hat. Tote Einheiten (State Dead) und verschwundene IDs fallen heraus.
    /// </summary>
    public sealed class UnitInterpolator
    {
        readonly List<EntityId> _ids = new List<EntityId>();
        readonly Dictionary<EntityId, int> _index = new Dictionary<EntityId, int>();
        readonly Dictionary<EntityId, int> _nextIndex = new Dictionary<EntityId, int>();
        List<UnitSample> _samples = new List<UnitSample>();
        List<UnitSample> _next = new List<UnitSample>();

        public IReadOnlyList<UnitSample> Samples => _samples;
        public long CapturedTick { get; private set; } = -1;

        public void Clear()
        {
            _samples.Clear();
            _index.Clear();
            CapturedTick = -1;
        }

        public void Capture(IUnitQuery units, UnitTypeRegistry types, long tick)
        {
            _next.Clear();
            _nextIndex.Clear();
            _ids.Clear();
            units.CopyAliveIds(_ids);
            foreach (EntityId id in _ids)
            {
                if (!units.TryGet(id, out UnitView v) || v.State == UnitState.Dead)
                    continue;
                var s = new UnitSample { Id = id, CivId = v.CivId, X = v.X, Z = v.Z, Heading = v.Heading, Moving = v.Speed > 0.05f };
                if (types != null && types.TryGet(v.Type, out UnitTypeData d))
                {
                    s.Role = d.Role;
                    s.Radius = d.Radius;
                }
                if (_index.TryGetValue(id, out int old))
                {
                    UnitSample p = _samples[old];
                    s.PrevX = p.X; s.PrevZ = p.Z; s.PrevHeading = p.Heading;
                }
                else
                {
                    s.PrevX = s.X; s.PrevZ = s.Z; s.PrevHeading = s.Heading;
                }
                _nextIndex[id] = _next.Count;
                _next.Add(s);
            }
            (_samples, _next) = (_next, _samples);
            _index.Clear();
            foreach (var kv in _nextIndex)
                _index[kv.Key] = kv.Value;
            CapturedTick = tick;
        }

        /// <summary>Position und Blickrichtung (Grad, 0 = +Z) zwischen vorletztem und letztem Tick.</summary>
        public static void Lerp(in UnitSample s, float alpha, out float x, out float z, out float heading)
        {
            float a = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
            x = s.PrevX + (s.X - s.PrevX) * a;
            z = s.PrevZ + (s.Z - s.PrevZ) * a;
            float d = s.Heading - s.PrevHeading;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            heading = s.PrevHeading + d * a;
        }
    }
}
