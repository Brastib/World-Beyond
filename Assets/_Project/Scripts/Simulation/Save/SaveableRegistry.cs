using System;
using System.Collections.Generic;

namespace WorldBeyond.Simulation.Save
{
    /// <summary>Alle ISaveable-Instanzen einer Simulation. Der SaveLoadService liest nur von hier.</summary>
    public sealed class SaveableRegistry
    {
        private readonly List<ISaveable> _items = new List<ISaveable>();
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        public void Add(ISaveable saveable)
        {
            if (saveable == null) throw new ArgumentNullException(nameof(saveable));
            if (string.IsNullOrWhiteSpace(saveable.SaveKey)) throw new ArgumentException("ISaveable.SaveKey ist leer.");
            if (saveable.StateType == null) throw new ArgumentException($"ISaveable '{saveable.SaveKey}' hat keinen StateType.");
            if (!_keys.Add(saveable.SaveKey))
                throw new InvalidOperationException($"SaveKey '{saveable.SaveKey}' ist doppelt registriert.");
            _items.Add(saveable);
        }

        /// <summary>Nach LoadOrder aufsteigend, bei Gleichstand in Registrierungsreihenfolge.</summary>
        public IReadOnlyList<ISaveable> InLoadOrder()
        {
            var list = new List<(int order, int seq, ISaveable s)>(_items.Count);
            for (int i = 0; i < _items.Count; i++) list.Add((_items[i].LoadOrder, i, _items[i]));
            list.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.seq.CompareTo(b.seq));
            var result = new ISaveable[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i].s;
            return result;
        }

        public bool TryGet(string key, out ISaveable saveable)
        {
            foreach (var s in _items)
                if (s.SaveKey == key) { saveable = s; return true; }
            saveable = null;
            return false;
        }

        public int Count => _items.Count;
    }
}
