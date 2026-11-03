# Bodentexturen v1 (Frühzeit-Stil)

Kachelbare Bodensätze für das Kartengelände. Stand 2026-11-03. Rohmaterial ist CC0 von Poly Haven; Farbkorrektur und Packen haben wir selbst gemacht.

## Dateien je Schicht
`T_Ground_<Schicht>_D.jpg` (Farbe, sRGB), `_N.jpg` (Normal Map, OpenGL-Format), `_MS.png` (RGB = 0 Metallic, Alpha = Glätte, also 1 − Rauheit), `_H.jpg` (Höhe, Graustufen; optional für höhenbasiertes Überblenden). Alle 1024 × 1024 und nahtlos kachelbar.

| Schicht | Einsatz | Quelle (Poly Haven) | Fotomaß | Empfohlene Wiederholung |
|---|---|---|---|---|
| Grass | Wiese, Grundschicht gemäßigt | grass_ground (Charlotte Baglioni), Farbton Richtung Grün verschoben | 2,5 m | 5 m |
| Earth | Hofplatz, Trampelfläche um Häuser | dirt (Charlotte Baglioni) | 2,0 m | 4 m |
| Path | Wege, Pfade | muddy_tracks (Amal Kumar), Rotstich reduziert | 2,25 m | 4,5 m |
| Field | Acker | farm_furrows (Amal Kumar) | 2,1 m | 6 m |
| Forest | Waldboden mit Laub | forest_leaves_03 (Rob Tuytel, Dimitrios Savva) | 2,3 m | 4,6 m |
| Sand | Strand, Küste, Wüste | coast_sand_04 (Rob Tuytel, Dario Barresi) | 3,9 m | 4 m |
| Rock | Fels, Hang | mossy_rock (Rob Tuytel) | 3,0 m | 6 m |
| Dry | trockene Erde, Steppe, Savanne | dry_ground_01 (Rob Tuytel) | 4,0 m | 4 m |
| Wet | Sumpf, Ufer, Matsch | mud_forest (eye-candy.xyz) | 2,35 m | 4,7 m |
| Snow | Schnee | snow_02 (Rob Tuytel) | 2,0 m | 4 m |

Die Wiederholungen liegen zwischen 4 und 6 m, also etwa eine Kachel (`WorldConstants.TileSize` = 4 m). Für eine Schicht gilt: UV = Weltposition XZ / Wiederholung, Wrap Mode „Repeat“.

`T_Ground_Macro.png` ist eigenes, kachelbares Rauschen (RGB = drei Größen). Gedacht für eine Wiederholung von etwa 64 m: Farbe der Bodenschicht × (0,8 … 1,15 aus dem R-Kanal). So sieht man die Kachelung aus der Spielkamera kaum.

## Vorschlag Biom → Grundschicht (BiomeType)
Temperate → Grass · Boreal → Forest/Grass · Tundra → Dry/Snow · Desert → Sand/Dry · Savanna → Dry · Wetland → Wet · Alpine → Rock/Snow · TropicalForest → Forest · Mediterranean → Dry/Grass · Coast → Sand · Ocean (Grund) → Sand.
Nutzung überlagert die Grundschicht: Earth um Gebäude, Path auf Wegen, Field auf Äckern.

## Einbau (offen, macht Lead oder Art & UI)
Das Gelände ist ein eigenes Chunk-Mesh (`ChunkMeshBuilder`, `WorldMaterials`). Die Schichten müssen dort per Maske (Splat-Map je Chunk oder Vertexfarbe) gemischt werden; die heutige Farbkarte kann als Maske dienen. Die Blender-Vorschau `Tools/Blender/ground_preview.py` zeigt eine solche Mischung mit weichen, verrauschten Rändern und den Frühzeit-Gebäuden im Spielmaßstab.

## Noch nicht erledigt
- In Unity nicht getestet (kein Unity in der Cloud). Shader und Maskenmischung fehlen noch.
- Field zeigt bei 6 m Wiederholung sichtbare Furchenreihen; das passt zu Äckern, ist aber regelmäßig.
- Keine Detailobjekte (Grasbüschel, Steine, Blumen) – folgen mit den Pflanzen.

CC0 verlangt keine Namensnennung. Die Urheber stehen hier, damit die Herkunft nachvollziehbar bleibt.
