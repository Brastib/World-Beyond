# Phase 3 – Zivilisationssimulation: Entwurf

Status: **ENTWURF**, Stand 2026-10-25. Noch kein Code. Wird an SYSTEM_INTERFACES.md (Phase 0) und die World-Engine-Schnittstellen (Phase 2) angepasst, sobald diese existieren. Namen von Welt-Schnittstellen hier sind Platzhalter (`IWorldQuery`, `IWorldMutation`, `IPathfinder`) und werden durch die echten ersetzt.

Grundlage: PROJECT_MASTER §3.2, ARCHITECTURE §1, §5, §8, §11.

## 1. Ziele aus dem Auftrag → Umsetzung

| Anforderung | Umsetzung (geplant) |
|---|---|
| Siedlungen gründen und wachsen | `SettlementStore`, Stufen Lager → Dorf → Stadt → Großstadt nach Bevölkerung + Gebäuden |
| Geburten, Todesfälle, Migration | `PopulationSystem` (Kohorten pro Siedlung, keine Einzelagenten fern vom Spieler) |
| Wohnraum, Nahrung, Holz, Stein, Metall, weitere | `ResourceType`-Enum + Lager pro Siedlung; Wohnraum als Kapazität aus Gebäuden |
| Gebäude: Wohnen, Versorgung, Produktion, Lagerung, Verteidigung | `BuildingTypeData` (Daten), `BuildingStore`, belegt Kacheln über Welt-Schnittstelle |
| Arbeiter, Einwohner, Transport, Aufgabenverteilung | `LaborSystem` verteilt Arbeiter auf Jobs; Transport als Karawanen zwischen Siedlungen |
| Neue Siedlungen | `ExpansionPlanner` (Standortbewertung) + Siedlergruppe läuft real dorthin |
| Territorien, Grenzen | `TerritorySystem` schreibt Kachelbesitz (Welt-Daten), gebündelt neu berechnet |
| Forschung | `ResearchSystem` + Tech-Baum als Daten (Epochen) |
| Verschiedene Zivilisationen | `CivilizationStore` (Name, Farbe, Merkmale/Traits) |
| Läuft ohne Spieler | `CivilizationAI` (Utility-Bewertung, alle ~5 Spielsekunden) |

## 2. Verbundene Wirtschaft (keine geskripteten Zahlen)

Der Kreislauf ist echt verkettet, jede Zahl hat eine Ursache:

```
Weltkachel (Wald/Fels/Erz/fruchtbares Land)
  → Gebäude mit Einzugsbereich (Holzfäller, Steinbruch, Mine, Farm) braucht Arbeiter
  → Ertrag = Arbeiter × Rate × Kachelqualität × Tech-Bonus; Vorkommen auf Kacheln sinkt (Wald wird abgeholzt)
  → Lager der Siedlung (Kapazität durch Lagerhäuser begrenzt)
  → Verbrauch: Nahrung pro Einwohner, Bau-Kosten, Forschung, Werkzeuge (Metall)
  → Mangel wirkt zurück: Hunger → höhere Sterberate, Abwanderung; Wohnraummangel → keine Geburten über Kapazität
  → Überschuss: Wachstum, Bau, Siedler für neue Siedlungen
  → Transport: Karawanen gleichen Überschuss/Mangel zwischen Siedlungen derselben Zivilisation aus
```

Kein Ertrag ohne Arbeiter, keine Arbeiter ohne Einwohner, keine Einwohner ohne Nahrung und Wohnraum.

## 3. Datenmodell (reines C#, Namespace `WorldBeyond.Simulation.Civilizations`)

- `Civilization`: Id, Name, Color (RGBA-Werttyp), Traits (Fleiß, Fruchtbarkeit, Expansionsdrang, Forschung, Militär als Multiplikatoren), CapitalId, Epoch, ResearchProgress, KnownTechs (BitSet), AiState, Alive.
- `Settlement`: Id, CivId, CenterTile, Name, Tier, Population (Kohorten: Kinder/Erwerbsfähige/Alte), Stockpile[ResourceType], StorageCap, HousingCap, BuildingIds, Workers pro JobType, Happiness/Stability, FoundedTick, SimLod.
- `Building`: Id, TypeId, SettlementId, Tiles (Footprint), HP, ConstructionProgress, AssignedWorkers.
- `BuildingTypeData` (Daten, aus JSON/ScriptableObject): Kategorie (Housing, Supply, Production, Storage, Defense, Research), Kosten, Bauzeit, Footprint, Wohnraum, Lager, Jobs, Erzeugt/Verbraucht, benötigte Tech, benötigter Geländetyp/Vorkommen.
- `ResourceType`: Food, Wood, Stone, Metal (Erz→Metall über Schmelze), Tools, Gold; erweiterbar (Öl ab Moderne für Fahrzeuge).
- `Caravan`/`SettlerGroup`: reale Bewegung über Weltpfad, sichtbar, speicherbar.

