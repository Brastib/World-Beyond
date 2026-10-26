using System;
using System.Collections.Generic;

namespace WorldBeyond.Simulation.World.Generation
{
    // Meer, Seen und Flüsse aus dem Höhenfeld.
    // Verfahren: Priority-Flood (Barnes et al. 2014) vom Meer aus. Dabei entstehen gefüllte Höhen ohne abflusslose Senken
    // und für jede Kachel eine Abflussrichtung. Größere Senken werden Seen, kleine aufgefüllt. Abflussmengen werden
    // entgegen der Flutreihenfolge aufsummiert; Flüsse sind Kacheln mit hoher Abflussmenge, die zu den größten Mündungen gehören.
    internal sealed class Hydrology
    {
        const float LakeMinDepth = 0.6f;
        const int LakeMinTiles = 5;
        const int WaterDistanceCap = 12;

        readonly int _w, _h, _n;

        public readonly bool[] Ocean;
        public readonly bool[] Lake;
        public readonly bool[] River;
        public readonly float[] WaterLevel;
        public readonly TileFlags[] Flags;
        public readonly int[] Accumulation;
        // Abstand (Kacheln, 4er-Nachbarschaft) zum nächsten Wasser, gekappt bei WaterDistanceCap.
        public readonly byte[] WaterDistance;
        public int RiverMouthCount { get; private set; }

        readonly int[] _parent;
        readonly int[] _order;

        public Hydrology(int width, int height)
        {
            _w = width;
            _h = height;
            _n = width * height;
            Ocean = new bool[_n];
            Lake = new bool[_n];
            River = new bool[_n];
            WaterLevel = new float[_n];
            Flags = new TileFlags[_n];
            Accumulation = new int[_n];
            WaterDistance = new byte[_n];
            _parent = new int[_n];
            _order = new int[_n];
        }

        public void Run(WorldGenSettings s, float[] height, bool[] oceanOut)
        {
            MarkOcean(s, height);
            float[] filled = new float[_n];
            int orderCount = PriorityFlood(s, height, filled);
            BuildLakes(height, filled);
            Accumulate(orderCount);
            BuildRivers(s, height, filled, orderCount);
            ComputeWaterDistance();
            Array.Copy(Ocean, oceanOut, _n);
        }

        void MarkOcean(WorldGenSettings s, float[] height)
        {
            var queue = new Queue<int>();
            for (int x = 0; x < _w; x++)
            {
                Seed(x, 0);
                Seed(x, _h - 1);
            }
            for (int y = 1; y < _h - 1; y++)
            {
                Seed(0, y);
                Seed(_w - 1, y);
            }

            void Seed(int x, int y)
            {
                int i = y * _w + x;
                if (!Ocean[i] && height[i] < s.SeaLevel)
                {
                    Ocean[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % _w, y = i / _w;
                TryAdd(x - 1, y);
                TryAdd(x + 1, y);
                TryAdd(x, y - 1);
                TryAdd(x, y + 1);
            }

            void TryAdd(int x, int y)
            {
                if ((uint)x >= (uint)_w || (uint)y >= (uint)_h)
                    return;
                int i = y * _w + x;
                if (Ocean[i] || height[i] >= s.SeaLevel)
                    return;
                Ocean[i] = true;
                queue.Enqueue(i);
            }

            for (int i = 0; i < _n; i++)
            {
                if (Ocean[i])
                    WaterLevel[i] = s.SeaLevel;
            }
        }

        // Liefert die Anzahl der Landkacheln in Flutreihenfolge (_order), Eltern stehen vor ihren Kindern.
        int PriorityFlood(WorldGenSettings s, float[] height, float[] filled)
        {
            // Gleichstand auf Ebenen nach glattem Rauschen statt nach Index: Abflüsse über Ebenen winden sich,
            // statt als gerade Linien parallel zu den Achsen zu laufen. Ändert keine Höhen.
            float[] tie = new float[_n];
            ulong seedT = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepFlatDrainage);
            for (int i = 0; i < _n; i++)
                tie[i] = HashNoise.Fbm(seedT, (i % _w) / 9f, (i / _w) / 9f, 2);
            var heap = new MinHeap(_n, tie);
            bool[] done = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                _parent[i] = -1;
                if (Ocean[i])
                {
                    filled[i] = s.SeaLevel;
                    done[i] = true;
                }
            }
            // Startfront: Landkacheln am Meer.
            for (int i = 0; i < _n; i++)
            {
                if (!Ocean[i])
                    continue;
                int x = i % _w, y = i / _w;
                Push(x - 1, y, i);
                Push(x + 1, y, i);
                Push(x, y - 1, i);
                Push(x, y + 1, i);
            }

            int count = 0;
            while (heap.Count > 0)
            {
                heap.Pop(out int i, out _);
                if (done[i])
                    continue;
                done[i] = true;
                _order[count++] = i;
                int x = i % _w, y = i / _w;
                Push(x - 1, y, i);
                Push(x + 1, y, i);
                Push(x, y - 1, i);
                Push(x, y + 1, i);
            }
            return count;

            void Push(int x, int y, int from)
            {
                if ((uint)x >= (uint)_w || (uint)y >= (uint)_h)
                    return;
                int j = y * _w + x;
                if (done[j])
                    return;
                // Ohne Epsilon: auf weiten Ebenen würde ein Epsilon sich aufsummieren und die Ebene fälschlich als See füllen.
                // Die Abflussrichtung auf Ebenen ergibt sich aus der Flutreihenfolge (Gleichstand nach Index).
                float level = Math.Max(height[j], filled[from]);
                // Erste (= niedrigste) Zuweisung gewinnt; spätere Einträge derselben Kachel werden beim Pop verworfen.
                if (_parent[j] == -1 || level < filled[j])
                {
                    filled[j] = level;
                    _parent[j] = from;
                    heap.Push(j, level);
                }
            }
        }

