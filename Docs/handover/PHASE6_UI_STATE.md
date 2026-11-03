# Phase 6 – UI-Stand (M1.5)

Stand: 2026-10-26, Branch `ui/m1-hud` auf Basis `integration/m1` @16ca06c. Status: **Code fertig, nicht im Unity-Editor gelaufen.**

## Was es gibt

Eine Komponente `WorldBeyond.Presentation.UI.GameHud` (UI Toolkit, baut ihr UIDocument und ihre PanelSettings zur Laufzeit, keine UI-Assets nötig):

| M1-Punkt | Umsetzung | Quelle der Daten / Wirkung |
|---|---|---|
| Zeitleiste | Pause, 1×, 2×, 5×, 10× als Knöpfe; Leertaste = Pause umschalten, Tasten 1–4 = 1×/2×/5×/10×; Anzeige „Jahr N, Tag N“ | `ISimulationRunner.Speed`; Datum aus `CivTime.TicksPerYear`, 360 Tage je Jahr (Platzhalter bis GameCalendar M2) |
| Civ-Übersicht | Name mit Farbstreifen, Einwohner, Siedlungen, Militär, Epoche, Kriegsstatus; einklappbar; Klick → Kamera zur Hauptstadt | `ICivilizationQuery`, `IWarQuery` |
| Ereignis-Feed | Krieg erklärt, Kriegsbeitritt, Frieden, Waffenstillstand, Bündnis, Eroberung, Zerfall, Gründung, Epochenwechsel; neueste oben, 12 sichtbar, 60 gespeichert; Klick → Kamera zum Ort + Auswahl | Events über `ctx.Events`; nach Laden leer |
| Speichern/Laden | 5 feste Plätze, Überschreiben und Laden mit zweitem Klick bestätigen, Meldung = `SaveResult/LoadResult.UserMessage`, Spiel läuft weiter | `SaveLoadController.Service` |
| Auswahl-Info | Einheit (Typ, Civ, Zustand, Lebenspunkte, Tempo) oder Kachel; steht auf der Kachel eine Siedlung, zeigt das Panel sie (Einwohner, Wohnraum, Arbeit, Nahrung, Gebäude, Lager) | `WorldSelection.SelectionChanged`, `IUnitQuery`, `ISettlementQuery.FindAt`; ausgewählte Siedlung → `ICivSimulationLod.SetSelectedSettlement` |

`GameHud` setzt `WorldSelection.IsPointerOverUi`, damit Klicks auf Panels nicht in die Welt durchgehen.

## Einbau in die Szene

`GameHud` auf ein beliebiges GameObject in `World.unity` legen. Verweise (GameBootstrap, SaveLoadController, WorldSelection, StrategyCameraController) werden automatisch gesucht, wenn sie im Inspector leer sind. Optional: ein Runtime-Theme (`ThemeStyleSheet`) zuweisen; nötig ist es nicht, weil die HUD nur eigene Stile benutzt.

## Geprüft

- `dotnet test Tools/UiTests`: 7 Tests der Unity-freien UI-Modelle gegen die echte GameComposition (Datum, Geschwindigkeit, Übersicht inkl. Kriegsstatus, Feed für Krieg/Frieden/Eroberung/Zerfall, Auswahl inkl. ungültiger ID, Speichern/Laden über SaveLoadService). Grün.
- `dotnet build Tools/UiViewCheck`: alle Skripte inkl. `GameHud` gegen Unity-Referenz-DLLs 2021.3 (NuGet `UnityEngine.Modules`). 0 Fehler. Prüft nur Typen/APIs, nicht Unity 6 und nicht den Input-System-Zweig.
- `dotnet build Tools/UnityApiCheck`: 0 Fehler (enthält Presentation nicht).

## Nicht geprüft / offen

- Kein Unity-Lauf: Layout, Lesbarkeit, Skalierung, Klicks und Tastatur sind erst nach dem DevOps-Lauf bestätigt.
- Schriften: Unity-Standardschrift; Barlow Condensed / IBM Plex (UI_STYLE_GUIDE B2) sind noch nicht importiert.
- Design-Tokens liegen in `UiTheme.cs` statt `Tokens.uss` (Abweichung von ARCHITECTURE §10, damit keine ungeprüften UI-Assets nötig sind).
- Mausrad über einem Panel zoomt weiterhin die Kamera (StrategyCameraController fragt `IsPointerOverUi` nicht ab; Besitzer World).
- Feed-Panel scrollt nicht; ältere Einträge sind gespeichert, aber nicht sichtbar.