Alle Stores mit Generations-IDs und `ISaveable` (ARCHITECTURE §1.3, §1.7).

## 4. Systeme und Tick-Raten

Basis 10 Ticks/Spielsekunde (ARCHITECTURE §1.2). Systeme laufen gestaffelt:

| System | Intervall | Aufgabe |
|---|---|---|
| ConstructionSystem | 1 s | Baufortschritt durch Arbeiter + Material |
| ProductionSystem | 1 s | Erträge, Kachel-Abbau, Verarbeitung |
| ConsumptionSystem | 1 s | Nahrung, Werkzeugverschleiß |
| PopulationSystem | 1 Spieltag (konfigurierbar) | Geburten, Tod, Alterung, Migration |
| LaborSystem | 2 s | Arbeiter auf Jobs nach Prioritäten |
| TransportSystem | 1 s Bewegung, 10 s Planung | Karawanen |
| TerritorySystem | bei Änderung, gebündelt max. alle 5 s | Kachelbesitz neu |
| ResearchSystem | 1 s | Forschungspunkte → Technologien |
| CivilizationAI | 5 s, pro Civ versetzt | Bauen, Expansion, Reaktion auf Engpässe |

## 5. Detailstufen (Performance)

- `SimLod.Active`: Siedlung im Kamerabereich oder ausgewählt → alle Systeme im Normaltakt, sichtbare Arbeiter-Einheiten (später Phase 4 Einheiten).
- `SimLod.Distant`: gleicher Rechenweg, aber seltener aufgerufen mit Δt × n (z. B. alle 5 s mit 5-fachem Zeitschritt). Gleiche Formeln → gleiche Ergebnisse im Mittel, keine Inkonsistenz beim Heranzoomen.
- Wechsel Distant → Active ändert nur die Frequenz, nie den Zustand.
- Test: dieselbe Siedlung über 1 Spieljahr in Active und Distant simuliert; Abweichung von Bevölkerung/Ressourcen muss unter einer festen Toleranz bleiben.

## 6. Autonomie (CivilizationAI)

Utility-Bewertung, jede Entscheidung mit protokolliertem Grund (für UI "Warum tut die Civ das?"):
- Nahrungsbilanz negativ → Farm/Fischer bauen, Arbeiter umschichten, Karawane anfordern.
- Wohnraum > 90 % belegt → Wohnhaus.
- Lager voll → Lagerhaus.
- Bevölkerung und Überschuss hoch, Siedlung ≥ Dorf → Siedlergruppe aufstellen, Ziel aus `ExpansionPlanner`.
- Forschungsziel nach Traits und Engpässen.
- Verteidigung nach Grenzkontakt (Kampf selbst: spätere Phase).

`ExpansionPlanner` bewertet Kandidatenkacheln (begehbar, nicht Wasser, nicht fremdes Territorium, Abstand zu eigenen Siedlungen, Nahrung/Holz/Stein/Wasser im Umkreis, Pfadkosten). Sampling statt Vollsuche.

## 7. Spielerinteraktion (über `IWorldCommand`, ARCHITECTURE §9)

- `CreateCivilizationCommand(name, color, traits, startTile)`
- `FoundSettlementCommand(civId, tile)` / `SuggestExpansionTargetCommand(civId, tile)`
- Auswahl über SelectionService (Siedlung, Gebäude, später Einwohner-Einheiten)
- Lese-Interface `ICivilizationQuery` für UI: Bevölkerung, Ressourcen, Bilanzen, Territorium, Tech, letzte KI-Entscheidungen
- Zeit: nutzt vorhandene SimClock (Pause, 1x–10x), nichts Eigenes
- Ereignisse: `TriggerEventCommand` (Seuche, Missernte, Babyboom, Goldfund, Brand). Wirken nur über die normalen Zustandsfelder (Sterberate-Modifikator mit Dauer usw.), damit nichts kaputtgeht.

