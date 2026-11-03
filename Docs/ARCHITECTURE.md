# World Beyond – ARCHITECTURE

Stand: 2026-10-25, Phase 0. Noch kein Code vorhanden. Alles hier ist geplante Architektur ([E] = Entscheidung von Claude, siehe PROJECT_MASTER §4).

## 1. Leitprinzipien

1. **Simulation ≠ Darstellung.** Die Spielwelt wird in reinem C# simuliert (`WorldBeyond.Simulation`), ohne `MonoBehaviour`, ohne `UnityEngine.Object`-Referenzen im Zustand. Unity-Komponenten (`WorldBeyond.Presentation`) lesen den Zustand und zeichnen ihn.
2. **Fester Simulations-Tick.** Simulation läuft in festen Schritten (Basis: 10 Ticks pro Spielsekunde bei 1x). Spielgeschwindigkeit (Pause, 1x, 2x, 5x, 10x) ändert nur, wie viele Ticks pro Frame laufen. Darstellung interpoliert.
3. **IDs statt Objektreferenzen.** Alle Weltobjekte (Einheiten, Gebäude, Siedlungen, Zivilisationen, Geschosse) haben stabile Integer-IDs. Referenzen zwischen Systemen laufen über IDs. Das macht Speichern/Laden und Zerstörung sicher.
4. **Daten in Arrays, Systeme als Verarbeiter.** Zustand liegt in Stores (z. B. `UnitStore`) mit dichten Arrays; Systeme (z. B. `MovementSystem`) verarbeiten ihn pro Tick. Keine God-Klasse, aber auch kein volles ECS-Framework (kein Unity Entities Paket, um Komplexität und Abhängigkeiten gering zu halten).
5. **Kommunikation über Events und Schnittstellen.** Systeme rufen sich nicht kreuz und quer auf; sie kommunizieren über `ISimEventBus` und die in SYSTEM_INTERFACES definierten Interfaces.
6. **Eine Composition Root.** `GameBootstrap` (einziges zentrales MonoBehaviour) erzeugt alle Systeme in fester Reihenfolge und verbindet sie. Keine verstreuten Singletons.
7. **Alles mit Zustand ist speicherbar.** Jedes Store/System mit Zustand implementiert `ISaveable`.
8. **Deterministisch, soweit sinnvoll.** Ein zentraler, gespeicherter Zufallsgenerator (`SimRandom`, Seed-basiert) statt `UnityEngine.Random` in der Simulation.

## 2. Schichten

```
┌──────────────────────────────────────────────────────────────┐
│ Presentation (MonoBehaviours, Unity)                         │
│  Kamera · TerrainChunkRenderer · UnitInstancedRenderer ·      │
│  BuildingRenderer · VFX · Audio · UI Toolkit (HUD, Panels)    │
├──────────────────────────────────────────────────────────────┤
│ Application / Control                                         │
│  GameBootstrap · SimulationRunner (Tick-Schleife) ·           │
│  InputRouter · SelectionService · CommandDispatcher ·          │
│  GodToolController · SaveLoadService                          │
├──────────────────────────────────────────────────────────────┤
│ Simulation (reines C#, testbar in EditMode)                   │
│  World(Grid) · Units · Civilizations · Settlements · Economy ·│
│  Tech · Diplomacy · Military · Combat · Projectiles · AI ·     │
│  Events · SimRandom · SimClock                                 │
├──────────────────────────────────────────────────────────────┤
│ Core (reines C#)                                              │
│  IDs · EventBus · SpatialHash · Pathfinding · Math-Helpers ·   │
│  Serialization-DTOs                                            │
└──────────────────────────────────────────────────────────────┘
```

Abhängigkeitsregel: nur nach unten. Simulation kennt Presentation nicht. Durchgesetzt über Assembly Definitions:

