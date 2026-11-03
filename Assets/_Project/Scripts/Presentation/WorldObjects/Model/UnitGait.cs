using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Gangphase je Einheit für Code-Animationen (Beine, Räder, Schaukeln) ohne Animator. Grundlage ist der tatsächlich
    // zurückgelegte Weg der interpolierten Position: passt bei jedem Spieltempo, im Pausenmodus steht alles still.
    // Unity-frei und damit headless testbar.
    public sealed class UnitGait
    {
        public const float TeleportDistance = 12f;   // größere Sprünge pro Bild (Laden, Ansiedeln) zählen nicht als Weg
        public const float FullSpeed = 0.6f;         // m/s, ab der die Bewegung voll ausschlägt

        struct State { public float X, Z, Distance, Amount; public int Seen; }

        readonly Dictionary<EntityId, State> _states = new Dictionary<EntityId, State>();
        readonly List<EntityId> _stale = new List<EntityId>();
        int _frame;

        public int Count => _states.Count;

        /// <summary>Einmal pro Bild vor den Step-Aufrufen; räumt selten verschwundene Einheiten ab.</summary>
        public void BeginFrame()
        {
            _frame++;
            if ((_frame & 127) != 0)
                return;
            _stale.Clear();
            foreach (var kv in _states)
                if (_frame - kv.Value.Seen > 60)
                    _stale.Add(kv.Key);
            foreach (var id in _stale)
                _states.Remove(id);
        }

        /// <summary>Liefert den bisher gelaufenen Weg (m) und den Bewegungsausschlag 0…1 (weich ein- und ausgeblendet).</summary>
        public void Step(EntityId id, float x, float z, float dt, out float distance, out float amount)
        {
            if (!_states.TryGetValue(id, out State s))
            {
                s = new State { X = x, Z = z, Distance = (id.GetHashCode() & 255) * 0.037f };
            }
            else
            {
                float dx = x - s.X, dz = z - s.Z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > TeleportDistance) d = 0f;
                s.Distance += d;
                float target = dt > 1e-5f ? Math.Min(1f, d / dt / FullSpeed) : 0f;
                float step = Math.Max(dt, 1e-3f) * 4f;
                s.Amount = s.Amount < target ? Math.Min(target, s.Amount + step) : Math.Max(target, s.Amount - step);
                s.X = x; s.Z = z;
            }
            s.Seen = _frame;
            _states[id] = s;
            distance = s.Distance;
            amount = s.Amount;
        }

        public void Clear() => _states.Clear();
    }
}
