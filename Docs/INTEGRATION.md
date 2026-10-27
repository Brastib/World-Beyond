# World Beyond – INTEGRATION (Zuständigkeiten und Integrationsplan)

Stand: 2026-10-25. Gepflegt vom Integrations-Thread ("Alle Phasen zu einem Spiel integrieren").
Rang: gleichrangig mit ARCHITECTURE. Bei Widerspruch zur Aussage des Nutzers gilt der Nutzer.

> **Aktuelle Signaturänderungen am Fundament (vor dem Programmieren lesen):**
> - `ISaveable` (SYSTEM_INTERFACES §11): `System.Type StateType`, `int SectionVersion`, `bool TryRestoreState(object dto, out string error)`. Betrifft alle Systeme mit Zustand.
> - `UnitDiedEvent` = { Unit, Killer, CivId, KillerCivId, UnitType, X, Z }. Kein zweites Kill-Event.
> - `MovementClass` liegt in `WorldBeyond.Core`.
> - Fachdienste kommen in `SimContext` über `Register<T>`/`Get<T>` in `GameComposition.RegisterPhaseServices` (SYSTEM_INTERFACES §2).

## 0. Arbeitsregeln (Nutzervorgabe 2026-10-25, verbindlich)

1. **Keine parallelen Änderungen an gemeinsamen Dateien.** Gemeinsame Dateien sind: alle Dateien unter `Docs/` außer den eigenen Entwürfen (`Docs/drafts/<PHASE>_*`, `Docs/proposals/<PHASE>_*`), das Fundament (`Assets/_Project/Scripts/Core/`, `Simulation/SimContext.cs`, `Simulation/ISimulationSystem.cs`, `Simulation/Commands/`, `Simulation/Save/`, `Simulation/Units/`, `Application/`), alle `.asmdef`, `Packages/manifest.json`, `ProjectSettings/`. Änderungen daran macht **nur der Integrations-Thread**. Andere Threads schicken ihm den gewünschten Text oder die Signatur und arbeiten derweil an ihren eigenen Dateien.
2. **Keine riesigen Codeblöcke ohne Integration.** Jede Phase liefert konkrete Dateien in ihrem eigenen Ordner (z. B. `Simulation/World/`), trägt ihre Dienste in `GameComposition.RegisterPhaseServices` ein (per Pull Request, Prüfung durch Integration), dokumentiert ihre Abhängigkeiten und hinterlässt einen prüfbaren Übergabestatus: welche Tests liefen (dotnet / Unity), was nur geschrieben, nicht geprüft ist.
3. **Realistisches erstes Spielziel = Meilenstein M1** (§4). Alles, was M1 nicht braucht, wartet.
4. **Sandbox: Die Welt entwickelt sich ohne Eingreifen des Spielers** (Nutzervorgabe 2026-10-26). Jedes Fachsystem muss mit null Spielereingaben sinnvoll laufen: Civs wachsen, expandieren, erklären Krieg und schließen Frieden durch ihre KI. Spieleraktionen (God-Tools, UI-Befehle) sind optionale Eingriffe über dieselben `IWorldCommand`s, nie Voraussetzung. Jede Phase weist das mit einem Test nach, der N Spieltage ohne Commands simuliert. M1 ist erst erreicht, wenn M1.2 und M1.4 ohne Spielereingabe eintreten.

5. **Ressourcen sparen** (Nutzervorgabe 2026-10-26): kurze Antworten, keine wiederholten Vollanalysen, kleine abgeschlossene Pakete, je Paket: Stand prüfen → kleinste Änderung → testen → knapp dokumentieren. Eine Aufgabe nach der anderen, gegenseitige Prüfung (Prüf-Thread reviewt jeden PR).
6. **Vision und Ausbaustufen:** `Docs/VISION.md`. Nichts aus M2–M6 beginnen, bevor M1 getestet ist.

