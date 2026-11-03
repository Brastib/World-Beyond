using UnityEngine;
using UnityEngine.Rendering;

namespace WorldBeyond.Presentation.WorldView
{
    // Licht und Atmosphäre laut UI_STYLE_GUIDE A5: eine warme Sonne (ca. 50° Höhe), weiche Schatten,
    // leicht blauer Umgebungsanteil, schwacher Nebel in der Ferne. Post-Processing (Tonemapping, SSAO, Sättigung)
    // gehört in ein URP-Volume in der Szene und wird hier bewusst nicht per Code gesetzt.
    [ExecuteAlways]
    public sealed class WorldAtmosphere : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField] float sunElevation = 50f;
        [SerializeField] float sunAzimuth = 135f;
        [SerializeField] Color sunColor = new Color(1f, 0.93f, 0.82f);
        [SerializeField] float sunIntensity = 1.25f;
        [SerializeField] Color skyAmbient = new Color(0.62f, 0.72f, 0.86f);
        [SerializeField] Color equatorAmbient = new Color(0.58f, 0.60f, 0.56f);
        [SerializeField] Color groundAmbient = new Color(0.33f, 0.30f, 0.25f);
        [SerializeField] Color fogColor = new Color(0.72f, 0.80f, 0.88f);
        [Tooltip("Nebel beginnt ab dieser Entfernung (m)")]
        [SerializeField] float fogStart = 600f;
        [SerializeField] float fogEnd = 2600f;
        [SerializeField] Camera[] cameras;

        void OnEnable() => Apply();

        void OnValidate() => Apply();

        public void Apply()
        {
            if (sun == null)
                sun = FindSun();
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyAmbient;
            RenderSettings.ambientEquatorColor = equatorAmbient;
            RenderSettings.ambientGroundColor = groundAmbient;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = Mathf.Max(fogStart + 1f, fogEnd);

            if (cameras != null)
            {
                foreach (Camera c in cameras)
                {
                    if (c == null)
                        continue;
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = fogColor;
                    c.farClipPlane = Mathf.Max(c.farClipPlane, fogEnd + 200f);
                }
            }
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
#if UNITY_6000_6_OR_NEWER
            foreach (Light l in FindObjectsByType<Light>())   // Unity 6.6: Überladung mit SortMode ist veraltet (CS0618)
#else
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
            {
                if (l.type == LightType.Directional)
                    return l;
            }
            return null;
        }
    }
}
