# World Beyond – PROJECT_MASTER

Status: Phase 1 (Dokumentation), kein Code. Stand: 2026-10-25.
Dieses Dokument ist die oberste Referenz. Bei Widerspruch gilt: Aussage des Nutzers > PROJECT_MASTER > ARCHITECTURE = INTEGRATION (Zuständigkeiten) > SYSTEM_INTERFACES > IMPLEMENTATION_PLAN.

## 1. Vision

> Langfristige Vision, Ausbaustufen M1–M7 und die Architekturentscheidungen dazu: **`Docs/VISION.md`** (Nutzeranweisung 2026-10-26, hat Vorrang vor älteren Aussagen hier).

World Beyond ist ein eigenständiger God-Simulator und eine Echtzeit-Weltsimulation für Windows-PC, inspiriert vom Sandbox-Prinzip von WorldBox, erweitert um echte Zivilisationen, moderne Militärtechnik, dynamische Kriege und strategische Mechaniken. Der Spieler erschafft und verändert eine Welt, beobachtet Zivilisationen und beeinflusst ihre Geschichte. Die Welt lebt auch ohne Eingriffe des Spielers weiter.

Kein Konzept-Mockup, keine Fake-Buttons, keine isolierten Systeme. Ziel ist ein integriertes, spielbares Spiel.

## 2. Feste Rahmenbedingungen (vom Nutzer vorgegeben)

| Punkt | Vorgabe |
|---|---|
| Engine | Unity |
| Sprache | C# |
| Plattform | Windows-PC |
| Modus | Einzelspieler |
| Rendering | stabiles System, bevorzugt URP |
| Pakete | keine unnötigen oder experimentellen Pakete, keine kostenpflichtigen Assets |
| Assets | CC0 / kompatible Open-Source-Lizenzen, dokumentiert in `Assets/ThirdParty/ASSET_CREDITS.md`; fehlende Assets durch echte Unity-Meshes/Materialien ersetzen |
| Prozess | ein Chat = eine Hauptaufgabe; diese Dokumente sind die gemeinsame Wahrheit |

## 3. Zentrale Spielsysteme (Zielumfang)

1. Prozedural generierte, erkundbare 3D-Welt: Terrain, Wasser, Wälder, Berge, Ressourcen.
2. Zivilisationen: Bevölkerung, Siedlungen, Städte, Territorien, Ressourcen, Technologien, Entwicklung.
3. Autonome Einheiten: bewegen, arbeiten, siedeln, reagieren auf Ereignisse.
4. Diplomatie: Beziehungen, Bündnisse, Handel, Spannungen, Kriegserklärungen.
5. Militär: Infanterie, Humvee, Humvee mit MG, weitere bewaffnete Fahrzeuge, Panzer, Artillerie, Raketenfahrzeuge.
6. Geschosse, Raketen, Explosionen, Treffererkennung, Schaden, Zerstörung.
7. KI: Zivilisation (Strategie), Wirtschaft, Militär (operativ/taktisch).
8. God-Modus: erschaffen, verändern, zerstören.
9. Vollständige UI: Auswahl, Infos, Befehle, Minimap, Spielgeschwindigkeit.
10. Speichern und Laden.
11. Audio, visuelles Feedback, Animationen, Ereignismeldungen.
12. Performance für große Welten und viele Einheiten.

Kein Punkt wird gestrichen. Wo ein Punkt zu groß ist, wird er im IMPLEMENTATION_PLAN in kleinere Phasen geteilt.

## 4. Grundsatzentscheidungen (Phase 1)

Status-Kennzeichnung: **[B]** = bestätigt (vom Nutzer vorgegeben oder geprüft), **[E]** = Entscheidung von Claude (änderbar, mit Begründung), **[O]** = offen, muss geprüft werden.

