# World Beyond – IMPLEMENTATION_PLAN

Stand: 2026-10-25. **Phasennummern = Nummern aus den Aufträgen des Nutzers** (PROJECT_MASTER D15). Zuständigkeiten pro Klasse: `Docs/INTEGRATION.md` §3 (Zuständigkeitsregister). Bei Widerspruch zu INTEGRATION.md gilt bei Zuständigkeiten INTEGRATION.md.
Jede Phase endet mit: Tests laut TEST_PLAN ausgeführt (oder ehrlich als "nicht ausgeführt" markiert) und CURRENT_STATE aktualisiert.

## Übersicht

| Phase | Thread | Hauptaufgabe | hängt ab von |
|---|---|---|---|
| 1 | Projektanalyse und Kerndokumentation | Doku (dieses Dokument und die sechs anderen) | – |
| 1b | Integrations-Thread | **Fundament**: Unity-Projekt, Core, Tick, EventBus, CommandQueue, ISaveable, UnitStore, GameBootstrap | 1, Projektort (V1–V3) |
| 2 | World Engine prozedural generieren | Welt, Terrain, Kamera, Navigation, SpatialIndex, Bewegung, Auswahl-Grundlage, Minimap-Grundlage | 1b |
| 3 | Zivilisationssimulation aufbauen | Zivilisationen, Siedlungen, Gebäude, Bevölkerung, Wirtschaft, Territorium, Technik, Civ-KI (Wirtschaft) | 1b, 2 |
| 4 | Diplomatie und Kriegs-KI umsetzen | Beziehungen, Verträge, Kriege, Kriegsentscheidung, Kriegsziele, Frieden | 3; Ausführung über 5 |
| 5 | Modernes Militärsystem entwickeln | Militär-Einheitentypen, Kampf, Geschosse, Raketen, Explosionen, Armeen, Armee-KI | 2, 3 (Produktion/Kosten), 4 (Kriegszustand) |
| 6 | Visual Polish und UI | Style Lock, vollständige UI, God-Modus-Werkzeuge, Audio/VFX-Darstellung, Menüs | 2–5 schrittweise |
| 7 | Save System und Performance | SaveLoadService vollständig, Migration, Fehlerfälle, Profiling, Optimierung | 1b–5 |
| 8 | Endabnahme | 15-Schritte-Spieltest (TEST_PLAN §6), Windows-Build, Abschlussdoku | alle |

**Harte Regel:** Fachcode kommt erst auf `main`, wenn 1b gemergt ist; vorher sind eigene Branches mit eigenen Dateien erlaubt. Erlaubt sind Entwürfe unter `Docs/drafts/` oder `Docs/proposals/` und Asset-Recherche.

Hinweis: Der God-Modus (Systemliste Punkt 8) hat keinen eigenen Nutzerauftrag. Er liegt laut INTEGRATION.md bei Phase 6 (Werkzeuge/UI); die zugehörigen Commands liefern die Fachsysteme (Terrain-Commands: Phase 2, Spawn/Siedlung: Phase 3, Krieg/Frieden erzwingen: Phase 4, Explosion/Zerstörung: Phase 5).

## Erstes Spielziel: Meilenstein M1 (Nutzervorgabe 2026-10-25)

Generierte Welt mit Terrain · zwei wachsende Zivilisationen · Einheiten, die sich bewegen · eine Kriegserklärung mit funktionierendem Kampf · bedienbare Oberfläche · Speichern und Laden. Ziele, Nachweise und M1-Umfang je Phase: `Docs/INTEGRATION.md` §4. Alles, was M1 nicht braucht, wartet bis nach M1.

Reihenfolge bis M1:
1. 1b Fundament → Simulation tickt, Geschwindigkeit, core/units speicherbar. *(Code + dotnet-Tests fertig, Unity offen)*
2. Phase 2 → Welt sichtbar, Kamera, Einheiten bewegen sich (M1.1, M1.3).
3. Phase 3 → zwei Civs mit Siedlung wachsen (M1.2).
4. Phase 7 → Welt + Einheiten + Civs speichern/laden (M1.6).
5. Phase 6 → Geschwindigkeit, Auswahl, Feed, Krieg-Knopf über echte Commands (M1.5).
6. Phase 4 + 5 → Kriegserklärung → zwei Infanteriegruppen kämpfen (M1.4).
7. M1-Abnahme im Editor, danach jede Phase in die Breite.

