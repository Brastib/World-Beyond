# World Beyond – Art Direction und UI Style Guide (Entwurf)

Status: **Vorschlag [E]**, noch nicht vom Nutzer bestätigt. Stand: 2026-10-25.
Noch nichts davon ist implementiert. Es existiert noch kein Unity-Projekt; die Systeme aus Phase 2–5 sind noch nicht gebaut.
Grundlage: PROJECT_MASTER D13 (1 Unity-Einheit = 1 m, 1 Kachel = 4 m), PROJECT_MASTER §8 (verbindliche Art-Direction-Regel; Style Lock §8.3 liegt bei Phase 6) und §10 (UI Toolkit, Tokens in `Assets/UI/Styles/Tokens.uss`).
Gilt für alle Threads, die Assets auswählen oder importieren (Welt, Zivilisation, Militär, Effekte, UI). Teil A legt den Grafikstil fest, Teil B die Oberfläche.

## A. Art Direction (Welt, Einheiten, Effekte)

### A1. Realismusgrad und Referenzen [E, aus Referenzbildern des Nutzers vom 2026-10-25 abgeleitet]

> **Stand 2026-10-26:** Stil = **„Naturrealismus mit warmer Farbgebung“**: realistische Formen und Materialien (VISION V5, nicht Low-Poly), dazu die kräftige, warme Farbigkeit der Forge-of-Empires-Referenz und die Fernlesbarkeit von WorldBox. Regeln: ASSETS_STYLE_ENVIRONMENT §3. Aussagen unten zu „Low-/Mid-Poly gemalt“ sind dadurch ersetzt. Bestätigung durch den Nutzer am Style-Lock-Bild. **Qualitätsmaßstab des Nutzers:** fotorealistischer Laubbaum, Referenz R1 (ASSETS_STYLE_ENVIRONMENT §3.1; Bild nur im Projektordner, Urheber unbekannt, nicht ins Spiel übernehmen). Beim Style Lock Nahvergleich mit R1; reicht URP sichtbar nicht, wird HDRP dem Nutzer zur Entscheidung vorgelegt.

Der Nutzer hat als Stil-Referenz vier Bilder geschickt: zwei WorldBox-Screenshots und zwei Forge-of-Empires-Screenshots (Stadt, Gefechtskarte mit Panzern). Daraus folgt:

**„Farbenfroher, gemalter stilisierter Realismus“**
- **Nahansicht wie Forge of Empires:** detailreiche Gebäude und Fahrzeuge mit **realistischen Proportionen**, warme, kräftige, aber natürliche Farben (sattes Grün, warmes Sand/Ocker, rote/braune Dächer, klares Blau), weiche „gemalte“ Wirkung durch Farbverläufe in Texturen, Ambient Occlusion und warmes Licht. Kein düsterer, grauer Realismus.
- **Fernansicht wie WorldBox:** die Welt liest sich wie eine bunte Landkarte: klar getrennte Biomfarben, helle Küstenstreifen (Flachwasser türkis, Tiefwasser kräftig blau), Siedlungen und Territorien als klare Farbflächen und Symbole.
- WorldBox' Pixel-/Voxel-Optik wird **nicht** übernommen (das Spiel ist 3D, und Pixel-Modelle würden zu den realistisch proportionierten Fahrzeugen nicht passen). Übernommen werden Farbigkeit und Lesbarkeit.

Ehrliche Einschränkung: Die handgemalte Detailtiefe von Forge of Empires (dort Auftragsgrafik) wird mit freien Assets nicht 1:1 erreicht. Erreichbar ist eine ähnliche Wirkung: Low-/Mid-Poly-Modelle, umgefärbt auf die gemeinsame Palette, mit Farbverlaufs-Texturen, AO und warmem Post-Processing. Das wird im Style Lock (PROJECT_MASTER §8.3) am gerenderten Bild geprüft.

