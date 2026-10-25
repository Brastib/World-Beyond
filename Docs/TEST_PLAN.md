# World Beyond – TEST_PLAN

Stand: 2026-10-25, Phase 1. **Kein Test wurde bisher ausgeführt** (es gibt noch keinen Code und kein Unity-Projekt).

## 1. Grundregeln

- Ergebnisse werden nur eingetragen, wenn der Test tatsächlich gelaufen ist: Datum, Unity-Version, wer/wo (z. B. "Remote Control auf PC des Nutzers"), Ergebnis, bei Fehlschlag Ausgabe.
- Statuswerte: `nicht ausgeführt` · `bestanden` · `fehlgeschlagen` · `blockiert (Grund)`.
- Claude-Cloud-Sitzungen können Unity nicht starten. Unity-Tests laufen nur auf dem PC des Nutzers (Remote Control oder manuell). Reine C#-Logik kann zusätzlich in der Cloud mit `dotnet test` geprüft werden, **sofern** die Simulation-Assembly ohne UnityEngine-Abhängigkeit kompiliert; das ersetzt nicht den Unity Test Runner.

## 2. Testarten

| Art | Werkzeug | Ort | Wofür |
|---|---|---|---|
| EditMode | Unity Test Framework (NUnit) | `Assets/Tests/EditMode/` | Simulation, Datenmodelle, Generator, Save-Roundtrip, KI-Entscheidungen |
| PlayMode | Unity Test Framework | `Assets/Tests/PlayMode/` | Szenen-Start, Bootstrap, Rendering-Anbindung, UI-Verbindung |
| Performance | eigene Benchmarks + Unity Profiler | `Assets/Tests/PlayMode/Performance/` | Zielwerte ARCHITECTURE §12 |
| Manuell | Checklisten unten | Editor / Build | Bedienung, Optik, Spielgefühl |
| Visuell | Screenshots im Spiel | `Docs/screenshots/` | Stilkonsistenz (PROJECT_MASTER §8.6) |

Kommandozeile (Unity, auf dem PC): `Unity.exe -batchmode -projectPath <Pfad> -runTests -testPlatform EditMode -testResults <Datei>.xml` (bzw. `PlayMode`). Ergebnis-XML wird ausgewertet und hier zusammengefasst.

## 3. Determinismus-Grundtest (für alle Phasen)

Gleicher Seed + gleiche Befehlsfolge → gleicher Zustand nach N Ticks. Prüfung über eine Zustands-Prüfsumme (`SimStateHash`), die jedes `ISaveable` beisteuert.
Speichern nach N Ticks, laden, M weitere Ticks → identische Prüfsumme wie bei N+M Ticks ohne Unterbrechung.

## 4. Tests je Phase