Arbeitsregeln (keine parallelen Änderungen an gemeinsamen Dateien, Branch + PR je Phase, prüfbarer Übergabestatus): `Docs/INTEGRATION.md` §0.

---

## Phase 1b – Fundament (Besitzer: Integrations-Thread)
**Voraussetzung:** Projektort festgelegt (V3), Unity-Version geprüft (V1).
1. Unity-Projekt (URP-Vorlage) anlegen oder vorhandenes übernehmen; Pakete aus ARCHITECTURE §15 prüfen; `Packages/manifest.json` und `ProjectSettings/ProjectVersion.txt` in CURRENT_STATE dokumentieren.
2. Ordnerstruktur und Assembly Definitions (ARCHITECTURE §13).
3. Core: `EntityId`, `ISimEventBus`, `SimRandom`; ID-Regeln nach SAVE_FORMAT_PROPOSAL §A.
4. Simulation: `SimClock`, `ISimulationSystem`, `SimContext`, `ICommandQueue`, Tick-Reihenfolge (SYSTEM_INTERFACES §2).
5. Application: `GameBootstrap`, `SimulationRunner` (Pause/1/2/5/10x), `InputRouter`.
6. `ISaveable` + `SaveLoadService`-Grundgerüst (Clock + Random speichern/laden).
7. `UnitStore` (gemeinsam für Zivil- und Militäreinheiten).
8. Szenen Boot/MainMenu/World, Debug-Overlay (FPS, Tick, Tick-Zeit, Geschwindigkeit).
**Abnahme:** Spiel startet, Tick zählt, Geschwindigkeit umschaltbar, Speichern/Laden stellt Tick und Zufallszustand wieder her, EditMode-Tests T-1b-* grün.

## Phase 2 – World Engine
1. `IWorldGrid`/`IWorldQuery`/`TileData` nach SYSTEM_INTERFACES §4 (Koordinaten: §4.1).
2. `IWorldGenerator`: Höhen (mehrstufiges Rauschen), Meeresspiegel, Küsten, Seen, Flüsse, Ebenen/Hügel/Berge, Biome (Höhe + Feuchte + Temperatur), Wälder, Ressourcen. **Deterministisch pro Seed** (nur `SimRandom`/eigene Noise-Funktion).
3. Terrain-Rendering: Chunk-Meshes (32 × 32 Kacheln), kachelbare echte Terrain-Texturen (z. B. Poly Haven/ambientCG, Lizenz prüfen), Wasser (Shader Graph), Instancing für Bäume/Felsen mit echten Modellen.
4. RTS-Kamera: Pan, Zoom, Rotation, Kantenscrollen, Grenzen.
5. Teil-Rebuild bei `TileChangedEvent`.
6. Begehbarkeit pro `MovementClass` (Fuß, Rad, Kette), Hindernisse, `IPathfinder` (A* mit Tick-Budget), `ISpatialIndex`, `MovementSystem`.
7. Auswahl-Grundlage: Raycast Bildschirm → Kachel/Welt, Markierung (Decal/Projektor).
8. Minimap-Grundlage: Textur aus Raster.
9. `ISaveable` "world": Seed + Kachel-Deltas (SAVE_FORMAT_PROPOSAL §C).
**Abnahme:** T-2-*: gleicher Seed = identisches Raster; 256 × 256 in < 3 s; Chunk-Rebuild < 5 ms; Pfad über 200 Kacheln < 10 ms; Speichern/Laden identisch.

