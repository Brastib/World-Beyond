using System.Collections.Generic;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldObjects
{
    /// <summary>
    /// Unity-frei: Kopie von TileData.OwnerCivId für die Gebietsdarstellung. Die Simulation meldet Besitzwechsel
    /// gebündelt ohne Ereignis je Kachel, darum vergleicht Refresh das ganze Raster (in Abständen, nicht jedes Bild)
    /// und liefert die Chunks, deren Besitz sich geändert hat. Grenzkanten: Seite einer besessenen Kachel,
    /// deren Nachbar einen anderen (oder keinen) Besitzer hat.
    /// </summary>
    public sealed class TerritoryGrid
    {
        public enum Side : byte { South = 0, East = 1, North = 2, West = 3 }   // South = -Y, North = +Y

        public readonly int Width, Height, ChunkSize, ChunkCountX, ChunkCountY;
        readonly int[] _owner;
        readonly bool[] _dirtyChunk;

        public TerritoryGrid(int width, int height, int chunkSize = WorldConstants.ChunkSize)
        {
            Width = width;
            Height = height;
            ChunkSize = chunkSize;
            ChunkCountX = (width + chunkSize - 1) / chunkSize;
            ChunkCountY = (height + chunkSize - 1) / chunkSize;
            _owner = new int[width * height];
            _dirtyChunk = new bool[ChunkCountX * ChunkCountY];
        }

        public int OwnerAt(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? 0 : _owner[y * Width + x];

        /// <summary>Liest das Besitzraster; dirtyChunks erhält die Chunk-Indizes (cy * ChunkCountX + cx) mit Änderungen,
        /// inklusive Nachbar-Chunks an der Kante (deren Grenzlinien hängen am Nachbarn).</summary>
        public void Refresh(IWorldQuery world, List<int> dirtyChunks)
        {
            dirtyChunks.Clear();
            System.Array.Clear(_dirtyChunk, 0, _dirtyChunk.Length);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int owner = world.Get(x, y).OwnerCivId;
                    int i = y * Width + x;
                    if (_owner[i] == owner)
                        continue;
                    _owner[i] = owner;
                    MarkTile(x, y);
                    MarkTile(x - 1, y);
                    MarkTile(x + 1, y);
                    MarkTile(x, y - 1);
                    MarkTile(x, y + 1);
                }
            }
            for (int c = 0; c < _dirtyChunk.Length; c++)
            {
                if (_dirtyChunk[c])
                    dirtyChunks.Add(c);
            }
        }

        void MarkTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            _dirtyChunk[y / ChunkSize * ChunkCountX + x / ChunkSize] = true;
        }

        public bool IsBorder(int x, int y, Side side)
        {
            int owner = OwnerAt(x, y);
            if (owner == 0)
                return false;
            switch (side)
            {
                case Side.South: return OwnerAt(x, y - 1) != owner;
                case Side.East: return OwnerAt(x + 1, y) != owner;
                case Side.North: return OwnerAt(x, y + 1) != owner;
                default: return OwnerAt(x - 1, y) != owner;
            }
        }

        public int OwnedTiles(int civId)
        {
            int n = 0;
            foreach (int o in _owner)
            {
                if (o == civId)
                    n++;
            }
            return n;
        }
    }
}
