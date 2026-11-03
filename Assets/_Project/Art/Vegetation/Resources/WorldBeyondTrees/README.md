# Bäume v1 (Frühzeit-Stil)

Eigene Modelle, gebaut per Skript (Blender 4.0, headless). Stand 2026-11-03.
- `Tools/Blender/tree_atlas.py` rendert die Blatt-Atlanten aus selbst modellierten 3D-Zweigen (Blätter, Kirschblüten, Fichtennadeln). Keine fremden Bilder.
- `Tools/Blender/tree_build.py` baut Stamm und Äste als Röhren und das Laub als Zweig-Karten.
- `Tools/Blender/tree_preview.py` erzeugt die Vorschau mit Hütte und Wiesenboden.

| Datei / Prefab-Name | Inhalt | Maße (B × T × H) | Dreiecke | Karten |
|---|---|---|---|---|
| `WB_Tree_Broadleaf` | Laubbaum (eichenartig) | 8,9 × 8,2 × 9,8 m | 6 047 | 562 |
| `WB_Tree_Cherry` | Kirschbaum, ausladend | 9,0 × 9,3 × 6,6 m | 3 712 | 375 |
| `WB_Tree_Spruce` | Fichte | 7,5 × 7,4 × 9,7 m | 9 360 | 1 102 |

Kirschblüte: dasselbe Modell `WB_Tree_Cherry`, im Material `M_LeafCherry` die Texturen `T_LeafCherry_*` gegen `T_LeafCherryBlossom_*` tauschen (Frühling).

## Technik
- 1 Einheit = 1 m. Ursprung = Stammfuß (0, 0, 0); der Stamm reicht 0,25 m in den Boden.
- FBX mit Y oben, ohne Drehung oder Skalierung, ein Mesh, zwei Material-Slots: 0 = Rinde `M_Bark<Art>`, 1 = Laub `M_Leaf<Art>`.
- Rinde (URP/Lit, wie bei den Gebäuden): Base Map `T_Bark*_D.jpg`, Normal Map `T_Bark*_N.jpg` (OpenGL), Metallic Map `T_Bark*_MS.png` (Smoothness Source „Metallic Alpha“), Wrap Mode „Repeat“ (UV in Weltmaß).
- Laub (URP/Lit): Base Map `T_Leaf*_D.png` mit Alpha, **Alpha Clipping an** (Schwelle etwa 0,5), **Render Face „Both“**, Normal Map `T_Leaf*_N.png`, Metallic Map `T_Leaf*_MS.png`. Wrap Mode „Clamp“. Atlas 2 × 2, je Karte ein Feld.
- Laub-Normalen zeigen kugelförmig von der Kronenmitte nach außen (weiches Licht wie bei echten Kronen). Beim Import „Normals: Import“ lassen.

## Noch nicht erledigt
- In Unity nicht getestet (kein Unity in der Cloud).
- Keine LOD-Stufen, keine Impostoren, keine Kollider. Für Wälder ist vor allem die Fichte (9k Dreiecke) zu schwer; LOD1/LOD2 und ein Impostor folgen.
- Keine Wind-Daten (Vertexfarben) und keine Jahreszeiten außer der Kirschblüte.
- Die Fichte wirkt aus der Nähe noch etwas flach (gekreuzte Karten).

## Herkunft der Rindentexturen (CC0, Poly Haven)
| Textur | Quelle |
|---|---|
| T_BarkOak | jolcham_oak_bark_01 (Charlotte Baglioni) |
| T_BarkCherry | sakura_bark (Charlotte Baglioni) |
| T_BarkPine | pine_bark (Dimitrios Savva) |
