using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Presentation.WorldView;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;
// Unity 6 hat einen eigenen Typ UnityEngine.EntityId; ohne Alias ist EntityId hier mehrdeutig (CS0104).
using EntityId = WorldBeyond.Core.EntityId;

namespace WorldBeyond.Presentation.CameraControl
{
    public enum WorldSelectionKind { None = 0, Tile = 1, Unit = 2 }

    public struct WorldSelectionInfo
    {
        public WorldSelectionKind Kind;
        public int TileX, TileY;
        public EntityId Unit;
        public Vector3 Point;     // getroffener Bodenpunkt
    }

    // Auswahl per Linksklick (Sandbox: Beobachten, z. B. Infopanel öffnen; keine Befehle).
    // Strahl aus der Kamera → IWorldQuery.Raycast (kein Collider nötig) → nächste Einheit im Umkreis über ISpatialIndex,
    // sonst die Kachel. Markierung als Linie auf dem Gelände: Kachelrahmen bzw. Ring um die Einheit (folgt ihr).
    // Die UI liest SelectionChanged und setzt IsPointerOverUi, damit Klicks auf Panels nicht durchgehen.
    public sealed class WorldSelection : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [SerializeField] WorldRenderer worldRenderer;
        [SerializeField] Camera viewCamera;
        [SerializeField] float unitPickRadius = 3f;
        [SerializeField] Color markerColor = new Color(1f, 0.85f, 0.35f, 1f);
        [SerializeField] float markerWidth = 0.35f;
        [SerializeField] Material markerMaterial;

        public event Action<WorldSelectionInfo> SelectionChanged;
        public Func<bool> IsPointerOverUi;
        public WorldSelectionInfo Current { get; private set; }

        LineRenderer _line;
        Material _ownMaterial;
        readonly List<EntityId> _hits = new List<EntityId>();
        readonly List<Vector3> _points = new List<Vector3>();
        const int RingSegments = 24;

        void OnEnable()
        {
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<GameBootstrap>();
            if (worldRenderer == null)
                worldRenderer = FindAnyObjectByType<WorldRenderer>();
            if (bootstrap != null)
                bootstrap.CompositionChanged += OnCompositionChanged;
        }

        void OnDisable()
        {
            if (bootstrap != null)
                bootstrap.CompositionChanged -= OnCompositionChanged;
        }

        void OnDestroy() => UnityObjects.Destroy(_ownMaterial);

        void OnCompositionChanged(GameComposition c) => Clear();

        public void Clear() => SetSelection(default);

        public void SelectTile(int x, int y)
        {
            IWorldQuery world = worldRenderer != null ? worldRenderer.World : null;
            if (world == null || !world.InBounds(x, y))
                return;
            var p = WorldCoords.TileToWorld(world, x, y);
            SetSelection(new WorldSelectionInfo { Kind = WorldSelectionKind.Tile, TileX = x, TileY = y, Point = new Vector3(p.X, p.Y, p.Z) });
        }

        public void SelectUnit(EntityId unit)
        {
            IUnitQuery units = bootstrap != null ? bootstrap.Composition?.Context.Units : null;
            if (units == null || !units.TryGet(unit, out UnitView v))
                return;
            var (tx, ty) = WorldCoords.WorldToTile(v.X, v.Z);
            SetSelection(new WorldSelectionInfo { Kind = WorldSelectionKind.Unit, Unit = unit, TileX = tx, TileY = ty, Point = new Vector3(v.X, 0f, v.Z) });
        }

        void Update()
        {
            if (!PointerInput.Available || !PointerInput.LeftClicked)
                return;
            if (IsPointerOverUi != null && IsPointerOverUi())
                return;
            Pick(PointerInput.MousePosition);
        }

        // Liefert true, wenn etwas getroffen wurde.
        public bool Pick(Vector2 screenPosition)
        {
            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            IWorldQuery world = worldRenderer != null ? worldRenderer.World : null;
            if (cam == null || world == null)
                return false;
            Ray r = cam.ScreenPointToRay(screenPosition);
            var ray = new WorldRay
            {
                Origin = new System.Numerics.Vector3(r.origin.x, r.origin.y, r.origin.z),
                Direction = new System.Numerics.Vector3(r.direction.x, r.direction.y, r.direction.z),
            };
            if (!world.Raycast(ray, cam.farClipPlane, out int tx, out int ty, out System.Numerics.Vector3 hit))
            {
                Clear();
                return false;
            }

            SimulationContextUnits(out IUnitQuery units, out ISpatialIndex spatial);
            if (units != null && spatial != null)
            {
                spatial.QueryRadius(hit.X, hit.Z, unitPickRadius, _hits);
                EntityId best = EntityId.None;
                float bestD = float.MaxValue;
                foreach (EntityId id in _hits)
                {
                    if (!units.TryGet(id, out UnitView v) || v.State == UnitState.Dead)
                        continue;
                    float dx = v.X - hit.X, dz = v.Z - hit.Z, d = dx * dx + dz * dz;
                    if (d < bestD || (d == bestD && id.Index < best.Index))
                    {
                        bestD = d;
                        best = id;
                    }
                }
                if (best.IsValid)
                {
                    SelectUnit(best);
                    return true;
                }
            }
            SetSelection(new WorldSelectionInfo { Kind = WorldSelectionKind.Tile, TileX = tx, TileY = ty, Point = new Vector3(hit.X, hit.Y, hit.Z) });
            return true;
        }