        void BuildLakes(float[] height, float[] filled)
        {
            bool[] visited = new bool[_n];
            var component = new List<int>();
            var stack = new Stack<int>();
            for (int start = 0; start < _n; start++)
            {
                if (visited[start] || Ocean[start] || filled[start] - height[start] < LakeMinDepth)
                    continue;

                // Zusammenhängende tiefe Senke sammeln.
                component.Clear();
                stack.Push(start);
                visited[start] = true;
                float surface = 0f;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    component.Add(i);
                    if (filled[i] > surface)
                        surface = filled[i];
                    int x = i % _w, y = i / _w;
                    Visit(x - 1, y);
                    Visit(x + 1, y);
                    Visit(x, y - 1);
                    Visit(x, y + 1);
                }

                void Visit(int x, int y)
                {
                    if ((uint)x >= (uint)_w || (uint)y >= (uint)_h)
                        return;
                    int j = y * _w + x;
                    if (visited[j] || Ocean[j] || filled[j] - height[j] < LakeMinDepth)
                        return;
                    visited[j] = true;
                    stack.Push(j);
                }

                if (component.Count < LakeMinTiles)
                    continue;

                // Seefläche ist eben: alle angrenzenden Kacheln unter der Oberfläche gehören dazu.
                stack.Clear();
                foreach (int i in component)
                {
                    Lake[i] = true;
                    stack.Push(i);
                }
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    WaterLevel[i] = surface;
                    int x = i % _w, y = i / _w;
                    Grow(x - 1, y);
                    Grow(x + 1, y);
                    Grow(x, y - 1);
                    Grow(x, y + 1);
                }

                void Grow(int x, int y)
                {
                    if ((uint)x >= (uint)_w || (uint)y >= (uint)_h)
                        return;
                    int j = y * _w + x;
                    if (Lake[j] || Ocean[j] || height[j] >= surface - 0.05f)
                        return;
                    Lake[j] = true;
                    visited[j] = true;
                    stack.Push(j);
                }
            }