| # | Entscheidung | Status | Begründung |
|---|---|---|---|
| D1 | Unity + C# + Windows, Einzelspieler | [B] | Nutzervorgabe |
| D2 | Unity-Version: die auf dem PC installierte; falls keine passende: aktuelle Unity 6 LTS | [O] | Installierte Version wurde noch nicht geprüft (kein Gerätezugriff) |
| D3 | Render Pipeline: URP | [E] | Stabil, Standard für neue Projekte, gute Performance mit vielen Objekten |
| D4 | Simulation getrennt von Darstellung (reine C#-Simulation mit festem Tick, Unity nur für View/Input) | [E] | Testbar ohne Szene, speicherbar, unabhängig von Framerate, Grundlage für Zeitraffer |
| D5 | Welt als Kachel-Raster (Tile Grid) mit Höhenwerten, gerendert als 3D-Terrain-Chunks | [E] | God-Tools, Territorien, Pfadfindung und Speichern werden auf einem Raster drastisch einfacher und schneller |
| D6 | Einheiten als Datensätze in Arrays (ID-basiert), Darstellung per GPU-Instancing | [E] | Tausende Einheiten ohne tausende GameObjects |
| D7 | UI: UI Toolkit (Runtime) für HUD und Menüs | [E] | In Unity 6 ausgereift, einheitliches Styling über USS, keine Zusatzpakete |
| D8 | Input: Input System Package | [E] | Offizielles Unity-Paket, Rebinding möglich |
| D9 | Speichern: versioniertes JSON über das offizielle Paket `com.unity.nuget.newtonsoft-json` | [E] | Lesbar, debugbar, Versionsmigration möglich; später optional komprimiert |
| D10 | Tests: Unity Test Framework (EditMode für Simulation, PlayMode für Integration) | [E] | Offizielles Paket |
| D11 | Versionsverwaltung: eigenes GitHub-Repository für das Unity-Projekt, Git LFS für Binär-Assets | [O] | Nutzer muss Repository anlegen; existiert noch nicht |
| D12 | Art-Stil: verbindliche Regeln in §8. **Richtung seit 2026-10-26: modern, realistisch, nicht Low-Poly** (VISION.md V5); konkrete Assets per Style Lock nach Recherche | [B] | Nutzerregel vom 2026-10-25: echte Internet-Assets, einheitlicher Stil, realistische Proportionen |
| D13 | Maßstab: 1 Unity-Einheit = 1 Meter; 1 Kachel = 4 m (`WorldConstants.TileSize`) | [E] | Echte Modelle (Soldat ≈ 1,8 m, Humvee ≈ 4,6 m, Panzer ≈ 9 m) passen ohne Umskalieren zueinander |
| D14 | ID-Regeln und Speicherformat nach `Docs/proposals/SAVE_FORMAT_PROPOSAL.md` Abschnitt A–F | [E] | Übernommen aus dem Vorschlag des Phase-7-Threads, damit alle Phasen von Anfang an speicherbar bauen |
| D15 | Phasennummern = Nummern aus den Aufträgen des Nutzers (Phase 1 Doku … Phase 8 Endabnahme) | [B] | Nutzer hat Phase 2–8 so beauftragt |

## 5. Was heißt "fertig"

Ein Feature gilt nur als fertig, wenn:
- es im Spiel über die UI oder die Simulation tatsächlich erreichbar ist,
- es mit Speichern/Laden funktioniert (sofern es Zustand hat),
- die Tests aus TEST_PLAN für diese Phase ausgeführt wurden und das Ergebnis in CURRENT_STATE steht,
- keine Platzhalter, leeren Methoden oder TODO-Fakes zurückbleiben.

## 6. Pflichtlektüre für jeden neuen Chat

1. `Docs/PROJECT_MASTER.md` (diese Datei)
2. `Docs/ARCHITECTURE.md`
3. `Docs/SYSTEM_INTERFACES.md`
4. `Docs/CURRENT_STATE.md`
5. der relevante Abschnitt aus `Docs/IMPLEMENTATION_PLAN.md`
6. `Docs/INTEGRATION.md` (Zuständigkeitsregister: wer besitzt welche Klasse)
7. bei Asset-Arbeit: `Assets/ThirdParty/ASSET_CREDITS.md`
8. bei Tests: `Docs/TEST_PLAN.md`
9. `Docs/VISION.md` (Langfristziele, Ausbaustufen M1–M7; nichts davon vor Test von M1 bauen)

Danach: betroffene Dateien lesen, bestehenden Code erweitern, keine zweite Implementierung anlegen, CURRENT_STATE am Ende aktualisieren.

## 8. Art Direction und Asset-Regeln (VERBINDLICH, Nutzerregel vom 2026-10-25)

### 8.1 Eigene Modelle, Rohstoffe aus dem Netz (Nutzer 2026-10-26, ersetzt die Fassung vom 2026-10-25)
- **Geometrie (Modelle) machen wir selbst**, prozedural oder modular, damit alles zusammenpasst (Nutzer: „Internet-Assets passen nicht zusammen, wir machen eigene“).
- Aus dem Internet nur **stilneutrale Rohstoffe**: CC0-Fototexturen (Rinde, Blätter, Boden, Fels), HDRI-Himmel, Geräusche; Einzelteile nur, wenn sie nach Umarbeitung in den Stil passen.
- 3D-Objekte = echte 3D-Modelle. Ein Bild ist nie Ersatz für ein Modell (Ausnahme: Impostor als ferne LOD-Stufe).
- Vor jeder Nutzung Lizenz, Format und Namensnennung prüfen und in `ASSET_CREDITS.md` eintragen. Nie „frei“ behaupten ohne geprüfte Lizenz. Keine Käufe ohne ausdrückliche Zustimmung des Nutzers.
- Katalog aller benötigten Assets: `Docs/proposals/ASSET_CATALOG.md`; Stil und Umweltkopplung: `Docs/proposals/ASSETS_STYLE_ENVIRONMENT.md` (PR #9).

### 8.2 Einheitlicher Grafikstil
Pflichtkriterien für jedes Asset, bevor es importiert wird:
1. vergleichbare Detailstufe und **realistische Proportionen** (keine Comic-Soldaten neben realistischen Panzern),
2. konsistente Materialien und Texturauflösungen (Zielwerte: Einheiten/Fahrzeuge 1K, Gebäude 1K–2K, Terrain-Texturen 1K–2K kachelbar),
3. passende Beleuchtung, Schatten, Farbgebung (gemeinsames URP-Lichtsetup, gemeinsames Post-Processing-Profil),
4. einheitlicher Maßstab (D13: 1 Einheit = 1 m),
5. harmonische Farbpalette, eine zusammenhängende Atmosphäre für Welt, Einheiten, Effekte und UI (UI-Richtung: `Docs/UI_STYLE_GUIDE.md`, Entwurf).

Passt ein gutes Asset nicht: Material, Farbe, Beleuchtung oder Geometrie anpassen. Geht das nicht sinnvoll: passende Alternative suchen.

### 8.3 Style Lock (Pflichtschritt vor dem ersten Massenimport)
1. Asset-Inventar erstellen (Kandidaten je Kategorie mit Quelle, Lizenz, Format, Polygonzahl, Texturgröße, Vorschau) in `Assets/ThirdParty/ASSET_CREDITS.md` Abschnitt "Kandidaten".
2. Auf Basis der tatsächlich verfügbaren Militär-Modelle (der knappste Bereich) eine Stilrichtung wählen. Arbeitshypothese [E]: **stilisiert-realistisch, Low-/Mid-Poly, realistische Proportionen, gedeckte natürliche Farben, einfache PBR-Materialien**. Diese Richtung hat bei CC0-Quellen die größte Auswahl über alle Kategorien.
3. Referenzszene ("Style Board" in Unity): Terrain-Ausschnitt + 1 Haus + 1 Soldat + 1 Humvee + 1 Panzer + 1 Explosion im echten Licht, **im Spiel gerendert beurteilen, nicht anhand von Vorschaubildern**.
4. Nutzer bestätigt den Stil (Screenshot). Danach gilt er für alle Phasen.

### 8.4 Integration
Modelle tatsächlich importieren und einrichten: Materialien auf URP-Shader umstellen, Maßstab und Pivot (Boden-Mitte, +Z vorne), Collider nur wo nötig, LODs, Animationen (Humanoid/Generic). Effiziente Formate, keine übergroßen Texturen, keine redundanten Dateien. Kompatibilität mit Unity-Version und URP prüfen. Regeln im Detail: ARCHITECTURE §16.

### 8.5 Platzhalter
Primitive (Würfel, Kapseln), Standardmaterialien und einfache Ersatzmodelle sind nur als **dokumentierte Übergangslösung** erlaubt (Eintrag in ASSET_CREDITS "Platzhalter-Register" und CURRENT_STATE). Sobald ein passendes Asset verfügbar ist, wird es eingebaut. Vor der Endabnahme (Phase 8) wird das Platzhalter-Register vollständig geprüft. Gibt es kein passendes Asset: dokumentieren und einen möglichst hochwertigen, stilpassenden Ersatz bauen.

### 8.6 Qualitätskontrolle
Regelmäßig die gerenderten Ergebnisse im Spiel prüfen (Screenshots in CURRENT_STATE verlinken). Ziel: Terrain, Siedlungen, Soldaten, Fahrzeuge, Explosionen und UI wirken wie Teile desselben professionellen Spiels.

## 7. Technische Grenze dieser Arbeitsumgebung

Claude-Cloud-Sitzungen haben keinen Unity-Editor. Kompilieren, Tests im Unity Test Runner und das Starten des Spiels sind nur möglich
- über eine Remote-Control-Sitzung auf dem PC des Nutzers, oder
- durch den Nutzer selbst.

Ergebnisse, die nicht so geprüft wurden, werden in CURRENT_STATE als "nicht im Editor geprüft" markiert.