| Assembly (.asmdef) | Darf referenzieren |
|---|---|
| `WorldBeyond.Core` | nur `Unity.Mathematics` (optional), sonst nichts |
| `WorldBeyond.Simulation` | Core |
| `WorldBeyond.Application` | Core, Simulation, UnityEngine, Input System, Newtonsoft |
| `WorldBeyond.Presentation` | Core, Simulation, Application, UnityEngine, URP, UI Toolkit |
| `WorldBeyond.Tests.EditMode` | Core, Simulation, Application |
| `WorldBeyond.Tests.PlayMode` | alle |

Hinweis: `UnityEngine.Vector3` usw. darf in Simulation genutzt werden, sofern es nur Werttypen sind (`UnityEngine.CoreModule` ist in EditMode-Tests verfügbar). Keine `GameObject`/`Transform`/`MonoBehaviour` in Simulation.

## 3. Weltmodell

- **Maßstab (PROJECT_MASTER D13):** 1 Unity-Einheit = 1 m. Eine Kachel = `WorldConstants.TileSize` = 4 m. Damit passen echte Modelle ohne Umskalieren zusammen.
- **Raster:** Welt = `W × H` Kacheln (Startgröße 256 × 256 = 1 024 m, Ziel bis 512 × 512 = 2 048 m). Waffenreichweiten werden spielerisch verkürzt (Daten in `WeaponData`), nicht die Modelle verkleinert.
- **Pro Kachel** (struct `TileData`, in einem Array): Höhe (float), Biom (byte), Geländetyp (byte: Wasser tief/flach, Strand, Gras, Wald, Fels, Schnee, Wüste …), Ressourcenvorkommen (Typ + Menge), Besitzer-Zivilisation (int, 0 = keine), Gebäude-ID (int, 0 = keines), Flags (brennt, verwüstet, Straße).
- **Generierung:** Seed-basiertes Rauschen (eigene Perlin/Simplex-Implementierung oder `Mathf.PerlinNoise` in Application-Schicht übergeben; Entscheidung in Phase 2 nach Tests), Höhen → Gewässer → Biome (Höhe + Feuchte + Temperatur) → Wälder → Ressourcen.
- **Darstellung:** Terrain als eigene Chunk-Meshes (z. B. 32 × 32 Kacheln pro Chunk, `Mesh` mit Vertex-Farben + Atlas-Textur), nicht Unity `Terrain`, weil God-Tools häufig einzelne Kacheln ändern und das Raster exakt sichtbar sein soll. Wasser als eigene Ebene mit einfachem URP-Shader-Graph-Wassermaterial. Nur geänderte Chunks werden neu gebaut.
- **Bäume/Felsen:** keine einzelnen GameObjects, sondern Instancing pro Chunk.

## 4. Einheiten und Bewegung

- `UnitStore`: dichte Arrays (Position, Ziel, Typ, Zivilisation, HP, Zustand, Auftrag). Freie IDs werden über eine Generations-ID wiederverwendet (`EntityId = index + generation`), damit alte Referenzen erkennbar ungültig werden.
- `UnitTypeDefinition` als ScriptableObject in Presentation/Application, in Simulation als reine Daten-Kopie (`UnitTypeData`) geladen. Enthält Geschwindigkeit, HP, Panzerung, Waffen, Kosten, Rolle (Zivilist, Arbeiter, Infanterie, Fahrzeug …).
- **Pfadfindung:** A* auf dem Kachelraster mit Gewichten (Gelände, Straßen) für einzelne Einheiten; für Armeen/Gruppen Flow Fields (Phase 10+). Pfad-Anfragen werden pro Tick begrenzt (Budget), Ergebnis-Cache.
- **Räumliche Abfragen:** `SpatialHash` (Zellen 8 × 8 Kacheln) für "Feinde in Reichweite", Auswahl, Kollision. Keine `FindObjectsOfType` oder O(n²)-Suchen.
- **Darstellung:** `Graphics.RenderMeshInstanced` pro Einheitentyp/LOD; Animation zunächst prozedural (Wippen, Rotationsausrichtung), später Vertex-Animation oder wenige animierte Modelle in Nahsicht.