## Phase 3 – Zivilisation
1. Zivil-Einheitentypen (Einwohner, Arbeiter, Siedler) in die gemeinsame `UnitTypeData`-Tabelle; Darstellung mit echten Modellen (`Graphics.RenderMeshInstanced`).
2. `ICivilizationRegistry`, `ISettlementRegistry` (int-IDs, nie wiederverwendet), Gebäude (Wohnen, Versorgung, Produktion, Lager, Verteidigung) als Daten + echte Modelle.
3. Bevölkerung (Geburt, Tod, Migration, Hunger, Wohnraum), Arbeiter sammeln/transportieren Ressourcen, `IEconomy` als einziges Lager.
4. `ITerritoryService`: schreibt nur über `IWorldGrid.Set` in `TileData.OwnerCivId` (einzige Wahrheit).
5. Siedler gründen neue Siedlungen; Stadtstufen.
6. `ITechTree` mit Epochen (Frühzeit → Industrie → Moderne); Moderne schaltet Militärtechnik aus Phase 5 frei.
7. Civ-KI-Rahmen (`CivAISystem`) mit Wirtschafts-/Expansionsbewertung; Kriegsbewerter kommt aus Phase 4.
8. Detailstufen: entfernte Siedlungen abstrahiert simulieren (größeres TickInterval), ohne sichtbare Inkonsistenz.
9. Spieler-Commands: Zivilisation erschaffen, Siedlung gründen, Ereignis auslösen.
**Abnahme:** T-3-*: ≥ 2 Zivilisationen wachsen 30 Spielminuten ohne Eingriff; Ressourcenverbrauch, Bevölkerung, Stadtgründung und Speichern/Laden geprüft.

## Phase 4 – Diplomatie und Kriegs-KI
Grundlage: `Docs/PHASE4_DIPLOMACY_WAR_DESIGN.md`.
1. `IDiplomacyService` / `IDiplomacyQuery`: Meinung, Modifikatoren, Status (Frieden, Spannung, Waffenstillstand, Bündnis, Krieg).
2. Verträge und Kriege als eigene Einträge mit int-ID (`IWarQuery`).
3. Faktoren: Grenzkontakt (`ITerritoryService`), Ressourcenkonkurrenz, Stärke, Bedrohung, Entfernung, Geschichte, Handel.
4. Kriegsbewerter für die Civ-KI (Utility AI): Kriegserklärung, Kriegsziele (Siedlungen, strategische Punkte), Bündnispflichten.
5. Friedensverhandlung abhängig von warScore/Erschöpfung; Gebietsabtretung über `ISettlementRegistry.TransferSettlement`.
6. Rekrutierungsanfragen über `IRecruitment` (von Phase 3/5 bereitgestellt); Ausführung der Kämpfe ausschließlich durch Phase 5.
7. Kriegsübersicht-Daten für UI (Phase 6).
**Abnahme:** T-4-* (Kriegserklärung, Frieden, Bündnis, Zielauswahl, Verluste, kein Krieg ohne Grund, Laden/Fortsetzen).

## Phase 5 – Modernes Militärsystem
1. Datenstruktur `MilitaryUnitTypeData` (erweitert `UnitTypeData`): HP, Panzerung, Geschwindigkeit, Sichtweite, `MovementClass`, Waffen, Munition, Kosten, Tech-Voraussetzung.
2. Mindestkategorien: Infanterie, Aufklärungsfahrzeug, Humvee (Transport), Humvee mit MG, weitere leichte Kampf-/Unterstützungsfahrzeuge, Kampfpanzer, Artillerie, Raketenfahrzeug, Versorgungsfahrzeug; weitere über Daten.
3. Rekrutierung/Produktion (Kaserne, Fabrik) über `IEconomy`, `IRecruitment`.
4. `ICombatService` (einziger Weg, HP zu senken), Schadensarten, Panzerung, Nachladen/Munition, Reichweite, Sichtlinie (Höhenraster).
5. `IProjectileSystem`: Hitscan, ballistisch, Rakete (echtes Spielobjekt mit Flug, Zielzuweisung, Treffer, Explosion), Artilleriegranate; Treffer über SpatialIndex + Höhe; Pooling.
6. Explosionen, Flächenschaden, Krater/Brand (`IWorldGrid.Set`), Gebäude- und Fahrzeugzerstörung (Wrack-Modell).
7. `IArmyRegistry`, Armee-KI (operativ), taktisches Verhalten; reagiert auf `WarDeclaredEvent`.
8. Manuelle Kontrolle: Befehle Bewegen, Wegpunkte, Angreifen, Verteidigen, Rückzug (Commands, UI in Phase 6).
9. Echte Militär-Modelle nach Style Lock importieren; VFX mit echten Partikel-Texturen.
**Abnahme:** T-5-*: Infanterie, Humvee, bewaffneter Humvee, Panzer und Rakete in realer Spielsituation; 2 × 200 Einheiten ≥ 30 FPS.

