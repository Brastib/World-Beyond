using System.Collections.Generic;
using UnityEngine;
using WorldBeyond.Presentation.WorldView;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Echte Gebäudemodelle (Blender, Asset-Katalog) statt Platzhalter. Ladeweg ohne Inspector-Verweis, weil der Schnellstart
    // die Szene zur Laufzeit baut: Resources/WorldBeyondBuildings/<Datei>.fbx (Assets/_Project/Art/Buildings/Primitive/Resources).
    // Laden und Materialien: ResourceModels (WorldView). Fehlt ein Modell, bleibt der Platzhalter.
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
            { BuildingShape.Hut, "WB_Prim_Hut_Wattle" },
            { BuildingShape.House, "WB_Prim_House_Wattle" },
            { BuildingShape.Storage, "WB_Prim_Granary" },
        };

        readonly Dictionary<BuildingShape, Model> _models = new Dictionary<BuildingShape, Model>();

        public BuildingModels(List<Object> ownedAssets)
        {
            var loader = new ResourceModels(Folder, ownedAssets);
            foreach (var kv in Files)
            {
                var r = loader.Load(kv.Value);
                if (r == null)
                    continue;
                Vector3 size = r.Mesh.bounds.size;
                _models[kv.Key] = new Model { Mesh = r.Mesh, Materials = r.Materials, FootprintMeters = Mathf.Max(0.1f, Mathf.Max(size.x, size.z)) };
            }
            if (_models.Count > 0)
                Debug.Log($"BuildingModels: {_models.Count} Gebäudemodell(e) geladen, übrige Formen bleiben Platzhalter.");
            else
                Debug.LogWarning("BuildingModels: keine Gebäudemodelle in Resources/" + Folder + " gefunden (Git LFS geladen?). Alle Gebäude sind Platzhalter.");
        }

        public bool TryGet(BuildingShape shape, out Model model) => _models.TryGetValue(shape, out model);
    }
}