## 5. Zivilisationen und Wirtschaft

- `Civilization`: ID, Name, Farbe, Kultur/Spezies, Ressourcenlager, Technologien, Haltungen (aggressiv/friedlich …), Hauptstadt-ID.
- `Settlement`: ID, Zivilisation, Zentrum, Bevölkerung, Gebäude-IDs, Stufe (Dorf → Stadt → Großstadt), Einflussradius.
- **Territorium:** Besitz pro Kachel, berechnet aus Einflussquellen der Siedlungen; Neuberechnung gebündelt (nicht jeden Tick).
- **Wirtschaft:** Ressourcen Nahrung, Holz, Stein, Erz, Öl, Gold (erweiterbar). Arbeiter sammeln → Lager → Verbrauch (Bevölkerung) → Produktion (Gebäude, Einheiten).
- **Technologie:** Tech-Baum als Daten (ScriptableObject/JSON). Epochen von Frühzeit bis Moderne, damit moderne Militärtechnik (Humvees, Panzer, Raketen) erst nach Entwicklung verfügbar ist. Der God-Modus kann Epochen direkt setzen.

## 6. Diplomatie

- Beziehungsmatrix `relation[civA, civB]` (−100 … +100), Status (Frieden, Spannung, Bündnis, Handel, Krieg).
- Einflussfaktoren: Grenzkontakt, Ressourcenneid, Kultur, vergangene Kriege, Handel, Spieler-Eingriffe.
- Entscheidungen durch Zivilisations-KI; Spieler kann im God-Modus Beziehungen erzwingen (Krieg erklären, Frieden schließen).

## 7. Militär und Kampf

- **Verbände:** `ArmyGroup` (Liste Einheiten-IDs, Ziel, Haltung).
- **Waffen:** `WeaponData` (Reichweite, Feuerrate, Schaden, Schadensart, Geschosstyp: Hitscan, ballistisch, Rakete, Artillerie).
- **Geschosse:** eigener `ProjectileStore` (Arrays, Pooling), Bewegung pro Tick, Treffererkennung über SpatialHash + einfache Kugel/Kapsel-Tests gegen Einheiten und Kachel-Höhen. Keine Unity-Physik für tausende Geschosse.
- **Schaden:** HP, Panzerung pro Schadensart, Flächenschaden mit Abfall, Gebäudeschaden, Gelände-Effekte (Krater = Kachel-Flag/Höhenänderung, Brand).
- **Darstellung:** Mündungsfeuer, Leuchtspur, Explosionen über gepoolte Partikelsysteme (Unity Particle System; kein VFX Graph, um URP-Kompatibilität einfach zu halten).

## 8. KI

Drei Ebenen, alle in Simulation, zeitlich gestaffelt (nicht jedes System jeden Tick):

| Ebene | Frequenz | Aufgabe |
|---|---|---|
| Strategisch (Zivilisation) | ~ alle 5 Spielsekunden | Expansion, Wirtschaftsprioritäten, Forschung, Diplomatie, Kriegsziele |
| Operativ (Armee) | ~ jede Spielsekunde | Verbände bilden, Ziele wählen, Rückzug |
| Taktisch (Einheit) | jeden Tick, günstig | Ziel wählen, feuern, Deckung/Abstand, Fahrzeug-Verhalten |

Umsetzung über Utility-Bewertung (Punkte pro Option) statt großer Behavior-Tree-Frameworks.

## 9. God-Modus

`GodToolController` (Application) übersetzt Eingaben in `IWorldCommand`s (z. B. `RaiseTerrainCommand`, `SpawnUnitCommand`, `IgniteCommand`, `ExplosionCommand`), die an der Tick-Grenze in die Simulation eingespeist werden. Dadurch: gleicher Weg für Spieler, KI und spätere Wiederholung/Undo.

## 10. UI

