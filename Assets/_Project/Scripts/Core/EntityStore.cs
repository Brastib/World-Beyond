using System;
using System.Collections.Generic;

namespace WorldBeyond.Core
{
    /// <summary>
    /// Verwaltet EntityIds einer Art (Einheiten, Gebäude, Geschosse). Index 0 reserviert.
    /// Destroy erhöht die Generation (Überlauf → 1, nie 0) und legt den Index auf die Freiliste.
    /// Freie Indizes werden in FIFO-Reihenfolge wiederverwendet, damit eine gerade zerstörte ID
    /// nicht sofort mit neuer Generation zurückkommt.
    /// </summary>
    public sealed class EntityStore
    {
        private int[] _generations;
        private bool[] _alive;
        private int _length;                     // benutzte Indizes inkl. 0
        private readonly Queue<int> _free = new Queue<int>();
        private int _aliveCount;

        public EntityStore(int initialCapacity)
        {
            int cap = Math.Max(2, initialCapacity + 1);
            _generations = new int[cap];
            _alive = new bool[cap];
            _length = 1;
        }

        public int AliveCount => _aliveCount;

        /// <summary>Höchster bisher vergebener Index + 1. Arrays paralleler Stores müssen mindestens so groß sein.</summary>
        public int IndexLimit => _length;

        public EntityId Create()
        {
            int index;
            if (_free.Count > 0)
            {
                index = _free.Dequeue();
            }
            else
            {
                if (_length == _generations.Length) Grow(_generations.Length * 2);
                index = _length++;
                _generations[index] = 1;
            }
            _alive[index] = true;
            _aliveCount++;
            return new EntityId(index, _generations[index]);
        }

        /// <summary>false bei ungültiger oder veralteter ID, keine Exception.</summary>
        public bool Destroy(EntityId id)
        {
            if (!Exists(id)) return false;
            int i = id.Index;
            _alive[i] = false;
            _generations[i] = _generations[i] == int.MaxValue ? 1 : _generations[i] + 1;
            _free.Enqueue(i);
            _aliveCount--;
            return true;
        }

        /// <summary>Index im Bereich, lebt, Generation stimmt.</summary>
        public bool Exists(EntityId id)
        {
            int i = id.Index;
            return i > 0 && i < _length && _alive[i] && _generations[i] == id.Generation;
        }

        /// <summary>Lebende ID an diesem Index oder None.</summary>
        public EntityId IdAt(int index)
        {
            if (index <= 0 || index >= _length || !_alive[index]) return EntityId.None;
            return new EntityId(index, _generations[index]);
        }

        public EntityStoreState CaptureState()
        {
            var state = new EntityStoreState
            {
                Generations = new int[_length],
                Alive = new bool[_length],
                FreeList = _free.ToArray(),
            };
            Array.Copy(_generations, state.Generations, _length);
            Array.Copy(_alive, state.Alive, _length);
            return state;
        }

        /// <summary>
        /// Prüft Konsistenz (Längen, Freiliste ↔ Lebend-Markierung, keine Duplikate, Generationen &gt; 0).
        /// Bei false bleibt der bisherige Zustand unverändert.
        /// </summary>
        public bool TryRestoreState(EntityStoreState state, out string error)
        {
            if (state == null || state.Generations == null || state.Alive == null || state.FreeList == null)
            {
                error = "EntityStoreState unvollständig.";
                return false;
            }
            int len = state.Generations.Length;
            if (len < 1 || state.Alive.Length != len)
            {
                error = $"EntityStoreState: Längen passen nicht (Generations {len}, Alive {state.Alive.Length}).";
                return false;
            }
            if (state.Alive[0])
            {
                error = "EntityStoreState: Index 0 ist reserviert und darf nicht leben.";
                return false;
            }
            for (int i = 1; i < len; i++)
            {
                if (state.Generations[i] <= 0)
                {
                    error = $"EntityStoreState: Generation an Index {i} ist {state.Generations[i]} (muss > 0 sein).";
                    return false;
                }
            }
            var seen = new bool[len];
            foreach (int f in state.FreeList)
            {
                if (f <= 0 || f >= len) { error = $"EntityStoreState: Freiliste enthält ungültigen Index {f}."; return false; }
                if (state.Alive[f]) { error = $"EntityStoreState: Index {f} ist lebend und zugleich frei."; return false; }
                if (seen[f]) { error = $"EntityStoreState: Index {f} steht doppelt in der Freiliste."; return false; }
                seen[f] = true;
            }
            int alive = 0;
            for (int i = 1; i < len; i++)
            {
                if (state.Alive[i]) alive++;
                else if (!seen[i]) { error = $"EntityStoreState: Index {i} ist weder lebend noch frei."; return false; }
            }

            int cap = Math.Max(2, len);
            _generations = new int[cap];
            _alive = new bool[cap];
            Array.Copy(state.Generations, _generations, len);
            Array.Copy(state.Alive, _alive, len);
            _length = len;
            _free.Clear();
            foreach (int f in state.FreeList) _free.Enqueue(f);
            _aliveCount = alive;
            error = null;
            return true;
        }

        private void Grow(int newCapacity)
        {
            Array.Resize(ref _generations, newCapacity);
            Array.Resize(ref _alive, newCapacity);
        }
    }

    [Serializable]
    public sealed class EntityStoreState             // DTO
    {
        public int[] Generations;                    // je Index
        public bool[] Alive;                         // je Index
        public int[] FreeList;                       // Reihenfolge wird erhalten
    }
}
