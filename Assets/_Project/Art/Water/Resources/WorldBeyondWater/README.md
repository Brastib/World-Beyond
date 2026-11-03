# Wasser-Texturen v1

Eigene Texturen, per Skript erzeugt (`Tools/Blender/water_tex.py`, reines Python mit numpy/PIL, FFT-Rauschen, dadurch nahtlos kachelbar). Keine fremden Bilder. Den Shader baut Art & UI.

| Datei | Inhalt | Größe | Import |
|---|---|---|---|
| `T_WaterFine_N.png` | feine Kräuselwellen, leichte Windrichtung | 1024² | Normal Map (OpenGL), linear, Repeat (.meta liegt bei) |
| `T_WaterCoarse_N.png` | grobe, lange Wellen in Windrichtung | 1024² | Normal Map (OpenGL), linear, Repeat (.meta liegt bei) |
| `T_WaterFoam_Mask.png` | Schaumflocken und Schlieren, weiß = Schaum | 1024², Graustufen | linear (sRGB aus), Repeat |
| `T_WaterCaustics.png` | Lichtnetz am Grund (aus gebrochenem Licht berechnet) | 512², Graustufen | linear (sRGB aus), Repeat |

Vorschläge für den Shader (nicht getestet):
- Wiederholung: fein etwa alle 3 m, grob etwa alle 16 m; beide gegeneinander verschoben bewegen (z. B. 0,03 und 0,012 m/s in leicht unterschiedlichen Richtungen) und mischen.
- Uferschaum: Schaummaske × (1 − Wassertiefe / 0,6 m), mit weichem Schwellwert, der langsam pulsiert.
- Kaustik nur im flachen Wasser (bis etwa 2 m Tiefe), zweimal mit verschiedener Geschwindigkeit überlagern und das Minimum nehmen.

Noch nicht erledigt: in Unity nicht getestet; Steg und Furt sind laut Lead noch nicht dran.
