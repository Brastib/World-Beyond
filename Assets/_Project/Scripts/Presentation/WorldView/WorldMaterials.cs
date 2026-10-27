using UnityEngine;

namespace WorldBeyond.Presentation.WorldView
{
    // Erzeugt Ersatzmaterialien, wenn im Inspector keine zugewiesen sind. Bevorzugt URP Lit, sonst Built-in Standard.
    // Hinweis: Shader.Find findet im Build nur Shader, die im Build enthalten sind. Für Builds deshalb Materialien
    // im Inspector zuweisen (oder den Shader unter Graphics > Always Included Shaders eintragen).
    public static class WorldMaterials
    {
        static readonly string[] LitShaders = { "Universal Render Pipeline/Lit", "Standard" };

        public static Material CreateLit(string name, Color color, float smoothness, Texture texture = null)
        {
            Shader shader = null;
            foreach (string s in LitShaders)
            {
                shader = Shader.Find(s);
                if (shader != null)
                    break;
            }
            if (shader == null)
            {
                Debug.LogError("WorldMaterials: weder URP Lit noch Standard-Shader gefunden. Bitte Materialien im Inspector zuweisen.");
                return null;
            }
            var m = new Material(shader) { name = name, enableInstancing = true };
            m.color = color;                         // URP: _BaseColor, Standard: _Color (beide [MainColor])
            if (texture != null)
                m.mainTexture = texture;             // URP: _BaseMap, Standard: _MainTex
            SetFloatIfPresent(m, "_Smoothness", smoothness);   // URP
            SetFloatIfPresent(m, "_Glossiness", smoothness);   // Standard
            SetFloatIfPresent(m, "_Metallic", 0f);
            return m;
        }

        // Kein Sonnen-Glanzpunkt und keine Himmelsspiegelung (Wasser: sonst grell weiße Scheibe bzw. Streifen bei
        // flachem Sonnenstand, Unity-Lauf 2026-10-27). Wirkt auf URP Lit und Built-in Standard.
        public static void DisableSpecular(Material m)
        {
            if (m == null)
                return;
            SetFloatIfPresent(m, "_SpecularHighlights", 0f);
            SetFloatIfPresent(m, "_EnvironmentReflections", 0f);   // URP
            SetFloatIfPresent(m, "_GlossyReflections", 0f);        // Standard
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        }

        static void SetFloatIfPresent(Material m, string property, float value)
        {
            if (m.HasProperty(property))
                m.SetFloat(property, value);
        }
    }
}