- UI Toolkit: ein `UIDocument` pro Hauptbereich (HUD, Seitenpanel, Dialoge, Hauptmenü), gemeinsame USS-Stylesheets mit Design-Tokens (Farben, Abstände, Schriftgrößen) in `Assets/UI/Styles/Tokens.uss`.
- UI liest nur über Read-Interfaces (`IWorldQuery`, `ICivilizationQuery` …) und sendet nur Commands. Keine Spiellogik in UI-Code.
- Minimap: eine aus dem Raster erzeugte `Texture2D`, gebündelt aktualisiert.

## 11. Speichern / Laden (verbindlich, Nutzervorgabe 2026-10-25)

Status: **Architekturvorgabe, nicht implementiert, nicht getestet.** Gilt ab Phase 1 für alle Systeme (PROJECT_MASTER D14, Nutzerauftrag "Verbindliche Umsetzung: Versioniertes Speicherformat und stabile IDs"). Phase 7 vervollständigt und optimiert das Speichersystem, baut aber keine Datenmodelle oder Referenzsysteme früherer Phasen um. Abweichungen werden **vor** der Implementierung hier und in SYSTEM_INTERFACES dokumentiert. Herkunft: `Docs/proposals/SAVE_FORMAT_PROPOSAL.md`.

### 11.1 Identitätsregeln (sechs Pflichtregeln)

1. **Einheiten, Gebäude, Geschosse:** `EntityId` (Index + Generation), verwaltet von einem `EntityStore`. Generations-Tabelle, Lebend-Markierung und Freiliste werden gespeichert und exakt wiederhergestellt, damit alte IDs auch nach dem Laden als ungültig erkannt werden. Index 0 ist reserviert (= ungültig).
2. **Zivilisationen, Siedlungen, Armeen, Kriege, Verträge:** `int`-IDs aus einem `SequentialIdAllocator`, **nie wiederverwendet**, Zählerstand gespeichert, 0 = keine/ungültig.
3. **Inhaltstypen** (Einheit, Gebäude, Ressource, Technologie, Biom, Geländetyp): im Spielstand als stabiler String-Schlüssel (`"unit.humvee_mg"`, `"res.iron"`), zur Laufzeit als `int` über die `ContentRegistry`. Laufzeit-Integer stehen nie im Spielstand.
4. **Enums** mit expliziten Zahlenwerten; neue Werte nur anhängen, nie umsortieren oder umnummerieren.
5. **DTOs** enthalten keine `UnityEngine.Object`, `GameObject`, `Transform`, Delegates, Events, Interfaces oder Klassenreferenzen auf Laufzeitobjekte. Querverweise nur über IDs.
6. **Jeder ID-Zugriff** prüft `Exists(id)`. Ungültig → definierter Rückfall, keine Exception: Auftrag → `Idle`, Angriffsziel → neue Zielwahl, Armee ohne Einheiten → aufgelöst, Siedlungsverweis → entfernt. Der Rückfall wird im System dokumentiert.

Zusätzlich: Aufträge sind Daten (Typ + Ziel-ID/Position), keine Coroutines oder Lambdas. Speichern nur an der Tick-Grenze. Nicht gespeichert, sondern neu aufgebaut: Pfadanfragen und -caches, SpatialHash, Chunk-Meshes, Pools, Partikel, Audio, UI-Zustand.

### 11.2 Bausteine (Signaturen in SYSTEM_INTERFACES §1 und §11)

