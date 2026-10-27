using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World.Navigation
{
    // Zusammenhangskomponenten der begehbaren Kacheln je MovementClass. Da A* nicht über Ecken schneidet,
    // ist die Erreichbarkeit genau die 4er-Nachbarschaft. Neuberechnung erst bei der nächsten Abfrage nach einer
    // Änderung (NavigationVersion), damit viele Änderungen in einem Tick nur eine Berechnung kosten.
    public sealed class NavRegions
    {
        const int ClassCount = 3;

        readonly IWorldQuery _world;
        readonly int[][] _labels = new int[ClassCount][];
        readonly int[] _version = { -1, -1, -1 };
        readonly int[] _regionCount = new int[ClassCount];
        readonly Stack<int> _stack = new Stack<int>();

        public NavRegions(IWorldQuery world)
        {
            _world = world;
        }

        // 0 = nicht begehbar.
        public int RegionOf(int x, int y, MovementClass mc)
        {
            if (!_world.InBounds(x, y))
                return 0;
            return Labels(mc)[y * _world.Width + x];
        }

        // Wie RegionOf; steht die Einheit auf einer blockierten Kachel, zählt die erste begehbare Nachbarkachel.
        public int RegionOfStart(int x, int y, MovementClass mc)
        {
            int r = RegionOf(x, y, mc);
            if (r != 0)
                return r;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    r = RegionOf(x + dx, y + dy, mc);
                    if (r != 0)
                        return r;
                }
            }
            return 0;
        }

        public int RegionCount(MovementClass mc)
        {
            Labels(mc);
            return _regionCount[(int)mc];
        }

        int[] Labels(MovementClass mc)
        {
            int m = (int)mc;
            int n = _world.Width * _world.Height;
            if (_labels[m] == null || _labels[m].Length != n)
            {
                _labels[m] = new int[n];
                _version[m] = -1;
            }
            if (_version[m] != _world.NavigationVersion)
                Rebuild(mc, _labels[m]);
            return _labels[m];
        }

        void Rebuild(MovementClass mc, int[] labels)
        {
            int w = _world.Width, h = _world.Height, n = w * h;
            var walkable = new bool[n];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    walkable[y * w + x] = _world.IsWalkable(x, y, mc);
            }

            System.Array.Clear(labels, 0, n);
            int next = 0;
            for (int start = 0; start < n; start++)
            {
                if (!walkable[start] || labels[start] != 0)
                    continue;
                next++;
                labels[start] = next;
                _stack.Push(start);
                while (_stack.Count > 0)
                {
                    int i = _stack.Pop();
                    int x = i % w, y = i / w;
                    if (x > 0) Visit(i - 1);
                    if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w);
                    if (y < h - 1) Visit(i + w);
                }
            }
            _regionCount[(int)mc] = next;
            _version[(int)mc] = _world.NavigationVersion;

            void Visit(int j)
            {
                if (!walkable[j] || labels[j] != 0)
                    return;
                labels[j] = next;
                _stack.Push(j);
            }
        }
    }
}
