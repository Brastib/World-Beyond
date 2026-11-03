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
        [Tooltip("Bis zu dieser Entfernung (m) Baummodelle, dahinter einfache Platzhalter-Bäume (Bildrate).")]
        [SerializeField] float treeModelDistance = 120f;
        [SerializeField] int maxChunkRebuildsPerFrame = 6;
        [SerializeField] float oceanMargin = 3000f;

        IWorldQuery _world;
        WorldBeyond.Core.ISimEventBus _events;
        WorldColorMap _colorMap;
        TerrainSplat _splat;   // Bodentexturen; null = einfarbig über Farbkarte
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
        Material[] _vegTrunks, _vegCrowns;   // je VegetationLayer.Kind, nah (Modell, falls geladen)
        Mesh[] _vegFarMeshes;
        Material[] _vegFarTrunks, _vegFarCrowns;   // je Kind, fern (immer Platzhalter)

        public IWorldQuery World => _world;
        public Texture2D ColorMap => _colorMap?.Texture;

        // Neu aufgebaut (z. B. für Minikarte, die ColorMap neu holen muss).
        public event System.Action<WorldRenderer> Rebuilt;

        void OnEnable()
        {
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<GameBootstrap>();
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
            if (terrainMaterial == null)
                _splat = TerrainSplat.TryCreate(world, _colorMap.Texture);
            CreateMaterials();
            CreateVegetationAssets();

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
            _splat?.Dispose();
            _splat = null;
        }

        void OnTileChanged(TileChangedEvent e)
        {
            if (_world == null || e.Kind == TileChangeKind.Owner || e.Kind == TileChangeKind.Resource)
                return;
            _colorMap.UpdateTile(e.X, e.Y);
            _splat?.UpdateTile(e.X, e.Y);
            // Wege (Flags) und Gebäude ändern weder Höhe noch Wasser: kein Neubau von Gelände-/Wasser-Mesh nötig
            // (Farbe kommt aus Farbkarte/Steuerkarten). Spart bei wachsenden Städten viele Chunk-Neubauten pro Sekunde.
            if (e.Kind == TileChangeKind.Flags || e.Kind == TileChangeKind.Building)
            {
                _dirtyVegetation.Add(e.Y / WorldConstants.ChunkSize * _countX + e.X / WorldConstants.ChunkSize);
                return;
            }
            // Ein Gitterpunkt gehört bis zu zwei Terrain-Chunks je Achse; Normalen hängen an den Nachbarn.
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = e.X + dx, y = e.Y + dy;
                    if (!_world.InBounds(x, y))
                        continue;
                    int cx = x / WorldConstants.ChunkSize, cy = y / WorldConstants.ChunkSize;
                    // Wasser ist seit 03.11.2026 ein Höhenfeld wie das Gelände: gleiche Nachbar-Chunks betroffen.
                    MarkChunk(cx, cy);
                    if (x % WorldConstants.ChunkSize == 0 && cx > 0)
                        MarkChunk(cx - 1, cy);
                    if (y % WorldConstants.ChunkSize == 0 && cy > 0)
                        MarkChunk(cx, cy - 1);
                    if (x % WorldConstants.ChunkSize == 0 && cx > 0 && y % WorldConstants.ChunkSize == 0 && cy > 0)
                        MarkChunk(cx - 1, cy - 1);
                }
            }
            _dirtyVegetation.Add(e.Y / WorldConstants.ChunkSize * _countX + e.X / WorldConstants.ChunkSize);
        }

        void MarkChunk(int cx, int cy)
        {
            _dirtyTerrain.Add(cy * _countX + cx);
            _dirtyWater.Add(cy * _countX + cx);
        }

        void LateUpdate()
        {
            if (_world == null)
                return;
            _colorMap.ApplyIfDirty();
            _splat?.ApplyIfDirty();
            Drain(_dirtyTerrain, RebuildTerrain);
            Drain(_dirtyWater, RebuildWater);
            Drain(_dirtyVegetation, i => _vegetation.RebuildChunk(i % _countX, i / _countX));

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            _vegetation.Draw(cam, vegetationDistance, treeModelDistance, _vegMeshes, _vegTrunks, _vegCrowns,
                _vegFarMeshes, _vegFarTrunks, _vegFarCrowns, _rockMat, gameObject.layer);
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
            // Wasserdaten für WaterSurface: tief, keine Strömung.
            var water = new System.Collections.Generic.List<Vector4>(v.Length);
            for (int k = 0; k < v.Length; k++) water.Add(new Vector4(20f, 0f, 0f, 0f));
            mesh.SetUVs(1, water);
            mesh.triangles = t;
            mesh.RecalculateBounds();
            var go = NewObject("Ocean", parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Eigener Wasser-Shader (Tiefenfarbe, Wellen, Fresnel, Uferschaum, Strömung); null, wenn er fehlt.
        Material CreateWaterSurface(Texture2D colorMap)
        {
            var shader = Resources.Load<Shader>("WorldBeyondWater/WaterSurface");
            if (shader == null) shader = Shader.Find("WorldBeyond/WaterSurface");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("WorldRenderer: Shader WorldBeyond/WaterSurface fehlt oder wird nicht unterstützt, Wasser bleibt matt.");
                return null;
            }
            var m = Own(new Material(shader) { name = "WaterSurface" });
            m.SetTexture("_ColorMap", colorMap);
            // Kachelbare Wasser-Texturen (Asset-Katalog); fehlt eine, bleibt der neutrale Standard des Shaders.
            SetIfFound(m, "_FineNormal", "WorldBeyondWater/T_WaterFine_N");
            SetIfFound(m, "_CoarseNormal", "WorldBeyondWater/T_WaterCoarse_N");
            SetIfFound(m, "_FoamMask", "WorldBeyondWater/T_WaterFoam_Mask");
            SetIfFound(m, "_Caustics", "WorldBeyondWater/T_WaterCaustics");
            return m;
        }

        static void SetIfFound(Material m, string property, string resource)
        {
            var t = Resources.Load<Texture2D>(resource);
            if (t != null)
                m.SetTexture(property, t);
        }

        void CreateMaterials()
        {
            Texture2D tex = _colorMap.Texture;
            _terrainMat = terrainMaterial != null ? Own(new Material(terrainMaterial))
                : _splat != null ? _splat.Material   // gehört TerrainSplat, wird dort zerstört
                : Own(WorldMaterials.CreateLit("Terrain", Color.white, 0.1f));
            if (waterMaterial != null)
                _waterMat = Own(new Material(waterMaterial));
            else
            {
                _waterMat = CreateWaterSurface(tex);
                if (_waterMat == null)
                {
                    // Rückfall: matte Oberfläche, Farbe aus der Textur (UI_STYLE_GUIDE A4).
                    _waterMat = Own(WorldMaterials.CreateLit("Water", Color.white, 0.4f));
                    WorldMaterials.DisableSpecular(_waterMat);
                }
            }
            if (_terrainMat != null && _splat == null) _terrainMat.mainTexture = tex;
            if (_waterMat != null && !_waterMat.HasProperty("_ColorMap")) _waterMat.mainTexture = tex;
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

        // Bäume: Blender-Modelle aus Resources/WorldBeyondTrees (Art/Vegetation), sonst prozedurale Platzhalter.
        // Im Inspector gesetzte Meshes haben Vorrang. Submesh 0 = Rinde/Stamm, 1 = Laub/Krone (bei Modell und Platzhalter gleich).
        void CreateVegetationAssets()
        {
            int n = VegetationLayer.KindCount;
            _vegMeshes = new Mesh[n];
            _vegTrunks = new Material[n];
            _vegCrowns = new Material[n];
            Mesh procBroadleaf = broadleafMesh != null ? broadleafMesh : Own(ProceduralMeshes.Broadleaf());
            Set(VegetationLayer.Kind.Conifer, coniferMesh != null ? coniferMesh : Own(ProceduralMeshes.Conifer()), _trunkMat, _crownMats[0]);
            Set(VegetationLayer.Kind.Broadleaf, procBroadleaf, _trunkMat, _crownMats[1]);
            Set(VegetationLayer.Kind.Cherry, procBroadleaf, _trunkMat, _crownMats[1]);
            _vegMeshes[(int)VegetationLayer.Kind.Rock] = rockMesh != null ? rockMesh : Own(ProceduralMeshes.Rock());

            _vegFarMeshes = (Mesh[])_vegMeshes.Clone();
            _vegFarTrunks = (Material[])_vegTrunks.Clone();
            _vegFarCrowns = (Material[])_vegCrowns.Clone();

            var loader = new ResourceModels("WorldBeyondTrees/", _ownedAssets);
            int loaded = 0;
            // Fernfarben = mittlere Farbe der Laub-Atlanten, damit der LOD-Wechsel bei treeModelDistance kaum auffällt.
            if (coniferMesh == null) loaded += TryModel(loader, "WB_Tree_Spruce", VegetationLayer.Kind.Conifer, new Color32(0x41, 0x55, 0x37, 255));
            if (broadleafMesh == null)
            {
                loaded += TryModel(loader, "WB_Tree_Broadleaf", VegetationLayer.Kind.Broadleaf, new Color32(0x51, 0x62, 0x38, 255));
                loaded += TryModel(loader, "WB_Tree_Cherry", VegetationLayer.Kind.Cherry, new Color32(0x5A, 0x6E, 0x3C, 255));
            }
            if (loaded > 0) Debug.Log($"WorldRenderer: {loaded} Baummodell(e) geladen.");
            else Debug.Log("WorldRenderer: keine Baummodelle in Resources/WorldBeyondTrees, Bäume bleiben Platzhalter.");

            void Set(VegetationLayer.Kind k, Mesh mesh, Material trunk, Material crown)
            {
                _vegMeshes[(int)k] = mesh;
                _vegTrunks[(int)k] = trunk;
                _vegCrowns[(int)k] = crown;
            }

            int TryModel(ResourceModels l, string file, VegetationLayer.Kind k, Color farCrown)
            {
                var m = l.Load(file);
                if (m == null || m.Mesh.subMeshCount < 2 || m.Materials[0] == null || m.Materials[1] == null)
                {
                    if (m != null) Debug.LogWarning($"WorldRenderer: {file} braucht 2 Material-Slots (Rinde, Laub), Platzhalter bleibt.");
                    return 0;
                }
                Set(k, m.Mesh, m.Materials[0], m.Materials[1]);
                _vegFarCrowns[(int)k] = Own(WorldMaterials.CreateLit("CrownFar_" + k, farCrown, 0.05f));
                return 1;
            }
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
