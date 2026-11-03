# Ochse und Ochsenkarren v1 (Frühzeit)

Eigene Modelle, gebaut per `Tools/Blender/unit_build.py` (Blender 4.0, headless). Stand 2026-11-03.
Beide Modelle bestehen aus getrennten Teilen, damit der Code sie ohne Skelett animieren kann.
Jedes Teil hat seinen Drehpunkt als Ursprung. Gedreht wird um die lokale X-Achse (quer zur Laufrichtung).

| Datei | Teile (Objektnamen) | Drehpunkt | Dreiecke |
|---|---|---|---|
| `WB_Unit_Ox` | `Ox_Body` | Boden zwischen den Hufen (Wurzel) | 7 752 gesamt |
| | `Ox_Head` (mit Hals, Hörnern, Ohren) | Nacken | |
| | `Ox_Leg_FL`, `Ox_Leg_FR`, `Ox_Leg_BL`, `Ox_Leg_BR` | Schulter bzw. Hüfte oben am Bein | |
| | `Ox_Tail` | Schwanzansatz | |
| `WB_Unit_Cart` | `Cart_Body` (Plattform, Flechtwände, Deichsel, Joch, Ladung) | Boden unter der Achsmitte (Wurzel) | 2 752 gesamt |
| | `Cart_Wheel_L`, `Cart_Wheel_R` (Scheibenräder aus drei Bohlen, Ø 0,92 m) | Achsmitte des Rads | |

- Maße: Ochse etwa 2,5 m lang, Widerrist 1,6 m. Karren 1,9 m Ladefläche, Spur 1,36 m.
- Einspannen: Ochsen-Ursprung = Karren-Ursprung + 2,53 m nach vorn (Unity +Z). Dann liegt das Joch auf dem Nacken.
- Rad drehen: Winkel (Grad) = Wegstrecke / (2π · 0,46 m) · 360 um die lokale X-Achse.
- Gang-Vorschlag: Beine diagonal paarweise (FL+BR, FR+BL) um ±18° schwingen, Kopf ±4° nicken, Schwanz pendeln.
- Blickrichtung: Unity +Z. 1 Einheit = 1 m.

## Materialien
Slot `M_<Name>` → `Textures/T_<Name>_D/_N/_MS` wie bei den Gebäuden: `M_Fur` (eigene Felltextur), `M_Hide`, `M_Wood`, `M_Bark`, `M_Horn` (vorerst eine Kopie von T_Wood).

## Noch nicht erledigt
- In Unity nicht getestet (kein Unity in der Cloud). Ob die Teile beim Import ihren Drehpunkt behalten, muss beim Einbinden geprüft werden.
- Der Ochse ist eine einfache, glatte Form ohne Muskeldetails und ohne Augen. Fürs Spiel aus der Ferne reicht das; für Nahansichten folgt eine bessere Fassung.
- Keine LOD-Stufen, kein Geschirr außer Joch und Deichsel.

## Herkunft
T_Hide: fabric_leather_02 · T_Wood: rough_wood · T_Bark: bark_brown_02 (Poly Haven, CC0). T_Fur: eigene, per Skript erzeugte Textur.
