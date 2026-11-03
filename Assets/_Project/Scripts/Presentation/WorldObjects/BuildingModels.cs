using System.Collections.Generic;
using UnityEngine;
using WorldBeyond.Presentation.WorldView;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Echte Gebäudemodelle (Blender, Asset-Katalog) statt Platzhalter. Ladeweg ohne Inspector-Verweis, weil der Schnellstart
    // die Szene zur Laufzeit baut: Resources/WorldBeyondBuildings/<Datei>.fbx (Assets/_Project/Art/Buildings/Primitive/Resources).
    // Materialien werden hier aus den Texturen gebaut (URP Lit: Base, Normal, Metallic/Glätte im Alpha), zugeordnet über den
    // Namen des Material-Slots im FBX (M_Thatch → T_Thatch_*). Fehlt Modell oder Textur, bleibt der Platzhalter bzw. die Grundfarbe.
    public sealed class BuildingModels
    {
        public const string Folder = "WorldBeyondBuildings/";

        public sealed class Model
        {
            public Mesh Mesh;
            public Material[] Materials;    // je Submesh
            public float FootprintMeters;   // größere Seite der Grundfläche
        }

        static readonly Dictionary<BuildingShape, string> Files = new Dictionary<BuildingShape, string>
        {
            { BuildingShape.Hut, "WB_Prim_House_Wattle" },
            { BuildingShape.Storage, "WB_Prim_Granary" },
        };

        readonly Dictionary<BuildingShape, Model> _models = new Dictionary<BuildingShape, Model>();
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        readonly List<Object> _owned;

        public BuildingModels(List<Object> ownedAssets)
        {
            _owned = ownedAssets;
            foreach (var kv in Files)
            {
                var m = Load(kv.Value);
                if (m != null)
                    _models[kv.Key] = m;
            }
            if (_models.Count > 0)
                Debug.Log($"BuildingModels: {_models.Count} Gebäudemodell(e) geladen, übrige Formen bleiben Platzhalter.");
            else
                Debug.LogWarning("BuildingModels: keine Gebäudemodelle in Resources/" + Folder + " gefunden (Git LFS geladen?). Alle Gebäude sind Platzhalter.");
        }

        public bool TryGet(BuildingShape shape, out Model model) => _models.TryGetValue(shape, out model);

        Model Load(string file)
        {
            var go = Resources.Load<GameObject>(Folder + file);
            if (go == null)
                return null;
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mf == null || mf.sharedMesh == null || mr == null)
            {
                Debug.LogWarning($"BuildingModels: {file} hat kein Mesh mit MeshRenderer, Platzhalter bleibt.");
                return null;
            }
            if (mf.transform != go.transform && mf.transform.localToWorldMatrix != Matrix4x4.identity)
                Debug.LogWarning($"BuildingModels: {file}: Mesh-Knoten hat eine eigene Transformation, sie wird ignoriert.");
            Mesh mesh = mf.sharedMesh;
            var slots = mr.sharedMaterials;
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = MaterialFor(i < slots.Length && slots[i] != null ? slots[i].name : "");
            Vector3 size = mesh.bounds.size;
            return new Model { Mesh = mesh, Materials = mats, FootprintMeters = Mathf.Max(0.1f, Mathf.Max(size.x, size.z)) };
        }

        // "M_Thatch" (auch "M_Thatch (Instance)" oder "M_Thatch.001") → Texturen T_Thatch_D/_N/_MS.
        Material MaterialFor(string slotName)
        {
            string key = slotName;
            int cut = key.IndexOfAny(new[] { ' ', '.' });
            if (cut > 0) key = key.Substring(0, cut);
            if (key.StartsWith("M_")) key = key.Substring(2);
            if (_materials.TryGetValue(key, out Material cached))
                return cached;

            var baseMap = Resources.Load<Texture2D>(Folder + "Textures/T_" + key + "_D");
            var m = WorldMaterials.CreateLit("Model_" + key, Color.white, 0.2f, baseMap);
            _materials[key] = m;
            if (m == null)
                return null;
            _owned.Add(m);
            if (baseMap != null)
            {
                var normal = Resources.Load<Texture2D>(Folder + "Textures/T_" + key + "_N");
                if (normal != null && m.HasProperty("_BumpMap"))
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }
                var ms = Resources.Load<Texture2D>(Folder + "Textures/T_" + key + "_MS");
                if (ms != null && m.HasProperty("_MetallicGlossMap"))
                {
                    m.SetTexture("_MetallicGlossMap", ms);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);   // Alpha der Metallic-Map
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);                               // Faktor auf die Map
                }
            }
            else
            {
                Debug.LogWarning($"BuildingModels: Textur T_{key}_D fehlt, Material {slotName} bleibt grau.");
            }
            return m;
        }
    }
}
