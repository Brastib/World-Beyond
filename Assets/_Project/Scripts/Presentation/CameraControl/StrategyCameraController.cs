using UnityEngine;
using WorldBeyond.Presentation.WorldView;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.CameraControl
{
    // Beobachter-Kamera (Sandbox: Zuschauen ist der Normalfall, UI_STYLE_GUIDE B0).
    // Kreist um einen Drehpunkt auf dem Boden: schwenken (WASD/Pfeile, Bildschirmrand, rechte Maustaste ziehen),
    // drehen (Q/E, mittlere Maustaste ziehen), zoomen (Mausrad, 15–400 m bzw. bis zur Weltgröße).
    // Je weiter herausgezoomt, desto steiler der Blick (Kartenansicht). Läuft mit unskalierter Zeit,
    // funktioniert also auch bei pausierter Simulation. FocusOn springt weich zu einer Stelle (Ereignis-Feed).
    [RequireComponent(typeof(Camera))]
    public sealed class StrategyCameraController : MonoBehaviour
    {
        [SerializeField] WorldRenderer worldRenderer;

        [Header("Zoom (Meter Abstand zum Drehpunkt)")]
        [SerializeField] float minDistance = 15f;
        [SerializeField] float maxDistance = 400f;
        [Tooltip("Maximaler Abstand wächst mit der Weltgröße bis zu diesem Wert, damit die ganze Karte sichtbar wird")]
        [SerializeField] float absoluteMaxDistance = 2200f;
        [SerializeField] float zoomStep = 0.15f;
        [SerializeField] float minPitch = 32f;
        [SerializeField] float maxPitch = 72f;

        [Header("Bewegung")]
        [SerializeField] float panSpeed = 1.1f;            // Anteil des Abstands pro Sekunde
        [SerializeField] float rotateSpeed = 90f;          // Grad pro Sekunde (Q/E)
        [SerializeField] float mouseRotateSpeed = 0.25f;   // Grad pro Pixel
        [SerializeField] bool edgeScroll = false;
        [SerializeField] float edgeSize = 12f;
        [SerializeField] float smoothTime = 0.12f;
        [SerializeField] float minHeightAboveGround = 4f;

        Camera _camera;
        Vector3 _pivot, _pivotTarget;
        float _yaw = 0f, _yawTarget = 0f;
        float _distance = 120f, _distanceTarget = 120f;
        Vector2 _lastMouse;
        bool _hasMouse;
        bool _initialized;

        public Camera Camera => _camera;
        public Vector3 Pivot => _pivot;
        public float Distance => _distance;
        public float Yaw => _yaw;

        void Awake() => _camera = GetComponent<Camera>();

        void OnEnable()
        {
            if (worldRenderer == null)
                worldRenderer = FindFirstObjectByType<WorldRenderer>();
            if (worldRenderer != null)
                worldRenderer.Rebuilt += OnWorldRebuilt;
        }

        void OnDisable()
        {
            if (worldRenderer != null)
                worldRenderer.Rebuilt -= OnWorldRebuilt;
        }

        void OnWorldRebuilt(WorldRenderer r) => _initialized = false;

        IWorldQuery World => worldRenderer != null ? worldRenderer.World : null;

        float MaxDistance
        {
            get
            {
                IWorldQuery w = World;
                if (w == null)
                    return maxDistance;
                float size = Mathf.Max(w.Width, w.Height) * WorldConstants.TileSize;
                return Mathf.Clamp(size * 0.9f, maxDistance, absoluteMaxDistance);
            }
        }

        // Kamera weich auf eine Weltposition ausrichten (z. B. Klick auf ein Ereignis). distance &lt;= 0 behält den Zoom.
        public void FocusOn(Vector3 worldPosition, float distance = -1f)
        {
            _pivotTarget = ClampPivot(worldPosition);
            if (distance > 0f)
                _distanceTarget = Mathf.Clamp(distance, minDistance, MaxDistance);
        }

        // Sofort, ohne Übergang (z. B. nach dem Laden).
        public void SnapTo(Vector3 worldPosition, float distance = -1f)
        {
            FocusOn(worldPosition, distance);
            _pivot = _pivotTarget;
            _distance = _distanceTarget;
            _yaw = _yawTarget;
            ApplyTransform();
        }

        void LateUpdate()
        {
            IWorldQuery world = World;
            if (world == null)
                return;
            if (!_initialized)
            {
                _initialized = true;
                float w = world.Width * WorldConstants.TileSize, h = world.Height * WorldConstants.TileSize;
                SnapTo(new Vector3(w * 0.5f, 0f, h * 0.5f), Mathf.Min(MaxDistance, Mathf.Max(w, h) * 0.6f));
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (PointerInput.Available)
                ReadInput(dt);

            float k = smoothTime > 0f ? 1f - Mathf.Exp(-dt / smoothTime) : 1f;
            _pivot = Vector3.Lerp(_pivot, _pivotTarget, k);
            _distance = Mathf.Lerp(_distance, _distanceTarget, k);
            _yaw = Mathf.LerpAngle(_yaw, _yawTarget, k);
            ApplyTransform();
        }

        void ReadInput(float dt)
        {
            Vector2 mouse = PointerInput.MousePosition;
            Vector2 mouseDelta = _hasMouse ? mouse - _lastMouse : Vector2.zero;
            _lastMouse = mouse;
            _hasMouse = true;

            // Zoom
            float scroll = PointerInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
                _distanceTarget = Mathf.Clamp(_distanceTarget * Mathf.Exp(-scroll * zoomStep * 2f), minDistance, MaxDistance);

            // Drehen
            _yawTarget += PointerInput.RotateKeys * rotateSpeed * dt;
            if (PointerInput.MiddleHeld)
                _yawTarget += mouseDelta.x * mouseRotateSpeed;

            // Schwenken
            Vector2 pan = PointerInput.PanKeys;
            if (edgeScroll && !PointerInput.RightHeld && !PointerInput.MiddleHeld)
            {
                if (mouse.x <= edgeSize) pan.x -= 1f;
                else if (mouse.x >= Screen.width - edgeSize) pan.x += 1f;
                if (mouse.y <= edgeSize) pan.y -= 1f;
                else if (mouse.y >= Screen.height - edgeSize) pan.y += 1f;
            }
            float speed = panSpeed * _distanceTarget * (PointerInput.FastModifier ? 2.5f : 1f);
            Quaternion yawRot = Quaternion.Euler(0f, _yawTarget, 0f);
            Vector3 right = yawRot * Vector3.right, forward = yawRot * Vector3.forward;
            Vector3 move = (right * pan.x + forward * pan.y) * (speed * dt);

            if (PointerInput.RightHeld && _camera != null)
            {
                // „Karte greifen“: ein Pixel entspricht ungefähr der Bodenbreite eines Pixels am Drehpunkt.
                float metersPerPixel = 2f * _distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                move -= (right * mouseDelta.x + forward * mouseDelta.y) * metersPerPixel;
            }
            if (move.sqrMagnitude > 0f)
                _pivotTarget = ClampPivot(_pivotTarget + move);
        }

        void ApplyTransform()
        {
            IWorldQuery world = World;
            if (world == null)
                return;
            _pivot.y = world.SampleHeight(_pivot.x, _pivot.z);
            if (world.IsWater(WorldCoordsTileX(_pivot.x), WorldCoordsTileY(_pivot.z)))
                _pivot.y = Mathf.Max(_pivot.y, world.SeaLevel);

            float t = Mathf.InverseLerp(minDistance, MaxDistance, _distance);
            float pitch = Mathf.Lerp(minPitch, maxPitch, Mathf.Sqrt(t));
            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0f);
            Vector3 pos = _pivot - rot * Vector3.forward * _distance;

            // Nie unter den Boden oder in einen Berg.
            float ground = world.SampleHeight(pos.x, pos.z);
            if (pos.y < ground + minHeightAboveGround)
                pos.y = ground + minHeightAboveGround;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_pivot - pos, Vector3.up));
        }

        Vector3 ClampPivot(Vector3 p)
        {
            IWorldQuery world = World;
            if (world == null)
                return p;
            float maxX = world.Width * WorldConstants.TileSize, maxZ = world.Height * WorldConstants.TileSize;
            p.x = Mathf.Clamp(p.x, 0f, maxX - 0.01f);
            p.z = Mathf.Clamp(p.z, 0f, maxZ - 0.01f);
            return p;
        }

        static int WorldCoordsTileX(float x) => WorldCoords.WorldToTile(x, 0f).x;
        static int WorldCoordsTileY(float z) => WorldCoords.WorldToTile(0f, z).y;
    }
}
