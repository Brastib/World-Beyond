using UnityEngine;
#if ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WorldBeyond.Presentation.CameraControl
{
    // Eingaben für Kamera und Auswahl, unabhängig vom Eingabesystem:
    // - Input System Package, wenn installiert (Define WB_INPUT_SYSTEM aus den versionDefines der Presentation-asmdef)
    //   und in den Player Settings aktiv (ENABLE_INPUT_SYSTEM),
    // - sonst der alte Input Manager (ENABLE_LEGACY_INPUT_MANAGER),
    // - sonst keine Eingabe (einmalige Warnung).
    // Belegung: WASD/Pfeile schwenken, Q/E drehen, Mausrad zoomen, mittlere Maustaste ziehen = drehen,
    // rechte Maustaste ziehen = verschieben, linke Maustaste = auswählen.
    public static class PointerInput
    {
#if !(ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM) && !ENABLE_LEGACY_INPUT_MANAGER
        static bool _warned;
#endif

        public static bool Available
        {
            get
            {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                return Mouse.current != null || Keyboard.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("PointerInput: Kein nutzbares Eingabesystem. Input System Package installieren (Define WB_INPUT_SYSTEM) oder Active Input Handling auf 'Both' stellen.");
                }
                return false;
#endif
            }
        }

        public static Vector2 MousePosition
        {
            get
            {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#else
                return Vector2.zero;
#endif
            }
        }

        // Mausrad in „Rasten“ (positiv = vom Nutzer weg = hineinzoomen).
        public static float Scroll
        {
            get
            {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#else
                return 0f;
#endif
            }
        }

        public static bool LeftClicked => Button(0, true);
        public static bool RightHeld => Button(1, false);
        public static bool MiddleHeld => Button(2, false);

        static bool Button(int index, bool pressedThisFrame)
        {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
            Mouse m = Mouse.current;
            if (m == null)
                return false;
            var b = index == 0 ? m.leftButton : (index == 1 ? m.rightButton : m.middleButton);
            return pressedThisFrame ? b.wasPressedThisFrame : b.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return pressedThisFrame ? Input.GetMouseButtonDown(index) : Input.GetMouseButton(index);
#else
            return false;
#endif
        }

        // Schwenken: x = rechts, y = vorwärts, je -1..1.
        public static Vector2 PanKeys
        {
            get
            {
                float x = 0f, y = 0f;
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                Keyboard k = Keyboard.current;
                if (k == null)
                    return Vector2.zero;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) y -= 1f;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) y += 1f;
#elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
#endif
                return new Vector2(x, y);
            }
        }

        // Drehen per Tastatur: -1 (Q, links) bis 1 (E, rechts).
        public static float RotateKeys
        {
            get
            {
                float r = 0f;
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                Keyboard k = Keyboard.current;
                if (k == null)
                    return 0f;
                if (k.qKey.isPressed) r -= 1f;
                if (k.eKey.isPressed) r += 1f;
#elif ENABLE_LEGACY_INPUT_MANAGER
                if (Input.GetKey(KeyCode.Q)) r -= 1f;
                if (Input.GetKey(KeyCode.E)) r += 1f;
#endif
                return r;
            }
        }

        public static bool FastModifier
        {
            get
            {
#if (ENABLE_INPUT_SYSTEM && WB_INPUT_SYSTEM)
                return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetKey(KeyCode.LeftShift);
#else
                return false;
#endif
            }
        }
    }
}
