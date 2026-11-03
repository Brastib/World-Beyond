using System;
using System.Collections.Generic;

using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World
{
    // Gleichmäßiges Zellraster über die Weltfläche (ARCHITECTURE §4). Jede Zelle hält eine doppelt verkettete Liste
    // in Arrays, adressiert über EntityId.Index; Einfügen, Bewegen und Entfernen sind O(1) und allokationsfrei.
    public sealed class SpatialHash : ISpatialIndex
    {
        readonly float _cellSize;
        readonly float _invCellSize;
        readonly int _cellsX, _cellsZ;
        readonly int[] _cellHead;

        // Je Entity-Index
        EntityId[] _ids = new EntityId[0];
        int[] _cell = new int[0];          // -1 = nicht enthalten
        int[] _next = new int[0];
        int[] _prev = new int[0];
        float[] _x = new float[0];
        float[] _z = new float[0];

        public int Count { get; private set; }

        public SpatialHash(float worldSizeX, float worldSizeZ, float cellSize = 32f, int initialCapacity = 1024)
        {
            if (!(cellSize > 0f) || !(worldSizeX > 0f) || !(worldSizeZ > 0f))
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Welt- und Zellgröße müssen positiv sein.");
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
            _cellsX = Math.Max(1, (int)Math.Ceiling(worldSizeX / cellSize));
            _cellsZ = Math.Max(1, (int)Math.Ceiling(worldSizeZ / cellSize));
            _cellHead = new int[_cellsX * _cellsZ];
            for (int i = 0; i < _cellHead.Length; i++)
                _cellHead[i] = -1;
            EnsureCapacity(initialCapacity);
        }

        public bool Contains(EntityId id) =>
            id.IsValid && id.Index < _ids.Length && _cell[id.Index] >= 0 && _ids[id.Index].Equals(id);

        public void Insert(EntityId id, float x, float z)
        {
            if (!id.IsValid)
                throw new ArgumentException("Ungültige EntityId.", nameof(id));
            EnsureCapacity(id.Index + 1);
            int slot = id.Index;
            if (_cell[slot] >= 0)
            {
                // Index wird von einer neueren Generation wiederverwendet oder doppelt eingefügt: alten Eintrag ersetzen.
                Unlink(slot);
                Count--;
            }
            _ids[slot] = id;
            _x[slot] = x;
            _z[slot] = z;
            Link(slot, CellOf(x, z));
            Count++;
        }

        public void Move(EntityId id, float x, float z)
        {
            if (!Contains(id))
                return;
            int slot = id.Index;
            _x[slot] = x;
            _z[slot] = z;
            int cell = CellOf(x, z);
            if (cell == _cell[slot])
                return;
            Unlink(slot);
            Link(slot, cell);
        }

        public void Remove(EntityId id)
        {
            if (!Contains(id))
                return;
            Unlink(id.Index);
            Count--;
        }

        public bool TryGetPosition(EntityId id, out float x, out float z)
        {
            if (!Contains(id))
            {
                x = z = 0f;
                return false;
            }
            x = _x[id.Index];
            z = _z[id.Index];
            return true;
        }

        public void QueryRadius(float x, float z, float radius, List<EntityId> results)
        {
            results.Clear();
            if (radius < 0f)
                return;
            float r2 = radius * radius;
            GetCellRange(x - radius, z - radius, x + radius, z + radius, out int cx0, out int cz0, out int cx1, out int cz1);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    for (int s = _cellHead[cz * _cellsX + cx]; s >= 0; s = _next[s])
                    {
                        float dx = _x[s] - x, dz = _z[s] - z;
                        if (dx * dx + dz * dz <= r2)
                            results.Add(_ids[s]);
                    }
                }
            }
        }

        public void QueryRect(float minX, float minZ, float maxX, float maxZ, List<EntityId> results)
        {
            results.Clear();
            if (minX > maxX) (minX, maxX) = (maxX, minX);
            if (minZ > maxZ) (minZ, maxZ) = (maxZ, minZ);
            GetCellRange(minX, minZ, maxX, maxZ, out int cx0, out int cz0, out int cx1, out int cz1);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    for (int s = _cellHead[cz * _cellsX + cx]; s >= 0; s = _next[s])
                    {
                        if (_x[s] >= minX && _x[s] <= maxX && _z[s] >= minZ && _z[s] <= maxZ)
                            results.Add(_ids[s]);
                    }
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _cellHead.Length; i++)
                _cellHead[i] = -1;
            for (int i = 0; i < _cell.Length; i++)
                _cell[i] = -1;
            Count = 0;
        }

        int CellOf(float x, float z)
        {
            int cx = (int)Math.Floor(x * _invCellSize);
            int cz = (int)Math.Floor(z * _invCellSize);
            cx = cx < 0 ? 0 : (cx >= _cellsX ? _cellsX - 1 : cx);
            cz = cz < 0 ? 0 : (cz >= _cellsZ ? _cellsZ - 1 : cz);
            return cz * _cellsX + cx;
        }

        void GetCellRange(float minX, float minZ, float maxX, float maxZ, out int cx0, out int cz0, out int cx1, out int cz1)
        {
            cx0 = Clamp((int)Math.Floor(minX * _invCellSize), _cellsX);
            cz0 = Clamp((int)Math.Floor(minZ * _invCellSize), _cellsZ);
            cx1 = Clamp((int)Math.Floor(maxX * _invCellSize), _cellsX);
            cz1 = Clamp((int)Math.Floor(maxZ * _invCellSize), _cellsZ);
        }

        static int Clamp(int v, int count) => v < 0 ? 0 : (v >= count ? count - 1 : v);

        void Link(int slot, int cell)
        {
            int head = _cellHead[cell];
            _next[slot] = head;
            _prev[slot] = -1;
            if (head >= 0)
                _prev[head] = slot;
            _cellHead[cell] = slot;
            _cell[slot] = cell;
        }

        void Unlink(int slot)
        {
            int cell = _cell[slot];
            int n = _next[slot], p = _prev[slot];
            if (p >= 0)
                _next[p] = n;
            else
                _cellHead[cell] = n;
            if (n >= 0)
                _prev[n] = p;
            _cell[slot] = -1;
            _next[slot] = _prev[slot] = -1;
        }

        void EnsureCapacity(int capacity)
        {
            if (capacity <= _ids.Length)
                return;
            int old = _ids.Length;
            int size = Math.Max(capacity, Math.Max(16, old * 2));
            Array.Resize(ref _ids, size);
            Array.Resize(ref _cell, size);
            Array.Resize(ref _next, size);
            Array.Resize(ref _prev, size);
            Array.Resize(ref _x, size);
            Array.Resize(ref _z, size);
            for (int i = old; i < size; i++)
                _cell[i] = _next[i] = _prev[i] = -1;
        }
    }
}