Begründung (geprüft am 2026-10-25, Stichprobe):
- Zusammenpassende freie Sets gibt es vor allem in diesem Stil, z. B. Quaternius „Animated Tanks Pack“ (laut Seite CC0, FBX) und ITHappy „Military Free“ (60 Modelle inkl. Panzer, Fahrzeug, Hubschrauber, Mörser; FBX/glTF; URP-kompatibel laut Seite). **Die Lizenz von ITHappy stand nicht auf der Produktseite und ist noch ungeprüft.**
- Nachtrag aus dem Militär-Thread (Details: `proposals/PHASE5_MILITARY_ASSETS.md`): bestes zusammenpassendes Fahrzeug-Set ist Zsky „Low Poly Military Vehicles“ auf poly.pizza (9 Modelle, FBX/glTF, CC BY 3.0, Namensnennung Pflicht). Passt zu diesem Stil. Quaternius-Panzer sind untexturiert und nur Reserve. Humvee, MG-Humvee, Artillerie und Raketenwerfer müssen aus Jeep/Truck plus Anbauteilen zusammengesetzt werden. Vorschaubilder noch nicht visuell geprüft.
- Fotorealistische freie Militärmodelle (z. B. einzelne Sketchfab-Modelle) stammen von verschiedenen Autoren mit stark schwankender Qualität und meist CC BY. Ein einheitliches Bild wäre damit kaum erreichbar.
- Bei vielen Einheiten und weit herausgezoomter Kamera sind schlichte Formen besser lesbar und deutlich performanter.
Alternative: „Realistisch“ (PBR, Poly-Haven-Texturen, realistische Fahrzeuge). Höherer Aufwand, Risiko von Stilbrüchen und Performanceproblemen.

### A2. Maßstab (verbindlich für jeden Import)

- Verbindlich laut PROJECT_MASTER D13: **1 Unity-Einheit = 1 m, 1 Kachel = 4 m, alle Modelle in Originalgröße** (kein Überhöhungsfaktor). Lesbarkeit beim Herauszoomen kommt aus den Symbol-Ebenen in B §7.
- Referenzgrößen (Unity-Einheiten = Meter): Soldat 1,8 hoch · Humvee ca. 4,6 × 2,2 · Kampfpanzer ca. 9,5 × 3,7 (mit Rohr) · Wohnhaus ca. 8 × 8 (2 × 2 Kacheln) · Baum 8–15 hoch.
- (Frühere Fassung mit ×2,5 und 10-m-Kacheln ist verworfen.)
- Pivot unten mittig, Vorderseite +Z, Import-Skala im Prefab korrigieren, nicht im Modell.

### A3. Geometrie und Texturen

| Kategorie | Dreiecke LOD0 | Textur max. |
|---|---|---|
| Soldat | ≤ 1 500 | ≤ 1K (oft Farbpalette) |
| Fahrzeug | ≤ 5 000 | ≤ 1K |
| Gebäude | ≤ 4 000 | 1K–2K |
| Bäume/Felsen | LOD-Kette: LOD0 ca. 15 000–30 000 (nur wenige Bäume direkt an der Kamera, Maßstab R1), LOD1 ≈ 1 500, LOD2 ≈ 300, danach Impostor; Endwerte nach Messung in Unity | 2K für LOD0, sonst ≤ 1K |
| Terrain-Oberflächen | – | 1K–2K, kachelbar |

Mindestens 2 LOD-Stufen plus Symbol in der Fernansicht. Bevorzugte Formate: FBX oder glTF, Texturen PNG. Keine 4K-Texturen. Texturwerte folgen PROJECT_MASTER §8.2.

Style Lock: vor dem ersten Massenimport Referenzszene laut PROJECT_MASTER §8.3 (Terrain + Haus + Soldat + Humvee + Panzer + Explosion im echten Licht), Bestätigung durch den Nutzer per Screenshot.

### A4. Farben und Materialien