| Baustein | Aufgabe | Besitzer |
|---|---|---|
| `EntityId`, `EntityStore` | Laufzeit-IDs, Generationen, Freiliste, `Exists` | Integration (Fundament) |
| `SequentialIdAllocator` | nie wiederverwendete fachliche IDs | Integration (Fundament) |
| `ContentRegistry` | String-Schlüssel ↔ Laufzeit-int je Inhaltsart | Integration (Fundament); Fachsysteme registrieren ihre Inhalte |
| `ISaveable` | Vertrag für speicherbare Systemdaten (`SectionVersion`, INTEGRATION §5) | Integration (Definition), jedes System (Implementierung) |
| `SaveGameData` | versioniertes Wurzel-Datenmodell | Save (Phase 7) |
| `SaveGameSerializer` | JSON ↔ `SaveGameData`, Prüfsumme | Save |
| `SaveGameMigrator` | Migrationskette je Sektion und für den Rahmen | Save; Migrationsschritte schreibt das jeweilige Fachsystem |
| `SaveLoadService` | Slots, atomares Schreiben, Ladeablauf, Fehlermeldungen | Save |

### 11.3 Dateiformat

JSON (Newtonsoft), Datei `<slot>.wbsave` in `Application.persistentDataPath/Saves/`. Rahmen: `format = "WorldBeyondSave"`, `formatVersion` (int), `gameVersion` (nur Info), `createdUtc`, `header` (Name, Seed, Weltgröße, Tick, Anzahl Civs/Einheiten), `settings` (Simulationsgeschwindigkeit, Pause, relevante Einstellungen), `start` (Start-Einstellungen: Seed, Weltgröße, Civ-Anzahl; damit baut der Ladevorgang die leere Simulation neu auf), `sections` (Schlüssel = `ISaveable.SaveKey`, je `{ "v": SectionVersion, "data": … }`), `checksum` (SHA-256 über die ganze kanonisch serialisierte Datei ohne das Feld `checksum`, also header, start, settings, sections; Phase 7, 4ea38bc). Terrain: Seed + geänderte Kacheln, ab ca. 30 % Änderungen komplettes Raster (`fullGrid`), siehe World-Entwurf §9.

### 11.4 Speichern

1. An der Tick-Grenze: CommandQueue und EventBus geleert.
2. Jedes `ISaveable.CaptureState()` → DTO; Inhaltstypen über `ContentRegistry` in Schlüssel umgesetzt.
3. `SaveGameSerializer` erzeugt JSON inkl. Prüfsumme.
4. Atomar: `<slot>.wbsave.tmp` schreiben → zurücklesen und Prüfsumme prüfen → bestehende Datei nach `<slot>.wbsave.bak` → `.tmp` umbenennen. Ein Abbruch in jedem Schritt lässt mindestens eine gültige Datei zurück.

### 11.5 Laden (Reihenfolge verbindlich)

1. **Datei und Metadaten validieren:** lesbar, JSON gültig, `format` korrekt, Pflichtfelder vorhanden, Prüfsumme stimmt → sonst `Corrupted` (bzw. `NotFound`, `IoError`).
   *Präzisierung (Phase 7, 2026-10-26):* `TooNew` wird **vor** der Prüfsummenprüfung gemeldet, weil eine neuere Formatversion die Prüfsumme anders bilden könnte.
2. **Formatversion feststellen:** `formatVersion` und jede Sektionsversion. Neuer als unterstützt → `TooNew`. Älter, aber keine lückenlose Migrationskette → `Unsupported`. Eine nicht unterstützte Version wird **nie** als aktuelle Version interpretiert.
3. **Migrieren:** `SaveGameMigrator` arbeitet auf einer Kopie im Speicher, Schritt für Schritt (v → v+1), Rahmen zuerst, dann Sektionen. Die Rahmen-Migration (Schlüssel `"$frame"`) läuft zwischen `SaveGameSerializer.TryParseFrame` und `TryMap`.
4. **Inhaltsschlüssel und Referenzen validieren:** Systeme mit Inhaltsschlüsseln implementieren `ISaveContentCheck` und liefern ihre Schlüssel vor dem Einspielen; alle Schlüssel in der `ContentRegistry` vorhanden → sonst `MissingContent` mit Liste der Schlüssel. Pflichtsektionen vorhanden → sonst `Corrupted`. Querverweise werden nach dem Einspielen geprüft (11.6).
5. **In eine neue, leere Simulationsinstanz einspielen** (`LoadOrder` aufsteigend), danach Referenzprüfung. Harte Inkonsistenz (z. B. doppelte IDs, Generationstabelle widersprüchlich) → `Corrupted`. Weiche Inkonsistenz (Verweis auf nicht existierende Einheit) → Rückfall nach Regel 6 + Warnung im Ladebericht.
6. **Erst nach erfolgreicher Validierung** wird die neue Instanz übernommen und die Presentation neu aufgebaut. Bei jedem Fehler bleibt der bisherige Spielzustand unverändert.

