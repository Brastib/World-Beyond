using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Bäume und Felsen als GPU-Instanzen pro Chunk (Graphics.DrawMeshInstanced, keine GameObjects).
    // Platzierung deterministisch aus Seed und Kachel, rein darstellend: Holz-Vorkommen und Begehbarkeit kommen
    // ausschließlich aus dem Weltraster. Auf Kacheln mit Gebäude, Straße, Brand, Krater oder Wasser steht nichts.
    public sealed class VegetationLayer
    {
        public enum Kind { Conifer = 0, Broadleaf = 1, Rock = 2 }
        const int KindCount = 3;
        const int MaxPerBatch = 1023;   // Grenze von DrawMeshInstanced

        sealed class Chunk
        {
            public readonly List<Matrix4x4>[] Instances = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            public Bounds Bounds;
        }

        readonly IWorldQuery _world;
        readonly Chunk[] _chunks;
        readonly int _countX, _countY;
        readonly Matrix4x4[] _batch = new Matrix4x4[MaxPerBatch];
        readonly Plane[] _frustum = new Plane[6];

        public VegetationLayer(IWorldQuery world)
        {
            _world = world;
            _countX = ChunkMeshBuilder.ChunkCountX(world);
            _countY = ChunkMeshBuilder.ChunkCountY(world);
            _chunks = new Chunk[_countX * _countY];
            for (int cy = 0; cy < _countY; cy++)
            {
                for (int cx = 0; cx < _countX; cx++)
                    RebuildChunk(cx, cy);
            }
        }

        public int InstanceCount(Kind kind)
        {
            int n = 0;
            foreach (Chunk c in _chunks)
                n += c.Instances[(int)kind].Count;
            return n;
        }

        public void RebuildChunk(int cx, int cy)
        {
            var chunk = _chunks[cy * _countX + cx] ??= new Chunk();
            foreach (var list in chunk.Instances)
                list.Clear();

            int x0 = cx * WorldConstants.ChunkSize, y0 = cy * WorldConstants.ChunkSize;
            int x1 = Mathf.Min(x0 + WorldConstants.ChunkSize, _world.Width);
            int y1 = Mathf.Min(y0 + WorldConstants.ChunkSize, _world.Height);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    TileData t = _world.Get(x, y);
                    minY = Mathf.Min(minY, t.Height);
                    maxY = Mathf.Max(maxY, t.Height);
                    AddTile(chunk, x, y, in t);
                }
            }
            float ts = WorldConstants.TileSize;
            var min = new Vector3(x0 * ts, minY - 1f, y0 * ts);
            var max = new Vector3(x1 * ts, maxY + 16f, y1 * ts);
            chunk.Bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        void AddTile(Chunk chunk, int x, int y, in TileData t)
        {
            if (t.WaterLevel > t.Height + 0.05f || t.Building.IsValid)
                return;
            if ((t.Flags & (TileFlags.Road | TileFlags.Burning | TileFlags.Scorched | TileFlags.Crater | TileFlags.Wreck)) != 0)
                return;

            uint h = Hash(x, y);
            int count;
            Kind kind;
            switch (t.Terrain)
            {
                case TerrainType.Forest:
                    count = 2 + (int)(h % 3);
                    kind = t.Biome == BiomeType.Boreal || t.Biome == BiomeType.Alpine || t.Biome == BiomeType.Tundra ? Kind.Conifer : Kind.Broadleaf;
                    break;
                case TerrainType.Swamp:
                    count = h % 4 == 0 ? 1 : 0;
                    kind = Kind.Broadleaf;
                    break;
                case TerrainType.Grass:
                    count = h % 23 == 0 ? 1 : 0;
                    kind = t.Biome == BiomeType.Boreal ? Kind.Conifer : Kind.Broadleaf;
                    break;
                case TerrainType.Rock:
                    count = h % 3 == 0 ? 1 : 0;
                    kind = Kind.Rock;
                    break;
                case TerrainType.Mountain:
                    count = h % 6 == 0 ? 1 : 0;
                    kind = Kind.Rock;
                    break;
                default:
                    return;
            }

            float ts = WorldConstants.TileSize;
            for (int i = 0; i < count; i++)
            {
                uint r = Hash((int)(h + (uint)i * 0x9E3779B9u), i * 31 + 7);
                float fx = (r & 0xFF) / 255f, fz = ((r >> 8) & 0xFF) / 255f;
                float wx = (x + 0.15f + fx * 0.7f) * ts;
                float wz = (y + 0.15f + fz * 0.7f) * ts;
                float wy = _world.SampleHeight(wx, wz) - 0.15f;
                float scale = 0.75f + ((r >> 16) & 0xFF) / 255f * 0.5f;
                float yaw = ((r >> 24) & 0xFF) / 255f * 360f;
                chunk.Instances[(int)kind].Add(Matrix4x4.TRS(new Vector3(wx, wy, wz), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale));
            }
        }

        uint Hash(int x, int y)
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)_world.Seed * 0xCB1AB31Fu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }

        // Zeichnet sichtbare Chunks bis maxDistance (Meter) um die Kamera. Krone und Stamm sind getrennte Submeshes.
        public void Draw(Camera camera, float maxDistance, Mesh[] meshes, Material trunk, Material[] crowns, Material rock, int layer)
        {
            if (camera == null)
                return;
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            Vector3 camPos = camera.transform.position;
            float maxSq = maxDistance * maxDistance;
            for (int i = 0; i < _chunks.Length; i++)
            {
                Chunk c = _chunks[i];
                if (c.Bounds.SqrDistance(camPos) > maxSq || !GeometryUtility.TestPlanesAABB(_frustum, c.Bounds))
                    continue;
                for (int k = 0; k < KindCount; k++)
                {
                    List<Matrix4x4> list = c.Instances[k];
                    if (list.Count == 0 || meshes[k] == null)
                        continue;
                    if (k == (int)Kind.Rock)
                    {
                        DrawList(meshes[k], 0, rock, list, layer);
                    }
                    else
                    {
                        DrawList(meshes[k], 0, trunk, list, layer);
                        DrawList(meshes[k], 1, crowns[k % crowns.Length], list, layer);
                    }
                }
            }
        }

        void DrawList(Mesh mesh, int subMesh, Material material, List<Matrix4x4> list, int layer)
        {
            if (material == null)
                return;
            for (int start = 0; start < list.Count; start += MaxPerBatch)
            {
                int n = Mathf.Min(MaxPerBatch, list.Count - start);
                list.CopyTo(start, _batch, 0, n);
                Graphics.DrawMeshInstanced(mesh, subMesh, material, _batch, n, null, ShadowCastingMode.On, true, layer);
            }
        }
    }
}