## 8. Speichern/Laden

Jeder Store `ISaveable`; DTOs ohne Referenzen, nur IDs. Reihenfolge: Welt → Civs → Settlements → Buildings → Caravans → AI-Zustand. Test: Speichern nach N Ticks, laden, weitere M Ticks, Vergleich mit ununterbrochenem Lauf (deterministisch über SimRandom).

## 9. Abnahmetest (geplant, EditMode, ohne Szene)

`CivilizationLongRunTests`:
1. Welt mit festem Seed (World Engine), 2–3 Civs an gültigen Startpunkten.
2. Simulation ≥ 20 Spieljahre im Schnelldurchlauf.
3. Prüfungen: jede Civ hat Bevölkerung > Start; mindestens eine neue Siedlung gegründet; Ressourcenbilanz plausibel (Vorkommen auf Kacheln gesunken = produzierte Menge); keine negativen Lager; Territorien überlappen nicht; Tech-Fortschritt > 0.
4. Save/Load-Gleichheit (siehe §8).
5. Active-vs-Distant-Toleranztest (siehe §5).
6. Tick-Zeitmessung als Ausgabe (nicht als harte Grenze in CI).

## 10. Offene Punkte / Abhängigkeiten

- [O] Echte Namen der Welt-Schnittstellen (Phase 2). Vorläufig mit Phase 2 abgestimmt (2026-10-25, noch nicht in SYSTEM_INTERFACES):
  - `TileData.Fertility` (byte), `IWorldQuery.IsWater(x,y)`, `IsWalkable(x,y,MovementClass)`
  - `IWorldGrid.TryHarvest(x,y,amount,out taken)` → `TileChangedEvent`, `ResourceDepletedEvent` (Wald → Gras bei 0)
  - `IWorldGrid.SetOwner(x,y,civId)`, `SetBuilding(x,y,EntityId)`, `ClearBuilding` (OwnerChanged gebündelt)
  - `IPathfinder` (A*, MovementClass Foot/Wheeled/Tracked), `IPathfinder.EstimateCost(from,to,mc)`
  - `IWorldQuery.ForEachInRect(...)`, `FindBestTiles(cx,cy,radius,scoreFunc,maxResults,resultList)`
  - `WorldGrid : ISaveable`, SaveKey "world", LoadOrder 0 → Zivilisations-Stores laden danach (LoadOrder ≥ 10)
- [B] Kachelbesitz liegt in TileData (ARCHITECTURE §3), Schreibzugriff nur über `IWorldGrid.SetOwner`. Kein eigener Besitz-Array in der Zivilisationsschicht.
- [B] Einheiten (abgestimmt mit Integrations-Thread, Docs/INTEGRATION.md §3): Es gibt genau einen gemeinsamen `UnitStore` aus dem Fundament (dazu SimClock, Runner, EventBus, CommandQueue, ISaveable, GameBootstrap). Phase 3 legt **keine** eigene Variante an. Siedler, Karawanen und sichtbare Arbeiter sind Einheiten im `UnitStore`; Phase 3 trägt Zivil-Typen (Settler, Worker, Caravan) in `UnitTypeData` ein. Arbeiter fern vom Spieler bleiben als Zahl pro Job abstrahiert.
- [B] Zuständigkeit Phase 3: Civ-, Settlement-, Territory-Registry, `IEconomy` (einziges Ressourcenlager), `ITechTree`, Rahmen des einen `CivAISystem` (Phase 4 liefert darin den Kriegs-Bewerter). Jedes System implementiert `ISaveable` selbst.
- [O] Kein Unity-Editor in der Cloud: Tests nur auf dem PC des Nutzers oder per Remote Control. Die reine C#-Simulation könnte zusätzlich headless mit `dotnet test` laufen, wenn das SDK installiert werden kann.

## 11. Schnittstellen, die Phase 3 anderen Phasen bereitstellt (abgestimmt mit Phase 4, 2026-10-25)