## Nach dem ersten Unity-Lauf (2026-10-27, integration/m1 @b008d8c)

- Weiße Scheibe/Streifen auf dem Wasser: Ersatz-Wassermaterial jetzt matt (Glätte 0,4, Specular Highlights und Environment Reflections aus). In Unity ungeprüft. Falls Bloom im URP-Volume der Szene den Rest erzeugt: Schwelle anheben (Volume liegt in der Szene, nicht im Code).
- Warnung "No Theme Style Sheet": GameHud setzt ein leeres ThemeStyleSheet und die eingebaute Schrift. In Unity ungeprüft.
- Notiert, kein M1-Blocker: Flüsse wirken treppig, weil Wasser kachelweise als Vierecke gebaut wird (ChunkMeshBuilder.BuildWater). Möglich später: Flussverlauf als geglättete Linie/Spline mit eigener Breite.

## M1: Gebäude, Einheiten, Gebiete, Ortsnamen (Branch art/m1-world-objects, 27.10.2026)

**Nichts davon ist in Unity geprüft.** Geprüft: `dotnet build Tools/UiViewCheck` und `Tools/UnityApiCheck` fehlerfrei,
`dotnet test Tools/UiTests` 12/12 grün (5 neue Tests in WorldObjectsTests.cs).

Neue Dateien:
- `Presentation/WorldObjects/Model/BuildingVisuals.cs` – Unity-frei: alle lebenden Gebäude (IBuildingQuery) mit Form, Civ (über Siedlung), Bauzustand; zerstörte Gebäude bleiben ein Spieljahr als Ruine. Version steigt nur bei Änderung.
- `Presentation/WorldObjects/Model/UnitInterpolator.cs` – Unity-frei: Position vorletzter/letzter Tick je Einheit, Lerp mit `InterpolationAlpha`; Tote fallen heraus.
- `Presentation/WorldObjects/Model/TerritoryGrid.cs` – Unity-frei: Kopie von `OwnerCivId`, liefert geänderte Chunks und Grenzkanten (die Simulation meldet Besitz nicht je Kachel, darum Vergleich alle 2 s).
- `Presentation/WorldObjects/PlaceholderMeshes.cs` – **Platzhalter bis R2**: flach schattierte Formen je Gebäudeform (10) und Einheitenrolle (5). Natürliche Materialfarben (Putz, Holz, Ziegel, Stroh), Civ-Farbe nur als Sockelband/Fahne.
- `Presentation/WorldObjects/WorldObjectsRenderer.cs` – Graphics.DrawMeshInstanced (keine GameObjects), Gebäude je Chunk mit Frustum-Culling, Einheiten jedes Bild; ein Material je Civ-Farbe; Einheiten werden ab 60 m Kameraabstand bis 6× vergrößert (Lesbarkeit aus der Strategieansicht). Gebiete: ein Mesh je Chunk mit Shader `Sprites/Default` (Rückfall URP Particles/Unlit), Tönung 16 % + Grenzlinie.
- `Presentation/UI/View/SettlementLabels.cs` – Ortsschilder (UI Toolkit, nicht anklickbar), Hauptstadt mit ★, Sichtweite nach Stufe.
- GameHud: Knöpfe „Gebiete“ und „Namen“ in der Zeitleiste; legt einen WorldObjectsRenderer an, falls die Szene keinen hat.

Im Unity-Lauf bitte prüfen: Gebäude stehen auf dem Boden (nicht schwebend/versunken), Ruinen/Baustellen sichtbar, Einheiten bewegen sich ohne Ruckeln bei 1×–10×, Gebietsfläche flackert nicht (z-fighting) und ist im Build vorhanden (Shader `Sprites/Default` muss im Build enthalten sein), Ortsschilder ziehen beim Kameraschwenk nicht nach, Bildrate mit vielen Einheiten.
Offen: Ruinen-Anzeige ist nicht durch einen Test abgedeckt (kein öffentlicher Weg, ein Gebäude gezielt zu zerstören). Endgültige Modelle folgen nach ASSET_CATALOG (Bauteil-Sätze je Stamm, PR #9).
