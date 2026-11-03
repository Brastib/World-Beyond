using System.Collections.Generic;
using UnityEngine;

namespace WorldBeyond.Presentation.WorldObjects
{
    // PLATZHALTER bis R2 (UI_STYLE_GUIDE A7: keine dauerhaften Platzhalter): einfache, flach schattierte Formen,
    // damit Siedlungen, Gebäude und Einheiten in M1 überhaupt sichtbar sind. Werden durch geprüfte Modelle ersetzt
    // (ASSET_CREDITS.md), sobald der Stil-Lock steht.
    //
    // Gebäude: echte Höhen (Hütte ~3,5 m, Haus ~6 m, Ortszentrum 2×2 ~8–12 m), Grundfläche 1 Kachel = 4 × 4 m, zentriert um den Ursprung, Fuß auf y = 0; der Renderer skaliert
    // x/z mit SizeX/SizeY und y mit dem Baufortschritt. Sockel reicht 1 m unter 0, damit Hänge keine Lücken zeigen.
    // Submesh 0 = heller Baukörper (Putz/Holz hell), 1 = Civ-Farbe (nur Sockelband, Fahne, Wimpel), 2 = dunkle Teile (Holz, Stein, Erde),
    // 3 = Ziegeldach, 4 = Stroh. Natürliche Materialfarben nach den Stilreferenzen des Nutzers (realistisch, 27.10.2026);
    // die Civ-Farbe ist nur Akzent, damit Gebäude nicht wie Spielsteine wirken.
    // Einheiten: echte Größe (Mensch 1,8 m), Ursprung am Fuß, Blick +Z. Submesh 0 = neutral, 1 = Civ-Farbe.
    public static class PlaceholderMeshes
    {
        public const int BuildingSubMeshes = 5;
        public const int UnitSubMeshes = 2;
        const int Body = 0, Civ = 1, Dark = 2, Roof = 3, Thatch = 4;

        sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<int>[] _sub = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _n.Add(n); _n.Add(n); _n.Add(n);
                _sub[sub].Add(i); _sub[sub].Add(i + 1); _sub[sub].Add(i + 2);
            }