- `ICivilizationQuery`: Civs auflisten, Name/Farbe/Traits, Bevölkerung gesamt, Ressourcen gesamt, Hauptstadt-ID, Epoche/Techs, letzte KI-Entscheidungen.
- `ISettlementQuery`: Siedlungen einer Civ, Zentrum, Stufe, Bevölkerung, Lager, Gebäude.
- `ISettlementControl.TransferSettlement(settlementId, newCivId)`: Besitzwechsel (Eroberung); danach gebündelte Territoriums-Neuberechnung. Wird die alte Hauptstadt übertragen, wählt die alte Civ eine neue; ohne Siedlungen wird sie `Alive = false`.
- `ITerritoryQuery.GetSharedBorderLength(civA, civB)`: Anzahl Kachelkanten zwischen beiden Gebieten, gecacht bei jeder Territoriums-Neuberechnung.
- `IEconomy.TryConsume(civId, resource, amount)`: zieht atomar aus den Lagern der Civ (Hauptstadt zuerst, dann nach Vorrat); `false` ohne Abbuchung, wenn nicht genug vorhanden. Gedacht für Truppenunterhalt.
- Zuständigkeit: Wirtschafts-, Bau-, Expansions- und Forschungs-KI = Phase 3. Diplomatie, Krieg, Kriegs-KI = Phase 4.
- Für Military (Docs/INTEGRATION.md §5): `IEconomy.TrySpend(civId, ResourceCost cost)` atomar für mehrere Ressourcen (Rekrutierung); `TryConsume(civId, resource, amount)` ist nur die Ein-Ressourcen-Variante desselben Codes, kein zweiter Weg. `ITechTree.IsUnlocked(civId, techId)` stabil. Zivileinheiten (Settler, Worker, Caravan) produziert Phase 3, Militäreinheiten Military über `IRecruitment`; beide spawnen über den einen `UnitStore`.

## 12. Abgleich mit SYSTEM_INTERFACES §7 und IMPLEMENTATION_PLAN Phase 3 (2026-10-25, 21:01)

SYSTEM_INTERFACES §7 ist ab jetzt verbindlich; bei Abweichungen gilt es vor diesem Entwurf.
- Namen: `ICivilizationRegistry`, `ISettlementRegistry` (TransferSettlement dort), `ITerritoryService` (`OwnerAt`, `SharedBorderLength`, `MarkDirty`), `IEconomy` (`GetStock`, `TrySpend(in ResourceCost)`, `Add`), `ITechTree` (`IsUnlocked`, `CurrentEpoch`, `SetResearch`, `ForceEpoch`), `IRecruitment`. Ersetzen die Namen aus §7/§11 dieses Entwurfs.
- Siedlungs-IDs: `int` aus fortlaufendem Zähler, nie wiederverwendet (nicht `EntityId`). Gebäude: gleiche Regel angenommen, mit Phase 7 zu bestätigen.
- `IEconomy` ist nach außen pro Zivilisation. Intern führt es Lager pro Siedlung (nötig für Transport, Lagerkapazität und lokalen Hunger); `GetStock` summiert, `TrySpend` zieht Hauptstadt zuerst, `Add` ohne Ort bucht in die Hauptstadt. Es bleibt das einzige Lager, kein zweites System.
- Abnahme laut Plan: ≥ 2 Zivilisationen wachsen 30 Spielminuten ohne Eingriff. Die Zeitskala (wie viele Spieltage/-jahre sind 30 Spielminuten) ist im Entwurf noch offen und wird beim Bau an SimClock festgelegt; Geburten/Tode müssen in diesem Zeitraum sichtbar wirken.
- [B] IRecruitment gehört Phase 3 und ist der einzige Produktionsweg für alle Einheiten (zivil und militärisch); Phase 5 liefert nur Typen und Produktionsgebäude als Daten und ruft `Request` auf (bestätigt vom Integrations-Thread, INTEGRATION.md §5).

## 13. Abgleich World-Schnittstellen (SYSTEM_INTERFACES §4/§5) und Speicher-/ID-Regeln (ARCHITECTURE §11), 2026-10-25 21:03

