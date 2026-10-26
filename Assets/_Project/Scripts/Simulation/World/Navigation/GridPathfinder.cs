using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World.Navigation
{
    // A* auf dem Kachelraster (8er-Nachbarschaft ohne Eckenschneiden), Kosten aus IWorldQuery.MoveCost.
    // - Budget: ProcessRequests(maxExpansions) wird einmal pro Tick vom PathfindingSystem aufgerufen. Eine Suche, die das
    //   Budget überschreitet, wird im nächsten Tick fortgesetzt (eine aktive Suche, Zustand in wiederverwendeten Arrays).
    // - Regionen: Ziele in einer anderen Zusammenhangskomponente werden ohne Suche sofort als NotFound gemeldet.
    // - Ändert sich die Welt während einer Suche (NavigationVersion), startet die Suche neu.
    // Pfadanfragen werden nicht gespeichert (ARCHITECTURE §11): Einheiten fordern nach dem Laden neu an.
    public sealed class GridPathfinder : IPathfinder
    {
        const float Sqrt2 = 1.41421356f;
        const int SmoothingLookahead = 24;

        sealed class Request
        {
            public int Id;
            public int FromX, FromY, ToX, ToY;
            public MovementClass Mc;
            public PathStatus Status;
            public readonly List<(int x, int y)> Path = new List<(int x, int y)>();
        }

        readonly IWorldQuery _world;
        readonly NavRegions _regions;
        readonly Dictionary<int, Request> _requests = new Dictionary<int, Request>();
        readonly Queue<Request> _pending = new Queue<Request>();
        readonly List<(int x, int y)> _rawPath = new List<(int x, int y)>();
        int _nextId = 1;

        // Suchzustand
        int _n;
        float[] _g;
        int[] _parent;
        int[] _openStamp;
        int[] _closedStamp;
        int _stamp;
        readonly NodeHeap _open = new NodeHeap();
        Request _active;
        int _activeGoal;
        int _activeNavVersion;

        public int PendingCount => _pending.Count + (_active != null ? 1 : 0);

        // Gesamtzahl der Knotenexpansionen seit Erzeugung (Diagnose, Performance-Tests).
        public long TotalExpansions { get; private set; }

        public GridPathfinder(IWorldQuery world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _regions = new NavRegions(world);
        }

        public int RequestPath(int fromX, int fromY, int toX, int toY, MovementClass mc)
        {
            var r = new Request
            {
                Id = _nextId++,
                FromX = fromX,
                FromY = fromY,
                ToX = toX,
                ToY = toY,
                Mc = mc,
                Status = PathStatus.Pending,
            };
            if (_nextId == int.MaxValue)
                _nextId = 1;
            _requests[r.Id] = r;

            if (!_world.InBounds(fromX, fromY) || !_world.InBounds(toX, toY))
                r.Status = PathStatus.Invalid;
            else if (!AreConnected(fromX, fromY, toX, toY, mc))
                r.Status = PathStatus.NotFound;
            else
                _pending.Enqueue(r);
            return r.Id;
        }

        public PathStatus TryGetPath(int requestId, List<(int x, int y)> path)
        {
            if (!_requests.TryGetValue(requestId, out Request r))
                return PathStatus.Invalid;
            if (r.Status == PathStatus.Pending)
                return PathStatus.Pending;
            if (path != null)
            {
                path.Clear();
                path.AddRange(r.Path);
            }
            _requests.Remove(requestId);
            return r.Status;
        }

        public void Cancel(int requestId)
        {
            if (!_requests.TryGetValue(requestId, out Request r))
                return;
            _requests.Remove(requestId);
            r.Status = PathStatus.Invalid;
            if (_active == r)
                _active = null;
            // Wartende Anfrage bleibt in der Queue und wird beim Herausnehmen übersprungen.
        }

        // Synchrone Suche, unabhängig vom Budget (für Werkzeuge und Tests; im Spiel RequestPath verwenden).
        public PathStatus FindPathImmediate(int fromX, int fromY, int toX, int toY, MovementClass mc, List<(int x, int y)> path)
        {
            int id = RequestPath(fromX, fromY, toX, toY, mc);
            Request r = _requests[id];
            if (r.Status == PathStatus.Pending)
            {
                // Laufende Suchen anderer Anfragen nicht stören: diese Anfrage vorziehen.
                Request suspended = _active;
                _active = null;
                Begin(r);
                while (_active == r)
                    Step(int.MaxValue);
                if (suspended != null && _requests.ContainsKey(suspended.Id))
                    _pending.Enqueue(suspended);
            }
            return TryGetPath(id, path);
        }

        public bool AreConnected(int ax, int ay, int bx, int by, MovementClass mc)
        {
            if (!_world.InBounds(ax, ay) || !_world.InBounds(bx, by))
                return false;
            int ra = _regions.RegionOfStart(ax, ay, mc);
            int rb = _regions.RegionOf(bx, by, mc);
            return ra != 0 && ra == rb;
        }

        public float EstimateCost(int fromX, int fromY, int toX, int toY, MovementClass mc)
        {
            if (!AreConnected(fromX, fromY, toX, toY, mc))
                return float.PositiveInfinity;
            float a = _world.MoveCost(fromX, fromY, mc);
            float b = _world.MoveCost(toX, toY, mc);
            float avg = float.IsInfinity(a) ? b : (a + b) * 0.5f;
            return Octile(fromX, fromY, toX, toY) * Math.Max(1f, avg);
        }

        // Verarbeitet wartende Anfragen bis maxExpansions Knoten expandiert wurden.
        public void ProcessRequests(int maxExpansions)
        {
            int budget = maxExpansions;
            while (budget > 0)
            {
                if (_active == null)
                {
                    if (_pending.Count == 0)
                        return;
                    Request next = _pending.Dequeue();
                    if (next.Status != PathStatus.Pending || !_requests.ContainsKey(next.Id))
                        continue;
                    // Die Welt kann sich seit der Anfrage geändert haben.
                    if (!AreConnected(next.FromX, next.FromY, next.ToX, next.ToY, next.Mc))
                    {
                        next.Status = PathStatus.NotFound;
                        continue;
                    }
                    Begin(next);
                }
                if (_activeNavVersion != _world.NavigationVersion)
                {
                    Request restart = _active;
                    _active = null;
                    if (AreConnected(restart.FromX, restart.FromY, restart.ToX, restart.ToY, restart.Mc))
                        Begin(restart);
                    else
                    {
                        restart.Status = PathStatus.NotFound;
                        continue;
                    }
                }
                budget -= Step(budget);
            }
        }

        void EnsureArrays()
        {
            int n = _world.Width * _world.Height;
            if (_n == n && _g != null)
                return;
            _n = n;
            _g = new float[n];
            _parent = new int[n];
            _openStamp = new int[n];
            _closedStamp = new int[n];
            _stamp = 0;
        }

        void Begin(Request r)
        {
            EnsureArrays();
            _stamp++;
            if (_stamp == int.MaxValue)
            {
                Array.Clear(_openStamp, 0, _n);
                Array.Clear(_closedStamp, 0, _n);
                _stamp = 1;
            }
            _open.Clear();
            _active = r;
            _activeNavVersion = _world.NavigationVersion;
            int w = _world.Width;
            int start = r.FromY * w + r.FromX;
            _activeGoal = r.ToY * w + r.ToX;
            _g[start] = 0f;
            _parent[start] = -1;
            _openStamp[start] = _stamp;
            _open.Push(start, Heuristic(r.FromX, r.FromY, r.ToX, r.ToY, r.Mc));
        }

        // Expandiert bis zu budget Knoten der aktiven Suche; liefert die Anzahl tatsächlich expandierter Knoten.
        int Step(int budget)
        {
            Request r = _active;
            int w = _world.Width, h = _world.Height;
            MovementClass mc = r.Mc;
            int used = 0;
            while (used < budget)
            {
                if (_open.Count == 0)
                {
                    r.Status = PathStatus.NotFound;
                    _active = null;
                    return used + 1;
                }
                int cur = _open.Pop();
                if (_closedStamp[cur] == _stamp)
                    continue;
                _closedStamp[cur] = _stamp;
                used++;
                TotalExpansions++;

                if (cur == _activeGoal)
                {
                    Finish(r, cur);
                    _active = null;
                    return used;
                }

                int cx = cur % w, cy = cur / w;
                float curCost = Cost(cx, cy, mc, cur == r.FromY * w + r.FromX);
                for (int dir = 0; dir < 8; dir++)
                {
                    int dx = Dx[dir], dy = Dy[dir];
                    int nx = cx + dx, ny = cy + dy;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h)
                        continue;
                    int ni = ny * w + nx;
                    if (_closedStamp[ni] == _stamp)
                        continue;
                    float nCost = _world.MoveCost(nx, ny, mc);
                    if (float.IsInfinity(nCost))
                        continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!_world.IsWalkable(cx + dx, cy, mc) || !_world.IsWalkable(cx, cy + dy, mc)))
                        continue;
                    float step = (curCost + nCost) * 0.5f * (diagonal ? Sqrt2 : 1f);
                    float g = _g[cur] + step;
                    if (_openStamp[ni] == _stamp && g >= _g[ni])
                        continue;
                    _openStamp[ni] = _stamp;
                    _g[ni] = g;
                    _parent[ni] = cur;
                    _open.Push(ni, g + Heuristic(nx, ny, r.ToX, r.ToY, mc));
                }
            }
            return used;
        }

        // Startkachel darf blockiert sein (Einheit steht z. B. auf einer gerade bebauten Kachel); dann normale Kosten.
        float Cost(int x, int y, MovementClass mc, bool isStart)
        {
            float c = _world.MoveCost(x, y, mc);
            if (float.IsInfinity(c))
                return isStart ? 1f : c;
            return c;
        }

        void Finish(Request r, int goal)
        {
            int w = _world.Width;
            _rawPath.Clear();
            for (int i = goal; i >= 0; i = _parent[i])
                _rawPath.Add((i % w, i / w));
            _rawPath.Reverse();
            Smooth(_rawPath, r.Path, r.Mc);
            r.Status = PathStatus.Found;
        }

        // Entfernt Zwischenpunkte, wenn die direkte Linie begehbar ist und nicht durch teureres Gelände führt,
        // damit Einheiten und Fahrzeuge nicht im Zickzack laufen.
        void Smooth(List<(int x, int y)> raw, List<(int x, int y)> result, MovementClass mc)
        {
            result.Clear();
            if (raw.Count == 0)
                return;
            result.Add(raw[0]);
            int i = 0;
            int last = raw.Count - 1;
            while (i < last)
            {
                int best = i + 1;
                int far = Math.Min(last, i + SmoothingLookahead);
                for (int j = far; j > i + 1; j--)
                {
                    float maxCost = 0f;
                    for (int k = i + 1; k <= j; k++)
                    {
                        float c = _world.MoveCost(raw[k].x, raw[k].y, mc);
                        if (c > maxCost) maxCost = c;
                    }
                    if (LineWalkable(raw[i].x, raw[i].y, raw[j].x, raw[j].y, mc, maxCost))
                    {
                        best = j;
                        break;
                    }
                }
                result.Add(raw[best]);
                i = best;
            }
        }

        // Prüft alle Kacheln, die die Linie berührt (Supercover); bei exakt diagonalen Schritten beide Nachbarn.
        bool LineWalkable(int x0, int y0, int x1, int y1, MovementClass mc, float maxCost)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x1 > x0 ? 1 : -1, sy = y1 > y0 ? 1 : -1;
            int x = x0, y = y0;
            int err = dx - dy;
            int n = dx + dy;
            for (int k = 0; k < n; k++)
            {
                int e2 = 2 * err;
                if (e2 > -dy && e2 < dx)
                {
                    // Linie geht genau durch eine Ecke: beide berührten Kacheln müssen frei sein.
                    if (!Ok(x + sx, y) || !Ok(x, y + sy))
                        return false;
                    err += dx - dy;
                    x += sx;
                    y += sy;
                    k++;
                }
                else if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                else
                {
                    err += dx;
                    y += sy;
                }
                if (!Ok(x, y))
                    return false;
            }
            return true;

            bool Ok(int tx, int ty)
            {
                float c = _world.MoveCost(tx, ty, mc);
                return !float.IsInfinity(c) && c <= maxCost + 1e-4f;
            }
        }

        static float Octile(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int min = Math.Min(dx, dy), max = Math.Max(dx, dy);
            return (max - min) + min * Sqrt2;
        }

        static float Heuristic(int x0, int y0, int x1, int y1, MovementClass mc) =>
            Octile(x0, y0, x1, y1) * MovementRules.MinCost(mc);

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // Binärer Min-Heap über (f, Knoten); Gleichstand nach Knotenindex (deterministisch).
        sealed class NodeHeap
        {
            int[] _nodes = new int[256];
            float[] _keys = new float[256];
            public int Count { get; private set; }

            public void Clear() => Count = 0;

            public void Push(int node, float key)
            {
                if (Count == _nodes.Length)
                {
                    Array.Resize(ref _nodes, Count * 2);
                    Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                _nodes[i] = node;
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

            public int Pop()
            {
                int top = _nodes[0];
                Count--;
                if (Count > 0)
                {
                    _nodes[0] = _nodes[Count];
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
                return top;
            }

            bool Less(int a, int b) => _keys[a] < _keys[b] || (_keys[a] == _keys[b] && _nodes[a] < _nodes[b]);

            void Swap(int a, int b)
            {
                (_nodes[a], _nodes[b]) = (_nodes[b], _nodes[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