| ID | Phase | Test | Art | Status |
|---|---|---|---|---|
| T-1b-01 | 1b | `EntityId`: Generation macht alte IDs ungültig | EditMode | nicht ausgeführt |
| T-1b-02 | 1b | `SimRandom`: gleicher Seed = gleiche Folge; Zustand speicher-/wiederherstellbar | EditMode | nicht ausgeführt |
| T-1b-03 | 1b | EventBus liefert erst bei `Flush`, Reihenfolge stabil | EditMode | nicht ausgeführt |
| T-1b-04 | 1b | SimulationRunner: Ticks pro Sekunde bei 1x/2x/5x/10x, Pause = 0 Ticks | EditMode | nicht ausgeführt |
| T-1b-05 | 1b | Save/Load-Gerüst: Tick + Random-Zustand Roundtrip | EditMode | nicht ausgeführt |
| T-1b-06 | 1b | World-Szene startet ohne Fehler in der Konsole | PlayMode | nicht ausgeführt |
| T-2-01 | 2 | Gleicher Seed = identisches Raster (Hash), anderer Seed = anderes | EditMode | nicht ausgeführt |
| T-2-02 | 2 | Welt enthält Ebenen, Hügel, Berge, Flüsse, Seen, Küste, Wald (Mindestanteile) | EditMode | nicht ausgeführt |
| T-2-03 | 2 | Ressourcen sinnvoll verteilt (z. B. Erz nur in Fels/Berg, Holz nur im Wald) | EditMode | nicht ausgeführt |
| T-2-04 | 2 | Koordinaten-Umrechnung Welt ↔ Kachel inkl. Ränder | EditMode | nicht ausgeführt |
| T-2-05 | 2 | Pfadfindung: Pfad existiert/fehlt korrekt, Fahrzeuge meiden zu steiles Gelände | EditMode | nicht ausgeführt |
| T-2-06 | 2 | Generierung 256 × 256 < 3 s, Chunk-Rebuild < 5 ms | Performance | nicht ausgeführt |
| T-2-07 | 2 | Kamera: Zoom, Rotation, Pan, Grenzen | Manuell | nicht ausgeführt |
| T-2-08 | 2 | Auswahl/Markierung einer Kachel bzw. eines Objekts | PlayMode/Manuell | nicht ausgeführt |
| T-2-09 | 2 | Welt Save/Load identisch (Seed + Deltas) | EditMode | nicht ausgeführt |
| T-3-01 | 3 | ≥ 2 Zivilisationen wachsen 30 Spielminuten ohne Eingriff | EditMode | nicht ausgeführt |
| T-3-02 | 3 | Ressourcenverbrauch: Nahrungsmangel senkt Bevölkerung | EditMode | nicht ausgeführt |
| T-3-03 | 3 | Siedler gründen neue Siedlung an gültigem Ort | EditMode | nicht ausgeführt |
| T-3-04 | 3 | Territorium: Besitz nur über `ITerritoryService`, keine Überlappung | EditMode | nicht ausgeführt |
| T-3-05 | 3 | Save/Load mitten im Wachstum, Verlauf gleich (Determinismus) | EditMode | nicht ausgeführt |
| T-4-01 | 4 | Kriegserklärung setzt `War` beidseitig, ruft Verbündete | EditMode | nicht ausgeführt |
| T-4-02 | 4 | Friedensangebot abhängig von warScore; Abtretung wechselt Besitzer | EditMode | nicht ausgeführt |
| T-4-03 | 4 | Bündnis: Angriff auf Mitglied zieht Partner in den Krieg | EditMode | nicht ausgeführt |
| T-4-04 | 4 | Zielauswahl: wertvolleres Ziel bei gleicher Entfernung; Verteidigung bei Bedrohung | EditMode | nicht ausgeführt |
| T-4-05 | 4 | Kein Krieg ohne Grund (kein Grenzkontakt, neutrale Meinung) | EditMode | nicht ausgeführt |
| T-4-06 | 4 | Verluste erhöhen Erschöpfung, senken warScore | EditMode | nicht ausgeführt |
| T-4-07 | 4 | Laden während Krieg, Fortsetzung konsistent | EditMode | nicht ausgeführt |
| T-5-01 | 5 | Infanterie bewegt sich, erkennt Feind, feuert, verursacht Schaden | PlayMode | nicht ausgeführt |
| T-5-02 | 5 | Humvee fährt über Terrain, meidet Hindernisse | PlayMode | nicht ausgeführt |
| T-5-03 | 5 | Bewaffneter Humvee bekämpft Infanterie, Munition/Nachladen | PlayMode | nicht ausgeführt |
| T-5-04 | 5 | Panzer: Panzerung wirkt (Kleinwaffen fast wirkungslos), Kanone zerstört Fahrzeug | PlayMode | nicht ausgeführt |
| T-5-05 | 5 | Rakete: Flug, Ziel, Treffer, Explosion, Flächenschaden, Krater | PlayMode | nicht ausgeführt |
| T-5-06 | 5 | Zerstörte Einheit: alle Referenzen fallen sauber zurück (kein Fehler) | EditMode | nicht ausgeführt |
| T-5-07 | 5 | 2 × 200 Einheiten kämpfen ≥ 30 FPS, keine GC-Spitzen pro Schuss | Performance | nicht ausgeführt |
| T-6-01 | 6 | Jede sichtbare Schaltfläche löst eine echte Aktion aus (Liste aller Buttons) | Manuell/PlayMode | nicht ausgeführt |
| T-6-02 | 6 | Auflösungen 1280×720, 1920×1080, 2560×1440, 3840×2160; UI-Skalierung 75–150 % | Manuell | nicht ausgeführt |
| T-6-03 | 6 | Auswahl Klick/Rechteck/Gruppe, Befehle, Wegpunkte | Manuell | nicht ausgeführt |
| T-6-04 | 6 | Menüs, Tooltips, Eingabefelder, Bestätigungsdialoge | Manuell | nicht ausgeführt |
| T-6-05 | 6 | Stilprüfung: Screenshot nah/mittel/fern, keine unpassenden Assets | Visuell | nicht ausgeführt |
| T-7-01 | 7 | Roundtrip aller Sektionen, Prüfsumme gleich | EditMode | nicht ausgeführt |
| T-7-02 | 7 | Beschädigte Datei → `Corrupted`, verständliche Meldung, alter Zustand bleibt | EditMode | nicht ausgeführt |
| T-7-03 | 7 | Neuere `formatVersion` → `TooNew` | EditMode | nicht ausgeführt |
| T-7-04 | 7 | Absturz während Speichern simuliert → `.bak` intakt | EditMode | nicht ausgeführt |
| T-7-05 | 7 | Laden nach Neustart der Anwendung (Build) | Manuell | nicht ausgeführt |
| T-7-06 | 7 | Großwelt 512 × 512, 8 Zivilisationen, 2 000+ Einheiten: FPS, Tick-Zeit, Speicher, GC | Performance | nicht ausgeführt |
| T-7-07 | 7 | Langlauf 2 Spielstunden bei 10x ohne Fehler/Inkonsistenz | PlayMode | nicht ausgeführt |
| T-7-08 | 1b/7 | `EntityStore`: Generationen, Lebend-Markierung und Freiliste speichern → wiederherstellen → identisch; nächstes `Create()` liefert dieselbe ID wie ohne Unterbrechung | EditMode | nicht ausgeführt |
| T-7-09 | 1b/7 | Veraltete ID (alte Generation) und ungültige ID (Index 0, außerhalb, negativ) → `Exists` false, `Destroy` false, keine Exception; auch nach Laden | EditMode | nicht ausgeführt |
| T-7-10 | 1b/7 | Widersprüchlicher `EntityStoreState` (Längen ungleich, Index doppelt in Freiliste, lebender Index in Freiliste) → `TryRestoreState` false → Laden `Corrupted` | EditMode | nicht ausgeführt |
| T-7-11 | 1b/7 | `SequentialIdAllocator`: IDs nach Zerstörung und nach Speichern/Laden nie wiederverwendet (Civ, Siedlung, Armee, Krieg, Vertrag) | EditMode | nicht ausgeführt |
| T-7-12 | 7 | Inhaltstabellen umsortiert / neuer Typ dazwischen eingefügt zwischen Speichern und Laden → alle Einheiten, Gebäude, Ressourcen behalten ihren Typ | EditMode | nicht ausgeführt |
| T-7-13 | 7 | Fehlender Inhaltsschlüssel (z. B. `"unit.removed"`) → `MissingContent`, Meldung nennt den Schlüssel, alter Zustand bleibt | EditMode | nicht ausgeführt |
| T-7-14 | 7 | Ungültige Querverweise (Auftrag auf tote Einheit, Armee mit gelöschter Einheit, Siedlung einer nicht existierenden Civ) → definierter Rückfall + Warnung im `LoadReport`; harte Widersprüche → `InvalidReferences` | EditMode | nicht ausgeführt |
| T-7-15 | 7 | Migration: Testdatei mit Sektion v1 → aktuell; Ergebnis gleich einem frisch gespeicherten Stand; Quelldatei byte-identisch unverändert | EditMode | nicht ausgeführt |
| T-7-16 | 7 | Lücke in der Migrationskette → `Unsupported`; Version 0, negativ oder fehlend → `Corrupted`; nie stillschweigend als aktuelle Version geladen | EditMode | nicht ausgeführt |
| T-7-17 | 7 | Beschädigte Dateien: abgeschnitten, leer, kein JSON, falsches `format`, Prüfsumme falsch, Pflichtsektion fehlt → jeweils kontrollierter Fehler mit verständlicher Meldung | EditMode | nicht ausgeführt |
| T-7-18 | 7 | Migrierten Stand speichern → `<slot>.v<alt>.wbsave` existiert und ist unverändert | EditMode | nicht ausgeführt |
| T-7-19 | 7 | Determinismus nach Laden: N Ticks → speichern → laden → M Ticks ergibt dieselbe `SimStateHash` wie N+M Ticks ohne Unterbrechung | EditMode | nicht ausgeführt |
| T-7-20 | 7 | Neustart: speichern, Anwendung beenden, neu starten, laden → Zustand gleich (Editor-Variante von T-7-05) | PlayMode/Manuell | nicht ausgeführt |

