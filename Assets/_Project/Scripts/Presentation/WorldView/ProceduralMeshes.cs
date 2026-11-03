using System.Collections.Generic;
using UnityEngine;

namespace WorldBeyond.Presentation.WorldView
{
    // Einfache Low-Poly-Ersatzmodelle für Bäume und Felsen, solange keine geprüften CC0-Modelle im Projekt liegen
    // (UI_STYLE_GUIDE: schlichte Formen sind aus der Ferne gut lesbar). Flache Normalen je Fläche.
    // Submesh 0 = Stamm (bzw. Fels), Submesh 1 = Krone. Maße in Metern, Ursprung am Fuß.
    public static class ProceduralMeshes
    {
        sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<int>[] _sub = { new List<int>(), new List<int>() };

            // Dreieck im Uhrzeigersinn von außen gesehen (Normale = Cross(b - a, c - a) zeigt nach außen).
            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _n.Add(n); _n.Add(n); _n.Add(n);
                _sub[sub].Add(i); _sub[sub].Add(i + 1); _sub[sub].Add(i + 2);
            }

            public Mesh Build(string name, int subMeshes)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.subMeshCount = subMeshes;
                for (int s = 0; s < subMeshes; s++)
                    m.SetTriangles(_sub[s], s, true);
                m.RecalculateBounds();
                return m;
            }
        }

        // Kegel/Zylinder-Mantel um die Y-Achse; Ringe von unten (y0, r0) nach oben (y1, r1).
        static void Frustum(Builder b, int sub, int sides, float y0, float r0, float y1, float r1, bool capTop, bool capBottom)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var p1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Vector3 b0 = p0 * r0 + Vector3.up * y0, b1 = p1 * r0 + Vector3.up * y0;
                Vector3 t0 = p0 * r1 + Vector3.up * y1, t1 = p1 * r1 + Vector3.up * y1;
                if (r1 > 0f)
                {
                    b.Tri(sub, b0, t1, t0);
                    b.Tri(sub, b0, b1, t1);
                }
                else
                {
                    b.Tri(sub, b0, b1, Vector3.up * y1);
                }
                if (capTop && r1 > 0f)
                    b.Tri(sub, Vector3.up * y1, t0, t1);
                if (capBottom)
                    b.Tri(sub, Vector3.up * y0, b1, b0);
            }
        }

        // Nadelbaum, ca. 10 m hoch: Stamm + drei gestufte Kegel.
        public static Mesh Conifer()
        {
            var b = new Builder();
            Frustum(b, 0, 6, 0f, 0.35f, 2.2f, 0.25f, false, false);
            Frustum(b, 1, 7, 1.6f, 2.4f, 5.2f, 0f, false, true);
            Frustum(b, 1, 7, 3.8f, 1.9f, 7.6f, 0f, false, true);
            Frustum(b, 1, 7, 6.0f, 1.3f, 10.0f, 0f, false, true);
            return b.Build("Conifer_Procedural", 2);
        }

        // Laubbaum, ca. 9 m hoch: Stamm + abgeflachte, facettierte Krone (zwei Kegelstümpfe).
        public static Mesh Broadleaf()
        {
            var b = new Builder();
            Frustum(b, 0, 6, 0f, 0.4f, 3.6f, 0.3f, false, false);
            Frustum(b, 1, 8, 2.8f, 1.2f, 5.6f, 3.2f, false, true);
            Frustum(b, 1, 8, 5.6f, 3.2f, 8.8f, 1.0f, true, false);
            return b.Build("Broadleaf_Procedural", 2);
        }

        // Felsbrocken, ca. 2,5 m breit: verzerrter Oktaeder-Doppelkegel, alles Submesh 0.
        public static Mesh Rock()
        {
            var b = new Builder();
            Vector3 top = new Vector3(0.2f, 1.6f, -0.1f), bottom = new Vector3(0f, -0.4f, 0f);
            Vector3[] ring =
            {
                new Vector3(1.3f, 0.5f, 0.1f), new Vector3(0.6f, 0.7f, 1.1f), new Vector3(-0.7f, 0.4f, 1.0f),
                new Vector3(-1.2f, 0.6f, -0.2f), new Vector3(-0.5f, 0.5f, -1.2f), new Vector3(0.8f, 0.3f, -1.0f),
            };
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 a = ring[i], c = ring[(i + 1) % ring.Length];
                b.Tri(0, a, top, c);
                b.Tri(0, a, c, bottom);
            }
            return b.Build("Rock_Procedural", 1);
        }
    }
}
