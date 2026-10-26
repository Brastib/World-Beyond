using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldBeyond.Application;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Zeichnet die Welt: Terrain- und Wasser-Chunks (32 × 32 Kacheln), Ozeanebene um die Welt, Bäume und Felsen
    // als Instanzen. Liest nur aus IWorldQuery und hört auf TileChangedEvent; geänderte Chunks werden verteilt
    // über mehrere Frames neu gebaut. Bei neuer Komposition (Neues Spiel, Laden) wird alles neu aufgebaut.
    // Die Farbkarte (WorldColorMap.Texture) ist gleichzeitig die Quelle für die Minikarte (ColorMap-Eigenschaft).
    public sealed class WorldRenderer : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [Tooltip("Kamera für Vegetations-Culling; leer = Camera.main")]
        [SerializeField] Camera viewCamera;

        [Header("Materialien (leer = Ersatzmaterial aus URP Lit bzw. Standard)")]
        [SerializeField] Material terrainMaterial;
        [SerializeField] Material waterMaterial;
        [SerializeField] Material trunkMaterial;
        [SerializeField] Material[] crownMaterials;   // [0] Nadelbaum, [1] Laubbaum
        [SerializeField] Material rockMaterial;

        [Header("Modelle (leer = prozedurale Ersatzmodelle)")]
        [SerializeField] Mesh coniferMesh;    // Submesh 0 Stamm, 1 Krone
        [SerializeField] Mesh broadleafMesh;  // Submesh 0 Stamm, 1 Krone
        [SerializeField] Mesh rockMesh;

        [Header("Darstellung")]
        [SerializeField] float vegetationDistance = 450f;
        [SerializeField] int maxChunkRebuildsPerFrame = 6;
        [SerializeField] float oceanMargin = 3000f;

        IWorldQuery _world;
        WorldBeyond.Core.ISimEventBus _events;
        WorldColorMap _colorMap;
        VegetationLayer _vegetation;
        readonly ChunkMeshBuilder _builder = new ChunkMeshBuilder();
        readonly List<GameObject> _owned = new List<GameObject>();
        readonly List<Object> _ownedAssets = new List<Object>();
        MeshFilter[] _terrainChunks;
        MeshFilter[] _waterChunks;
        int _countX, _countY;
        readonly SortedSet<int> _dirtyTerrain = new SortedSet<int>();
        readonly SortedSet<int> _dirtyWater = new SortedSet<int>();
        readonly SortedSet<int> _dirtyVegetation = new SortedSet<int>();
        readonly List<int> _drain = new List<int>();
        Mesh[] _vegMeshes;
        Material _terrainMat, _waterMat, _trunkMat, _rockMat;
        Material[] _crownMats;

        public IWorldQuery World => _world;
        public Texture2D ColorMap => _colorMap?.Texture;

        // Neu aufgebaut (z. B. für Minikarte, die ColorMap neu holen muss).
        public event System.Action<WorldRenderer> Rebuilt;

        void OnEnable()
        {
            if (bootstrap == null)
                bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("WorldRenderer: kein GameBootstrap in der Szene.", this);
                return;
            }
            bootstrap.CompositionChanged += Build;
            if (bootstrap.Composition != null)
                Build(bootstrap.Composition);
        }

        void OnDisable()
        {
            if (bootstrap != null)
                bootstrap.CompositionChanged -= Build;
            Teardown();
        }

        void Build(GameComposition composition)
        {
            Teardown();
            if (composition == null || !composition.Context.TryGet(out IWorldQuery world))
                return;
            _world = world;
            _events = composition.Context.Events;
            _events.Subscribe<TileChangedEvent>(OnTileChanged);

            _colorMap = new WorldColorMap(world);
            CreateMaterials();
            _vegMeshes = new[]
            {
                coniferMesh != null ? coniferMesh : Own(ProceduralMeshes.Conifer()),
                broadleafMesh != null ? broadleafMesh : Own(ProceduralMeshes.Broadleaf()),
                rockMesh != null ? rockMesh : Own(ProceduralMeshes.Rock()),
            };

            _countX = ChunkMeshBuilder.ChunkCountX(world);
            _countY = ChunkMeshBuilder.ChunkCountY(world);
            _terrainChunks = new MeshFilter[_countX * _countY];
            _waterChunks = new MeshFilter[_countX * _countY];
            var root = NewObject("World", transform);
            for (int cy = 0; cy < _countY; cy++)
            {
                for (int cx = 0; cx < _countX; cx++)
                {
                    int i = cy * _countX + cx;
                    _terrainChunks[i] = NewChunk($"Terrain_{cx}_{cy}", root.transform, _terrainMat, true);
                    _waterChunks[i] = NewChunk($"Water_{cx}_{cy}", root.transform, _waterMat, false);
                    RebuildTerrain(i);
                    RebuildWater(i);
                }
            }
            BuildOcean(root.transform);
            _vegetation = new VegetationLayer(world);
            Rebuilt?.Invoke(this);
        }

        void Teardown()
        {
            if (_events != null)
                _events.Unsubscribe<TileChangedEvent>(OnTileChanged);
            _events = null;
            _world = null;
            _vegetation = null;
            _dirtyTerrain.Clear();
            _dirtyWater.Clear();
            _dirtyVegetation.Clear();
            foreach (GameObject go in _owned)
                UnityObjects.Destroy(go);
            _owned.Clear();
            foreach (Object o in _ownedAssets)
                UnityObjects.Destroy(o);
            _ownedAssets.Clear();
            _colorMap?.Dispose();
            _colorMap = null;
        }

        void OnTileChanged(TileChangedEvent e)
        {
            if (_world == null || e.Kind == TileChangeKind.Owner || e.Kind == TileChangeKind.Resource)
                return;
            _colorMap.UpdateTile(e.X, e.Y);
            // Ein Gitterpunkt gehört bis zu zwei Terrain-Chunks je Achse; Normalen hängen an den Nachbarn.
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = e.X + dx, y = e.Y + dy;
                    if (!_world.InBounds(x, y))
                        continue;
                    int cx = x / WorldConstants.ChunkSize, cy = y / WorldConstants.ChunkSize;
                    _dirtyWater.Add(cy * _countX + cx);
                    _dirtyTerrain.Add(cy * _countX + cx);
                    if (x % WorldConstants.ChunkSize == 0 && cx > 0)
                        _dirtyTerrain.Add(cy * _countX + cx - 1);
                    if (y % WorldConstants.ChunkSize == 0 && cy > 0)
                        _dirtyTerrain.Add((cy - 1) * _countX + cx);
                    if (x % WorldConstants.ChunkSize == 0 && cx > 0 && y % WorldConstants.ChunkSize == 0 && cy > 0)
                        _dirtyTerrain.Add((cy - 1) * _countX + cx - 1);
                }
            }
            _dirtyVegetation.Add(e.Y / WorldConstants.ChunkSize * _countX + e.X / WorldConstants.ChunkSize);
        }

        void LateUpdate()
        {
            if (_world == null)
                return;
            _colorMap.ApplyIfDirty();
            Drain(_dirtyTerrain, RebuildTerrain);
            Drain(_dirtyWater, RebuildWater);
            Drain(_dirtyVegetation, i => _vegetation.RebuildChunk(i % _countX, i / _countX));

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            _vegetation.Draw(cam, vegetationDistance, _vegMeshes, _trunkMat, _crownMats, _rockMat, gameObject.layer);
        }

        void Drain(SortedSet<int> set, System.Action<int> rebuild)
        {
            if (set.Count == 0)
                return;
            _drain.Clear();
            foreach (int i in set)
            {
                _drain.Add(i);
                if (_drain.Count >= maxChunkRebuildsPerFrame)
                    break;
            }
            foreach (int i in _drain)
            {
                set.Remove(i);
                rebuild(i);
            }
        }

        void RebuildTerrain(int i)
        {
            MeshFilter mf = _terrainChunks[i];
            mf.gameObject.SetActive(_builder.BuildTerrain(_world, i % _countX, i / _countX, mf.sharedMesh));
        }

        void RebuildWater(int i)
        {
            MeshFilter mf = _waterChunks[i];
            mf.gameObject.SetActive(_builder.BuildWater(_world, i % _countX, i / _countX, mf.sharedMesh));
        }

        // Große Ebene auf Meereshöhe um die Welt (mit Loch in der Mitte), damit der Horizont Meer zeigt.
        void BuildOcean(Transform parent)
        {
            float w = _world.Width * WorldConstants.TileSize, h = _world.Height * WorldConstants.TileSize;
            float y = _world.SeaLevel, m = oceanMargin;
            // Innenrand = Weltrand; innen zeichnen die Wasser-Chunks (Randkacheln sind immer Tiefwasser), keine Überlappung.
            float i0 = 0f, ix = w, iz = h;
            var v = new[]
            {
                new Vector3(-m, y, -m), new Vector3(w + m, y, -m), new Vector3(w + m, y, h + m), new Vector3(-m, y, h + m),
                new Vector3(i0, y, i0), new Vector3(ix, y, i0), new Vector3(ix, y, iz), new Vector3(i0, y, iz),
            };
            // Ring aus vier Trapezen, im Uhrzeigersinn von oben.
            int[] t =
            {
                0, 4, 5, 0, 5, 1,   // Süd
                1, 5, 6, 1, 6, 2,   // Ost
                2, 6, 7, 2, 7, 3,   // Nord
                3, 7, 4, 3, 4, 0,   // West
            };
            var mesh = Own(new Mesh { name = "Ocean" });
            mesh.vertices = v;
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int k = 0; k < v.Length; k++)
            {
                n[k] = Vector3.up;
                uv[k] = new Vector2(0.001f, 0.001f);   // Ecke der Farbkarte = Tiefwasser
            }
            mesh.normals = n;
            mesh.uv = uv;
            mesh.triangles = t;
            mesh.RecalculateBounds();
            var go = NewObject("Ocean", parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void CreateMaterials()
        {
            Texture2D tex = _colorMap.Texture;
            _terrainMat = terrainMaterial != null ? Own(new Material(terrainMaterial)) : Own(WorldMaterials.CreateLit("Terrain", Color.white, 0.1f));
            _waterMat = waterMaterial != null ? Own(new Material(waterMaterial)) : Own(WorldMaterials.CreateLit("Water", Color.white, 0.85f));
            if (_terrainMat != null) _terrainMat.mainTexture = tex;
            if (_waterMat != null) _waterMat.mainTexture = tex;
            _trunkMat = trunkMaterial != null ? trunkMaterial : Own(WorldMaterials.CreateLit("Trunk", new Color32(0x6B, 0x4A, 0x2F, 255), 0.05f));
            _rockMat = rockMaterial != null ? rockMaterial : Own(WorldMaterials.CreateLit("Rock", WorldPalette.Rock, 0.15f));
            if (crownMaterials != null && crownMaterials.Length >= 2)
                _crownMats = crownMaterials;
            else
                _crownMats = new[]
                {
                    Own(WorldMaterials.CreateLit("CrownConifer", new Color32(0x2F, 0x5F, 0x34, 255), 0.05f)),
                    Own(WorldMaterials.CreateLit("CrownBroadleaf", new Color32(0x4E, 0x8C, 0x34, 255), 0.05f)),
                };
        }

        MeshFilter NewChunk(string name, Transform parent, Material material, bool castShadows)
        {
            var go = NewObject(name, parent);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Own(new Mesh { name = name });
            mf.sharedMesh.MarkDynamic();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return mf;
        }

        GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            if (parent == transform)
                _owned.Add(go);
            return go;
        }

        T Own<T>(T asset) where T : Object
        {
            if (asset != null)
                _ownedAssets.Add(asset);
            return asset;
        }
    }
}
