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
        readonly List<Vector4> _waterData = new List<Vector4>();   // Wasser: x = Tiefe (m), y = 0, zw = Strömung (m/s)
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

        // Wasser: je Kachelmitte eine Tiefe D (Wasser: Spiegel − Boden, Land: −LandWaterDepth). Zellen zwischen vier Kachelmitten
        // mit Wasser werden WaterSubdiv-fach unterteilt; D wird dazwischen per Catmull-Rom interpoliert und auf das Gelände gelegt
        // (y = Boden + D). Die sichtbare Uferlinie ist die Nulllinie von D und verläuft dadurch rund statt in Stufen
        // (Unity-Feedback 03.11.2026, Lauf 7). Randkacheln der Welt reichen bis an den Weltrand (Ozeanebene schließt an).
        const float LandWaterDepth = 0.6f;
        const int WaterSubdiv = 4;
        float[] _depth = new float[0], _level = new float[0], _fill = new float[0];
        int _depthStride, _depthI0, _depthJ0;

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
            // Tiefenfeld mit 1 Kachel Rand (Catmull-Rom braucht die Nachbarn).
            _depthI0 = i0 - 1; _depthJ0 = j0 - 1;
            _depthStride = i1 - i0 + 4;
            int rows = j1 - j0 + 4;
            if (_depth.Length < _depthStride * rows) { _depth = new float[_depthStride * rows]; _level = new float[_depthStride * rows]; _fill = new float[_depthStride * rows]; }
            for (int r = 0; r < rows; r++)
            {
                for (int q = 0; q < _depthStride; q++)
                {
                    int i = Mathf.Clamp(_depthI0 + q, 0, world.Width - 1), j = Mathf.Clamp(_depthJ0 + r, 0, world.Height - 1);
                    float ground = world.SampleHeight(WorldCoords.TileCenterX(i), WorldCoords.TileCenterZ(j));
                    bool wet = IsWaterTile(world, i, j);
                    _depth[r * _depthStride + q] = wet ? Mathf.Max(0.05f, world.Get(i, j).WaterLevel - ground) : -LandWaterDepth;
                    _level[r * _depthStride + q] = wet ? world.Get(i, j).WaterLevel : float.MinValue;
                }
            }

            // Spiegelhöhe auch an Landpunkten (Mittel der nassen Nachbarn): Uferzellen bekommen so einen stetig fallenden
            // Flussspiegel statt einer ebenen Stufe je Zelle (Lauf 8: Flussufer stufig). Pro Punkt, also nahtlos.
            for (int r = 0; r < rows; r++)
            {
                for (int q = 0; q < _depthStride; q++)
                {
                    int k = r * _depthStride + q;
                    if (_level[k] > float.MinValue) { _fill[k] = _level[k]; continue; }
                    float sum = 0f; int cnt = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dq = -1; dq <= 1; dq++)
                        {
                            int rr = r + dr, qq = q + dq;
                            if (rr < 0 || rr >= rows || qq < 0 || qq >= _depthStride) continue;
                            float l = _level[rr * _depthStride + qq];
                            if (l > float.MinValue) { sum += l; cnt++; }
                        }
                    _fill[k] = cnt > 0 ? sum / cnt : float.MinValue;
                }
            }

            float ts = WorldConstants.TileSize;
            float invW = 1f / (world.Width * ts), invH = 1f / (world.Height * ts);
            int n = WaterSubdiv;
            for (int j = j0; j < j1; j++)
            {
                for (int i = i0; i < i1; i++)
                {
                    if (Depth(i, j) <= 0f && Depth(i + 1, j) <= 0f && Depth(i, j + 1) <= 0f && Depth(i + 1, j + 1) <= 0f)
                        continue;
                    float xa = i == 0 ? 0f : WorldCoords.TileCenterX(i);
                    float xb = i + 1 == world.Width - 1 ? world.Width * ts : WorldCoords.TileCenterX(i + 1);
                    float za = j == 0 ? 0f : WorldCoords.TileCenterZ(j);
                    float zb = j + 1 == world.Height - 1 ? world.Height * ts : WorldCoords.TileCenterZ(j + 1);
                    // Ganz im Wasser: Spiegel bilinear (glatt, keine Wellen vom Seegrund). Am Ufer: Boden + D, nie über dem
                    // höchsten Wasserspiegel der Ecken.
                    float l00 = Level(i, j), l10 = Level(i + 1, j), l01 = Level(i, j + 1), l11 = Level(i + 1, j + 1);
                    bool allWet = l00 > float.MinValue && l10 > float.MinValue && l01 > float.MinValue && l11 > float.MinValue;
                    float maxLevel = Mathf.Max(Mathf.Max(l00, l10), Mathf.Max(l01, l11));
                    // Strömung bergab entlang des Wasserspiegels (Flüsse); Seen und Meer sind eben und strömen nicht.
                    float f00 = l00 > float.MinValue ? l00 : maxLevel, f10 = l10 > float.MinValue ? l10 : maxLevel;
                    float f01 = l01 > float.MinValue ? l01 : maxLevel, f11 = l11 > float.MinValue ? l11 : maxLevel;
                    var grad = new Vector2(((f10 + f11) - (f00 + f01)) * 0.5f, ((f01 + f11) - (f00 + f10)) * 0.5f) / ts;
                    Vector2 flow = grad.sqrMagnitude > 1e-6f ? -grad.normalized * Mathf.Clamp(grad.magnitude * 40f, 0.3f, 1.5f) : Vector2.zero;
                    float g00 = Fill(i, j, maxLevel), g10 = Fill(i + 1, j, maxLevel), g01 = Fill(i, j + 1, maxLevel), g11 = Fill(i + 1, j + 1, maxLevel);
                    int b = _vertices.Count;
                    if (allWet)
                    {
                        // Offenes Wasser braucht keine Unterteilung (Ozean-Chunks bleiben bei 4 Ecken je Zelle).
                        AddWaterVertex(xa, l00, za, invW, invH, l00 - world.SampleHeight(xa, za), flow);
                        AddWaterVertex(xb, l10, za, invW, invH, l10 - world.SampleHeight(xb, za), flow);
                        AddWaterVertex(xa, l01, zb, invW, invH, l01 - world.SampleHeight(xa, zb), flow);
                        AddWaterVertex(xb, l11, zb, invW, invH, l11 - world.SampleHeight(xb, zb), flow);
                        _indices.Add(b); _indices.Add(b + 3); _indices.Add(b + 1);
                        _indices.Add(b); _indices.Add(b + 2); _indices.Add(b + 3);
                        continue;
                    }
                    for (int sv = 0; sv <= n; sv++)
                    {
                        float v = (float)sv / n;
                        for (int su = 0; su <= n; su++)
                        {
                            float u = (float)su / n;
                            float x = Mathf.Lerp(xa, xb, u), z = Mathf.Lerp(za, zb, v);
                            float ground = world.SampleHeight(x, z);
                            float cap = Mathf.Lerp(Mathf.Lerp(g00, g10, u), Mathf.Lerp(g01, g11, u), v);
                            float y = Mathf.Min(ground + DepthAt(i, j, u, v), cap);
                            AddWaterVertex(x, y, z, invW, invH, y - ground, flow);
                        }
                    }
                    for (int sv = 0; sv < n; sv++)
                    {
                        for (int su = 0; su < n; su++)
                        {
                            int v00 = b + sv * (n + 1) + su, v10 = v00 + 1, v01 = v00 + n + 1, v11 = v01 + 1;
                            _indices.Add(v00); _indices.Add(v11); _indices.Add(v10);
                            _indices.Add(v00); _indices.Add(v01); _indices.Add(v11);
                        }
                    }
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

        float Fill(int i, int j, float fallback)
        {
            float f = _fill[(j - _depthJ0) * _depthStride + (i - _depthI0)];
            return f > float.MinValue ? f : fallback;
        }

        float Level(int i, int j) => _level[(j - _depthJ0) * _depthStride + (i - _depthI0)];

        float Depth(int i, int j) => _depth[(j - _depthJ0) * _depthStride + (i - _depthI0)];

        // Catmull-Rom über die 4 × 4 Kachelmitten um Zelle (i, j); u, v in [0, 1].
        float DepthAt(int i, int j, float u, float v)
        {
            float r0 = CatmullRom(Depth(i - 1, j - 1), Depth(i, j - 1), Depth(i + 1, j - 1), Depth(i + 2, j - 1), u);
            float r1 = CatmullRom(Depth(i - 1, j), Depth(i, j), Depth(i + 1, j), Depth(i + 2, j), u);
            float r2 = CatmullRom(Depth(i - 1, j + 1), Depth(i, j + 1), Depth(i + 1, j + 1), Depth(i + 2, j + 1), u);
            float r3 = CatmullRom(Depth(i - 1, j + 2), Depth(i, j + 2), Depth(i + 1, j + 2), Depth(i + 2, j + 2), u);
            return CatmullRom(r0, r1, r2, r3, v);
        }

        static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        public static bool IsWaterTile(IWorldQuery world, int x, int y)
        {
            if (!world.InBounds(x, y))
                return true;   // außerhalb liegt Meer (Ozeanebene)
            TileData t = world.Get(x, y);
            return t.WaterLevel > t.Height + 0.05f;
        }

        void AddWaterVertex(float x, float y, float z, float invW, float invH, float depth, Vector2 flow)
        {
            _vertices.Add(new Vector3(x, y, z));
            _normals.Add(Vector3.up);
            _uvs.Add(new Vector2(x * invW, z * invH));
            _waterData.Add(new Vector4(depth, 0f, flow.x, flow.y));
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
            _waterData.Clear();
            _indices.Clear();
        }

        void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            if (_waterData.Count == _vertices.Count)
                mesh.SetUVs(1, _waterData);
            mesh.SetTriangles(_indices, 0, true);
        }
    }
}