Welt (verbindlich, ersetzt die Platzhalter aus §10):
- Standortsuche: `IWorldQuery.FindStartLocations` (Startpunkte neuer Civs), `FindBestTiles` mit `TileScorer` (Expansion), `IPathfinder.AreConnected` + `EstimateCost` (Erreichbarkeit/Entfernung, MovementClass.Foot).
- Bauen: `CanPlaceFootprint` (Gelände/Belegung) + eigene Besitzprüfung über `TileData.OwnerCivId` (eigenes oder neutrales Gebiet); Wald im Footprint vorher mit `TryHarvest` roden, Holz an die Siedlung; dann `SetBuilding` je Kachel, bei `false` Rollback.
- Ertrag: Farmen aus `Fertility`, Holzfäller/Steinbruch/Mine über `TryHarvest` auf `Resource`/`ResourceAmount` im Einzugsbereich; auf `ResourceDepletedEvent` sucht der Betrieb neue Kacheln oder wird stillgelegt.
- Territorium: `SetOwner` (gebündelt → `OwnershipChangedEvent`).

Speicher/IDs (verbindlich laut Nutzer):
- Civ- und Siedlungs-IDs aus `SequentialIdAllocator` (Zählerstand im Save, nie wiederverwendet).
- Gebäude = `EntityId` über `EntityStore`; Siedler/Arbeiter/Karawanen = Einheiten im gemeinsamen `UnitStore`.
- Gebäude-, Technologie- und Einheitentypen als String-Schlüssel über `ContentRegistry`.
- Alle Stores `ISaveable` mit `SectionVersion`; Querverweise (Siedlung → Civ, Gebäude → Siedlung, Einheit → Siedlung) über `ISaveReferenceCheck`. Rückfall: verwaiste Gebäude werden zu herrenlosen Ruinen (Kachel bleibt belegt, keine Produktion), verwaiste Siedlungen werden entfernt und ihr Gebiet freigegeben, ungültige Hauptstadt-ID → größte verbleibende Siedlung.

Offen:
- [O] `ResourceType` (§4) hat Food, Wood, Stone, Ore, Oil, Gold. Der Auftrag nennt „Metall“. Vorschlag: Werte `Metal = 7`, `Tools = 8` anhängen (Erz → Metall in Schmelze, Metall → Werkzeug). Bei World und Phase 7 angefragt.
- [O] Phase 7 nennt Ressourcentypen als String-Schlüssel, §4 als Enum mit festen Werten. Annahme: Enum mit festen Werten genügt für Ressourcen (Regel „Enums erhalten explizite Werte“); bei Phase 7 nachgefragt.

## 14. Umgesetzte Abweichungen von SYSTEM_INTERFACES §7 (Stand 2026-10-26, Branch `phase3-civilization`)

Bitte vom Integrations-Thread in SYSTEM_INTERFACES §7 übernehmen:
- `ICivilizationRegistry.Create/TryCreate` nehmen `CivColor` (eigener Struct R, G, B, A) statt `UnityEngine.Color32`, damit die Simulation ohne Unity kompiliert. Die Darstellung wandelt in `Color32` um.
- Zusätzlich zu §7: `ISettlementRegistry.TryFound`, `CanFoundAt`; `ISettlementQuery.FindAt`, `CopyBuildings`, `AllSettlementIds`; `IEconomy.GetSettlementStock`, `TrySpendAt`; `ITechTree.TryGetTech`, `IsUnlocked(civId, key)`; `IRecruitment.TryGetRecruitable`; `ICivSimulationLod` (Fokus/Auswahl), `ICivDecisionLog` (nachvollziehbare KI-Entscheidungen), `ICivWarEvaluator` (Anschluss für den Kriegsbewerter aus Phase 4).
- `CivState` bietet öffentliche Lesezugriffe (`CivIdsInOrder`, `SettlementIdsInOrder`, `FindCiv`, `FindSettlement`) für Darstellung und Tests; geschrieben wird nur über die Dienste.
- Ladefehler-Rückfall (abweichend von §13): verwaiste Gebäude werden abgerissen statt als Ruine stehen zu bleiben (Warnung im LoadReport); ungültige Hauptstadt → größte verbleibende Siedlung; Karawanen ohne Einheit werden verworfen.
- `ResourceType.Metal = 7`, `Tools = 8` sind von World angehängt (nur Lagergüter).

Anschluss in `GameComposition.RegisterPhaseServices` (zwei Zeilen):
```csharp
var civ = CivModule.Install(ctx, runner.Register);
civ.SpawnInitialCivilizations(settings.CivilizationCount, settings.Seed);
```

## 15. Zeitskala und Raten (abgestimmt im Langzeittest)

