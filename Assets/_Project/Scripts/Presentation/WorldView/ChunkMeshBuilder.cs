using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Baut die Meshes eines Chunks (ChunkSize × ChunkSize Kacheln).
    // Terrain: Gitterpunkte auf den Kachelmittelpunkten, Diagonale von (i, j) nach (i+1, j+1), also exakt wie
    // IWorldQuery.SampleHeight interpoliert; Einheiten stehen dadurch genau auf dem sichtbaren Boden.
    // Wasser: ein Viereck pro Wasserkachel auf Höhe WaterLevel, zu Landnachbarn hin um eine halbe Kachel
    // verlängert, damit die Uferlinie (zwischen den Kachelmittelpunkten) lückenlos bedeckt ist.
    public sealed class ChunkMeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1100);
        readonly List<Vector3> _normals = new List<Vector3>(1100);
        readonly List<Vector2> _uvs = new List<Vector2>(1100);
        readonly List<int> _indices = new List<int>(6200);

        public static int ChunkCountX(IWorldQuery world) => (world.Width + WorldConstants.ChunkSize - 1) / WorldConstants.ChunkSize;

        public static int ChunkCountY(IWorldQuery world) => (world.Height + WorldConstants.ChunkSize - 1) / WorldConstants.ChunkSize;

        public static void ChunkOfTile(int x, int y, out int cx, out int cy)
        {
            cx = x / WorldConstants.ChunkSize;
            cy = y / WorldConstants.ChunkSize;
        }

        // Liefert false, wenn der Chunk keine Terrain-Fläche hat (z. B. letzte Spalte einer Welt mit Breite 32·n+1).
        public bool BuildTerrain(IWorldQuery world, int cx, int cy, Mesh mesh)
        {
            Clear();
            int i0 = cx * WorldConstants.ChunkSize;
            int j0 = cy * WorldConstants.ChunkSize;
            int i1 = Mathf.Min(i0 + WorldConstants.ChunkSize, world.Width - 1);
            int j1 = Mathf.Min(j0 + WorldConstants.ChunkSize, world.Height - 1);
            if (i1 <= i0 || j1 <= j0)
            {
                mesh.Clear();
                return false;
            }

            float invW = 1f / world.Width, invH = 1f / world.Height;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    float wx = WorldCoords.TileCenterX(i), wz = WorldCoords.TileCenterZ(j);
                    _vertices.Add(new Vector3(wx, world.SampleHeight(wx, wz), wz));
                    _normals.Add(VertexNormal(world, i, j));
                    _uvs.Add(new Vector2((i + 0.5f) * invW, (j + 0.5f) * invH));
                }
            }

            int stride = i1 - i0 + 1;
            for (int j = 0; j < j1 - j0; j++)
            {
                for (int i = 0; i < i1 - i0; i++)
                {
                    int v00 = j * stride + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + stride;
                    int v11 = v01 + 1;
                    // Unity: Vorderseite im Uhrzeigersinn von oben gesehen.
                    _indices.Add(v00); _indices.Add(v11); _indices.Add(v10);
                    _indices.Add(v00); _indices.Add(v01); _indices.Add(v11);
                }
            }
            Upload(mesh);
            return true;
        }

        public bool BuildWater(IWorldQuery world, int cx, int cy, Mesh mesh)
        {
            Clear();
            int x0 = cx * WorldConstants.ChunkSize;
            int y0 = cy * WorldConstants.ChunkSize;
            int x1 = Mathf.Min(x0 + WorldConstants.ChunkSize, world.Width);
            int y1 = Mathf.Min(y0 + WorldConstants.ChunkSize, world.Height);
            float ts = WorldConstants.TileSize, half = ts * 0.5f;
            float invW = 1f / (world.Width * ts), invH = 1f / (world.Height * ts);

            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (!IsWaterTile(world, x, y))
                        continue;
                    float level = world.Get(x, y).WaterLevel;
                    float minX = x * ts, maxX = minX + ts, minZ = y * ts, maxZ = minZ + ts;
                    if (!IsWaterTile(world, x - 1, y)) minX -= half;
                    if (!IsWaterTile(world, x + 1, y)) maxX += half;
                    if (!IsWaterTile(world, x, y - 1)) minZ -= half;
                    if (!IsWaterTile(world, x, y + 1)) maxZ += half;

                    int b = _vertices.Count;
                    AddWaterVertex(minX, level, minZ, invW, invH);
                    AddWaterVertex(maxX, level, minZ, invW, invH);
                    AddWaterVertex(minX, level, maxZ, invW, invH);
                    AddWaterVertex(maxX, level, maxZ, invW, invH);
                    _indices.Add(b); _indices.Add(b + 2); _indices.Add(b + 3);
                    _indices.Add(b); _indices.Add(b + 3); _indices.Add(b + 1);
                }
            }
            if (_vertices.Count == 0)
            {
                mesh.Clear();
                return false;
            }
            Upload(mesh);
            return true;
        }

        public static bool IsWaterTile(IWorldQuery world, int x, int y)
        {
            if (!world.InBounds(x, y))
                return true;   // außerhalb liegt Meer (Ozeanebene)
            TileData t = world.Get(x, y);
            return t.WaterLevel > t.Height + 0.05f;
        }

        void AddWaterVertex(float x, float y, float z, float invW, float invH)
        {
            _vertices.Add(new Vector3(x, y, z));
            _normals.Add(Vector3.up);
            _uvs.Add(new Vector2(x * invW, z * invH));
        }

        static Vector3 VertexNormal(IWorldQuery world, int i, int j)
        {
            int il = i > 0 ? i - 1 : i, ir = i < world.Width - 1 ? i + 1 : i;
            int jd = j > 0 ? j - 1 : j, ju = j < world.Height - 1 ? j + 1 : j;
            float hl = HeightAt(world, il, j), hr = HeightAt(world, ir, j);
            float hd = HeightAt(world, i, jd), hu = HeightAt(world, i, ju);
            float dx = (ir - il) * WorldConstants.TileSize, dz = (ju - jd) * WorldConstants.TileSize;
            var n = new Vector3(-(hr - hl) / Mathf.Max(dx, 1e-3f), 1f, -(hu - hd) / Mathf.Max(dz, 1e-3f));
            return n.normalized;
        }

        static float HeightAt(IWorldQuery world, int i, int j) =>
            world.SampleHeight(WorldCoords.TileCenterX(i), WorldCoords.TileCenterZ(j));

        void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _indices.Clear();
        }

        void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_indices, 0, true);
        }
    }
}