Die Quelldatei wird beim Laden nie verändert. Ein migrierter Spielstand wird erst beim nächsten Speichern geschrieben; vor dem ersten Überschreiben einer älteren Version wird die Originaldatei als `<slot>.v<alteVersion>.wbsave` aufbewahrt.

### 11.6 Referenzprüfung

Systeme mit Querverweisen implementieren zusätzlich zur `ISaveable`-Implementierung die optionale Prüfung `ISaveReferenceCheck` (SYSTEM_INTERFACES §11). Der `SaveLoadService` ruft sie nach dem Einspielen aller Sektionen auf und sammelt Ergebnisse im `LoadReport`.

### 11.7 Fehlermeldungen

`SaveLoadError`: `None = 0, NotFound = 1, IoError = 2, Corrupted = 3, TooNew = 4, Unsupported = 5, MissingContent = 6, InvalidReferences = 7, CaptureFailed = 8` (CaptureFailed: ein `CaptureState` warf beim Speichern; nichts wird geschrieben). Jede Art hat eine deutsche Meldung für den Spieler (z. B. "Dieser Spielstand wurde mit einer neueren Version von World Beyond erstellt und kann nicht geladen werden.") und technische Details im Log.

## 1a. Zuständigkeiten

Wer welche Klasse besitzt, steht im Zuständigkeitsregister `Docs/INTEGRATION.md` §3. Nur der Besitzer legt eine Klasse an oder ändert ihre Signatur.

## 12. Performance-Budget (Zielwerte, noch nicht gemessen)

| Größe | Ziel |
|---|---|
| Welt | 256 × 256 flüssig, 512 × 512 spielbar |
| Einheiten | 2 000 bei ≥ 60 FPS, 5 000 bei ≥ 30 FPS (Mittelklasse-PC) |
| Geschosse | 1 000 gleichzeitig |
| Sim-Tick | ≤ 8 ms bei 1x |

Werkzeuge: Unity Profiler, eigene Tick-Zeitmessung im Debug-Overlay. Burst/Jobs erst einsetzen, wenn Messungen es verlangen (Phase 15).

## 13. Ordnerstruktur (geplant)

```
WorldBeyond/                      (Unity-Projektwurzel)
├─ Docs/                          (diese Dokumente)
├─ Assets/
│  ├─ _Project/
│  │  ├─ Scripts/
│  │  │  ├─ Core/                 WorldBeyond.Core.asmdef
│  │  │  ├─ Simulation/           WorldBeyond.Simulation.asmdef
│  │  │  │   ├─ World/ Units/ Civilizations/ Economy/ Tech/
│  │  │  │   ├─ Diplomacy/ Military/ Combat/ AI/ Events/
│  │  │  ├─ Application/          WorldBeyond.Application.asmdef
│  │  │  └─ Presentation/         WorldBeyond.Presentation.asmdef
│  │  ├─ Data/                    ScriptableObjects (Einheiten, Gebäude, Techs, Waffen)
│  │  ├─ Materials/ Shaders/ Prefabs/ Scenes/
│  │  └─ Audio/
│  ├─ UI/                         UXML, USS, Icons, Fonts
│  ├─ ThirdParty/                 importierte fremde Assets + ASSET_CREDITS.md
│  └─ Tests/
│     ├─ EditMode/                WorldBeyond.Tests.EditMode.asmdef
│     └─ PlayMode/                WorldBeyond.Tests.PlayMode.asmdef
├─ Packages/manifest.json
└─ ProjectSettings/
```

