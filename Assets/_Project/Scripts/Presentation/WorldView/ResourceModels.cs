using System.Collections.Generic;
using UnityEngine;

namespace WorldBeyond.Presentation.WorldView
{
    // Lädt eigene Blender-Modelle (FBX) aus Resources, ohne Inspector-Verweis (Schnellstart baut die Szene zur Laufzeit).
    // Materialien entstehen hier aus den Texturen im Unterordner Textures/: Slot-Name M_<Name> → T_<Name>_D/_N/_MS
    // (URP Lit: Base, Normal, Metallic mit Glätte im Alpha). Laub-Slots (M_Leaf*) bekommen Alpha Clipping und beidseitiges
    // Rendern (README der Bäume). Fehlt ein Modell, liefert Load null und der Aufrufer bleibt beim Platzhalter.
    public sealed class ResourceModels
    {
        public sealed class Model
        {
            public Mesh Mesh;
            public Material[] Materials;   // je Submesh
        }

        readonly string _folder;
        readonly List<Object> _owned;
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        public ResourceModels(string folder, List<Object> ownedAssets)
        {
            _folder = folder.EndsWith("/") ? folder : folder + "/";
            _owned = ownedAssets;
        }

        public Model Load(string file)
        {
            var go = Resources.Load<GameObject>(_folder + file);
            if (go == null)
                return null;
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mf == null || mf.sharedMesh == null || mr == null)
            {
                Debug.LogWarning($"ResourceModels: {_folder}{file} hat kein Mesh mit MeshRenderer, Platzhalter bleibt.");
                return null;
            }
            Mesh mesh = mf.sharedMesh;
            var slots = mr.sharedMaterials;
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = MaterialFor(i < slots.Length && slots[i] != null ? slots[i].name : "");
            return new Model { Mesh = mesh, Materials = mats };
        }

        // Slot-Namen, deren Texturen anders heißen (Unity-Lauf 6: weiße Fichten/Laubbäume).
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "BarkSpruce", "BarkPine" },
            { "LeafSpruce", "LeafNeedle" },
            { "LeafOak", "LeafBroad" },
        };

        // "M_Thatch" (auch "M_Thatch (Instance)" oder "M_Thatch.001") → "Thatch"; danach Alias-Tabelle.
        public static string TextureKey(string slotName)
        {
            string key = slotName ?? "";
            int cut = key.IndexOfAny(new[] { ' ', '.' });
            if (cut > 0) key = key.Substring(0, cut);
            if (key.StartsWith("M_")) key = key.Substring(2);
            return Aliases.TryGetValue(key, out string alias) ? alias : key;
        }

        // Ersatzfarbe, wenn die Farbtextur fehlt: lieber Grün/Braun als Weiß.
        static Color FallbackColor(string key) =>
            key.StartsWith("Leaf") ? new Color32(0x4E, 0x7A, 0x34, 255)
            : key.StartsWith("Bark") || key == "Wood" ? new Color32(0x6B, 0x4A, 0x2F, 255)
            : new Color32(0xA8, 0x98, 0x7A, 255);

        /// <summary>Material aus T_<key>_D/_N/_MS; textureKey überschreibt die Zuordnung (z. B. Kirschblüte).</summary>
        public Material MaterialFor(string slotName, string textureKey = null)
        {
            string key = textureKey ?? TextureKey(slotName);
            if (_materials.TryGetValue(key, out Material cached))
                return cached;

            var baseMap = Resources.Load<Texture2D>(_folder + "Textures/T_" + key + "_D");
            var m = WorldMaterials.CreateLit("Model_" + key, baseMap != null ? Color.white : FallbackColor(key), 0.2f, baseMap);
            _materials[key] = m;
            if (m == null)
                return null;
            _owned.Add(m);
            if (baseMap == null)
            {
                Debug.LogWarning($"ResourceModels: Textur T_{key}_D fehlt (Slot {slotName}), Ersatzfarbe statt Textur.");
                return m;
            }
            var normal = Resources.Load<Texture2D>(_folder + "Textures/T_" + key + "_N");
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            var ms = Resources.Load<Texture2D>(_folder + "Textures/T_" + key + "_MS");
            if (ms != null && m.HasProperty("_MetallicGlossMap"))
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);   // Alpha der Metallic-Map
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            }
            if (key.StartsWith("Leaf"))
                MakeFoliage(m, baseMap);
            return m;
        }

        // Alpha Clipping + beidseitig (URP Lit; Built-in Standard: nur Cutout).
        static void MakeFoliage(Material m, Texture2D baseMap)
        {
            baseMap.wrapMode = TextureWrapMode.Clamp;   // Atlas 2 × 2 (README)
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // Both
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 1f);   // Standard: Cutout
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.doubleSidedGI = true;
        }
    }
}