        void SimulationContextUnits(out IUnitQuery units, out ISpatialIndex spatial)
        {
            units = null;
            spatial = null;
            var ctx = bootstrap != null ? bootstrap.Composition?.Context : null;
            if (ctx == null)
                return;
            units = ctx.Units;
            ctx.TryGet(out spatial);
        }

        void SetSelection(WorldSelectionInfo info)
        {
            Current = info;
            SelectionChanged?.Invoke(info);
            UpdateMarker();
        }

        void LateUpdate()
        {
            // Einheit kann sich bewegen oder sterben: Ring nachführen bzw. Auswahl aufheben.
            if (Current.Kind != WorldSelectionKind.Unit)
                return;
            IUnitQuery units = bootstrap != null ? bootstrap.Composition?.Context.Units : null;
            if (units == null || !units.TryGet(Current.Unit, out UnitView v) || v.State == UnitState.Dead)
            {
                Clear();
                return;
            }
            var info = Current;
            info.Point = new Vector3(v.X, 0f, v.Z);
            Current = info;
            UpdateMarker();
        }

        void UpdateMarker()
        {
            IWorldQuery world = worldRenderer != null ? worldRenderer.World : null;
            if (Current.Kind == WorldSelectionKind.None || world == null)
            {
                if (_line != null)
                    _line.enabled = false;
                return;
            }
            EnsureLine();
            _points.Clear();
            const float lift = 0.25f;
            if (Current.Kind == WorldSelectionKind.Tile)
            {
                float ts = WorldConstants.TileSize;
                float x0 = Current.TileX * ts, z0 = Current.TileY * ts;
                const int perEdge = 4;
                for (int e = 0; e < 4; e++)
                {
                    for (int s = 0; s < perEdge; s++)
                    {
                        float f = (float)s / perEdge;
                        float x = e == 0 ? x0 + f * ts : e == 1 ? x0 + ts : e == 2 ? x0 + ts - f * ts : x0;
                        float z = e == 0 ? z0 : e == 1 ? z0 + f * ts : e == 2 ? z0 + ts : z0 + ts - f * ts;
                        _points.Add(new Vector3(x, Surface(world, x, z) + lift, z));
                    }
                }
            }
            else
            {
                float radius = 1.2f;
                var ctx = bootstrap.Composition?.Context;
                if (ctx != null && ctx.Units.TryGet(Current.Unit, out UnitView v) && ctx.UnitTypes.TryGet(v.Type, out UnitTypeData type))
                    radius = Mathf.Max(0.8f, type.Radius + 0.6f);
                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i * Mathf.PI * 2f / RingSegments;
                    float x = Current.Point.x + Mathf.Sin(a) * radius, z = Current.Point.z + Mathf.Cos(a) * radius;
                    _points.Add(new Vector3(x, Surface(world, x, z) + lift, z));
                }
            }
            _line.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++)
                _line.SetPosition(i, _points[i]);
            _line.enabled = true;
        }

        static float Surface(IWorldQuery world, float x, float z)
        {
            float h = world.SampleHeight(x, z);
            var (tx, ty) = WorldCoords.WorldToTile(x, z);
            if (world.InBounds(tx, ty))
            {
                TileData t = world.Get(tx, ty);
                if (t.WaterLevel > h)
                    h = t.WaterLevel;
            }
            return h;
        }

        void EnsureLine()
        {
            if (_line != null)
                return;
            var go = new GameObject("SelectionMarker");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = true;
            _line.widthMultiplier = markerWidth;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            Material m = markerMaterial;
            if (m == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (s != null)
                {
                    _ownMaterial = new Material(s) { name = "SelectionMarker" };
                    _ownMaterial.color = markerColor;
                    m = _ownMaterial;
                }
            }
            _line.sharedMaterial = m;
            _line.startColor = markerColor;
            _line.endColor = markerColor;
        }
    }
}
