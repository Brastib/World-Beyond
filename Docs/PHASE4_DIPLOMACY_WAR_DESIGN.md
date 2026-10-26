# World Beyond – Phase 4: Diplomatie und Kriegs-KI (Entwurf)

Stand: 2026-10-26. Status: **Simulationskern implementiert (Branch `phase4/diplomacy-war`), mit dotnet getestet, nicht im Unity-Editor geprüft.** Umsetzungsstand: §11.
Grundlage: PROJECT_MASTER.md, ARCHITECTURE.md (§1, §6, §7, §8, §11). Bei Widerspruch gelten diese und SYSTEM_INTERFACES.md (sobald vorhanden). Dieses Dokument wird bei der Umsetzung an die tatsächlichen Schnittstellen aus Phase 0/2/3 angepasst.

## 1. Abhängigkeiten und Blockaden

| Braucht Phase 4 | Kommt aus | Stand 2026-10-25 |
|---|---|---|
| Unity-Projekt, asmdefs, `GameBootstrap`, `SimulationRunner`, `ISimEventBus`, `ISaveable`, `SimRandom`, `EntityId` | Phase 0/1 | nur geplant |
| Kachelraster mit Besitzer pro Kachel, A*-Pfadfindung, `SpatialHash` | Phase 2 (World Engine) | nur geplant |
| `Civilization`, `Settlement`, Bevölkerung, Ressourcenlager, Territorium, Einheiten-Store | Phase 3 (Zivilisation) | nur geplant |
| Einheitentypen, Waffen, Geschosse, Schaden, taktische Zielerfassung, Produktion | Phase 5 (Militär) | nur geplant |

Ohne diese Grundlagen wird Phase 4 nicht implementiert, damit keine zweiten Versionen dieser Systeme entstehen.

## 2. Abgrenzung (keine doppelten Systeme)

| Thema | Eigentümer | Phase 4 macht |
|---|---|---|
| Beziehungen, Status, Verträge, Kriegserklärung, Frieden | **Phase 4** | alles |
| Kriegsziele, Kriegspunktestand (War Score), Friedensbedingungen | **Phase 4** | alles |
| Strategische Zivilisations-KI für Krieg/Diplomatie | **Phase 4** | alles (Wirtschafts-KI bleibt Phase 3) |
| Operative Armee-KI (`ArmyGroup`, Befehle Angriff/Verteidigung/Rückzug/Sichern) | Military (`IArmyRegistry`, `IArmyAI`) | liefert Kriegsziele und Zielprioritäten (`IWarQuery`), die `IArmyAI` auswertet; die Logik aus §6 "Operativ" ist ein Vorschlag an Military |
| Strategische Kriegsentscheidung | **Phase 4** als Bewerter im einen `CivAISystem` (Rahmen: Civ) | Kriegs-Bewerter, keine eigene KI-Schleife |
| Fundament (SimClock, Runner, EventBus, CommandQueue, ISaveable, UnitStore, GameBootstrap) | Integration | nutzt es nur |
| Rekrutierung | Phase 3/5 (Produktion) | stellt nur *Rekrutierungsanfragen* über eine Schnittstelle |
| Versorgung | Phase 3 (Lager) | bucht Unterhalt pro Einheit über die Wirtschaftsschnittstelle ab |
| Einheitentypen, Waffen, Geschosse, Schaden, Treffer | Phase 5 | nutzt sie nur |
| Taktik pro Einheit (Feind erkennen, feuern) | Phase 5 (`CombatSystem`) | gibt nur Ziele/Haltung vor. Falls Phase 4 vor Phase 5 läuft: kleinste Grundversion *im selben* `CombatSystem`, die Phase 5 erweitert, nicht ersetzt |
| Territorium neu berechnen | Phase 3 | löst nur Besitzwechsel einer Siedlung aus (`TransferSettlement`) |

## 3. Datenmodell (Simulation, reines C#)