- 1 Spieljahr = 60 Spielsekunden = 600 Ticks. 30 Spielminuten = 30 Spieljahre.
- Detailstufen: aktive Siedlungen alle 10 Ticks, entfernte alle 50 Ticks, beide mit der tatsächlich vergangenen Zeit als Zeitschritt.
- Pro Jahr: Nahrung 1,0 je Erwachsenem, 0,6 je Kind, 0,8 je Altem; Geburten 0,085 je Erwachsenem (skaliert mit Versorgung, Wohnraum, gedämpftem Fruchtbarkeitsmerkmal ±7,5 %); Reifung 1/6, Altern 1/40; Sterblichkeit Kinder 9 %, Erwachsene 1,5 %, Alte 15 %; bei Hunger sterben 40 % der Unversorgten pro Jahr. Ergebnis: vorindustriell höchstens ~1 %/Jahr Nettowachstum, mit Medizin ~2 %/Jahr.
- Forschung: 0,08 × √Einwohner je Siedlung und Jahr plus Bibliotheken (0,25 je Gelehrtem). Kosten: Frühzeit 120–200, Antike 2 500–4 500, Mittelalter 8 000–10 000, Industrie 10 000–12 000, Moderne 15 000–18 000 Punkte.
- Tochtersiedlungen erst ab 20 Erwachsenen im Ursprungsort.
- Zielvorräte statt Horten: Rohstoffbetriebe ruhen über einem Zielvorrat (Holz 80 + 3 je Einwohner, Stein 60 + 2, Erz 40 + 1 …); Lager werden nur gebaut, wenn der Zielvorrat die Lagergrenze übersteigt. Rodungsholz zählt nur bis zur Lagergrenze.
- Startwerte je Civ: 10 Einwohner, Nahrung 80, Holz 60, Stein 10. Siedler kosten Nahrung 40, Holz 30 und 5 Erwachsene; neue Siedlung startet mit 5 Einwohnern, Nahrung 30, Holz 20.

**Realistisches Tempo (VISION V7, 2026-10-26):** Der Unity-Lauf (Seed 20261026, 4 Civs) erreichte das Industriezeitalter nach 75 Jahren. Ursachen: Forschung wuchs linear mit der Einwohnerzahl, Technologien waren billig, und die Bevölkerung wuchs ~8 %/Jahr. Nach der Umstellung, gemessen headless (Welt 128 × 128, 2 Civs, Seeds 1 und 20261026): Frühzeit dauert 700 bis über 1 000 Jahre, Antike beginnt um Jahr 700–900; die Bevölkerung wächst in 1 000 Jahren von 10 auf 1 000–3 400 je Civ mit 16–37 Siedlungen. Abgesichert durch `CivTechPaceTests` (300 Jahre: noch Frühzeit, aber Wachstum und Tochtersiedlungen; explizit 1 000 Jahre: höchstens Mittelalter).

Offen (Fundament/Integration): `CivTime.TicksPerYear = 600` (1 Jahr = 60 s bei 1×) ist bis zum `GameCalendar` (VISION V7/M2) die Kalenderbasis der Civ-Schicht. Alle Civ-Raten sind „pro Jahr“ formuliert; wenn der Kalender kommt, wird nur diese Konstante ersetzt. Bei 1× = Echtzeit wären Jahrhunderte nur mit Zeitsprung (V1/V3) erlebbar.

Im Langzeittest gefundene und behobene Fehler: Einheiten zielten in den Ortskern (Gebäude blockieren Bewegung) → begehbarer Ankunftspunkt neben dem Kern; Expansion scheiterte an der Wegprüfung vom blockierten Kern aus; Lager-Schleife (volles Holzlager → neues Lager → mehr Holz); Holzfäller kostete Holz (Siedlung ohne Holz kam nie wieder heraus).