## 14. Szenen

- `Boot.unity`: lädt Einstellungen, wechselt ins Hauptmenü.
- `MainMenu.unity`: Neue Welt (Seed, Größe), Laden, Einstellungen, Beenden.
- `World.unity`: enthält `GameBootstrap`, Kamera, Licht, UI-Dokumente.

## 15. Geplante Unity-Pakete

Nur offizielle Unity-Pakete. Versionen werden erst nach Prüfung der installierten Unity-Version festgelegt.

| Paket | Zweck | Status |
|---|---|---|
| `com.unity.render-pipelines.universal` | URP | geplant |
| `com.unity.inputsystem` | Eingabe | geplant |
| `com.unity.ugui` (enthält TextMeshPro in Unity 6) | nur falls nötig (World-Space-Labels) | optional |
| `com.unity.nuget.newtonsoft-json` | Speichern | geplant |
| `com.unity.test-framework` | Tests | geplant (meist vorinstalliert) |
| `com.unity.mathematics`, `com.unity.burst`, `com.unity.collections` | nur bei Bedarf in Phase 15 | aufgeschoben |
| `com.unity.shadergraph` | Wasser/Terrain-Shader (kommt mit URP) | geplant |

## 16. Asset-Pipeline (verbindlich, siehe PROJECT_MASTER §8)

| Punkt | Regel |
|---|---|
| Ablage | Unveränderte Downloads unter `Assets/ThirdParty/<Quelle>/<Paketname>/` inkl. Lizenzdatei; eigene Anpassungen (Materialien, Prefabs) unter `Assets/_Project/` |
| Formate | Modelle FBX oder glTF (glTF nur, wenn ein offizielles bzw. stabiles Importer-Paket für die installierte Unity-Version geprüft wurde; sonst vorher in Blender nach FBX konvertieren), Texturen PNG/JPG/TGA |
| Maßstab | 1 Einheit = 1 m; Import-Skalierung so einstellen, dass Soldat ≈ 1,8 m, Humvee ≈ 4,6 m lang, Kampfpanzer ≈ 9 m lang (inkl. Rohr) |
| Pivot / Ausrichtung | Pivot unten Mitte, Vorderseite +Z, Y oben; sonst über Eltern-Objekt im Prefab korrigieren |
| Materialien | URP Lit (oder ein gemeinsamer eigener Shader Graph); Standard-/Built-in-Materialien werden konvertiert |
| Texturen | Max. 2K; Einheiten/Fahrzeuge 1K; Kompression an; Mipmaps an |
| Polygone (Richtwerte) | Soldat ≤ 3k Tris, Fahrzeug ≤ 10k, Gebäude ≤ 8k, Baum ≤ 2k; zusätzlich LOD1/LOD2 oder Impostor für Fern-Zoom |
| Instancing | Modelle, die über `Graphics.RenderMeshInstanced` gezeichnet werden, brauchen genau ein Mesh + ein Material pro LOD (ggf. Submeshes zusammenführen, Texturatlas) und ein Material mit aktiviertem GPU Instancing |
| Animation | Infanterie: Humanoid-Rig mit Lauf/Idle/Schießen/Sterben in Nahsicht; Fahrzeuge: prozedural (Räder/Ketten drehen, Turm/Rohr ausrichten über benannte Child-Transforms bzw. Shader) |
| Collider | Keine Mesh-Collider für Einheiten (Treffer laufen über die Simulation); Collider nur für Mausauswahl, falls nötig |
| Dokumentation | Jedes Asset sofort in `Assets/ThirdParty/ASSET_CREDITS.md` eintragen (Quelle, Link, Lizenz, Autor, Speicherort, Anpassungen) |
| Platzhalter | nur mit Eintrag im Platzhalter-Register (ASSET_CREDITS §3) |