```
DiplomacyState : ISaveable
  RelationMatrix   float opinion[civA, civB]   (−100 … +100, symmetrisch gespeichert als Dreieck)
  PairStatus       enum { Peace, Tension, Truce, Alliance, War } pro Paar
  List<OpinionModifier>  { civA, civB, source, value, decayPerDay, expiresTick }
  List<Treaty>     { id, type(Alliance|Truce|Peace|NonAggression), parties, startTick, endTick, terms }
  List<War>        siehe unten

War
  id, attackers[], defenders[], startTick, casusBelli
  List<WarGoal>    { type(ConquerSettlement|HoldPoint|Tribute|Humiliate), targetId, ownerCiv }
  WarStats         pro Seite: Verluste (Einheiten, Bevölkerung), eroberte/verlorene Siedlungen, gehaltene Punkte, Ressourcenverbrauch
  warScore         −100 … +100, aus WarStats berechnet (nicht frei gesetzt)
  exhaustion       pro Seite, steigt mit Verlusten und Dauer

StrategicPoint     { id, tile, type(Pass|Bridge|ResourceSite|Fort), controllerCiv }
(ArmyGroup und ArmyOrder gehören Military, siehe INTEGRATION.md §3)
```

Alle Referenzen über IDs. Alles mit Zustand ist Teil des Spielstands (inkl. laufender Befehle und KI-Abklingzeiten), damit die Simulation nach dem Laden identisch weiterläuft.

### 3a. Speichern und IDs (verbindlich laut Nutzervorgabe vom 2026-10-25, ARCHITECTURE §11, SYSTEM_INTERFACES §1/§11)

- Kriegs-, Vertrags- und Modifikator-IDs kommen aus dem `SequentialIdAllocator`. Sie werden nie wiederverwendet.
- Beziehungen und Modifikatoren werden als Daten mit Civ-IDs gespeichert, ohne Unity-Referenzen.
- Alle Enums (`DiplomaticStatus`, `TreatyType`, `WarGoalType`, `CasusBelli`, `OpinionSource`) bekommen feste Zahlenwerte.
- `DiplomacyState` implementiert `ISaveable` mit `SectionVersion`.
- In `ISaveReferenceCheck` prüft Phase 4 Verweise auf Civs, Siedlungen und strategische Punkte. Rückfall bei einer untergegangenen oder fehlenden Civ: der Krieg wird beendet, Verträge werden aufgelöst und ihre Modifikatoren entfernt. Bei einer fehlenden Siedlung oder einem fehlenden Punkt wird das Kriegsziel gestrichen.
- Keine geplanten Abweichungen.

## 4. Diplomatie-Mechanik

- **Meinung** = Summe aller aktiven `OpinionModifier`, begrenzt auf ±100. Jede Änderung hat eine Quelle (für die UI: "Warum mag A B nicht?").
- **Ereignisquellen** (über `ISimEventBus`): Grenzkontakt (+Spannung pro gemeinsamer Grenzlänge), Ressourcenkonkurrenz (beide beanspruchen dieselbe Ressourcenkachel), Kulturähnlichkeit, Handel (später), Krieg gegen gemeinsamen Feind (+), Angriff auf Verbündeten (−), Vertragsbruch (−stark, langlebig), Spieler-Eingriffe.
- **Statusübergänge** (nur über Befehle, gleicher Weg für KI und Spieler): `DeclareWarCommand`, `ProposePeaceCommand`, `ProposeTruceCommand`, `ProposeAllianceCommand`, `BreakTreatyCommand`. Waffenstillstand hat feste Dauer; danach Frieden oder Wiederaufnahme des Kriegs.
- **Bündnisse:** Ruf zu den Waffen bei Kriegserklärung gegen ein Mitglied; Ablehnung kostet Meinung.

## 5. Krieg und Frieden

- Kriegserklärung erzeugt `War` mit Kriegszielen (KI wählt Ziele, siehe §6).
- **War Score** wird aus echten Werten berechnet: eroberte Siedlungen (gewichtet nach Bevölkerung), gehaltene strategische Punkte, Verlustverhältnis, Kriegsziel-Erfüllung.
- **Friedensverhandlung:** Angebot mit Bedingungen (Siedlungen abtreten, Tribut in Ressourcen, Waffenstillstand). Annahme, wenn `AcceptScore(empfänger) > 0`, abhängig von warScore, exhaustion, Bedrohung und Ressourcenlage. Bedingungen sind nur zulässig, wenn ihr Wert ≤ warScore der fordernden Seite.
- **Folgen:** Abgetretene Siedlungen → `TransferSettlement` (Phase 3 berechnet Territorium neu), Tribut → Lagerbuchung, Meinungsmodifikator "Demütigung".