Nachtprüfung 2026-10-27 (Prüf-Thread), behoben:
- Siedlungen mauerten sich gegenseitig ein, weil Nachbarorte aneinanderwuchsen und jede ihr eigenes Raster hatte. Jetzt gilt ein weltweites Straßenraster: Jede 6. Spalte und Zeile der Karte bleibt unbebaut (`SettlementRegistry.IsStreet(x, y)`). Nur Gelände unterbricht Straßen.
- Ankunftspunkt (`CivState.ArrivalTile`): Vorrang hat eine Straßenkachel mit mindestens zwei begehbaren Nachbarn, dann jede Straßenkachel, dann jede begehbare Kachel.
- `CanBuild` lehnt Grundflächen ab, auf denen laut `ISpatialIndex` Einheiten stehen.
- Karawanen fahren nur, wenn ein Weg existiert (`IPathfinder.AreConnected`). Nach einem Fehlschlag ist die Strecke 5 Jahre gesperrt; die Sperren werden gespeichert (`civs.BlockedRoutes`).
- `TerritoryService.CaptureDirty` speichert Nullen, wenn nichts offen ist.
- Höchstens 10 % der Einwohner stehen unter Waffen, mindestens 2 (`Recruitment.MaxMilitary`). Grund: Die Kriegs-KI hatte kleine Völker bis auf 6 Einwohner herunterrekrutiert.
- Die Standortwahl zählt ebene 2×2-Feldplätze (mindestens 25). Startplätze werden im Umkreis 8 verbessert, weil das hügeligere World-Gelände Orte an Hänge ohne Felder setzte.

## 16. Tests und Übergabestatus

Headless (Tools/SimTests, .NET 8, NUnit), Stand 2026-10-27 (`phase3-civilization` @73cf2fa): 126 von 126 Tests grün. Im Probe-Merge auf integration/m1 @9eea9a2 sind es 353 von 353. Neu sind `CivAccessTests` (Straßenraster, Erreichbarkeit der Hauptstadt, keine Einheit in Gebäuden; 400 Jahre, Seeds 1 und 12345), `CivTransportTests` (Streckensperre; Langlauf mit 0 Karawanen-Fehlschlägen) und `TerritorySaveTests`. Davor waren es 120 Tests, davon 14 Civ-Tests (+2 explizite Langläufe) in `Assets/Tests/EditMode/Civilizations/`:
- `TwoCivs_GrowAndExpand_WithoutAnyCommand` (3 Seeds, 200 Spieljahre, kein Command): Bevölkerung mindestens verdoppelt, Gebiet wächst, mindestens drei neue Siedlungen durch echte Siedler-Einheiten, Gebäude gebaut, Forschung, Holz auf den Weltkacheln abgebaut, keine Civ bricht zusammen.
- `FoundedSettlements_RespectPlacementRules`, `AutonomousRun_IsDeterministic`, `Hunger_ReducesPopulation_AndConsumesFood`, `Territory_IsConsistent_WithSettlementsAndCounts`, `Lod_ActiveAndDistant_DevelopSimilarly` (Abweichung < 20 %, gemessen 0–7 %), `CreateCivilization_FoundSettlement_AndEvents_Work`, `Recruitment_SpawnsRealUnit_AndPaysPopulation`.
- `SaveMidGrowth_LoadIntoFreshGame_ContinuesIdentically`: nach 15 Jahren alle Sektionen über JSON (Newtonsoft) in ein frisches Spiel mit anderem Seed eingespielt, danach 10 Jahre weitergerechnet – Zustand bitgenau gleich. `CivSection_RejectsBrokenState_AndKeepsCurrent`.
- Tools/UnityApiCheck (netstandard2.1) baut ohne Warnungen.

Nicht geprüft: Der Unity-Editor selbst (Import, Szenen, Darstellung). Die Darstellung der Siedlungen, Gebäude und Gebietsgrenzen in Unity ist nicht Teil dieses Branches; sie gehört zum UI-/Darstellungs-Thread und liest die Daten über `ISettlementQuery`, `ICivilizationQuery`, `IBuildingQuery`, `ITerritoryService.OwnerAt` und die Civ-Ereignisse.

Bekannte Grenzen:
- Arbeiter sind Zahlen je Siedlung (Berufe, Bau, Abbau), keine einzeln laufenden Figuren. Siedler und Karawanen sind echte Einheiten auf der Karte.
- Migration zwischen Siedlungen derselben Civ ist abstrakt (sofortiger Wechsel bei Wohnraummangel), nicht als laufende Einheiten.
- Der World-Test `TenMinutes_WithoutPlayerCommands_UnitsKeepMoving` setzt seine Test-Einheiten auf die Startplätze, auf denen jetzt Ortskerne stehen. Er ist grün, sollte aber `CivilizationCount = 0` setzen (beim World-Thread angefragt).
