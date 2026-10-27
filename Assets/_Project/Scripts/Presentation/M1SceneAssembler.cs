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
            if (Object.FindAnyObjectByType<GameBootstrap>() != null)
                return;

            Debug.Log("M1SceneAssembler: Szene ohne GameBootstrap, baue Prototyp-Szene zur Laufzeit auf.");

            // Reihenfolge zählt: OnEnable der späteren Komponenten sucht die früheren per FindAnyObjectByType.
            var game = new GameObject("Game");
            game.SetActive(false);                   // Awake erst nach dem Setzen der Starteinstellungen
            game.AddComponent<GameBootstrap>().StartSettings = PrototypeWorld();
            game.AddComponent<SaveLoadController>();
            game.SetActive(true);                    // Awake erzeugt die Welt

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

        // Prototyp-Welt: Seed 99, 256², 4 Zivilisationen. Headless (dotnet) erklärt hier die KI nach rund 68 Spielminuten
        // den ersten Krieg (Tick 40551), bei 10× also nach knapp 7 Minuten. Die Standardwelt (Seed 12345, 2 Civs)
        // hatte in 12 Spielstunden keinen Krieg. Kriege bleiben KI-Entscheidung, hier wird nichts erzwungen.
        static GameStartSettings PrototypeWorld() =>
            new GameStartSettings { Seed = 99, WorldWidth = 256, WorldHeight = 256, CivilizationCount = 4 };

        static Light FindDirectionalLight()
        {
#if UNITY_6000_6_OR_NEWER
            foreach (Light l in Object.FindObjectsByType<Light>())   // Unity 6.6: Überladung mit SortMode ist veraltet (CS0618)
#else
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
            {
                if (l.type == LightType.Directional)
                    return l;
            }
            return null;
        }
    }
}
