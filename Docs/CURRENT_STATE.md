# World Beyond – CURRENT_STATE

Letzte Aktualisierung: 2026-10-25, Ende Phase 1 (Projektanalyse und Kerndokumentation).
Regel: Nur Tatsachen. Jede Phase aktualisiert diese Datei am Ende. Nicht im Unity-Editor Geprüftes wird als solches markiert.

## 1. Umgebung (Stand 2026-10-26, 11:20 UTC)

| Prüfung | Ergebnis | Quelle |
|---|---|---|
| Unity-Version | **6000.6.0f1** (f7f8ed4d1e24), einzige installierte Version; Windows-Standalone + WebGL; Batchmode-Lizenz ok | DevOps-Thread auf PC FliegeBOX |
| Unity-Projekt des Nutzers | `World-Beyond\World-Beyon` (untracked im Repo), frisches URP-Template: URP 17.6.0, Test Framework 1.8.0, Input System 1.20.0, ugui 2.6.0 (+ ai.assistant-Prerelease, visualscripting, collab-proxy, timeline – nicht übernehmen) | DevOps-Thread |
| `ProjectSettings/`, `Packages/manifest.json`, `.meta` im Repo | **fehlen noch**; DevOps legt sie auf Branch `devops/unity-project` an (Template + `com.unity.nuget.newtonsoft-json` 3.2.1) | angefordert |
| Unity in der Cloud | nicht verfügbar; dort `dotnet test Tools/SimTests` und `dotnet build Tools/UnityApiCheck` | – |

## 2. Was tatsächlich existiert

**Repository:** `github.com/Brastib/World-Beyond` (einzige Wahrheit). Git LFS für Binär-Assets (`.gitattributes`).

| Bereich | Stand | Geprüft wie |
|---|---|---|
| Fundament 1b | PR #2 (`claude/phase1b-fundament` @04b14fb), Merge nach main wartet auf Nutzerfreigabe | **Unity 6000.6.0f1: 0 Fehler, 0 Warnungen, EditMode 44/44** (DevOps, Scratch-Kopie mit Newtonsoft 3.2.1); dotnet 44/44 |
| Phasen-PRs | #1 Save-Kern, #5 SaveLoadService, #3 Diplomatie/Krieg, #4 Militär, #6 UI-Styleguide, #7 Civ; World auf `phase2-world-engine` | je Phase headless grün (Angaben der Phasen); Civ @503ae48 in Unity: 102 bestanden, 3 übersprungen (DevOps) |
| `integration/m1` | alle Phasen zusammengeführt (ohne Konflikte), War + Military in `RegisterPhaseServices` verdrahtet, Adapter `Application/Integration/PhaseAdapters.cs` | dotnet **293/293** (@73750c1), UnityApiCheck grün; **nicht in Unity** |
| Autonomer Krieg im Gesamtspiel | **funktioniert noch nicht**: beide Civs ohne Armee → Stärkeverhältnis 0 → kein Krieg (M1SandboxProbe, 3 Seeds, 2 Spielstunden) | an War gemeldet |
| `GameBootstrap`, Szenen, Kamera, Rendering, UI, Assets | Bootstrap geschrieben, Rest fehlt | Bootstrap in Unity kompiliert (Teil von 1b) |

## 3. Implementierte Schnittstellen

Siehe SYSTEM_INTERFACES §0 (Status je Schnittstelle). Implementiert (1b, ungemergt): `EntityId`, `EntityStore`, `SequentialIdAllocator`, `ContentRegistry`, `SimRandom`, `SimEventBus`, `SimClock`, `ISimulationSystem`, `SimContext`, `IWorldCommand`/`CommandQueue`/`CommandProcessingSystem`, `ISaveable`/`SaveableRegistry`/`SimCoreSaveable`, `UnitTypeRegistry`, `UnitStore`, `SimulationRunner`, `GameComposition`, `GameBootstrap`.

## 4. Ausgeführte Tests

| Datum | Was | Umgebung | Ergebnis |
|---|---|---|---|
| 2026-10-26 | Fundament 1b, EditMode-Testdateien `Assets/Tests/EditMode/*.cs` | dotnet 8 in der Cloud (`Tools/SimTests`), ohne Unity | 44/44 bestanden. Zwei Fehler im ersten Lauf gefunden und behoben: Events aus Handlern kamen im selben Flush an; Runner verlor durch float-Rundung einen Tick pro Sekunde |
| 2026-10-26 | Fundament 1b @04b14fb | Unity 6000.6.0f1, PC des Nutzers (DevOps) | 0 Fehler, 0 Warnungen, EditMode 44/44 |
| 2026-10-26 | Civ @503ae48 | Unity 6000.6.0f1 (DevOps) | 102 bestanden, 3 übersprungen ([Explicit]) |
| 2026-10-26 | `integration/m1` @73750c1 (alle Phasen) | dotnet 8, Cloud | 293/293; M1SandboxProbe: kein Krieg in 72 000 Ticks |

## 5. Bekannte Probleme / Widersprüche

| # | Problem | Status |
|---|---|---|
| K1 | UI_STYLE_GUIDE A2 auf D13 korrigiert (1 Einheit = 1 m, 1 Kachel = 4 m, Modelle in Originalgröße). PHASE5_MILITARY_ASSETS.md prüfen, ob dort noch alte Werte stehen. | Phase 6 erledigt, Phase 5 offen |
| K2 | Stilrichtung nur Arbeitshypothese; Style Lock braucht Unity und Nutzerbestätigung | offen |
| K3 | Asset-Downloads aus der Cloud laut Phase 5 vom Proxy blockiert (403); Import realistisch nur auf dem PC | nicht selbst geprüft |
| K4 | Newtonsoft in `WorldBeyond.Application` | erledigt: kompiliert in Unity mit Paket 3.2.1 |
| K5 | Zeitmaßstab: Civ rechnet 1 Spieljahr = 60 s bei 1× (`CivTime.TicksPerYear`), Einheiten laufen in Echtzeit. Widerspricht VISION V7 (1× = Echtzeit). | bewusst bis M2, dann `GameCalendar` als einzige Zeitbasis |
| K6 | Kein autonomer Krieg im Gesamtspiel (siehe §2) | War, offen |

## 6. Fehlende Abhängigkeiten

1. Unity-Projektdateien im Repo (DevOps, `devops/unity-project`).
2. Nutzerfreigabe für den Merge von PR #2.

## 7. Nächste Aufgabe

K6 beheben (War), dann M1-Sandbox-Abnahmetest auf `integration/m1` (ohne Eingriff: Wachstum, Krieg, Kampf, Speichern/Laden), Unity-Lauf von `integration/m1`, danach Phasen-PRs der Reihe nach nach main.
