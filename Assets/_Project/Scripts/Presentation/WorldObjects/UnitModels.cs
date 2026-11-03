using System.Collections.Generic;
using UnityEngine;
using WorldBeyond.Presentation.WorldView;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Ochse und Karren aus Teilen (Asset-Katalog, Resources/WorldBeyondUnits/, README dort): jedes Teil ist ein eigenes
    // Objekt mit dem Drehpunkt als Ursprung. Rest = Lage des Teils wie bei einer Instanz im Ursprung (inkl. Import-Drehung/
    // -Maßstab), animiert wird um die lokale X-Achse des Teils. Fehlt ein Modell, zeichnet der Renderer die Platzhalter-Teile.
    public sealed class UnitModels
    {
        public const string Folder = "WorldBeyondUnits/";
        public const float OxOffsetZ = 2.53f;       // Ochsen-Ursprung vor dem Karren-Ursprung (README)
        public const float WheelRadius = 0.46f;     // Scheibenrad Ø 0,92 m

        public sealed class Part
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4 Rest;
        }

        // Reihenfolge der Beine wie im Renderer: 0 hinten links, 1 hinten rechts, 2 vorn links, 3 vorn rechts.
        public Part OxBody, OxHead, OxTail, CartBody;
        public readonly Part[] OxLegs = new Part[4];
        public readonly Part[] Wheels = new Part[2];

        public bool HasOxCart => OxBody != null && OxHead != null && CartBody != null
            && OxLegs[0] != null && OxLegs[1] != null && OxLegs[2] != null && OxLegs[3] != null
            && Wheels[0] != null && Wheels[1] != null;

        public UnitModels(List<Object> ownedAssets)
        {
            var loader = new ResourceModels(Folder, ownedAssets);
            var ox = Collect(loader, "WB_Unit_Ox");
            var cart = Collect(loader, "WB_Unit_Cart");
            ox.TryGetValue("Ox_Body", out OxBody);
            ox.TryGetValue("Ox_Head", out OxHead);
            ox.TryGetValue("Ox_Tail", out OxTail);
            ox.TryGetValue("Ox_Leg_BL", out OxLegs[0]);
            ox.TryGetValue("Ox_Leg_BR", out OxLegs[1]);
            ox.TryGetValue("Ox_Leg_FL", out OxLegs[2]);
            ox.TryGetValue("Ox_Leg_FR", out OxLegs[3]);
            cart.TryGetValue("Cart_Body", out CartBody);
            cart.TryGetValue("Cart_Wheel_L", out Wheels[0]);
            cart.TryGetValue("Cart_Wheel_R", out Wheels[1]);
            if (HasOxCart)
            {
                // Ochse vor den Karren spannen: Teile des Ochsen um +Z verschieben.
                var shift = Matrix4x4.Translate(new Vector3(0f, 0f, OxOffsetZ));
                OxBody.Rest = shift * OxBody.Rest;
                OxHead.Rest = shift * OxHead.Rest;
                if (OxTail != null) OxTail.Rest = shift * OxTail.Rest;
                for (int i = 0; i < 4; i++) OxLegs[i].Rest = shift * OxLegs[i].Rest;
                Debug.Log("UnitModels: Ochsenkarren aus Teilen geladen.");
            }
            else
                Debug.LogWarning("UnitModels: Ochse/Karren unvollständig in Resources/" + Folder + " (Git LFS geladen?), Platzhalter bleibt.");
        }

        static Dictionary<string, Part> Collect(ResourceModels loader, string file)
        {
            var parts = new Dictionary<string, Part>();
            var go = Resources.Load<GameObject>(Folder + file);
            if (go == null)
                return parts;
            // Lage wie bei einer Instanz im Ursprung (Import-Drehung/-Maßstab der Wurzel bleiben erhalten; Asset nicht ändern).
            Matrix4x4 toOrigin = Matrix4x4.Translate(-go.transform.position);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null)
                    continue;
                var slots = mr.sharedMaterials;
                var mats = new Material[mf.sharedMesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = loader.MaterialFor(i < slots.Length && slots[i] != null ? slots[i].name : "");
                parts[mf.name] = new Part { Mesh = mf.sharedMesh, Materials = mats, Rest = toOrigin * mf.transform.localToWorldMatrix };
            }
            return parts;
        }
    }
}
