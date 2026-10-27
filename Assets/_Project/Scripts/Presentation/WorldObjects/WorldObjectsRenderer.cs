using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldBeyond.Application;
using WorldBeyond.Presentation.WorldView;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Zeichnet Gebäude, Einheiten und Gebiete der Zivilisationen. Nur Darstellung: liest IBuildingQuery, IUnitQuery,
    // ISettlementQuery, ICivilizationQuery und TileData.OwnerCivId, schreibt nichts in die Simulation.
    // Gebäude und Einheiten als GPU-Instanzen (Graphics.DrawMeshInstanced, keine GameObjects) wie VegetationLayer;
    // Gebiete als ein Mesh je Chunk (Tönung + Grenzlinie in Civ-Farbe), abschaltbar über ShowTerritory.
    // Modelle sind PLATZHALTER bis R2 (PlaceholderMeshes). Je Civ-Farbe ein Material, weil URP Lit keine
    // Farbe je Instanz annimmt.
    public sealed class WorldObjectsRenderer : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [Tooltip("Kamera für Culling; leer = Camera.main")]
        [SerializeField] Camera viewCamera;

        [Header("Sichtweiten (Meter)")]
        [SerializeField] float buildingDistance = 1600f;
        [SerializeField] float unitDistance = 900f;

        [Header("Lesbarkeit aus der Ferne")]
        [Tooltip("Ab dieser Kameraentfernung werden Einheiten vergrößert, damit sie aus der Strategieansicht sichtbar bleiben.")]
        [SerializeField] float unitTrueScaleDistance = 60f;
        [SerializeField] float unitMaxScale = 6f;

        [Header("Aktualisierung (Sekunden Echtzeit)")]
        [SerializeField] float buildingRefreshSeconds = 0.25f;
        [SerializeField] float territoryRefreshSeconds = 2f;

        [Header("Gebiete")]
        [SerializeField] bool showTerritory = true;
        [SerializeField, Range(0f, 1f)] float territoryFillAlpha = 0.16f;
        [SerializeField, Range(0f, 1f)] float territoryBorderAlpha = 0.85f;
        [SerializeField] float territoryBorderWidth = 0.6f;
        [SerializeField] float territoryLift = 0.35f;

        const int MaxPerBatch = 1023;
        const int StateCount = 3;

        sealed class Chunk
        {
            public readonly Dictionary<int, List<Matrix4x4>> Lists = new Dictionary<int, List<Matrix4x4>>();
            public Bounds Bounds;
            public bool HasBounds;
        }

        GameComposition _comp;
        IWorldQuery _world;
        ICivilizationQuery _civs;
        readonly BuildingVisuals _buildings = new BuildingVisuals();
        readonly UnitInterpolator _units = new UnitInterpolator();
        TerritoryGrid _territory;
        readonly List<int> _dirtyTerritory = new List<int>();
        Chunk[] _chunks;
        int _countX, _countY, _builtVersion = -1;
        float _buildingTimer, _territoryTimer;
        readonly Dictionary<int, List<Matrix4x4>> _unitLists = new Dictionary<int, List<Matrix4x4>>();
        readonly Matrix4x4[] _batch = new Matrix4x4[MaxPerBatch];
        readonly Plane[] _frustum = new Plane[6];

        Mesh[] _buildingMeshes, _unitMeshes;
        Material _bodyMat, _darkMat, _roofMat, _thatchMat, _scaffoldMat, _ruinMat, _neutralCivMat, _territoryMat;
        readonly Dictionary<int, Material> _civMats = new Dictionary<int, Material>();
        readonly List<Object> _ownedAssets = new List<Object>();
        MeshFilter[] _territoryChunks;
        GameObject _territoryRoot;
        bool _territoryWarned;

        // Territorien-Mesh-Puffer (wiederverwendet)
        readonly List<Vector3> _tv = new List<Vector3>();
        readonly List<Color32> _tc = new List<Color32>();
        readonly List<int> _ti = new List<int>();

        public bool ShowTerritory
        {
            get => showTerritory;
            set
            {
                showTerritory = value;
                if (_territoryRoot != null) _territoryRoot.SetActive(value);
                _territoryTimer = 0f;   // beim Einschalten sofort aktualisieren
            }
        }

        public int VisibleUnitCount { get; private set; }
        public int BuildingCount => _buildings.Items.Count;

        void OnEnable()
        {
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("WorldObjectsRenderer: kein GameBootstrap in der Szene.", this);
                return;
            }
            CreateSharedAssets();
            bootstrap.CompositionChanged += Build;
            if (bootstrap.Composition != null)
                Build(bootstrap.Composition);
        }

        void OnDisable()
        {
            if (bootstrap != null)
                bootstrap.CompositionChanged -= Build;
            Teardown();
            foreach (Object o in _ownedAssets)
                UnityObjects.Destroy(o);
            _ownedAssets.Clear();
            _buildingMeshes = null;
            _unitMeshes = null;
        }

        void Build(GameComposition comp)
        {
            Teardown();
            if (comp == null || !comp.Context.TryGet(out IWorldQuery world))
                return;
            _comp = comp;
            _world = world;
            comp.Context.TryGet(out _civs);
            _buildings.Attach(comp.Context);
            _countX = (world.Width + WorldConstants.ChunkSize - 1) / WorldConstants.ChunkSize;
            _countY = (world.Height + WorldConstants.ChunkSize - 1) / WorldConstants.ChunkSize;
            _chunks = new Chunk[_countX * _countY];
            for (int i = 0; i < _chunks.Length; i++)
                _chunks[i] = new Chunk();
            _builtVersion = -1;
            _buildingTimer = 0f;
            _territoryTimer = 0f;

            _territory = new TerritoryGrid(world.Width, world.Height);
            _territoryRoot = new GameObject("Territory");
            _territoryRoot.layer = gameObject.layer;
            _territoryRoot.transform.SetParent(transform, false);
            _territoryRoot.SetActive(showTerritory);
            _territoryChunks = new MeshFilter[_chunks.Length];
        }

        void Teardown()
        {
            _buildings.Detach();
            _units.Clear();
            _comp = null;
            _world = null;
            _civs = null;
            _chunks = null;
            _territory = null;
            _territoryChunks = null;
            if (_territoryRoot != null)
            {
                foreach (MeshFilter mf in _territoryRoot.GetComponentsInChildren<MeshFilter>(true))
                    UnityObjects.Destroy(mf.sharedMesh);
                UnityObjects.Destroy(_territoryRoot);
            }
            _territoryRoot = null;
            // Civ-Materialien gehören zur Komposition (neue Welt = neue Farben).
            foreach (Material m in _civMats.Values)
                UnityObjects.Destroy(m);
            _civMats.Clear();
            foreach (var l in _unitLists.Values)
                l.Clear();
        }

        void CreateSharedAssets()
        {
            if (_buildingMeshes != null)
                return;
            _buildingMeshes = new Mesh[10];
            for (int s = 0; s < _buildingMeshes.Length; s++)
                _buildingMeshes[s] = Own(PlaceholderMeshes.Building((BuildingShape)s));
            _unitMeshes = new Mesh[PlaceholderMeshes.UnitShapeCount];
            for (int s = 0; s < _unitMeshes.Length; s++)
                _unitMeshes[s] = Own(PlaceholderMeshes.Unit((PlaceholderMeshes.UnitShape)s));

            // Farben nach UI_STYLE_GUIDE A4 (warme, natürliche Töne; Civ-Farbe nur als Akzent).
            _bodyMat = Own(WorldMaterials.CreateLit("BuildingBody", new Color32(0xE2, 0xD3, 0xB2, 255), 0.08f));
            _darkMat = Own(WorldMaterials.CreateLit("BuildingDark", new Color32(0x6E, 0x55, 0x3C, 255), 0.05f));
            _roofMat = Own(WorldMaterials.CreateLit("BuildingRoof", new Color32(0xA0, 0x55, 0x3A, 255), 0.12f));
            _thatchMat = Own(WorldMaterials.CreateLit("BuildingThatch", new Color32(0xB8, 0x9A, 0x5E, 255), 0.02f));
            _scaffoldMat = Own(WorldMaterials.CreateLit("BuildingScaffold", new Color32(0xB8, 0x95, 0x62, 255), 0.02f));
            _ruinMat = Own(WorldMaterials.CreateLit("BuildingRuin", new Color32(0x4A, 0x46, 0x42, 255), 0.02f));
            _neutralCivMat = Own(WorldMaterials.CreateLit("CivNeutral", new Color32(0x8E, 0x95, 0x9C, 255), 0.1f));

            Shader overlay = Shader.Find("Sprites/Default");
            if (overlay == null)
                overlay = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (overlay != null)
            {
                _territoryMat = Own(new Material(overlay) { name = "TerritoryOverlay" });
                _territoryMat.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        T Own<T>(T asset) where T : Object
        {
            if (asset != null)
                _ownedAssets.Add(asset);
            return asset;
        }

        Material CivMaterial(int civId)
        {
            if (civId <= 0)
                return _neutralCivMat;
            if (_civMats.TryGetValue(civId, out Material m))
                return m;
            // Zerfallene Civ: Exists/TryGet liefern false → neutral, aber nicht zwischenspeichern (ID wird nicht wiederverwendet).
            if (_civs == null || !_civs.TryGet(civId, out CivilizationView v))
                return _neutralCivMat;
            m = WorldMaterials.CreateLit("Civ_" + civId, CivColor(v), 0.1f);
            _civMats[civId] = m;
            return m;
        }

        static Color CivColor(in CivilizationView v) => new Color32(v.Color.R, v.Color.G, v.Color.B, 255);

        Color32 CivColor32(int civId, float alpha)
        {
            var a = (byte)Mathf.RoundToInt(alpha * 255f);
            if (_civs != null && _civs.TryGet(civId, out CivilizationView v))
                return new Color32(v.Color.R, v.Color.G, v.Color.B, a);
            return new Color32(0x8E, 0x95, 0x9C, a);
        }

        // ---------------- Bild ----------------

        void LateUpdate()
        {
            if (_comp == null || _world == null)
                return;
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            float dt = Time.unscaledDeltaTime;

            _buildingTimer -= dt;
            if (_buildingTimer <= 0f)
            {
                _buildingTimer = buildingRefreshSeconds;
                _buildings.Refresh();
            }
            if (_builtVersion != _buildings.Version)
                RebuildBuildingChunks();

            long tick = _comp.Context.Clock.Tick;
            if (tick != _units.CapturedTick)
                _units.Capture(_comp.Context.Units, _comp.Context.UnitTypes, tick);

            if (showTerritory && (_territoryTimer -= dt) <= 0f)
            {
                _territoryTimer = territoryRefreshSeconds;
                RefreshTerritory();
            }

            if (cam == null)
                return;
            GeometryUtility.CalculateFrustumPlanes(cam, _frustum);
            DrawBuildings(cam);
            DrawUnits(cam, _comp.Runner.InterpolationAlpha);
        }

        // ---------------- Gebäude ----------------

        static int Key(BuildingShape shape, BuildingState state, int civId) => ((civId * StateCount) + (int)state) * 16 + (int)shape;

        void RebuildBuildingChunks()
        {
            _builtVersion = _buildings.Version;
            foreach (Chunk c in _chunks)
            {
                foreach (var l in c.Lists.Values)
                    l.Clear();
                c.HasBounds = false;
            }
            float ts = WorldConstants.TileSize;
            var items = _buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                BuildingInstance b = items[i];
                if (!_world.InBounds(b.X, b.Y))
                    continue;
                float cx = (b.X + b.SizeX * 0.5f) * ts, cz = (b.Y + b.SizeY * 0.5f) * ts;
                // Tiefster Punkt der Grundfläche, damit am Hang nichts schwebt (Sockel reicht 1 m nach unten).
                float y = Mathf.Min(
                    Mathf.Min(_world.SampleHeight(b.X * ts + 0.5f, b.Y * ts + 0.5f), _world.SampleHeight((b.X + b.SizeX) * ts - 0.5f, b.Y * ts + 0.5f)),
                    Mathf.Min(_world.SampleHeight(b.X * ts + 0.5f, (b.Y + b.SizeY) * ts - 0.5f), _world.SampleHeight((b.X + b.SizeX) * ts - 0.5f, (b.Y + b.SizeY) * ts - 0.5f)));
                y = Mathf.Min(y, _world.SampleHeight(cx, cz));
                float horiz = b.Shape == BuildingShape.Wall ? 1f : (b.SizeX + b.SizeY) * 0.5f;
                var scale = new Vector3(b.SizeX, horiz * b.Height01, b.SizeY);
                // Mauern 2×1 entlang der längeren Achse, Rest nach Hash leicht gedreht (nur 0/90/180/270°, damit nichts über die Kachel ragt).
                float yaw = b.Shape == BuildingShape.Wall || b.SizeX != b.SizeY ? 0f : ((b.X * 7 + b.Y * 13) & 3) * 90f;
                var m = Matrix4x4.TRS(new Vector3(cx, y, cz), Quaternion.Euler(0f, yaw, 0f), scale);

                int chunk = Mathf.Min(b.Y / WorldConstants.ChunkSize, _countY - 1) * _countX + Mathf.Min(b.X / WorldConstants.ChunkSize, _countX - 1);
                Chunk c = _chunks[chunk];
                int key = Key(b.Shape, b.State, b.CivId);
                if (!c.Lists.TryGetValue(key, out var list))
                    c.Lists[key] = list = new List<Matrix4x4>();
                list.Add(m);
                var bounds = new Bounds(new Vector3(cx, y + 3f * scale.y, cz), new Vector3(b.SizeX * ts + 2f, 8f * scale.y + 2f, b.SizeY * ts + 2f));
                if (c.HasBounds) c.Bounds.Encapsulate(bounds);
                else { c.Bounds = bounds; c.HasBounds = true; }
            }
        }

        void DrawBuildings(Camera cam)
        {
            Vector3 camPos = cam.transform.position;
            float maxSq = buildingDistance * buildingDistance;
            int layer = gameObject.layer;
            foreach (Chunk c in _chunks)
            {
                if (!c.HasBounds || c.Bounds.SqrDistance(camPos) > maxSq || !GeometryUtility.TestPlanesAABB(_frustum, c.Bounds))
                    continue;
                foreach (var kv in c.Lists)
                {
                    if (kv.Value.Count == 0)
                        continue;
                    int key = kv.Key;
                    var shape = (BuildingShape)(key % 16);
                    var state = (BuildingState)((key / 16) % StateCount);
                    int civId = key / 16 / StateCount;
                    Mesh mesh = _buildingMeshes[(int)shape];
                    Material civ = CivMaterial(civId);
                    for (int sub = 0; sub < PlaceholderMeshes.BuildingSubMeshes; sub++)
                    {
                        Material mat;
                        if (state == BuildingState.Ruin) mat = _ruinMat;
                        else if (sub == 1) mat = civ;
                        else if (state == BuildingState.UnderConstruction) mat = _scaffoldMat;
                        else mat = sub == 0 ? _bodyMat : sub == 2 ? _darkMat : sub == 3 ? _roofMat : _thatchMat;
                        DrawList(mesh, sub, mat, kv.Value, layer, true);
                    }
                }
            }
        }

        // ---------------- Einheiten ----------------

        void DrawUnits(Camera cam, float alpha)
        {
            foreach (var l in _unitLists.Values)
                l.Clear();
            Vector3 camPos = cam.transform.position;
            float maxSq = unitDistance * unitDistance;
            int visible = 0;
            var samples = _units.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                UnitSample s = samples[i];
                UnitInterpolator.Lerp(s, alpha, out float x, out float z, out float heading);
                var pos = new Vector3(x, _world.SampleHeight(x, z), z);
                float sq = (pos - camPos).sqrMagnitude;
                if (sq > maxSq)
                    continue;
                float scale = Mathf.Clamp(Mathf.Sqrt(sq) / Mathf.Max(1f, unitTrueScaleDistance), 1f, unitMaxScale);
                if (!GeometryUtility.TestPlanesAABB(_frustum, new Bounds(pos + Vector3.up * scale, Vector3.one * (6f * scale))))
                    continue;
                var shape = PlaceholderMeshes.ShapeFor(s.Role);
                int key = s.CivId * PlaceholderMeshes.UnitShapeCount + (int)shape;
                if (!_unitLists.TryGetValue(key, out var list))
                    _unitLists[key] = list = new List<Matrix4x4>();
                list.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, heading, 0f), Vector3.one * scale));
                visible++;
            }
            VisibleUnitCount = visible;

            int layer = gameObject.layer;
            foreach (var kv in _unitLists)
            {
                if (kv.Value.Count == 0)
                    continue;
                Mesh mesh = _unitMeshes[kv.Key % PlaceholderMeshes.UnitShapeCount];
                DrawList(mesh, 0, _darkMat, kv.Value, layer, false);
                DrawList(mesh, 1, CivMaterial(kv.Key / PlaceholderMeshes.UnitShapeCount), kv.Value, layer, false);
            }
        }

        void DrawList(Mesh mesh, int subMesh, Material material, List<Matrix4x4> list, int layer, bool shadows)
        {
            if (material == null || mesh == null)
                return;
            for (int start = 0; start < list.Count; start += MaxPerBatch)
            {
                int n = Mathf.Min(MaxPerBatch, list.Count - start);
                list.CopyTo(start, _batch, 0, n);
                Graphics.DrawMeshInstanced(mesh, subMesh, material, _batch, n, null,
                    shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, layer);
            }
        }

        // ---------------- Gebiete ----------------

        void RefreshTerritory()
        {
            if (_territory == null)
                return;
            if (_territoryMat == null)
            {
                if (!_territoryWarned)
                    Debug.LogWarning("WorldObjectsRenderer: Kein Shader für die Gebietsdarstellung gefunden (Sprites/Default bzw. URP Particles/Unlit). Gebiete bleiben unsichtbar.", this);
                _territoryWarned = true;
                return;
            }
            _territory.Refresh(_world, _dirtyTerritory);
            foreach (int c in _dirtyTerritory)
                RebuildTerritoryChunk(c);
        }

        void RebuildTerritoryChunk(int index)
        {
            MeshFilter mf = _territoryChunks[index];
            if (mf == null)
            {
                var go = new GameObject("Territory_" + index);
                go.layer = gameObject.layer;
                go.transform.SetParent(_territoryRoot.transform, false);
                mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = new Mesh { name = go.name };
                mf.sharedMesh.MarkDynamic();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _territoryMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _territoryChunks[index] = mf;
            }
            _tv.Clear(); _tc.Clear(); _ti.Clear();
            int cs = WorldConstants.ChunkSize;
            int x0 = index % _countX * cs, y0 = index / _countX * cs;
            int x1 = Mathf.Min(x0 + cs, _world.Width), y1 = Mathf.Min(y0 + cs, _world.Height);
            float ts = WorldConstants.TileSize, w = territoryBorderWidth;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int owner = _territory.OwnerAt(x, y);
                    if (owner == 0)
                        continue;
                    float ax = x * ts, az = y * ts, bx = ax + ts, bz = az + ts;
                    Color32 fill = CivColor32(owner, territoryFillAlpha);
                    // Fläche als Fächer um die Kachelmitte, damit sie dem Gelände folgt.
                    int c0 = _tv.Count;
                    _tv.Add(Lift(ax + ts * 0.5f, az + ts * 0.5f, x, y));
                    _tv.Add(Lift(ax, az, x, y)); _tv.Add(Lift(bx, az, x, y)); _tv.Add(Lift(bx, bz, x, y)); _tv.Add(Lift(ax, bz, x, y));
                    for (int k = 0; k < 5; k++) _tc.Add(fill);
                    for (int k = 0; k < 4; k++)
                    {
                        _ti.Add(c0); _ti.Add(c0 + 1 + (k + 1) % 4); _ti.Add(c0 + 1 + k);
                    }
                    Color32 line = CivColor32(owner, territoryBorderAlpha);
                    if (_territory.IsBorder(x, y, TerritoryGrid.Side.South)) Strip(ax, az, bx, az + w, x, y, line);
                    if (_territory.IsBorder(x, y, TerritoryGrid.Side.North)) Strip(ax, bz - w, bx, bz, x, y, line);
                    if (_territory.IsBorder(x, y, TerritoryGrid.Side.West)) Strip(ax, az, ax + w, bz, x, y, line);
                    if (_territory.IsBorder(x, y, TerritoryGrid.Side.East)) Strip(bx - w, az, bx, bz, x, y, line);
                }
            }
            Mesh mesh = mf.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(_tv);
            mesh.SetColors(_tc);
            mesh.SetTriangles(_ti, 0, true);
            mf.gameObject.SetActive(_tv.Count > 0);
        }

        void Strip(float ax, float az, float bx, float bz, int x, int y, Color32 color)
        {
            int i = _tv.Count;
            _tv.Add(Lift(ax, az, x, y, 0.05f)); _tv.Add(Lift(ax, bz, x, y, 0.05f)); _tv.Add(Lift(bx, bz, x, y, 0.05f)); _tv.Add(Lift(bx, az, x, y, 0.05f));
            for (int k = 0; k < 4; k++) _tc.Add(color);
            _ti.Add(i); _ti.Add(i + 1); _ti.Add(i + 2);
            _ti.Add(i); _ti.Add(i + 2); _ti.Add(i + 3);
        }

        // Höhe über Gelände bzw. Wasser der Kachel.
        Vector3 Lift(float wx, float wz, int tileX, int tileY, float extra = 0f)
        {
            float ground = _world.SampleHeight(wx, wz);
            float water = _world.Get(tileX, tileY).WaterLevel;
            return new Vector3(wx, Mathf.Max(ground, water) + territoryLift + extra, wz);
        }
    }
}
