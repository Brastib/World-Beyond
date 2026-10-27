using UnityEngine;
using WorldBeyond.Application;
using WorldBeyond.Presentation.CameraControl;
using WorldBeyond.Presentation.UI;
using WorldBeyond.Presentation.WorldView;

namespace WorldBeyond.Presentation
{
    // Schnellstart für den M1-Prototyp: Enthält die geladene Szene keinen GameBootstrap, baut diese Klasse
    // beim Play-Start alle nötigen Objekte selbst auf (Spiel, Welt, Licht, Kamera, HUD).
    // Eine leere Szene oder die Standardszene von Unity reicht damit zum Spielen. Vorhandene Main Camera und
    // Directional Light werden weiterverwendet. Szenen mit eigenem GameBootstrap bleiben unberührt.
    public static class M1SceneAssembler
    {
        const float MinFarClip = 2800f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AssembleIfMissing()
        {
            if (Object.FindFirstObjectByType<GameBootstrap>() != null)
                return;

            Debug.Log("M1SceneAssembler: Szene ohne GameBootstrap, baue Prototyp-Szene zur Laufzeit auf.");

            // Reihenfolge zählt: OnEnable der späteren Komponenten sucht die früheren per FindFirstObjectByType.
            var game = new GameObject("Game");
            game.AddComponent<GameBootstrap>();      // Awake erzeugt die Welt
            game.AddComponent<SaveLoadController>();

            new GameObject("World").AddComponent<WorldRenderer>();

            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }

            Light sun = FindDirectionalLight();
            if (sun == null)
                sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            if (sun.GetComponent<WorldAtmosphere>() == null)
                sun.gameObject.AddComponent<WorldAtmosphere>(); // OnEnable setzt Sonne, Umgebungslicht, Nebel

            // Was WorldAtmosphere sonst über seine Kamera-Liste macht: Hintergrund in Nebelfarbe, Sichtweite über das Nebelende.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, MinFarClip);

            if (cam.GetComponent<WorldSelection>() == null)
                cam.gameObject.AddComponent<WorldSelection>();
            if (cam.GetComponent<StrategyCameraController>() == null)
                cam.gameObject.AddComponent<StrategyCameraController>();

            new GameObject("HUD").AddComponent<GameHud>();
        }

        static Light FindDirectionalLight()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                    return l;
            }
            return null;
        }
    }
}
