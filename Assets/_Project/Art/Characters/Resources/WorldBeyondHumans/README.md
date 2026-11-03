# Menschen v1 (Frühzeit, Schritt 1: stehende Figur ohne Skelett)

Gebaut per `Tools/Blender/human_build.py` (Blender 4.0, headless). Stand 2026-11-03.
Der Körper stammt aus MakeHuman (Grundkörper, Formziele, Proxy-Topologie, Haut, Augen, Brauen, Haare). Das Skript liest nur die Daten-Dateien; Formmischung, Anpassung und die ganze Kleidung sind eigener Code.

| Datei / Prefab-Name | Inhalt | Größe | Dreiecke | Materialien |
|---|---|---|---|---|
| `WB_Human_Prim_Male` | Mann, Lederkittel mit Fellkragen, Gürtel, Beinwickel/Schuhe | 1,80 m | 52 632 | M_Skin, M_Eyes, M_eyebrow001, M_short02, M_Hide, M_Cord, M_Fur |
| `WB_Human_Prim_Female` | Frau, langer Lederkittel mit Fellkragen, Gürtel, Beinwickel/Schuhe | 1,68 m | 55 878 | M_Skin, M_Eyes, M_eyebrow002, M_braid01, M_Hide, M_Cord, M_Fur |

## Technik
- 1 Einheit = 1 m, Ursprung zwischen den Füßen auf dem Boden, Blick nach vorn (+Z in Unity), Ruhepose mit leicht abgespreizten Armen (A-Pose).
- Texturen in `Textures/`. Zuordnung: M_Skin → `young_lightskinned_*_diffuse.png` mit Base-Color-Faktor etwa (0,78; 0,62; 0,50) für sonnengebräunte Haut · M_Eyes → `brown_eye.png` · Brauen/Haare → `eyebrow00x.png`, `short02_diffuse.png` (+ `short02_normal.png`), `braid01_diffuse.png` mit Alpha Clipping und Render Face „Both“ · M_Hide → `T_Hide_*` · M_Cord → `T_Hide_D` mit Faktor (0,45; 0,36; 0,28) · M_Fur → `T_Fur_*`.
- Kleidung: Kittel, Fellkragen und Beinwickel sind nach außen versetzte Kopien der Körperflächen, der Rock ist eine Röhre um die Hüfte. Damit folgen sie später denselben Knochen.

## Noch nicht erledigt (Schritt 2)
- Kein Skelett und keine Gewichte, also noch nicht animierbar. Geplant: Knochen nach MakeHuman-„game_engine“-Rig (Unreal-Namen, passt zu Unity Humanoid), Gewichte aus den CC0-Gewichtsdaten.
- Zu viele Dreiecke für viele Figuren gleichzeitig: Ziel LOD0 ≤ 10k, dazu LOD1/LOD2. Dafür kommt die niedrig aufgelöste Proxy-Topologie dazu.
- Nur zwei Grundfiguren; Varianten (Alter, Körperbau, Haut, Haare, Kleidung je Stamm) folgen über dieselben Formziele.
- In Unity nicht getestet (kein Unity in der Cloud).

## Herkunft und Lizenz
- MakeHuman-Daten: Grundkörper, Formziele und Proxies aus der MPFB-2.0.18-Erweiterung (extensions.blender.org), Haut, Augen, Brauen, Haare aus `makehuman_system_assets_cc0.zip` (files.makehumancommunity.org). Laut MakeHuman sind alle Kern-Assets CC0 und dürfen auch in geschlossenen, kommerziellen Spielen verwendet werden (static.makehumancommunity.org/mpfb/faq/use_in_closed_source.html).
- `T_Hide`: fabric_leather_02 (Poly Haven, CC0), bearbeitet. `T_Fur`: eigene Textur, per Skript aus Rauschen erzeugt.
