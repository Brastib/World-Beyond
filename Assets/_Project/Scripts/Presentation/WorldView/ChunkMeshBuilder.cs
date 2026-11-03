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

        // Wasser als Höhenfeld über den Kachelmitten (wie das Gelände, gleiche Diagonale). Wasserkacheln liegen auf ihrem
        // Wasserspiegel, Landkacheln 0,5 m unter dem Boden; die sichtbare Uferlinie ist der Schnitt mit dem Gelände und
        // verläuft dadurch schräg und weich statt in 4-m-Treppen (Unity-Feedback 03.11.2026: eckige Flüsse).
        // Randkacheln der Welt werden bis an den Weltrand gezogen, dort schließt die Ozeanebene an.
        const float LandWaterDepth = 0.5f;

        public bool BuildWater(IWorldQuery world, int cx, int cy, Mesh mesh)
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
            float ts = WorldConstants.TileSize;
            float invW = 1f / (world.Width * ts), invH = 1f / (world.Height * ts);
            int stride = i1 - i0 + 1;
            _water.Clear();
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    bool wet = IsWaterTile(world, i, j);
                    TileData t = world.Get(i, j);
                    float wx = i == 0 ? 0f : i == world.Width - 1 ? world.Width * ts : WorldCoords.TileCenterX(i);
                    float wz = j == 0 ? 0f : j == world.Height - 1 ? world.Height * ts : WorldCoords.TileCenterZ(j);
                    float y = wet ? t.WaterLevel : world.SampleHeight(WorldCoords.TileCenterX(i), WorldCoords.TileCenterZ(j)) - LandWaterDepth;
                    AddWaterVertex(wx, y, wz, invW, invH);
                    _water.Add(wet);
                }
            }
            for (int j = 0; j < j1 - j0; j++)
            {
                for (int i = 0; i < i1 - i0; i++)
                {
                    int v00 = j * stride + i, v10 = v00 + 1, v01 = v00 + stride, v11 = v01 + 1;
                    if (!_water[v00] && !_water[v10] && !_water[v01] && !_water[v11])
                        continue;
                    _indices.Add(v00); _indices.Add(v11); _indices.Add(v10);
                    _indices.Add(v00); _indices.Add(v01); _indices.Add(v11);
                }
            }
            if (_indices.Count == 0)
            {
                mesh.Clear();
                return false;
            }
            Upload(mesh);
            return true;
        }

        readonly List<bool> _water = new List<bool>();

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