- Ein gemeinsames URP-Lit-Material pro Kategorie, Smoothness niedrig (0,1–0,3), keine Spiegelungen außer Wasser und Glas.
- Fremd-Assets werden auf eine gemeinsame Palette umgefärbt: Natur kräftig und warm (Gras #6FA83C, Wald #3F7A2E, Sand #E3C987, Fels #8A8378, Schnee #EEF1F2, Flachwasser #3FB6C9, Tiefwasser #2462B5); Gebäude mit warmen Ziegel-, Holz- und Steintönen; Militär in Oliv/Sand/Grau, etwas aufgehellt, damit es auf dem Terrain lesbar bleibt; die **Zivilisationsfarbe** erscheint als Akzent (Flaggen, Dächer, Fahrzeugmarkierungen, Territoriumsrand), in der Fernansicht als Fläche.
- Kräftige, warme Farben ja; grelle Neon- oder reine Spielzeugfarben (z. B. Plastik-Rot/Blau ohne Schattierung) nein.
- Flache Low-Poly-Modelle ohne Textur bekommen einen leichten Farbverlauf (unten dunkler) und AO, damit sie zur gemalten Wirkung passen.

### A5. Licht

Eine Directional Light (Sonne, warm, ca. 50° Höhe), weiche Schatten, leichter blauer Umgebungsanteil, schwacher Nebel in der Ferne. Post-Processing: Tonemapping (Neutral oder ACES, im Style Lock zu wählen), Sättigung leicht angehoben (+10–15), warme Farbbalance, SSAO, Bloom nur für Explosionen und Mündungsfeuer.

### A6. Effekte

Unity Particle System, gepoolt (ARCHITECTURE §7). Rauch grau-braun und halbtransparent, Feuer orange ohne Neon, Explosionen kurz (< 1,5 s) mit Staubwolke und Lichtblitz. Effekt-Texturen aus einer Quelle (z. B. Kenney Particle Pack, CC0, noch zu prüfen), damit sie zusammenpassen.

### A7. Abnahme jedes Assets

1. Lizenz geprüft und in `Assets/ThirdParty/ASSET_CREDITS.md` eingetragen (Quelle, Link, Lizenz, Pfad).
2. Maßstab gegen die Referenzgrößen geprüft.
3. Material auf Palette und gemeinsames Material umgestellt.
4. Im Spiel gerendert und neben Soldat, Humvee und Haus verglichen (Screenshot bei Nah- und Fernzoom), nicht nur anhand des Vorschaubilds.
5. Platzhalter werden in einer Liste geführt, bis sie ersetzt sind.

## B. Benutzeroberfläche

## 1. Designrichtung

„Kartentisch eines Generalstabs“: dunkle, warme Flächen (Holz-/Lederanmutung nur angedeutet, passend zur Forge-of-Empires-Referenz), die die bunte Welt nicht überdecken; Information wie auf einer Lagekarte. Keine Glas-/Neon-Effekte, keine Verläufe als Dekoration, keine runden „App“-Karten. Kanten leicht gefast (Radius 2 px), dünne Linien statt Schatten.

## 2. Typografie (alle Schriften SIL Open Font License, frei)

| Rolle | Schrift | Größe (bei 1080p, Skalierung 100 %) |
|---|---|---|
| Titel, Panel-Überschriften (Versalien, Laufweite +1 px) | Barlow Condensed SemiBold | 20 / 16 px |
| Fließtext, Buttons, Tooltips | IBM Plex Sans Regular/Medium | 14 px (min. 12 px) |
| Zahlen, Ressourcen, Koordinaten (Tabellenziffern) | IBM Plex Mono Medium | 13 px |

Regel: maximal 3 Größen pro Panel. Zahlen immer rechtsbündig in Mono.

## 3. Farbpalette (Tokens)

| Token | Hex | Verwendung |
|---|---|---|
| `--bg-0` | #12161B | Panel-Hintergrund (92 % Deckkraft) |
| `--bg-1` | #1B2128 | Zeilen, Eingabefelder |
| `--line` | #2E3742 | Rahmen, Trenner |
| `--text-hi` | #E8E4D8 | Haupttext (warmes Papierweiß) |
| `--text-lo` | #8E959C | Beschriftungen, deaktiviert |
| `--accent` | #C9A24A | Messing: Auswahl, aktiver Tab, Fokus |
| `--ok` | #6FA86A | Bestätigung, Frieden, Überschuss |
| `--warn` | #D99A3D | Warnung, Spannung |
| `--danger` | #C8553D | Krieg, Fehler, Verluste, destruktive Aktionen |
| `--info` | #5C8FB8 | Hinweise, Handel, Bündnis |

Zivilisationsfarben: eigene Palette aus 16 gut unterscheidbaren Farben (auch für Rot-Grün-Schwäche geprüft), getrennt von den UI-Statusfarben. Status nie nur über Farbe: immer zusätzlich Icon oder Text.

## 4. Abstände und Raster

4-px-Raster. Tokens: `--s1` 4, `--s2` 8, `--s3` 12, `--s4` 16, `--s6` 24 px. Panel-Innenabstand 12, Zeilenhöhe 28, Button-Höhe 32, Klickfläche min. 28 × 28 px.
UI-Skalierung: 75–150 % über `PanelSettings.scale` + Einstellung im Menü; getestete Ziel-Auflösungen 1280×720 bis 3840×2160.

## 5. Icons

- Stil: einfarbige Silhouetten, 2-px-Kontur, Raster 24 × 24, exportiert als PNG in 32/64 px (UI Toolkit zeichnet SVG nicht ohne Weiteres; genaue Möglichkeit hängt von der Unity-Version ab, noch zu prüfen).
- Einfärbung zur Laufzeit über `-unity-background-image-tint-color`, damit eine Datei für alle Zustände reicht.
- Quellen: Kenney Game Icons (CC0, laut Kenney; vor Import prüfen) bevorzugt; game-icons.net (CC BY 3.0, Nennung in `ASSET_CREDITS.md` nötig) als Ergänzung.

## 6. Zustände und Feedback

| Zustand | Darstellung |
|---|---|
| Hover | Hintergrund `--bg-1` → +6 % Helligkeit, 80 ms |
| Ausgewählt/aktiv | 2-px-Messinglinie links bzw. unten |
| Deaktiviert | `--text-lo`, Tooltip nennt den Grund („Erfordert Technologie X“) |
| Nicht implementiert | Button wird **nicht angezeigt** (kein Fake-Button) |
| Destruktiv | `--danger`-Rahmen + Bestätigungsdialog |

Animationen: nur Übergänge 80–150 ms (ease-out), keine dauerhaften Pulse außer bei kritischen Ereignissen. Option „Animationen reduzieren“ in den Einstellungen.

## 7. Weltdarstellung (Lesbarkeit beim Herauszoomen)

- Drei Zoomstufen: Nah (Modelle), Mittel (Modelle + Symbole), Fern (Territorien als flächige Zivilisationsfarbe, Städte als Symbol + Name, Armeen als Rautensymbole mit Stärkebalken).
- Auswahlmarkierung: Messing-Ring am Boden (Projektor/Decal), Gruppen mit Nummer; Feinde rot umrandet nur bei Auswahl/Hover.
- Beleuchtung: eine Sonne, fester warmer Winkel. Fernansicht im WorldBox-Sinn: Biomfarben kräftig, Territorien als halbtransparente Zivilisationsflächen mit kräftigem Rand, Einheiten und Städte als Symbole mit dunklem Rand, damit sie sich vom bunten Terrain abheben.

## 8. Offene Punkte [O]

- Richtung aus Referenzbildern abgeleitet (A1); Bestätigung durch den Nutzer am Style-Lock-Screenshot.
- Lizenz ITHappy „Military Free“ prüfen.
- Bestätigung der UI-Richtung, Schriften und Palette durch den Nutzer.
- Unity-Version (bestimmt SVG-Unterstützung und UI-Toolkit-Funktionen).
- Umsetzung erst, wenn Phase 2–5 existieren.

## 9. Zuständigkeit und Schnittstellen (laut Integrations-Thread, Docs/INTEGRATION.md §3)

Dieser Bereich verantwortet ISelectionService, GodToolController, HUD/Minimap, IGameEventFeed sowie die Darstellung von Audio und VFX. UI liest nur Query-Interfaces und sendet nur IWorldCommands über die eine ICommandQueue. Kein eigenes Zeit-/Tick-System und kein eigener Bootstrap: Spielgeschwindigkeit und Pause laufen über SimulationRunner/GameSpeed aus dem Fundament.

## 10. Regeln aus dem Speichersystem (laut Phase 7, ARCHITECTURE §11 / SYSTEM_INTERFACES §11)

- Kein Spielzustand in UI-Objekten.
- Auswahl wird nur als EntityId gehalten und vor jeder Nutzung mit `Exists` geprüft; ungültige IDs fallen still aus der Auswahl. Nach dem Laden ist die Auswahl leer.
- Lade- und Speicherfehler zeigt die UI mit der `UserMessage` aus SaveResult/LoadResult an (Fehlerarten ARCHITECTURE §11.7), nicht mit eigenen Texten.
