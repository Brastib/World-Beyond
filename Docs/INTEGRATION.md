# World Beyond – INTEGRATION (Zuständigkeiten und Integrationsplan)

Stand: 2026-10-25. Gepflegt vom Integrations-Thread ("Alle Phasen zu einem Spiel integrieren").
Rang: gleichrangig mit ARCHITECTURE. Bei Widerspruch zur Aussage des Nutzers gilt der Nutzer.

> **Aktuelle Signaturänderungen am Fundament (vor dem Programmieren lesen):**
> - `ISaveable` (SYSTEM_INTERFACES §11): neu `System.Type StateType`, `int SectionVersion`; `RestoreState` → `bool TryRestoreState(object dto, out string error)`. Betrifft alle Systeme mit Zustand.
> - `UnitDiedEvent` = { Unit, Killer, CivId, KillerCivId, UnitType }. Kein zweites Kill-Event.
> - `MovementClass` liegt in `WorldBeyond.Core`.

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

Schnittstellen zwischen War und Military: War erklärt Krieg (`IDiplomacyService.DeclareWar`) und setzt Kriegsziele; Military reagiert auf `WarDeclaredEvent` und führt Verbände über `IArmyRegistry`. War ruft nie direkt Kampf- oder Geschosscode auf.

## 4. Integrationsplan (priorisiert)

| # | Aufgabe | Abhängig von | Status |
|---|---|---|---|
| 1 | Projektort festlegen (GitHub-Repo oder PC-Ordner) | Nutzer | **offen, blockiert alles Weitere** |
| 2 | Unity-Projekt anlegen (Version prüfen, URP, Pakete, asmdefs, Ordnerstruktur ARCHITECTURE §13) | 1, PC-Zugriff | offen |
| 3 | Fundament: `EntityId`, `SimRandom`, `ISimEventBus`, `SimClock`, `SimContext`, `ISimulationSystem`, `SimulationRunner`, `ICommandQueue`, `ISaveable`, `UnitStore`, `GameBootstrap` + EditMode-Tests | 1 | offen (Integration) |
| 4 | Welt erzeugen und in `GameBootstrap` einhängen, Terrain sichtbar | 3 | offen (World) |
| 5 | Civ: Siedlung gründen, Einheiten im `UnitStore`, Wirtschaft tickt | 3, 4 | offen (Civ) |
| 6 | Save/Load-Durchstich: Welt + Einheiten + Civs speichern und laden | 3–5 | offen (Save) |
| 7 | UI-Durchstich: Geschwindigkeit, Auswahl, ein God-Tool, das einen echten Command sendet | 3, 4 | offen (UI) |
| 8 | Diplomatie + Kriegserklärung → Military-Verbände bewegen und kämpfen | 5 | offen (War, Military) |
| 9 | Kernspielschleife end-to-end im Editor testen (QA-Ablauf aus TEST_PLAN) | alle | offen |

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
| 2026-10-25 | `LoadReport` liegt in `WorldBeyond.Simulation` statt `Application.Save` (eingetragen von Phase 7, Bestätigung durch Integration offen) | [V] | `ISaveReferenceCheck` (Simulation) braucht es; Simulation darf nicht auf Application zugreifen |
