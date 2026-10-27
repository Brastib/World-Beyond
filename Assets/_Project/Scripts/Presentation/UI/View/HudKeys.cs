using UnityEngine;
#if ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WorldBeyond.Presentation.UI
{
    // Tasten der Oberfläche, gleiche Eingabe-Weiche wie CameraControl.PointerInput.
    // Leertaste = Pause umschalten, 1–4 = Geschwindigkeit 1×/2×/5×/10×, Esc = Menü schließen.
    public static class HudKeys
    {
        public static bool PausePressed => Pressed(KeyCode.Space);
        public static bool EscapePressed => Pressed(KeyCode.Escape);

        // Ziffer 1–4 → Index in TimeControl.Speeds (1 = 1×); -1 = keine.
        public static int SpeedKey
        {
            get
            {
                if (Pressed(KeyCode.Alpha1)) return 1;
                if (Pressed(KeyCode.Alpha2)) return 2;
                if (Pressed(KeyCode.Alpha3)) return 3;
                if (Pressed(KeyCode.Alpha4)) return 4;
                return -1;
            }
        }

        static bool Pressed(KeyCode key)
        {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
            var kb = Keyboard.current;
            if (kb == null)
                return false;
            switch (key)
            {
                case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
                case KeyCode.Escape: return kb.escapeKey.wasPressedThisFrame;
                case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame;
                case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame;
                case KeyCode.Alpha3: return kb.digit3Key.wasPressedThisFrame;
                case KeyCode.Alpha4: return kb.digit4Key.wasPressedThisFrame;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }
    }
}
