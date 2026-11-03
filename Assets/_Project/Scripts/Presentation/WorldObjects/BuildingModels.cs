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
            public float SizeX, SizeZ;      // Grundfläche in m (Mesh-Bounds)
        }

        static readonly Dictionary<BuildingShape, string> Files = new Dictionary<BuildingShape, string>
        {
            { BuildingShape.Center, "WB_Prim_Center" },
            { BuildingShape.Hut, "WB_Prim_Hut_Wattle" },
            { BuildingShape.House, "WB_Prim_House_Wattle" },
            { BuildingShape.Storage, "WB_Prim_Granary" },
        };

        // Felder je Wachstumsstufe (umgepflügt, grün, reif) und Baustellen nach Grundfläche (1×1, 2×2 Kacheln).
        static readonly string[] FieldFiles = { "WB_Prim_Field_0Plowed", "WB_Prim_Field_1Green", "WB_Prim_Field_2Ripe" };
        static readonly string[] SiteFiles = { "WB_Prim_Construction_1x1", "WB_Prim_Construction_2x2" };

        readonly Dictionary<BuildingShape, Model> _models = new Dictionary<BuildingShape, Model>();
        readonly Model[] _fields = new Model[3];
        readonly Model[] _sites = new Model[2];

        public BuildingModels(List<Object> ownedAssets)
        {
            var loader = new ResourceModels(Folder, ownedAssets);
            foreach (var kv in Files)
            {
                Model m = Load(loader, kv.Value);
                if (m != null)
                    _models[kv.Key] = m;
            }
            for (int i = 0; i < FieldFiles.Length; i++)
                _fields[i] = Load(loader, FieldFiles[i]);
            for (int i = 0; i < SiteFiles.Length; i++)
                _sites[i] = Load(loader, SiteFiles[i]);
            if (_models.Count > 0)
                Debug.Log($"BuildingModels: {_models.Count} Gebäudemodell(e) geladen, übrige Formen bleiben Platzhalter.");
            else
                Debug.LogWarning("BuildingModels: keine Gebäudemodelle in Resources/" + Folder + " gefunden (Git LFS geladen?). Alle Gebäude sind Platzhalter.");
        }

        public bool TryGet(BuildingShape shape, out Model model) => _models.TryGetValue(shape, out model);

        /// <summary>Feldmodell der Stufe 0 umgepflügt, 1 grün, 2 reif; null, wenn es fehlt.</summary>
        public Model Field(int stage) => _fields[Mathf.Clamp(stage, 0, _fields.Length - 1)];

        /// <summary>Baustelle passend zur Grundfläche: 1×1 für einkachelige, sonst 2×2; null, wenn es fehlt.</summary>
        public Model Site(int sizeTiles) => sizeTiles <= 1 ? (_sites[0] ?? _sites[1]) : (_sites[1] ?? _sites[0]);

        static Model Load(ResourceModels loader, string file)
        {
            var r = loader.Load(file);
            if (r == null)
                return null;
            Vector3 size = r.Mesh.bounds.size;
            return new Model
            {
                Mesh = r.Mesh, Materials = r.Materials,
                FootprintMeters = Mathf.Max(0.1f, Mathf.Max(size.x, size.z)),
                SizeX = Mathf.Max(0.1f, size.x), SizeZ = Mathf.Max(0.1f, size.z),
            };
        }
    }
}