## 6. KI (Utility AI, passend zu ARCHITECTURE §8)

**Strategisch (pro Zivilisation, ~alle 5 Spielsekunden, versetzt):** bewertet Optionen mit Punktwerten, wählt die beste über einer Schwelle, sonst nichts tun. Abklingzeiten verhindern Hin-und-Her.

| Option | Wichtigste Faktoren |
|---|---|
| Krieg erklären an X | eigene Stärke / Stärke X (inkl. Verbündete), Meinung, Entfernung zur Grenze, Ressourcenmangel, den X lösen würde, eigene Erschöpfung, Bündnisse von X |
| Frieden anbieten / annehmen | warScore, exhaustion, Verlustrate, Bedrohung durch Dritte |
| Bündnis anbieten | gemeinsamer Bedrohungsfaktor, Meinung |
| Rekrutieren | Bedrohung, aktive Kriege, verfügbare Ressourcen/Bevölkerung, Unterhalt |

Jede Entscheidung wird mit ihren Top-Faktoren protokolliert (Ereignismeldung + Debug-Ansicht), damit Kriege nachvollziehbar sind. Zufall nur als kleiner Gleichstands-Brecher über `SimRandom`.

**Operativ (pro ArmyGroup, ~jede Spielsekunde):** einfache Zustandsmaschine
`Idle → Gather → Attack/Defend/Secure → Retreat → Gather`.
Zielauswahl per Punktwert: Wert des Ziels (Kriegsziel, Bevölkerung, strategischer Punkt) ÷ (Entfernung × erwartete Gegenwehr). Rückzug, wenn Stärkeverhältnis vor Ort < Schwelle oder Versorgung erschöpft. Verteidigung hat Vorrang, wenn eigene Siedlung bedroht ist.

**Taktisch:** gehört Phase 5 (siehe §2).

## 7. Spieler und UI

- **Diplomatie-Übersicht:** Liste aktiver Kriege (Seiten, warScore, Ziele, Dauer), Bündnisse, Feindschaften; Beziehungsmatrix mit Ursachen.
- **Eingriffe (God-Modus):** Krieg erklären, Frieden erzwingen, Bündnis schmieden, Meinung verschieben. Alles über dieselben Commands wie die KI.
- **Armeen:** Armee auswählen, Befehl geben (Angriff/Verteidigung/Rückzug/Sichern), Ziel auf Karte; Kartenmarkierungen für Fronten und Ziele.
- UI liest nur über `IDiplomacyService` / `IWarQuery` und sendet nur Commands.

## 8. Geplante Schnittstellen (Vorschlag für SYSTEM_INTERFACES.md)

**Verbindlich sind seit 2026-10-25 die Signaturen in SYSTEM_INTERFACES.md §8/§9** (IDiplomacyService, IDiplomacyQuery, IWarQuery; IArmyRegistry/IArmyQuery/IArmyAI gehören Military). Tests: TEST_PLAN T-4-01 bis T-4-07. Die Liste unten ist nur der ursprüngliche Vorschlag.