## 5. Platzhalter-Prüfung (vor Phase 8)

Alle Einträge im Platzhalter-Register (`Assets/ThirdParty/ASSET_CREDITS.md` §3) prüfen: ersetzt / begründet behalten / offen. Status: nicht ausgeführt.

## 6. Endabnahme (Phase 8) – durchgängiger Spieltest

Vom Nutzer vorgegebener Ablauf. Ein Schritt gilt nur als bestanden, wenn er tatsächlich ausgeführt wurde. Schlägt ein Schritt fehl, wird die Ursache behoben, bevor er als bestanden markiert wird.

| # | Schritt | Status |
|---|---|---|
| E-01 | Spiel starten und neues Spiel erstellen | nicht ausgeführt |
| E-02 | Eine Welt mit reproduzierbarem Seed generieren | nicht ausgeführt |
| E-03 | Mindestens zwei Zivilisationen entstehen lassen | nicht ausgeführt |
| E-04 | Bevölkerung, Ressourcen und Siedlungen wachsen lassen | nicht ausgeführt |
| E-05 | Diplomatische Beziehungen und territoriale Konkurrenz entwickeln | nicht ausgeführt |
| E-06 | Einen Krieg auslösen oder einen automatisch entstandenen Konflikt beobachten | nicht ausgeführt |
| E-07 | Militärische Einheiten produzieren und rekrutieren | nicht ausgeführt |
| E-08 | Infanterie, Humvees, bewaffnete Humvees und Panzer bewegen | nicht ausgeführt |
| E-09 | Raketen einsetzen und Treffer, Explosionen sowie Schaden prüfen | nicht ausgeführt |
| E-10 | Einheiten auswählen und Befehle erteilen | nicht ausgeführt |
| E-11 | Den Spielstand speichern | nicht ausgeführt |
| E-12 | Das Spiel schließen und neu starten | nicht ausgeführt |
| E-13 | Den Spielstand laden | nicht ausgeführt |
| E-14 | Prüfen, ob Welt, Zivilisationen, Konflikte und Einheiten korrekt wiederhergestellt wurden | nicht ausgeführt |
| E-15 | Die Simulation fortsetzen und auf Fehler oder inkonsistente Zustände prüfen | nicht ausgeführt |

Zusätzlich in Phase 8: Windows-Build tatsächlich erstellen und starten (Status: nicht ausgeführt). Ist Unity nicht erreichbar, wird die konkrete Blockade hier eingetragen.

## 7. Protokoll ausgeführter Testläufe

| Datum | Phase | Umgebung (Unity-Version, Gerät) | Tests | Ergebnis | Notizen |
|---|---|---|---|---|---|
| – | – | – | – | – | Noch keine Testläufe |