            // Kleine Senken auffüllen, damit Wasser sauber abfließt.
            for (int i = 0; i < _n; i++)
            {
                if (Ocean[i])
                    continue;
                if (Lake[i])
                {
                    Flags[i] |= TileFlags.Lake;
                    continue;
                }
                if (filled[i] > height[i])
                    height[i] = filled[i];
            }
        }

        void Accumulate(int orderCount)
        {
            for (int k = 0; k < orderCount; k++)
                Accumulation[_order[k]] = 1;
            for (int k = orderCount - 1; k >= 0; k--)
            {
                int i = _order[k];
                int p = _parent[i];
                if (p >= 0 && !Ocean[p])
                    Accumulation[p] += Accumulation[i];
            }
        }

        void BuildRivers(WorldGenSettings s, float[] height, float[] filled, int orderCount)
        {
            int minAcc = Math.Max(30, _n / 1500);
            int[] mouth = new int[_n];
            var mouths = new List<int>();
            for (int k = 0; k < orderCount; k++)
            {
                int i = _order[k];
                if (Lake[i])
                {
                    mouth[i] = -1;
                    continue;
                }
                int p = _parent[i];
                if (p < 0 || Ocean[p] || Lake[p])
                {
                    mouth[i] = i;
                    if (Accumulation[i] >= minAcc)
                        mouths.Add(i);
                }
                else
                {
                    mouth[i] = mouth[p];
                }
            }

            // Die größten Einzugsgebiete bekommen Flüsse; bei Gleichstand entscheidet der Index (deterministisch).
            mouths.Sort((a, b) => Accumulation[a] != Accumulation[b] ? Accumulation[b].CompareTo(Accumulation[a]) : a.CompareTo(b));
            int take = Math.Min(s.RiverCount, mouths.Count);
            var selected = new HashSet<int>();
            for (int k = 0; k < take; k++)
                selected.Add(mouths[k]);
            RiverMouthCount = take;

            // In Flutreihenfolge (Abfluss vor Zufluss), damit die Wasseroberfläche flussabwärts nie ansteigt
            // und an der Mündung nicht unter Meer oder See liegt.
            for (int k = 0; k < orderCount; k++)
            {
                int i = _order[k];
                if (Ocean[i] || Lake[i] || Accumulation[i] < minAcc || mouth[i] < 0 || !selected.Contains(mouth[i]))
                    continue;
                River[i] = true;
                float size = Math.Min(1f, Accumulation[i] / (float)(minAcc * 20));
                float surface = filled[i] - 0.25f;
                int p = _parent[i];
                if (p >= 0 && (Ocean[p] || Lake[p] || River[p]) && WaterLevel[p] > surface)
                    surface = WaterLevel[p];
                WaterLevel[i] = surface;
                height[i] = surface - (0.8f + 1.2f * size);
            }

            for (int i = 0; i < _n; i++)
            {
                if (!River[i])
                    continue;
                int x = i % _w, y = i / _w;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= (uint)_w || (uint)ny >= (uint)_h)
                            continue;
                        int j = ny * _w + nx;
                        if (!River[j] && !Ocean[j] && !Lake[j])
                            Flags[j] |= TileFlags.RiverBank;
                    }
                }
            }
        }

        void ComputeWaterDistance()
        {
            var queue = new Queue<int>();
            for (int i = 0; i < _n; i++)
            {
                if (Ocean[i] || Lake[i] || River[i])
                {
                    WaterDistance[i] = 0;
                    queue.Enqueue(i);
                }
                else
                {
                    WaterDistance[i] = WaterDistanceCap;
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int d = WaterDistance[i] + 1;
                if (d >= WaterDistanceCap)
                    continue;
                int x = i % _w, y = i / _w;
                Relax(x - 1, y);
                Relax(x + 1, y);
                Relax(x, y - 1);
                Relax(x, y + 1);

                void Relax(int nx, int ny)
                {
                    if ((uint)nx >= (uint)_w || (uint)ny >= (uint)_h)
                        return;
                    int j = ny * _w + nx;
                    if (WaterDistance[j] <= d)
                        return;
                    WaterDistance[j] = (byte)d;
                    queue.Enqueue(j);
                }
            }
        }

        // Binärer Min-Heap über (Wert, Index); Gleichstand nach Index für deterministische Reihenfolge.
        sealed class MinHeap
        {
            int[] _items;
            float[] _keys;
            public int Count { get; private set; }

            readonly float[] _tie;

            public MinHeap(int capacity, float[] tie = null)
            {
                _tie = tie;
                _items = new int[Math.Max(16, capacity)];
                _keys = new float[_items.Length];
            }

            public void Push(int item, float key)
            {
                if (Count == _items.Length)
                {
                    Array.Resize(ref _items, _items.Length * 2);
                    Array.Resize(ref _keys, _keys.Length * 2);
                }
                int i = Count++;
                _items[i] = item;
                _keys[i] = key;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Less(i, p))
                        break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out int item, out float key)
            {
                item = _items[0];
                key = _keys[0];
                Count--;
                if (Count == 0)
                    return;
                _items[0] = _items[Count];
                _keys[0] = _keys[Count];
                int k = 0;
                while (true)
                {
                    int l = 2 * k + 1, r = l + 1, m = k;
                    if (l < Count && Less(l, m)) m = l;
                    if (r < Count && Less(r, m)) m = r;
                    if (m == k)
                        break;
                    Swap(k, m);
                    k = m;
                }
            }

            bool Less(int a, int b)
            {
                if (_keys[a] != _keys[b])
                    return _keys[a] < _keys[b];
                if (_tie != null)
                {
                    float ta = _tie[_items[a]], tb = _tie[_items[b]];
                    if (ta != tb)
                        return ta < tb;
                }
                return _items[a] < _items[b];
            }

            void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