```
IDiplomacyService { float GetOpinion(a,b); PairStatus GetStatus(a,b); IReadOnlyList<OpinionModifier> GetModifiers(a,b); IReadOnlyList<Treaty> GetTreaties(civ); }
IWarQuery       { IReadOnlyList<War> ActiveWars; War GetWar(id); bool AreAtWar(a,b); }
IWarQuery.GetTargetPriorities(civ) → Liste (Ziel-ID, Wert, Kriegsziel)  – gelesen von Military IArmyAI
IWarCivAIEvaluator → Kriegs-Bewerter, aufgerufen vom CivAISystem (Civ)
Benötigt von anderen Phasen:
  ICivilizationQuery (Stärke, Ressourcen, Bevölkerung, Hauptstadt, Alive) – Phase 3
  ISettlementQuery + TransferSettlement(settlementId, newCiv)          – Phase 3
     (löst gebündelte Territoriums-Neuberechnung aus; Hauptstadtverlust → neue Hauptstadt;
      keine Siedlungen mehr → Alive=false → Phase 4 beendet alle Kriege/Verträge dieser Civ)
  ITerritoryQuery (Besitzer pro Kachel, GetSharedBorderLength = Kachelkanten, gecacht) – Phase 2/3
  IRecruitment.Request(civId, UnitRole, count, out queued) → RecruitResult
     (Queued/NoBuilding/NotUnlocked/NotEnoughResources/PopulationCap)  – Phase 5
  GetCombatStrength(UnitTypeId), GetCivCombatStrength(civId)           – Phase 5 (Stärkevergleich in §6)
  Verluste über UnitDiedEvent (kein eigenes UnitKilled-Event)
  Abgestimmt mit Phase 5 am 2026-10-25 (proposals/PHASE5_MILITARY_DESIGN.md §6).
  IEconomy.TryConsume(civId, resource, amount) → bool                  – Phase 3
     (atomar, Hauptstadt zuerst, false ohne Abbuchung → Phase 4 setzt supplyState=Mangel)
  Abgestimmt mit Phase 3 am 2026-10-25 (siehe drafts/CIVILIZATION_DESIGN_DRAFT.md §11).
  IUnitQuery / IUnitCommands (Bewegen, Ziel setzen, Haltung)            – Phase 3/5
Events: WarDeclared, PeaceSigned, TruceSigned, AllianceFormed, TreatyBroken,
        SettlementCaptured, StrategicPointCaptured, UnitDiedEvent (Integration, erweitert um CivId/KillerCivId/UnitType)
```

## 9. Tests (EditMode, Unity Test Framework) – umgesetzt, siehe §11.3

1. Kriegserklärung setzt Status `War` für beide Seiten und ruft Verbündete.
2. Friedensangebot wird abgelehnt bei schlechtem, angenommen bei gutem warScore; abgetretene Siedlung wechselt Besitzer.
3. Bündnis: Angriff auf Mitglied zieht Partner in den Krieg; Ablehnung senkt Meinung.
4. Zielauswahl: Armee wählt bei gleicher Entfernung das wertvollere Ziel; bei bedrohter eigener Siedlung Verteidigung.
5. Verluste: getötete Einheiten erhöhen Verlustwerte, exhaustion und senken warScore.
6. Keine Kriegserklärung ohne Grund: zwei Zivilisationen ohne Grenzkontakt und mit neutraler Meinung erklären über N simulierte Minuten keinen Krieg.
7. Speichern/Laden: Simulation N Ticks laufen lassen, speichern, laden, weitere M Ticks; Zustand muss mit durchgehendem Lauf übereinstimmen (Determinismus über `SimRandom`).
8. Untergang: Civ verliert letzte Siedlung (Alive=false) → ihre Kriege enden, Verträge werden aufgelöst.
9. Versorgung: TryConsume liefert false → Armee gerät in Mangel und zieht sich zurück.
10. PlayMode: zwei Zivilisationen, ein Krieg, eine Siedlung wird erobert, UI-Übersicht zeigt es.

## 10. Offen / noch fehlend

- Gesamter Code (nichts implementiert).
- Alle Abhängigkeiten aus §1.
- Handel als Beziehungsquelle (Phase Wirtschaft, später).
- Feinabstimmung der Gewichte erst mit laufender Simulation möglich.
- Tests können nur auf dem PC des Nutzers im Unity Test Runner laufen.

## 11. Umsetzungsstand (2026-10-26)

### 11.1 Dateien (nur Phase 4, keine gemeinsamen Dateien geändert)