            // a-b-c-d im Uhrzeigersinn von außen gesehen.
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(sub, a, b, c);
                Tri(sub, a, c, d);
            }

            // Quader von min bis max; bottom = Unterseite mitzeichnen.
            public void Box(int sub, Vector3 min, Vector3 max, bool bottom = false)
            {
                Vector3 p000 = min, p100 = new Vector3(max.x, min.y, min.z), p010 = new Vector3(min.x, max.y, min.z), p110 = new Vector3(max.x, max.y, min.z);
                Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z), p011 = new Vector3(min.x, max.y, max.z), p111 = max;
                Quad(sub, p000, p010, p110, p100);   // -Z
                Quad(sub, p101, p111, p011, p001);   // +Z
                Quad(sub, p001, p011, p010, p000);   // -X
                Quad(sub, p100, p110, p111, p101);   // +X
                Quad(sub, p010, p011, p111, p110);   // oben
                if (bottom)
                    Quad(sub, p000, p100, p101, p001);
            }

            // Satteldach über dem Rechteck (x0..x1, z0..z1) ab Höhe y0, First entlang X auf Höhe y1, Überstand o.
            public void Gable(int sub, float x0, float x1, float z0, float z1, float y0, float y1, float o)
            {
                x0 -= o; x1 += o; z0 -= o; z1 += o;
                float zm = (z0 + z1) * 0.5f;
                var a = new Vector3(x0, y0, z0); var b = new Vector3(x1, y0, z0);
                var c = new Vector3(x1, y0, z1); var d = new Vector3(x0, y0, z1);
                var r0 = new Vector3(x0, y1, zm); var r1 = new Vector3(x1, y1, zm);
                Quad(sub, a, r0, r1, b);       // Dachfläche -Z
                Quad(sub, c, r1, r0, d);       // Dachfläche +Z
                Tri(sub, d, r0, a);            // Giebel -X
                Tri(sub, b, r1, c);            // Giebel +X
                Quad(sub, a, b, c, d);         // Unterseite (Überstand von unten sichtbar)
            }

            // Pyramide/Kegel mit n Seiten.
            public void Cone(int sub, Vector3 baseCenter, float radius, float height, int sides)
            {
                var top = baseCenter + Vector3.up * height;
                for (int s = 0; s < sides; s++)
                {
                    float a0 = (s + 0.5f) * Mathf.PI * 2f / sides, a1 = (s + 1.5f) * Mathf.PI * 2f / sides;
                    var p0 = baseCenter + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * radius;
                    var p1 = baseCenter + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * radius;
                    Tri(sub, p0, p1, top);
                }
            }

            // Dreieck so gedreht, dass die Normale von center weg zeigt (für Räder und andere runde Teile).
            public void TriOut(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 center)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f)
                    Tri(sub, a, c, b);
                else
                    Tri(sub, a, b, c);
            }

            // Rad als n-seitiges Prisma um die X-Achse.
            public void Wheel(int sub, Vector3 center, float radius, float width, int sides = 10)
            {
                var off = new Vector3(width * 0.5f, 0f, 0f);
                for (int s = 0; s < sides; s++)
                {
                    float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                    var p0 = center + new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0)) * radius;
                    var p1 = center + new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1)) * radius;
                    TriOut(sub, p0 - off, p1 - off, p1 + off, center);
                    TriOut(sub, p0 - off, p1 + off, p0 + off, center);
                    TriOut(sub, center - off, p0 - off, p1 - off, center);
                    TriOut(sub, center + off, p1 + off, p0 + off, center);
                }
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

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // Sockel: dunkler Unterbau bis 1 m unter Boden + Civ-Band oben.
        static void Plinth(Builder b, float half, float top)
        {
            b.Box(Dark, V(-half, -1f, -half), V(half, top - 0.15f, half));
            b.Box(Civ, V(-half - 0.02f, top - 0.15f, -half - 0.02f), V(half + 0.02f, top, half + 0.02f));
        }

        public static Mesh Building(BuildingShape shape)
        {
            var b = new Builder();
            switch (shape)
            {
                case BuildingShape.Center:
                    Plinth(b, 1.9f, 0.35f);
                    b.Box(Body, V(-1.5f, 0.35f, -1.2f), V(1.5f, 2.2f, 1.2f));
                    b.Gable(Roof, -1.5f, 1.5f, -1.2f, 1.2f, 2.2f, 3.3f, 0.2f);
                    b.Box(Body, V(0.85f, 0.35f, 0.65f), V(1.45f, 3.4f, 1.25f));     // schlanker Turm
                    b.Cone(Roof, V(1.15f, 3.4f, 0.95f), 0.5f, 0.9f, 4);
                    b.Box(Dark, V(-0.3f, 0.35f, -1.25f), V(0.3f, 1.6f, -1.2f));        // Tor
                    break;
                case BuildingShape.Hut:
                    b.Box(Dark, V(-1.2f, -1f, -1.2f), V(1.2f, 0.15f, 1.2f));
                    b.Cone(Thatch, V(0f, 0.15f, 0f), 1.6f, 3.4f, 6);                 // Rundhütte mit Strohdach
                    b.Box(Civ, V(-0.05f, 3.3f, -0.05f), V(0.05f, 4.0f, 0.05f));
                    b.Box(Civ, V(0.05f, 3.75f, -0.02f), V(0.55f, 4.0f, 0.02f));       // Wimpel
                    break;
                case BuildingShape.House:
                    Plinth(b, 1.5f, 0.3f);
                    b.Box(Body, V(-1.4f, 0.3f, -1.2f), V(1.4f, 3.8f, 1.2f));
                    b.Gable(Roof, -1.4f, 1.4f, -1.2f, 1.2f, 3.8f, 6.0f, 0.25f);
                    b.Box(Dark, V(-0.4f, 0.3f, -1.25f), V(0.4f, 2.3f, -1.2f));
                    break;
                case BuildingShape.Block:
                    Plinth(b, 1.8f, 0.3f);
                    b.Box(Body, V(-1.6f, 0.3f, -1.4f), V(1.6f, 5.4f, 1.4f));
                    b.Box(Roof, V(-1.7f, 5.4f, -1.5f), V(1.7f, 5.7f, 1.5f));
                    b.Box(Civ, V(-1.72f, 5.25f, -1.52f), V(1.72f, 5.4f, 1.52f));
                    for (int i = 0; i < 3; i++)                                         // Fensterbänder
                        b.Box(Dark, V(-1.62f, 1.2f + i * 1.4f, -1.42f), V(1.62f, 1.6f + i * 1.4f, 1.42f));
                    break;
                case BuildingShape.Field:
                    b.Box(Dark, V(-1.9f, -0.6f, -1.9f), V(1.9f, 0.04f, 1.9f));        // Erde
                    for (int i = 0; i < 5; i++)                                         // Ährenreihen
                    {
                        float z = -1.6f + i * 0.8f;
                        b.Box(Thatch, V(-1.75f, 0.04f, z - 0.3f), V(1.75f, 0.2f, z + 0.3f));
                    }
                    b.Box(Civ, V(-1.92f, 0f, -1.92f), V(-1.82f, 0.5f, -1.82f));       // Grenzpfosten
                    b.Box(Civ, V(1.82f, 0f, 1.82f), V(1.92f, 0.5f, 1.92f));
                    break;
                case BuildingShape.Workshop:
                    Plinth(b, 1.6f, 0.3f);
                    b.Box(Body, V(-1.5f, 0.3f, -1.3f), V(1.5f, 3.4f, 1.3f));
                    b.Gable(Roof, -1.5f, 1.5f, -1.3f, 1.3f, 3.4f, 5.0f, 0.15f);
                    b.Box(Dark, V(0.7f, 2.8f, 0.3f), V(1.2f, 6.2f, 0.8f));            // Schornstein
                    b.Box(Dark, V(-1.8f, 0f, -0.8f), V(-1.45f, 0.9f, 0.8f));          // Holz-/Erzstapel
                    break;
                case BuildingShape.Storage:
                    Plinth(b, 1.7f, 0.3f);
                    b.Box(Dark, V(-1.6f, 0.3f, -1.4f), V(1.6f, 3.2f, 1.4f));
                    b.Gable(Roof, -1.6f, 1.6f, -1.4f, 1.4f, 3.2f, 4.8f, 0.25f);
                    b.Box(Body, V(-0.8f, 0.3f, -1.45f), V(0.8f, 2.6f, -1.4f));        // Tor
                    break;
                case BuildingShape.Library:
                    Plinth(b, 1.7f, 0.5f);
                    b.Box(Body, V(-1.3f, 0.5f, -1.0f), V(1.3f, 4.6f, 1.2f));
                    for (int i = 0; i < 4; i++)                                         // Säulen
                    {
                        float x = -1.2f + i * 0.8f;
                        b.Box(Body, V(x - 0.14f, 0.5f, -1.5f), V(x + 0.14f, 4.6f, -1.26f));
                    }
                    b.Gable(Roof, -1.5f, 1.5f, -1.6f, 1.3f, 4.6f, 6.4f, 0.05f);
                    break;
                case BuildingShape.Wall:
                    b.Box(Dark, V(-2f, -1f, -0.5f), V(2f, 2.4f, 0.5f));
                    for (int i = 0; i < 4; i++)                                         // Zinnen/Pfahlspitzen
                        b.Box(Dark, V(-1.9f + i * 1.05f, 2.4f, -0.5f), V(-1.4f + i * 1.05f, 2.9f, 0.5f));
                    b.Box(Civ, V(-2.02f, 1.8f, -0.52f), V(2.02f, 2.0f, 0.52f));
                    break;
                case BuildingShape.Barracks:
                    Plinth(b, 1.8f, 0.3f);
                    b.Box(Body, V(-1.6f, 0.3f, -1.0f), V(1.6f, 2.0f, 1.0f));
                    b.Gable(Roof, -1.6f, 1.6f, -1.0f, 1.0f, 2.0f, 2.8f, 0.15f);
                    b.Box(Dark, V(1.5f, 0.3f, 1.2f), V(1.6f, 4.6f, 1.3f));            // Fahnenmast
                    b.Box(Civ, V(1.6f, 3.6f, 1.22f), V(2.6f, 4.5f, 1.28f));           // Fahne
                    break;
            }
            return b.Build("Building_" + shape + "_Placeholder", BuildingSubMeshes);
        }

        // Baustelle (Unity-Feedback 03.11.2026: Häuser sollen nicht aus dem Boden wachsen): festgestampfte Fläche,
        // Eckpfosten mit Rahmen aus frischem Holz, Bretterstapel und Steinhaufen. Grundfläche 1 Kachel, der Renderer skaliert x/z.
        // Submesh 0 = frisches Holz, 1 = Civ-Wimpel, 2 = Erde/dunkles Holz/Stein.
        public static Mesh ConstructionSite()
        {
            var b = new Builder();
            b.Box(Dark, V(-1.9f, -0.5f, -1.9f), V(1.9f, 0.03f, 1.9f));                 // Erdfläche
            float p = 1.45f, t = 0.09f;
            foreach (var c in new[] { V(-p, 0f, -p), V(p, 0f, -p), V(-p, 0f, p), V(p, 0f, p) })
                b.Box(Dark, c + V(-t, 0f, -t), c + V(t, 3.2f, t));                         // Eckpfosten
            b.Box(Body, V(-p - t, 2.9f, -p - t), V(p + t, 3.05f, -p + t));                // Rahmen oben
            b.Box(Body, V(-p - t, 2.9f, p - t), V(p + t, 3.05f, p + t));
            b.Box(Body, V(-p - t, 2.9f, -p), V(-p + t, 3.05f, p));
            b.Box(Body, V(p - t, 2.9f, -p), V(p + t, 3.05f, p));
            b.Box(Body, V(-p - t, 1.2f, -p - t), V(p + t, 1.3f, -p + t));                // Querriegel vorn
            for (int i = 0; i < 4; i++)                                                    // Bretterstapel
                b.Box(Body, V(0.2f, 0.03f + i * 0.09f, -1.0f), V(1.2f, 0.11f + i * 0.09f, 0.1f + i * 0.03f));
            b.Box(Dark, V(-1.2f, 0.03f, 0.4f), V(-0.4f, 0.35f, 1.1f));                    // Steinhaufen
            b.Box(Dark, V(-1.0f, 0.35f, 0.55f), V(-0.6f, 0.55f, 0.9f));
            b.Box(Civ, V(p - 0.02f, 2.7f, p + t), V(p + 0.5f, 3.0f, p + t + 0.02f));     // Wimpel am Pfosten
            return b.Build("ConstructionSite_Placeholder", BuildingSubMeshes);
        }

        // Wagon = Ochsenkarren mit Holzrädern (Frühzeit-tauglich, Unity-Feedback 03.11.2026: keine „Autos“);
        // Carrier = Siedler zu Fuß mit Traglast. Vehicle/Gun gibt es erst mit Militärfahrzeugen späterer Epochen.
        public enum UnitShape { Person = 0, Soldier = 1, Vehicle = 2, Gun = 3, Wagon = 4, Carrier = 5 }
        public const int UnitShapeCount = 6;

        public static UnitShape ShapeFor(Simulation.Units.UnitRole role)
        {
            switch (role)
            {
                case Simulation.Units.UnitRole.Infantry: return UnitShape.Soldier;
                case Simulation.Units.UnitRole.Vehicle: return UnitShape.Vehicle;
                case Simulation.Units.UnitRole.Artillery:
                case Simulation.Units.UnitRole.RocketArtillery: return UnitShape.Gun;
                case Simulation.Units.UnitRole.Settler: return UnitShape.Carrier;
                case Simulation.Units.UnitRole.Trader: return UnitShape.Wagon;
                default: return UnitShape.Person;
            }
        }

        // Animierte Teile (Code-Animation in WorldObjectsRenderer). Jedes Teil ist um seinen Drehpunkt gebaut.
        public enum UnitPart { PersonLeg = 0, OxLeg = 1, OxHead = 2, Cart = 3, Wheel = 4 }
        public const int UnitPartCount = 5;

        // Drehpunkte im Einheitenraum (+Z vorn).
        public static readonly Vector3[] PersonLegPivots = { V(-0.09f, 0.85f, 0f), V(0.09f, 0.85f, 0f) };
        public static readonly Vector3[] OxLegPivots = { V(-0.23f, 0.8f, 1.88f), V(0.25f, 0.8f, 1.88f), V(-0.23f, 0.8f, 3.43f), V(0.25f, 0.8f, 3.43f) };
        public static readonly Vector3 OxHeadPivot = V(0f, 1.3f, 3.55f);
        public static readonly Vector3 CartPivot = V(0f, 0.62f, -0.35f);
        public static readonly Vector3[] WheelPivots = { V(-0.92f, 0.62f, -0.35f), V(0.92f, 0.62f, -0.35f) };
        public const float WheelRadius = 0.62f;

        public static Mesh Part(UnitPart part)
        {
            var b = new Builder();
            switch (part)
            {
                case UnitPart.PersonLeg:
                    b.Box(0, V(-0.085f, -0.85f, -0.11f), V(0.085f, 0f, 0.11f));
                    break;
                case UnitPart.OxLeg:
                    b.Box(0, V(-0.07f, -0.8f, -0.08f), V(0.07f, 0.02f, 0.08f));
                    b.Box(0, V(-0.08f, -0.8f, -0.09f), V(0.08f, -0.72f, 0.1f));        // Huf
                    break;
                case UnitPart.OxHead:
                    b.Box(0, V(-0.25f, -0.25f, 0.05f), V(0.25f, 0.2f, 0.6f));          // Kopf
                    b.Box(0, V(-0.2f, -0.05f, -0.05f), V(0.2f, 0.25f, 0.1f));          // Hals
                    b.Box(0, V(-0.45f, 0.12f, 0.2f), V(-0.25f, 0.2f, 0.3f));           // Hörner
                    b.Box(0, V(0.25f, 0.12f, 0.2f), V(0.45f, 0.2f, 0.3f));
                    break;
                case UnitPart.Cart:
                {
                    Vector3 o = CartPivot;
                    b.Box(0, V(-0.8f, 0.75f, -1.6f) - o, V(0.8f, 0.9f, 0.9f) - o);    // Ladefläche aus Brettern
                    b.Box(0, V(-0.8f, 0.9f, -1.6f) - o, V(-0.72f, 1.25f, 0.9f) - o);
                    b.Box(0, V(0.72f, 0.9f, -1.6f) - o, V(0.8f, 1.25f, 0.9f) - o);
                    b.Box(0, V(-0.8f, 0.9f, -1.6f) - o, V(0.8f, 1.25f, -1.52f) - o);
                    b.Box(0, V(-0.6f, 0.9f, -1.3f) - o, V(-0.05f, 1.3f, -0.3f) - o);  // Ladung: Säcke und Kisten
                    b.Box(0, V(0.05f, 0.9f, -0.9f) - o, V(0.6f, 1.2f, 0.4f) - o);
                    b.Box(0, V(0.74f, 1.25f, -1.58f) - o, V(0.8f, 2.4f, -1.52f) - o); // Wimpelstange
                    b.Box(1, V(0.8f, 2.0f, -1.56f) - o, V(1.35f, 2.35f, -1.54f) - o); // Wimpel in Civ-Farbe
                    b.Box(0, V(-1.0f, 0.57f, -0.4f) - o, V(1.0f, 0.67f, -0.3f) - o);  // Achse
                    b.Box(0, V(-0.05f, 0.78f, 0.9f) - o, V(0.05f, 0.88f, 2.4f) - o);  // Deichsel
                    break;
                }
                case UnitPart.Wheel:
                    b.Wheel(0, Vector3.zero, WheelRadius, 0.12f);
                    b.Box(0, V(-0.07f, -0.05f, -WheelRadius), V(0.07f, 0.05f, WheelRadius));   // Speichen, damit das Drehen sichtbar ist
                    b.Box(0, V(-0.07f, -WheelRadius, -0.05f), V(0.07f, WheelRadius, 0.05f));
                    break;
            }
            return b.Build("UnitPart_" + part + "_Placeholder", UnitSubMeshes);
        }

        public static Mesh Unit(UnitShape shape)
        {
            var b = new Builder();
            switch (shape)
            {
                case UnitShape.Person:
                    // Beine: Teil PersonLeg (animiert)
                    b.Box(1, V(-0.24f, 0.85f, -0.15f), V(0.24f, 1.45f, 0.15f));         // Kittel in Civ-Farbe
                    b.Box(0, V(-0.12f, 1.48f, -0.12f), V(0.12f, 1.78f, 0.12f));         // Kopf
                    break;
                case UnitShape.Soldier:
                    // Beine: Teil PersonLeg (animiert)
                    b.Box(1, V(-0.25f, 0.85f, -0.16f), V(0.25f, 1.45f, 0.16f));
                    b.Box(0, V(-0.12f, 1.48f, -0.12f), V(0.12f, 1.74f, 0.12f));
                    b.Box(1, V(-0.15f, 1.72f, -0.15f), V(0.15f, 1.84f, 0.15f));         // Helm
                    b.Box(0, V(0.26f, 0.9f, -0.05f), V(0.34f, 1.3f, 0.9f));             // Waffe nach vorn
                    break;
                case UnitShape.Vehicle:
                    b.Box(0, V(-1.3f, 0f, -2.6f), V(1.3f, 0.8f, 2.6f));                // Laufwerk/Räder
                    b.Box(1, V(-1.2f, 0.8f, -2.4f), V(1.2f, 1.7f, 2.2f));              // Wanne
                    b.Box(0, V(-0.7f, 1.7f, -1.0f), V(0.7f, 2.3f, 0.6f));              // Aufbau/Turm
                    b.Box(0, V(-0.1f, 1.9f, 0.6f), V(0.1f, 2.1f, 3.2f));               // Rohr
                    break;
                case UnitShape.Gun:
                    b.Box(0, V(-1.1f, 0f, -0.3f), V(-0.8f, 1.0f, 0.3f));               // Räder
                    b.Box(0, V(0.8f, 0f, -0.3f), V(1.1f, 1.0f, 0.3f));
                    b.Box(1, V(-0.8f, 0.4f, -0.5f), V(0.8f, 1.0f, 0.5f));              // Lafette
                    b.Box(0, V(-0.15f, 0.8f, 0.2f), V(0.15f, 1.1f, 2.6f));             // Rohr
                    b.Box(0, V(-0.1f, 0f, -2.2f), V(0.1f, 0.4f, -0.5f));               // Holm
                    break;
                case UnitShape.Wagon:
                    // Nur der Ochsenrumpf; Kopf, Beine, Karren und Räder sind animierte Teile (UnitPart, WagonParts).
                    b.Box(0, V(-0.42f, 0.75f, 1.7f), V(0.42f, 1.55f, 3.6f));
                    break;
                case UnitShape.Carrier:
                    // Beine: Teil PersonLeg (animiert)
                    b.Box(1, V(-0.24f, 0.85f, -0.15f), V(0.24f, 1.45f, 0.15f));         // Kittel
                    b.Box(0, V(-0.12f, 1.48f, -0.12f), V(0.12f, 1.78f, 0.12f));         // Kopf
                    b.Box(0, V(-0.26f, 0.8f, -0.55f), V(0.26f, 1.65f, -0.15f));         // Traglast auf dem Rücken
                    b.Box(1, V(-0.2f, 1.65f, -0.5f), V(0.2f, 1.8f, -0.2f));             // Bündel obenauf
                    b.Box(0, V(0.28f, 0f, 0.15f), V(0.33f, 1.6f, 0.2f));               // Wanderstab
                    break;
            }
            return b.Build("Unit_" + shape + "_Placeholder", UnitSubMeshes);
        }
    }
}
