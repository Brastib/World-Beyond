# World Beyond – CURRENT_STATE

Letzte Aktualisierung: 2026-10-25, Ende Phase 1 (Projektanalyse und Kerndokumentation).
Regel: Nur Tatsachen. Jede Phase aktualisiert diese Datei am Ende. Nicht im Unity-Editor Geprüftes wird als solches markiert.

## 1. Projektanalyse (geprüft am 2026-10-25)

| Prüfung | Ergebnis | Wie geprüft |
|---|---|---|
| Unity-Projekt in GitHub-Repos des Nutzers | **keins vorhanden**. Repos: `Brastib/Games` (nur README.md, 1 Commit), `Seniorenheim`, `Rogue-Core-ConfiguratorBastiu.Tom`, `Flappy-Bird`, `Flappy-Bird-` | Repo-Liste abgefragt; `Brastib/Games` geklont und durchsucht (kein `ProjectSettings/ProjectVersion.txt`, kein `Packages/manifest.json`). Die anderen Repos wurden nur nach Namen beurteilt, nicht geöffnet |
| Unity-Projekt auf dem PC des Nutzers | **unbekannt** – kein Gerät mit dieser Sitzung verbunden | Geräteliste abgefragt: leer |
| Installierte Unity-Version | **unbekannt** | nicht prüfbar ohne Gerätezugriff |
| Installierte Pakete | **unbekannt** (kein Projekt) | – |
| Unity in der Cloud-Umgebung | nicht verfügbar | Arbeitsumgebung |

## 2. Was tatsächlich existiert (Stand 2026-10-26, 01:15 UTC)

**Repository:** `github.com/Brastib/World-Beyond` (einzige Wahrheit seit 2026-10-25). Git LFS für Binär-Assets konfiguriert (`.gitattributes`).

| Bereich | Stand | Geprüft wie |
|---|---|---|
| `Docs/` | alle Kerndokumente auf `main` | – |
| Unity-Projekt (`ProjectSettings/`, `Packages/manifest.json`) | **fehlt** – Unity-Version des Nutzers unbekannt | – |
| Fundament 1b (`Assets/_Project/Scripts/Core`, `Simulation`, `Application`, asmdefs) | Code geschrieben, Branch `claude/phase1b-fundament` | `dotnet test Tools/SimTests`: **41/41 bestanden** (net8.0, C# 9, Warnungen als Fehler). **Nicht im Unity-Editor kompiliert**, Unity Test Runner nicht ausgeführt |
| `GameBootstrap` (MonoBehaviour) | geschrieben | **nicht kompiliert** (braucht UnityEngine) |
| Szenen, Kamera, Rendering, UI, Assets | fehlen | – |
| `Assets/ThirdParty/ASSET_CREDITS.md` | leere Import-Tabelle | – |
| Phase 7 Save-Kern | PR #1 (`phase7/save-core`), eigene Dateien | laut Phase 7: 57 headless Tests bestanden (nicht vom Integrations-Thread nachgeprüft) |
| Phasen 2–6 | Entwürfe in `Docs/drafts`, `Docs/proposals`; Phase-5-Vorarbeit im Projektordner `Prep/` (nicht im Repo) | – |

## 3. Implementierte Schnittstellen

Siehe SYSTEM_INTERFACES §0 (Status je Schnittstelle). Implementiert (1b, ungemergt): `EntityId`, `EntityStore`, `SequentialIdAllocator`, `ContentRegistry`, `SimRandom`, `SimEventBus`, `SimClock`, `ISimulationSystem`, `SimContext`, `IWorldCommand`/`CommandQueue`/`CommandProcessingSystem`, `ISaveable`/`SaveableRegistry`/`SimCoreSaveable`, `UnitTypeRegistry`, `UnitStore`, `SimulationRunner`, `GameComposition`, `GameBootstrap`.

## 4. Ausgeführte Tests

| Datum | Was | Umgebung | Ergebnis |
|---|---|---|---|
| 2026-10-26 | Fundament 1b, EditMode-Testdateien `Assets/Tests/EditMode/*.cs` | dotnet 8 in der Cloud (`Tools/SimTests`), ohne Unity | 41/41 bestanden. Zwei Fehler im ersten Lauf gefunden und behoben: Events aus Handlern kamen im selben Flush an; Runner verlor durch float-Rundung einen Tick pro Sekunde |
| – | Unity Test Runner (EditMode) | PC des Nutzers | **nicht ausgeführt** |

## 5. Bekannte Probleme / Widersprüche

| # | Problem | Status |
|---|---|---|
| K1 | UI_STYLE_GUIDE A2 auf D13 korrigiert (1 Einheit = 1 m, 1 Kachel = 4 m, Modelle in Originalgröße). PHASE5_MILITARY_ASSETS.md prüfen, ob dort noch alte Werte stehen. | Phase 6 erledigt, Phase 5 offen |
| K2 | Stilrichtung nur Arbeitshypothese; Style Lock braucht Unity und Nutzerbestätigung | offen |
| K3 | Asset-Downloads aus der Cloud laut Phase 5 vom Proxy blockiert (403); Import realistisch nur auf dem PC | nicht selbst geprüft |
| K4 | Ob `Newtonsoft.Json.dll` aus `com.unity.nuget.newtonsoft-json` in `WorldBeyond.Application` automatisch referenziert wird, ist ungeprüft | prüfen beim ersten Unity-Import |

## 6. Fehlende Abhängigkeiten

1. **Unity-Version auf dem PC** (Unity Hub). Ohne sie keine `ProjectSettings/` und kein `manifest.json`.
2. **Gerätezugriff** (Remote Control), um Unity zu öffnen, zu kompilieren und den Unity Test Runner auszuführen.

## 7. Nächste Aufgabe

PR 1b reviewen und mergen; danach Unity-Projekt per Remote Control anlegen und EditMode-Tests in Unity ausführen. Parallel: Phasen 2, 3, 7 bauen auf `main` gegen das Fundament (M1).