Ablauf:
- **Das Repository `Brastib/World-Beyond` ist die einzige Wahrheit** (seit 2026-10-25). Der Projektordner `/mnt/project-files/WorldBeyond/Docs/` ist nur noch eine veraltete Kopie.
- Jede Phase arbeitet auf ihrem eigenen Branch (`phaseN-<name>`) und öffnet einen Pull Request gegen `main`. Gemergt wird nach Prüfung durch den Integrations-Thread und Freigabe durch den Nutzer.
- Ein PR enthält nur Dateien der eigenen Phase plus höchstens die eigenen Zeilen in `RegisterPhaseServices`.
- Prüfstand in jedem PR: `dotnet test Tools/SimTests` (reines C#, läuft in der Cloud) und, sobald verfügbar, Unity EditMode-Tests auf dem PC des Nutzers.

## 1. Bestandsaufnahme (geprüft am 2026-10-25, 20:55 UTC)

| Punkt | Befund | Quelle |
|---|---|---|
| Unity-Projekt | **existiert nicht** (kein Repo, kein PC-Ordner verbunden) | Projektordner, Aussagen Phase 0 und Phase 2 |
| Code (C#) | **keiner** | Projektordner |
| Assets / ASSET_CREDITS.md | **keine** | Projektordner |
| Docs | PROJECT_MASTER, ARCHITECTURE, SYSTEM_INTERFACES vorhanden; CURRENT_STATE, IMPLEMENTATION_PLAN, TEST_PLAN noch nicht | `Docs/` |
| Kompilieren/Testen | in der Cloud nicht möglich, nur auf dem PC des Nutzers | PROJECT_MASTER §7 |

Folge: Es gibt noch nichts zusammenzuführen. Die Aufgabe jetzt ist, **Doppelungen zu verhindern, bevor Code entsteht**.

## 2. Phasen-Zuordnung (Threads ↔ Doku-Phasen)

Seit 2026-10-25 21:01 UTC nutzt IMPLEMENTATION_PLAN die Thread-Phasen 1–8 plus "1b Fundament" (Besitzer: Integration). Dort gilt die Nummerierung; die Tabelle unten bleibt nur als Zuordnung zu den alten Phasennummern aus früheren Fassungen.

| Thread | Umfang laut SYSTEM_INTERFACES |
|---|---|
| Phase 0 Docs | Doku-Phase 0 |
| **Integration** (dieser Thread) | Doku-Phase 1 (Fundament) + Verbindungsschichten |
| Phase 2 World | Doku-Phase 2 (+ Pfadfindung/SpatialIndex aus 4) |
| Phase 3 Civ | Doku-Phasen 5, 6 (Civ, Siedlung, Territorium, Wirtschaft, Tech) |
| Phase 4 War | Doku-Phase 9 (Diplomatie) + strategische Kriegsentscheidungen der Civ-KI (13, Teil) |
| Phase 5 Military | Doku-Phasen 10, 12 (Kampf, Geschosse) + Armee-KI (13, Teil) |
| Phase 6 UI | Doku-Phasen 7, 8, 14 (God-Tools, Auswahl, HUD, Feed, Audio/VFX-Darstellung) |
| Phase 7 Save/Perf | Doku-Phase 3 (SaveLoadService) + Performance (15) |
| Phase 8 QA | Endabnahme |

## 3. Zuständigkeitsregister (eine Funktion = ein Besitzer)

Regel: Nur der Besitzer legt die Klasse an und ändert ihre Signatur. Andere Threads **nutzen** sie über das Interface oder bitten den Besitzer um Erweiterung. Keine zweite Implementierung, kein eigener Ersatz-Stub in fremdem Namensraum.

| Funktion | Einzige Instanz / Klasse | Besitzer |
|---|---|---|
| Spielwelt (Kachelraster) | genau ein `IWorldGrid`, erzeugt von `IWorldGenerator` | World |
| Simulationstakt | genau ein `SimClock` + ein `SimulationRunner` | Integration |
| Composition Root | genau ein `GameBootstrap` (einziger Ort, an dem Systeme erzeugt und registriert werden) | Integration |
| `SimContext`, `ISimulationSystem`, Tick-Reihenfolge | eine Definition | Integration |
| Event-Bus | genau ein `ISimEventBus` | Integration |
| Befehle | genau eine `ICommandQueue`; jedes System definiert seine eigenen `IWorldCommand`-Klassen | Integration (Queue) / jeweiliges System (Commands) |
| Zufall | genau ein `SimRandom` (kein `UnityEngine.Random` in Simulation) | Integration |
| `EntityId` | eine Definition | Integration |
| Einheiten-Datenhaltung | genau ein `UnitStore` für **alle** Einheiten (Zivil + Militär) | Integration (Store) |
| Bewegung/Pfadfindung/SpatialIndex | ein `MovementSystem`, ein `IPathfinder`, ein `ISpatialIndex` | World |
| Einheitentypen-Daten | eine `UnitTypeData`-Tabelle; Civ trägt Zivil-Typen ein, Military trägt Militär-Typen ein | Civ / Military |
| Zivilisationen, Siedlungen, Territorium | `ICivilizationRegistry`, `ISettlementRegistry`, `ITerritoryService` | Civ |
| Wirtschaft (einziges Ressourcenlager) | `IEconomy` | Civ |
| Technologie/Epochen | `ITechTree` | Civ |
| Diplomatie (einzige Beziehungsmatrix) | `IDiplomacyService` | War |
| Kriegsentscheidung (wann/gegen wen) | Teil von `ICivAI` | War |
| Wirtschafts-/Expansionsentscheidung | Teil von `ICivAI` | Civ |
| Civ-KI-Rahmen (ein System, ruft Wirtschafts- und Kriegs-Bewertung auf) | ein `CivAISystem` | Civ (Rahmen), War liefert Kriegs-Bewerter |
| Armeen, Armee-KI | `IArmyRegistry`, `IArmyAI` | Military |
| Schaden (einziger Weg, HP zu senken) | `ICombatService` | Military |
| Geschosse | `IProjectileSystem` | Military |
| Speichern/Laden | genau ein `SaveLoadService`; jedes System implementiert selbst `ISaveable` | Save (Service) / jedes System (`ISaveable`) |
| Auswahl, God-Tools, HUD, Minimap, Ereignis-Feed | `ISelectionService`, `GodToolController`, UI Toolkit-Dokumente, `IGameEventFeed` | UI |
| Audio/VFX-Darstellung | `IAudioService`, `IVfxService` | UI |
| Asset-Import und ASSET_CREDITS.md | eine Datei `Assets/ThirdParty/ASSET_CREDITS.md` | jeweiliger Importeur trägt ein; Integration prüft |
| Art/Assets (Asset-Katalog, Stilregeln, eigene Modelle, z. B. PlantForge) | `Docs/proposals/ASSET_CATALOG.md`, `ASSETS_STYLE_ENVIRONMENT.md`; Darstellung im Spiel weiter über World/UI | Asset-Katalog-Thread |
| Umwelt (Vegetationszustand, Feuer, Wetter, Umweltfelder V9) | eigenes Datenfeld, **ohne** `TileData` zu ändern; Sektion `environment` | Umwelt (Thread noch nicht gestartet; erst ab Freigabe des Nutzers bzw. M5) |

Schnittstellen zwischen War und Military: War erklärt Krieg (`IDiplomacyService.DeclareWar`) und setzt Kriegsziele; Military reagiert auf `WarDeclaredEvent` und führt Verbände über `IArmyRegistry`. War ruft nie direkt Kampf- oder Geschosscode auf.

## 4. Integrationsplan (priorisiert, ausgerichtet auf M1)

### Meilenstein M1 – erste spielbare Version (Nutzervorgabe)

| # | M1-Ziel | Liefert | Nachweis |
|---|---|---|---|
| M1.1 | Generierte Welt mit Terrain | World (Phase 2) | Welt aus Seed sichtbar, Kamera bedienbar |
| M1.2 | Zwei Zivilisationen, die wachsen | Civ (Phase 3) | 2 Civs mit je einer Siedlung; Bevölkerung und Lager steigen über die Zeit |
| M1.3 | Einheiten, die sich bewegen | World (Bewegung) + Fundament (UnitStore) | Siedler/Arbeiter laufen auf dem Terrain zu Zielen |
| M1.4 | Eine Kriegserklärung und ein funktionierender Kampf | War (Phase 4) + Military (Phase 5) | Civ-KI erklärt Krieg ohne Spielereingabe (Spieler kann es zusätzlich); zwei Infanterie-Gruppen kämpfen, Einheiten sterben (UnitDiedEvent) |
| M1.5 | Eine bedienbare Oberfläche | UI (Phase 6) | Geschwindigkeit, Auswahl + Info, Krieg-erklären-Knopf, Ereignis-Feed; alles über echte Commands |
| M1.6 | Speichern und Laden | Save (Phase 7) | Spielstand mit Welt, Civs, Einheiten, Krieg speichern; nach Neustart laden, Simulation läuft identisch weiter |

M1-Umfang je Phase (alles darüber hinaus erst nach M1): World: Höhen, Wasser, 3–4 Biome, Chunk-Mesh, Kamera, A*, Bewegung. Civ: Siedlung, Bevölkerung, Nahrung/Holz, Siedler, eine Ausbaustufe, einfache Expansion. War: Beziehungswert, Status Frieden/Krieg, eine Kriegsregel (Grenzkontakt + Stärke), Frieden. Military: ein Infanterietyp, Hitscan-Waffe, ICombatService, Zielwahl, Tod. UI: HUD aus M1.5. Save: Service mit core/world/units/civs/diplomacy.

### Aufgaben

| # | Aufgabe | Abhängig von | Status |
|---|---|---|---|
| 1 | Projektort festlegen | Nutzer | **erledigt**: GitHub `Brastib/World-Beyond` |
| 2 | Docs ins Repo | 1 | **erledigt** (main, 2026-10-25) |
| 3 | Fundament (Core, Clock, Runner, EventBus, CommandQueue, ISaveable, UnitStore, GameComposition, GameBootstrap) + Tests | 1 | **Code geschrieben, dotnet-Tests grün (44/44), nicht im Unity-Editor kompiliert** – Branch `claude/phase1b-fundament` |
| 4 | Unity-Projekt anlegen (Version prüfen, URP, Pakete, ProjectSettings) | Unity-Version vom Nutzer / Remote Control | offen |
| 5 | Fundament in Unity kompilieren, EditMode-Tests per Unity Test Runner | 3, 4 | offen |
| 6 | M1.1 + M1.3 World | 3 | offen (World) |
| 7 | M1.2 Civ | 3, 6 | offen (Civ) |
| 8 | M1.6 Save-Durchstich (core + units bereits speicherbar) | 3 | offen (Save) |
| 9 | M1.5 UI-Grundlage (Geschwindigkeit, Auswahl, Feed) | 3, 6 | offen (UI) |
| 10 | M1.4 Krieg + Kampf | 7 | offen (War, Military) |
| 11 | M1 end-to-end im Editor testen | alle | offen |

Definition of Done (vom Nutzer): Systeme interagieren tatsächlich, Kernspielschleife läuft, UI führt echte Aktionen aus, Spielstand wird zuverlässig gespeichert und geladen. Nichts davon ist bisher erreicht.

## 5. Entscheidungsprotokoll

| Datum | Entscheidung | Status | Begründung |
|---|---|---|---|
| 2026-10-25 | Fundament (Doku-Phase 1) und `UnitStore` gehören dem Integrations-Thread | [E] | Kein Thread besaß es; jeder Thread bräuchte es und würde sonst eigene Varianten bauen |
| 2026-10-25 | Ein gemeinsamer `UnitStore` für Zivil- und Militäreinheiten | [E] | Verhindert zwei Datenmodelle für Einheiten zwischen Civ und Military |
| 2026-10-25 | Diplomatie/Kriegsentscheidung = War, Ausführung/Kampf = Military | [E] | Trennt die beiden überlappenden Threads klar |
| 2026-10-25 | RTS-Kamera (Zoom, Rotation, Verschiebung) gehört World, nicht dem Fundament | [E] | Nutzer hat sie Phase 2 beauftragt; Nutzeraussage hat Vorrang vor IMPLEMENTATION_PLAN |
| 2026-10-25 | `MovementClass` liegt in `WorldBeyond.Core` (nicht Simulation.Units) | [E] | Wird von IWorldQuery und Core-IPathfinder gebraucht; Core darf Simulation nicht referenzieren |
| 2026-10-25 | Maßstab: 1 Kachel ≈ 4 m (PROJECT_MASTER D13); ARCHITECTURE §3 (10 m) ist veraltet | [B] | Rangfolge PROJECT_MASTER > ARCHITECTURE |
| 2026-10-25 | `UnitDiedEvent` = { Unit, Killer, CivId, KillerCivId, UnitType }; kein zweites Kill-Event | [E] | Bitte Phase 5; War und Military nutzen dasselbe Event |
| 2026-10-25 | `UnitRole` bleibt grob (Fundament); feine Rolle als eigenes Feld `MilitaryRole` in `UnitTypeData`, Enum gehört Military | [E] | Civ muss Militär-Enum nicht kennen; Military kann erweitern, ohne Fundament zu ändern |
| 2026-10-25 | ~~`IRecruitment` (Military) für Militäreinheiten~~ **ersetzt 21:02 UTC:** `IRecruitment` gehört Civ und ist der einzige Produktionsweg für **alle** Einheiten (prüft Kosten via `IEconomy.TrySpend`, Bevölkerung, `ITechTree`, spawnt über den UnitStore). Military liefert Militär-Typen und Produktionsgebäude als Daten und ruft `IRecruitment.Request` aus der Armee-KI auf | [E] | Ein Produktionsweg statt zwei; entspricht SYSTEM_INTERFACES §7 |
| 2026-10-25 | `IEconomy` nach außen pro Civ, intern Lager pro Siedlung | [E] | Vorschlag Phase 3; Schnittstelle bleibt stabil |
| 2026-10-25 | Geländekosten pro MovementClass, Wenderate, Beschleunigung gehören ins MovementSystem (World); Werte stehen in UnitTypeData | [E] | Bewegung hat einen Besitzer |
| 2026-10-25 | `ISaveable` erhält `System.Type StateType`; der SaveLoadService wandelt JToken → StateType. `RestoreState` wird zu `bool TryRestoreState(object dto, out string error)` | [E] | Simulation darf Newtonsoft nicht referenzieren (asmdef-Regel ARCHITECTURE §2); harte Inkonsistenz muss an den Service zurückgemeldet werden können (ARCHITECTURE §11.5) |
| 2026-10-25 | Fundament-Signaturen `EntityStore`, `EntityStoreState`, `SequentialIdAllocator`, `ContentRegistry` (SYSTEM_INTERFACES §1, eingetragen von Phase 7) geprüft und übernommen | [E] | Decken ID-Regeln ARCHITECTURE §11.1 ab |
| 2026-10-25 | `ISaveable` erhält `int SectionVersion { get; }`; der SaveLoadService migriert jede Sektion auf die aktuelle Version, bevor `RestoreState(object dto)` aufgerufen wird (Signatur von RestoreState bleibt) | [E] | Vorschlag Phase 7; Systeme migrieren unabhängig, Fachsysteme sehen nur aktuelle DTOs |
| 2026-10-25 | `LoadReport` liegt in `WorldBeyond.Simulation` statt `Application.Save` (eingetragen von Phase 7, bestätigt durch Integration 2026-10-26) | [E] | `ISaveReferenceCheck` (Simulation) braucht es; Simulation darf nicht auf Application zugreifen |
| 2026-10-25 | Repository ist die einzige Wahrheit; nur der Integrations-Thread ändert gemeinsame Dateien (§0) | [B] | Nutzerregel 1 |
| 2026-10-26 | `SimContext`: Fundament-Dienste als feste Felder; Fachdienste über `Register<T>`/`Get<T>`, nach `Seal()` keine Registrierung mehr; doppelte Registrierung → Exception | [E] | Eine Instanz je Dienst ist technisch erzwungen; SimContext muss nicht bei jeder Phase geändert werden |
| 2026-10-26 | `IWorldGrid` ist kein festes Feld in SimContext, sondern `ctx.Get<IWorldGrid>()` | [E] | Fundament darf World-Typen nicht vorwegnehmen; kein Stub (§3 Regel) |
| 2026-10-26 | `IUnitStore` präzisiert: `TryGet`, `CopyAliveIds`, `ClearMoveTarget`, `SetState`, `SetPosition` (nur Movement), `SetHp` (nur Combat), `Despawn(id, killer, killerCivId)`; Methoden liefern `bool` statt Exception | [E] | Regel 6 (Gültigkeitsprüfung ohne Exception); Schreibrechte je Besitzer |
| 2026-10-26 | Reine C#-Teile werden in der Cloud mit `dotnet test Tools/SimTests` (net8.0, C# 9) geprüft | [E] | Unity läuft nur auf dem PC; ersetzt nicht den Unity Test Runner |
| 2026-10-26 | `ResourceType` bleibt Enum mit festen Werten (Besitzer World), Erweiterung `Metal = 7, Tools = 8` (nur Lagergüter). Im Spielstand als Enum-**Name** (String) gespeichert, nicht als Zahl und nicht über die ContentRegistry | [E] | Kleine feste Menge; Name ist stabiler Schlüssel (ARCHITECTURE §11.1 Regel 3 und 4) |
| 2026-10-26 | `ISaveReferenceCheck` und `LoadReport` liegen im Fundament (`Simulation/Save/LoadReport.cs`), nicht bei Save | [E] | Jedes Fachsystem braucht sie; Phase 4 wartete darauf; Signatur unverändert aus SYSTEM_INTERFACES §11 |
| 2026-10-26 | World-Abweichungen akzeptiert: `TileData.WaterLevel`; `IWorldQuery.Version`/`NavigationVersion`; `System.Numerics.Vector3` + `WorldRay` statt UnityEngine-Vektoren in Simulation; `IPathfinder`/`ISpatialIndex`/`SpatialHash` in `WorldBeyond.Simulation.World`; `TileChangeKind.Owner = 6`; Fluss als Furt (Foot 3, Wheeled gesperrt, Tracked 3); Terrain/Biome/Resource-Enums im Spielstand als Namen; `WorldEventsSystem` Order 850; `GameStartSettings` → `WorldGenSettings`. SYSTEM_INTERFACES §4/§5 wird beim Review des World-PR nachgezogen | [E] | Simulation bleibt ohne UnityEngine testbar; Fundament-Ordner unverändert |
| 2026-10-26 | `ISaveContentCheck` (Vorschlag Phase 7) liegt im Fundament; `UnitStore` implementiert es. `SaveLoadError.CaptureFailed = 8`, Rahmenfeld `start` übernommen. `Tools/SimTests` referenziert Newtonsoft.Json 13.0.2 | [E] | Fehlende Inhalte werden als MissingContent erkannt; Application/Save braucht Newtonsoft im dotnet-Test |
| 2026-10-26 | Sandbox-Prinzip verbindlich: Simulation läuft ohne Spielereingaben (§0 Regel 4); Nachweis je Phase durch einen Autonomie-Test | [B] | Nutzervorgabe |
| 2026-10-26 | Fehlertoleranter Tick: Exceptions aus Systemen und Event-Handlern werden gemeldet (`SimulationRunner.SystemFailed`), der Tick wird abgeschlossen, die Uhr rückt vor; `Validate`-Exceptions werden zu `CommandRejectedEvent` | [E] | Review PR #2 (nachgewiesener Doppel-Tick); Sandbox muss ohne Eingriff weiterlaufen |
| 2026-10-26 | Start-Civs anlegen (`GameStartSettings.CivilizationCount`) gehört Civ: deren Zeilen in `RegisterPhaseServices` gründen die Civs an `FindStartLocations` (World) | [E] | Review PR #2; ohne Start-Civs entwickelt sich die Sandbox nicht |
| 2026-10-26 | Nutzeranweisungen vom 2026-10-26 in `VISION.md` aufgenommen; Entscheidungen V1–V6 (mehrstufige Simulation, Chronik-Log, RunUntil, LLM nur in Application, realistische URP-Grafik, Namenssystem) | [E] | Erweitert bestehende Architektur, keine Neuplanung |
| 2026-10-26 | Inhalts-Schlüssel: Phaseninhalte (`unit.rifleman`, `unit.settler` …) registriert nur das zuständige Modul. `RegisterPhaseServices` läuft vor `contentSetup`; Tests verwenden für eigene Typen das Präfix `test.` | [E] | Doppelte Registrierung ließ 37 (Military) bzw. 23 (Save) Tests scheitern, sobald die Module verdrahtet waren |
| 2026-10-26 | Adapter zwischen Phasen (IDiplomacyWorldPort, ICivWarEvaluator, Combat-/ArmyEnvironment, Rifleman-Rekrutierung) liegen in `Application/Integration/PhaseAdapters.cs` und gehören der Integration; Heuristiken dort sind als PLATZHALTER markiert, bis die Fachphase echte Abfragen liefert | [E] | Simulation-Phasen bleiben voneinander unabhängig; eine Stelle für Verbindungen |
| 2026-10-26 | Verluste an Diplomatie: `DiplomacyService` abonniert `UnitDiedEvent` selbst, keine zusätzliche Weiterleitung | [E] | War 2285b8f; sonst doppelte Zählung |
| 2026-10-26 | Nutzeranweisungen „Realistische Welt, Umwelt und NPC-Kontrolle“ als VISION V7–V12 und Stufen M2–M7 aufgenommen; nichts davon vor M1-Test | [E] | Nutzer: schrittweise, eine Etappe zur Zeit |
| 2026-10-26 | Asset-Regeln KA1–KA4 übernommen: eigene Modelle, aus dem Netz nur CC0-Rohstoffe (PROJECT_MASTER §8.1); Stil „Naturrealismus mit warmer Farbgebung“ (UI_STYLE_GUIDE A1); Baum-Budget als LOD-Kette (A3); neue Besitzer Art/Assets und Umwelt (§3) | [E] | Nutzeraussage 2026-10-26 hat Vorrang; Vorschlag PR #9 |
| 2026-10-27 | Grundsatz „Spiel-KI entscheidet alles“ (VISION §2 Regel 6): feste Grenzen wie Aufrüstung 10 %/80 sind PLATZHALTER und werden durch KI-Abwägung (Unterhalt, Nahrung, Arbeitskräfte, Bedrohung, Ziele) ersetzt; Leistung wird technisch gesichert (V1, zusammengefasste Armeen) | [N] | Nutzer 2026-10-27 08:25: „Die ingame KI soll alles machen“, „keine Grenze“ |