| Datei | Inhalt |
|---|---|
| `Assets/_Project/Scripts/Simulation/Diplomacy/DiplomacyTypes.cs` | Enums (feste Werte), Datenklassen: OpinionModifier, Treaty, War, WarGoal, WarSideStats, StrategicPoint, PeaceTerm, DiplomaticDecision, TargetPriority, SettlementInfo, CivAiMemory |
| `.../DiplomacyEvents.cs` | WarDeclared, WarJoined, WarLeft, WarEnded, PeaceSigned, PeaceRejected, TruceSigned, WarResumed, AllianceFormed, TreatyBroken, StrategicPointCaptured, DiplomaticDecision |
| `.../DiplomacyInterfaces.cs` | IDiplomacyService, IDiplomacyQuery, IWarQuery (inkl. GetTargetPriorities für Military), IDiplomacyWorldPort |
| `.../DiplomacySettings.cs` | alle Stellschrauben |
| `.../DiplomacyService.cs`, `.../DiplomacyService.Update.cs` | Beziehungsmatrix, Verträge, Kriege, Bündnispflichten, Frieden, War Score, Erschöpfung, strategische Punkte, Zielprioritäten, Meldungen aus anderen Systemen |
| `.../WarStrategyEvaluator.cs` | Utility-KI: Kriegserklärung, Frieden (Forderung/Zugeständnis/weißer Frieden), Bündnisse, Rekrutierungsanfragen; jede Entscheidung mit Faktoren protokolliert |
| `.../DiplomacySystem.cs` | ISimulationSystem, Order 800, jede Spielsekunde |
| `.../DiplomacyCommands.cs` | DeclareWar (optional erzwungen), ProposePeace (optional erzwungen), SignTruce, FormAlliance, BreakTreaty, ModifyRelation |
| `.../DiplomacySaveable.cs` | ISaveable "diplomacy" (SectionVersion 1, LoadOrder 800), Validierung, tiefe Kopien, Referenzprüfung mit Rückfall (`ISaveReferenceCheck`) |
| `.../DiplomacyOverview.cs` | Lesemodell für die UI-Übersicht (Kriege, Bündnisse, Feindschaften) |
| `Assets/Tests/EditMode/Diplomacy/DiplomacyTests.cs`, `DiplomacyTestDoubles.cs` | 42 EditMode-Tests, Testwelt für den Port |

### 11.2 Anbindung an andere Phasen (über `IDiplomacyWorldPort`)

Phase 4 greift nicht direkt auf fremde Systeme zu. Ein Adapter in der Composition Root (`GameComposition.RegisterPhaseServices`, Besitzer Integration) implementiert den Port mit:
ICivilizationQuery (Bevölkerung, Hauptstadt, Haltung, Alive), ITerritoryService.SharedBorderLength, IEconomy (Bedarf, Tribut über TrySpend/Add), ISettlementRegistry (GetByCiv, Get, TransferSettlement), IRecruitment.Request, Military GetCivCombatStrength und eine "dominante Civ in Radius"-Abfrage (ISpatialIndex/IUnitQuery).
Der DiplomacyService abonniert UnitDiedEvent selbst (Stärke über `IDiplomacyWorldPort.GetUnitCombatStrength`). Der Adapter leitet SettlementCapturedEvent → `ReportSettlementCaptured`, CivilizationCollapsedEvent → `ReportCivilizationCollapsed`. Das CivAISystem (Civ) ruft `WarStrategyEvaluator.EvaluateAndAct(civId)` pro Civ auf.

### 11.2a Kriegsbewertung nach Review (2026-10-26)

Utility = Nähe × (0,35 × Stärkefaktor + 0,65 × Motiv) × (0,4, falls schon im Krieg). Der Stärkefaktor ist weich: 0 bei einem Verhältnis von 0,8 (darunter gibt es nie einen Angriff), 1 ab einem Verhältnis von 2. Das Motiv setzt sich zusammen aus 0,45 × Feindseligkeit, 0,30 × Bedarf und 0,25 × Aggressivität. Die Schwelle liegt bei 0,3.
Neu ist der Grenzgroll (`RelationReason.BorderGrievance`). Er wächst an einer gemeinsamen Grenze ohne Bündnis um 0,0005 pro Kante und Sekunde bis −50. Im Krieg und in der Friedensfrist ruht er, ohne Grenze baut er sich ab, und mit einem Friedensschluss wird er gelöscht. So geraten auch gleich starke Nachbarn ohne Spielereingabe in Konflikt, und nach einem Frieden kehrt eine Ruhephase ein (Sandbox-Regel).