## Phase 6 – Visual Polish und UI
Grundlage: `Docs/UI_STYLE_GUIDE.md` (Entwurf).
1. **Style Lock** (PROJECT_MASTER §8.3) – so früh wie möglich nach 1b, mit Asset-Kandidaten aus Phase 2 und 5.
2. Hauptmenü, Neues Spiel/Welteinstellungen, Pause/Geschwindigkeit, Speichern/Laden-Dialoge, Einstellungen (Audio, Grafik, Steuerung).
3. Zivilisationsübersicht, Einwohner/Ressourcen/Stadt-Infos, Diplomatie/Kriegsübersicht.
4. Auswahl (Klick, Rechteck, Gruppen), Befehlsleiste, Wegpunkte, Statusanzeigen.
5. God-Modus-Werkzeugleiste (`IGodTool`) mit Commands der Fachsysteme.
6. Minimap, Ereignis-Feed (`IGameEventFeed`), Tooltips, Fehlermeldungen, Bestätigungsdialoge.
7. `IAudioService`, `IVfxService`: Treffer, Explosionen, Partikel, Umgebung, UI-Sounds, Musik.
8. Lesbarkeit bei Fern-Zoom (Symbole, Territorien).
**Abnahme:** T-6-*: Auflösungen 1280×720 bis 3840×2160, Skalierung, jede Schaltfläche führt eine echte Aktion aus.

## Phase 7 – Save System und Performance
Grundlage: `Docs/proposals/SAVE_FORMAT_PROPOSAL.md`.
1. SaveLoadService vollständig: Sektionen, Prüfsumme, atomares Schreiben, Slots, Autosave.
2. Migration, Fehlerarten (`NotFound`, `Corrupted`, `TooNew`, `Incompatible`, `MissingContent`, `IoError`) mit verständlichen Meldungen.
3. Referenzprüfung nach dem Laden.
4. Profiling gegen ARCHITECTURE §12; Pooling, LOD, Culling, zeitversetzte Berechnung, ggf. Burst/Jobs – nur wo gemessen nötig.
5. Stabilität: ungültige Ziele, zerstörte Einheiten, aufgelöste Siedlungen, unterbrochene Kämpfe.
6. Reproduzierbarer Großwelt-Test (512 × 512, 8 Zivilisationen, 2 000+ Einheiten).
**Abnahme:** T-7-*.

## Phase 8 – Endabnahme
15-Schritte-Spieltest (TEST_PLAN §6), Integrationsprüfung, Platzhalter-Register prüfen, Windows-Build, Abschlussdokumentation. Kein Schritt gilt als bestanden, ohne dass er tatsächlich ausgeführt wurde.

---

## Offene Voraussetzungen (blockieren 1b)

| # | Frage | Wer |
|---|---|---|
| V1 | Ist Unity auf dem PC installiert, welche Version (Unity Hub)? | Nutzer / Prüfung per Remote Control |
| V2 | Existiert bereits ein Unity-Projekt für World Beyond auf dem PC? | Nutzer |
| V3 | Projektort: GitHub-Repository (Vorschlag `Brastib/WorldBeyond`, mit Git LFS) oder Ordner auf dem PC | Nutzer (Claude kann keine Repos erstellen) |
| V4 | Bis V3 erledigt ist: Dokumente liegen unter `/mnt/project-files/WorldBeyond/` (Projektordner, ohne Git-Historie) | Übergangslösung |
