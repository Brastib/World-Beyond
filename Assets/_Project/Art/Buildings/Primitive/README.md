# Frühzeit-Gebäude v1 (Blender)

Eigene Modelle, gebaut per Skript `Tools/Blender/prim_buildings.py` (Blender 4.0, headless). Stand 2026-11-03.

| Datei / Prefab-Name | Inhalt | Maße (B × T × H) | Dreiecke | Sim-Bezug (Katalog) |
|---|---|---|---|---|
| `WB_Prim_House_Wattle` | Wohnhaus: Pfosten, Flechtwerk mit Lehm, Reetdach 48°, Fellvorhang an der Tür, innen hohl mit Lehmboden | 9,1 × 7,0 × 6,3 m (Wände 8 × 5,6 m) | 23 444 | `building.hut` / `bld.hut` |
| `WB_Prim_Tent_Hide` | Stangenzelt mit Fellbahn, Türöffnung, Rauchklappen, Heringe | 5,5 × 5,5 × 5,3 m | 4 440 | `bld.kit.wall.textile` (kein Sim-Gebäude) |
| `WB_Prim_Granary` | Vorratsspeicher auf Pfählen mit Mäusetellern, Lehmwände, Brettertür, Reetdach, Kerbbalken-Leiter | 4,1 × 4,5 × 4,9 m | 7 740 | `building.storehouse` / `bld.storehouse` |

## Technik
- Ablage (seit 2026-11-03): `Resources/WorldBeyondBuildings/` (Modelle) und `Resources/WorldBeyondBuildings/Textures/`, damit `WorldObjectsRenderer` sie ohne Inspector-Verweis lädt (`BuildingModels.cs`). Die Materialien baut der Code zur Laufzeit aus den Texturen (Zuordnung über den Slot-Namen `M_<Name>` → `T_<Name>_D/_N/_MS`). Die `_N`-Texturen haben eine `.meta` mit Texture Type „Normal map“.
- 1 Einheit = 1 m. Ursprung = Bodenmitte (0, 0, 0); Pfosten reichen 0,15 m in den Boden.
- FBX mit Y oben, ohne Drehung oder Skalierung (`bake_space_transform`), ein Mesh je Modell, ein Material-Slot je Material.
- Materialien: `M_Thatch`, `M_Daub`, `M_Bark`, `M_Wood`, `M_Hide`, `M_Earth`. Einstellungen je Material in URP/Lit:
  - Base Map = `T_*_D.jpg`
  - Normal Map = `T_*_N.jpg` (Texture Type „Normal map“; OpenGL-Format, passt zu Unity)
  - Metallic Map = `T_*_MS.png` (Metallic 0, Glätte im Alpha; Smoothness Source „Metallic Alpha“)
- UVs sind in Weltmaß gekachelt (Reet 2,5 × 4,6 m, Lehm 1,6 m, Rinde 1 m, Holz 0,5 m, Fell 0,9 m, Erde 1,3 m). Deshalb Wrap Mode „Repeat“ verwenden.

## Noch nicht erledigt
- In Unity nicht getestet (kein Unity in der Cloud). Wie die Materialien beim Import zugeordnet werden, muss beim Einbinden geprüft werden.
- Keine LOD-Stufen und keine Kollider. Für Gruppen von Häusern ist das Wohnhaus mit 23k Dreiecken zu schwer; LOD1/LOD2 folgen.
- Keine Schadens-, Schnee- oder Nässe-Zustände (Katalog §0.4, §5.1).
- Stil = ein möglicher Stammes-Stil (Temperate, ohne Technik). Andere Stämme wählen laut Katalog §4 andere Bauteile.

## Herkunft der Texturen (CC0, Poly Haven, polyhaven.com)
| Textur | Quelle | Bearbeitung |
|---|---|---|
| T_Thatch | reed_roof_04 (Rob Tuytel) | Firstband entfernt, vertikal gespiegelt zu kachelbar, 2k |
| T_Daub | clay_plaster (Amal Kumar) + brown_mud_dry (Rob Tuytel) | gemischt, 1k |
| T_Bark | bark_brown_02 (Rob Tuytel) | 1k |
| T_Wood | rough_wood (Rob Tuytel) | 1k |
| T_Hide | fabric_leather_02 (Rob Tuytel) | entsättigt, abgedunkelt, 1k |
| T_Earth | brown_mud_dry (Rob Tuytel) | 1k |

CC0 verlangt keine Namensnennung. Die Urheber sind hier trotzdem festgehalten, damit sich nachvollziehen lässt, woher die Texturen stammen. Für `ASSET_CREDITS.md` vorgemerkt (Integration).