### 11.2b Nachtrag nach Integrationsprobe (2026-10-26)

- Wenn beide Civs keine Armee haben (Stärke 0 : 0, zum Beispiel beim Spielstart), gilt das als Gleichstand und nicht als unendliche Unterlegenheit.
- Im Frieden fordert jede Civ eine Grundarmee an: Zielstärke = Bevölkerung × 0,05 × (0,5 + Aggressivität), höchstens 5 Einheiten je Anfrage, alle 60 s.
- Nähe ohne gemeinsame Grenze wird jetzt über den Abstand der Gebietsränder berechnet, nicht mehr über den Abstand der Hauptstädte: Hauptstadtabstand minus √(Gebietsfläche/π) je Civ, Reichweite 48 Kacheln. Rivalen in Reichweite bauen Groll auf wie an einer Grenze (160 Kanten × Nähe). Neue Port-Methode: `GetTerritoryTiles`.
- Die Utility ist jetzt (0,5 + 0,5 × Nähe) × (0,35 × Stärke + 0,65 × Motiv), die Schwelle liegt bei 0,25.
- Probe mit der echten GameComposition (Branch integration/m1 plus diese Änderungen, 128², 2 Civs, ohne Eingabe): Seed 1 → Krieg nach 8,0 min, Seed 7 → 40,6 min, Seed 2026 → 97,5 min. Vorher gab es bei keinem Seed einen Krieg.

### 11.3 Tests

Ausgeführt am 2026-10-26 mit `dotnet test Tools/SimTests` (.NET 8, C# 9, Warnungen als Fehler) gegen das echte Fundament aus PR #2: 82 von 82 bestanden (40 Fundament, 42 Phase 4). **Nicht im Unity Test Runner ausgeführt.**
Abgedeckt: T-4-01 Kriegserklärung und Bündnisruf, T-4-02 Frieden abhängig vom Kriegsstand inkl. Abtretung und Tribut, T-4-03 Bündnisse, T-4-04 Zielauswahl, T-4-05 kein Krieg ohne Grund (30 Spielminuten) und Krieg mit Grund samt Begründung, Autonomie mit symmetrischen Nachbarn (4 Fälle aus dem Review), UnitDiedEvent über echten SimEventBus und UnitStore, T-4-06 Verluste/Erschöpfung/Friedensangebot des Verlierers, T-4-07 Sandbox-Autonomie (Krieg und Frieden ohne Spielereingabe, INTEGRATION.md §0 Regel 4), T-4-07 Speichern/Laden während des Kriegs mit identischer Fortsetzung (DTO-Rundlauf; der JSON-Teil gehört dem SaveLoadService, Phase 7), Validierung beschädigter Daten, Referenzprüfung; außerdem Waffenstillstand, Zusammenbruch einer Civ, strategische Punkte, Rekrutierung, Grenzreibung, Befehle, Übersicht, nie wiederverwendete IDs.

### 11.4 Noch fehlend (ehrlich)

- Adapter für `IDiplomacyWorldPort` und Registrierung in `GameComposition`: wartet auf Phase 3 (Civ) und Phase 5 (Military). Bis dahin läuft Diplomatie im Spiel nicht.
- Aufruf des Kriegsbewerters durch das CivAISystem (Phase 3).
- UI-Panel (UI Toolkit) für Diplomatie und Armeebefehle: Phase 6; Lesemodell und Commands sind fertig.
- Kampf, Armeen, Rückzug, Verteidigung, Versorgung: Phase 5 (nutzt `GetTargetPriorities` und die Events).
- Handel als Beziehungsquelle und Status `Trade`: noch kein Handelssystem.
- Gewichte sind Startwerte; Abstimmung erst mit laufender Simulation.
- Prüfung im Unity-Editor (Kompilierung mit Unity-Compiler, Test Runner).
