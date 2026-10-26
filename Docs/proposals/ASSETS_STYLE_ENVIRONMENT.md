# World Beyond – Eigener Asset-Stil, Pflanzen und Umweltkopplung (Vorschlag)

Status: **Vorschlag [E]**, nichts davon ist implementiert. Stand: 2026-10-26.
Verfasst vom Thread „Asset-Katalog für alle Pflanzen & Modelle aufbauen“.
Grundlage: Nutzernachricht vom 2026-10-26 13:20 UTC (siehe §0), PROJECT_MASTER §8, ARCHITECTURE §3/§16, VISION (V5, V9, V10), UI_STYLE_GUIDE Teil A, INTEGRATION §0/§3, Code-Stand `integration/m1` @9ccfe77 und `phase2-world-engine` @921fdbb.
Zugehöriger Katalog: `Docs/proposals/ASSET_CATALOG.md` (jedes Modell, jede Pflanze, jeder Effekt mit Status).

Diese Datei ändert **keine** gemeinsame Datei. Wo gemeinsame Dateien angepasst werden müssen, steht das in §9 als Übergabe an den Integrations-Thread.

## 0. Nutzervorgabe (2026-10-26, sinngemäß)

1. Gute, aufwendige Grafik. Kein „KI-Müll“, keine Vierecke, die zusammen einen billigen Minecraft-Charakter ergeben.
2. Modelle sind Teil der Welt: Bäume sterben, Blitze lösen Brände aus, alles verbrennt und stirbt. Das soll gut aussehen.
3. Die Feuerwehr kommt **erst, wenn jemand den Brand sieht**.
4. Assets aus dem Internet passen nicht zusammen, deshalb machen wir eigene.
5. Bäume sind echte, runde 3D-Bäume, die sich voneinander unterscheiden.
6. Platzhalter dürfen vorerst schlecht aussehen, müssen aber im Blick bleiben.
7. Ein Ort, an dem jedes Item und jedes Asset aufgezählt ist (Bäume, Pflanzen usw.).
8. Jede Pflanze dieses Planeten soll vorkommen. Alles ist mit der Umwelt verbunden: Wind, Orkan, Tsunami usw.

## 1. Ausgangslage (geprüft am 2026-10-26)

| Punkt | Befund | Quelle |
|---|---|---|
| Importierte Assets | **keine** | `Assets/ThirdParty/ASSET_CREDITS.md` |
| Sichtbare Bäume/Felsen | 3 prozedurale Ersatzmodelle: Nadelbaum aus 3 gestapelten Kegeln, Laubbaum aus 2 Kegelstümpfen, Fels als verzerrter Doppelkegel; **flache Normalen je Fläche**, einfarbige Materialien | `Presentation/WorldView/ProceduralMeshes.cs` auf `phase2-world-engine` @921fdbb (World) |
| Vegetationsdarstellung | `VegetationLayer`: GPU-Instancing pro Chunk, 3 Arten (Nadel, Laub, Fels), Platzierung deterministisch per Hash aus Kachel + Seed, rein darstellend; auf brennenden/verbrannten Kacheln steht **nichts** | `Presentation/WorldView/VegetationLayer.cs` (World) |
| Vegetation in der Simulation | nur `TerrainType.Forest` + `ResourceType.Wood`-Menge je Kachel; keine Arten, kein Alter, keine Gesundheit | `Simulation/World/WorldTypes.cs`, `Generation/Climate.cs` |
| Feuer/Zerstörung in der Simulation | Flags `Burning`, `Scorched`, `Crater`, `Wreck` existieren; **kein System** setzt oder verbreitet sie | `WorldTypes.cs` (TileFlags) |
| Biome | 9: Ocean, Coast, Temperate, Boreal, Tundra, Desert, Savanna, Wetland, Alpine. **Kein** Tropenwald, **kein** Mittelmeerklima. *Nachtrag 2026-10-26:* World hat `TropicalForest` (9) und `Mediterranean` (10) angehängt (24bd411). | `WorldTypes.cs` (BiomeType) |
| Wetter, Wind, Blitz, Tsunami | existiert nirgends; Umweltfelder sind VISION V9 (Stufe M5) | VISION §3/§4 |
| Gebäude, Einheiten | Inhalte definiert (19 Gebäude, 3 Zivil-Einheiten, 1 Soldat), **keine Darstellung** | `CivContent.cs`, `MilitaryCatalog.cs` auf `integration/m1` |

Die Ersatzmodelle sind genau das, was der Nutzer als „Vierecke/Minecraft“ beschreibt. Sie sind als Platzhalter in Ordnung (Nutzerpunkt 6), stehen aber noch nicht im Platzhalter-Register (ASSET_CREDITS §3). Der Katalog führt sie jetzt als P-001 bis P-005.

## 2. Konflikte mit bestehenden Regeln (offengelegt)

**Stand 2026-10-26 16:16 UTC: KA1–KA4 vom Integrations-Thread übernommen** (`integration/m1` @70a2d99, PR #10: PROJECT_MASTER §8.1, UI_STYLE_GUIDE A1/A3 mit LOD-Kette, INTEGRATION §3 mit Besitzern „Art/Assets“ und „Umwelt“, ASSET_CREDITS §3 P-001–P-005 und §4 Link auf diesen Katalog). Die Tabelle bleibt als Begründung stehen.

| # | Konflikt | Vorschlag | Wer entscheidet |
|---|---|---|---|
| KA1 | PROJECT_MASTER §8.1 (Nutzerregel 2026-10-25): „echte Internet-Assets aktiv importieren“. Nutzer 2026-10-26: „Internet-Assets passen nicht zusammen, wir machen eigene.“ | Neuere Nutzeraussage gilt. Neue Regel: **Geometrie (Modelle) machen wir selbst**, prozedural oder modular. Aus dem Internet kommen nur **stilneutrale Rohstoffe**: CC0-Fototexturen (Rinde, Blätter, Boden, Fels), HDRI-Himmel, Geräusche; außerdem Einzelteile, die nach Umarbeitung in den Stil passen. Lizenzprüfung und ASSET_CREDITS bleiben Pflicht. | Nutzer hat es gesagt; Integration trägt es in PROJECT_MASTER §8.1 ein |
| KA2 | UI_STYLE_GUIDE A1 „gemalter stilisierter Realismus, Low-/Mid-Poly“ und Kommentar in `ProceduralMeshes.cs` („Low-Poly-Ersatz“) widersprechen VISION V5 („realistisch, nicht Low-Poly“) und der Nutzernachricht („aufwendig, runde 3D-Bäume“). | Stil **„Naturrealismus mit warmer Farbgebung“** (§3): realistische Formen und Materialien, aber die kräftige, warme Farbigkeit der Forge-of-Empires-Referenz und die Fernlesbarkeit von WorldBox bleiben. | Integration/Phase 6 passen A1 an; Nutzer bestätigt am Style-Lock-Bild |
| KA3 | Polygon-Budgets: Bäume ≤ 1 500 (UI_STYLE_GUIDE A3) bzw. ≤ 2 000 (ARCHITECTURE §16). Für runde, detailreiche Bäume in der Nahsicht zu knapp. | LOD-Kette statt Einzelwert (§4.4): LOD0 bis 6 000 Dreiecke (nur nahe Kamera), LOD1 ≈ 1 500, LOD2 ≈ 300, ab mittlerer Entfernung Impostor (Bild-Ersatz). Endwerte erst nach Messung in Unity. | Integration, nach Messung |
| KA4 | Vegetation nur als Kacheltyp `Forest`. Sterben, Brand, Nachwachsen brauchen einen Vegetationszustand je Kachel. | Eigenes Datenfeld des neuen Umwelt-Bereichs (§6.2), **ohne** `TileData` zu ändern. | Integration (neuer Besitzer), World (Abstimmung) |

## 3. Stilregeln „Naturrealismus mit warmer Farbgebung“ (Vorschlag)

**Ja:**
- Organische Formen sind rund und weich: glatte Normalen, gebogene Äste, unregelmäßige Kronen. Jede Pflanze hat eine erkennbare Silhouette ihrer Wuchsform.
- Jedes Exemplar ist anders: Größe, Krone, Astwinkel, Neigung und Farbton werden pro Exemplar aus einem Seed abgeleitet.
- Physikalisch plausible Materialien (URP Lit, PBR): Rinde mit Normal-Map, Blätter mit Alpha-Ausschnitt und Durchscheinen (Subsurface-Näherung), Fels mit Triplanar-Textur.
- Gemeinsame Farbkalibrierung: alle Fototexturen werden auf einen gemeinsamen Helligkeits- und Sättigungsbereich gebracht (Albedo-Grenzen im Style Lock festlegen). Danach warme Farbbalance über das gemeinsame Post-Processing.
- Einheitliche Texeldichte für alle Nahobjekte (Zielwert im Style Lock festlegen, Vorschlag ca. 512 Pixel pro Meter).
- Fernsicht wie WorldBox: Biome und Territorien als klare Farbflächen, Wälder als geschlossene, farbige Kronendächer (Impostor), nicht als einzelne Stäbchen.

**Nein:**
- Würfel, facettierte Kegel, Voxel, flache Normalen bei organischen Objekten.
- Einfarbige Kronen ohne Licht/Schatten-Verlauf.
- Fertige Modelle verschiedener Autoren nebeneinander ohne Umarbeitung.
- Texturen unterschiedlicher Quellen ohne Farbangleichung.
- Comic-Proportionen neben realistischen Objekten.

**Abnahme:** wie UI_STYLE_GUIDE A7, zusätzlich: Vergleichsbild Nah/Mittel/Fern im echten Licht, mit Wind in Bewegung, und in jedem Zustand (gesund, trocken, tot, brennend, verkohlt).

## 4. Produktionsweg (wie wir die Assets selbst herstellen)

### 4.1 Bäume und Pflanzen: eigener Generator „PlantForge“

Ein eigenes Unity-Editor-Werkzeug in C#, das aus **Parametern + Seed** Pflanzen-Meshes erzeugt und als Assets speichert (Backen im Editor, im Spiel nur Instancing). Deterministisch: gleicher Seed = gleiche Pflanze.

| Baustein | Verfahren |
|---|---|
| Kronenform / Astgerüst | *Space Colonization* (Runions u. a., 2007): Äste wachsen auf Zielpunkte in einer Kronenhülle zu. Ergibt von Natur aus runde, unregelmäßige, jedes Mal andere Kronen. Hülle (Kugel, Ei, Schirm, Kegel, Säule) kommt aus der Wuchsform. Für Nadelbäume und Palmen zusätzlich ein regelbasiertes Verzweigungsmodell (Quirle, Wedel). |
| Stamm/Äste | Röhren mit sich verjüngendem Querschnitt, glatte Normalen, Wurzelanlauf am Stammfuß, Rinden-UVs entlang der Astlänge. |
| Laub | Blattbüschel-Karten (Alpha-Ausschnitt) an den Astenden, Ausrichtung leicht nach außen/oben, Normalen zur Kronenmitte gebogen (wirkt „rund“ im Licht). Blattatlas je Gattung. |
| Wind-Daten | pro Vertex in Vertexfarbe/UV2: Asthierarchie-Stufe, Phase, Steifigkeit, Abstand zum Stamm. Ein gemeinsamer URP-Shader-Graph biegt Stamm, Äste und Blätter abhängig vom Wind (§6.4). |
| Zustände | aus demselben Gerüst: Laubdichte pro Asthierarchie (Herbst, Dürre, tot), Verkohlungsgrad, abgebrochene Äste, Stumpf, Wurzelteller für umgestürzte Bäume (§5). |
| LOD | LOD0 voll, LOD1 vereinfachte Äste + größere Blattkarten, LOD2 sehr grob, danach Impostor (aus mehreren Winkeln vorgerendertes Bild). |

Warum kein fertiges Werkzeug:

| Alternative | Grund dagegen (Stand der Recherche 2026-10-26) |
|---|---|
| Fertige Baum-Pakete aus dem Internet | passen nicht zusammen (Nutzer), Lizenzen je Modell verschieden |
| Unity Tree Creator | laut Unity-Issue-Tracker für die Built-in-Pipeline gebaut; in URP werden die Materialien rosa, Nutzer berichten, dass der Editor den Shader zurückstellt |
| SpeedTree | kostenpflichtig (Kauf nur mit Zustimmung, Budget ≤ 60 €); Forenberichte über Schatten-/Lichtprobleme der URP-Shader (nicht selbst geprüft) |
| Blender + Sapling Tree Gen | kostenlos, aber seit Blender 4.2 nur noch Erweiterung „wie sie ist“ mit eingeschränkter Pflege; Bewertungen melden Fehler in neueren Blender-Versionen; zusätzliche Werkzeugkette auf dem PC |

Risiko: Der eigene Generator ist Arbeit (geschätzt mehrere Etappen, §8). Dafür bekommen wir einheitlichen Stil, unbegrenzte Varianten, alle Zustände und Wind aus einer Quelle und keine Lizenzfragen bei der Geometrie.

### 4.2 Texturen

CC0-Fotoscans für Rinde, Blätter, Boden, Fels, Sand, Schnee (Kandidaten: Poly Haven, ambientCG; Lizenz laut Seite CC0, **vor jedem Import je Datei prüfen** und in ASSET_CREDITS eintragen). Alle laufen durch denselben Kalibrierungsschritt (§3). Blattatlanten können zusätzlich vom Generator selbst gezeichnet werden (Blattform als Kurve), damit jede Gattung eine eigene Blattform bekommt. Hinweis: Downloads sind aus der Cloud laut Phase 5 blockiert (CURRENT_STATE K3), also auf dem PC.

### 4.3 Felsen, Gelände, Wasser

- Felsen: prozedural (verformte Kugel mit Rauschen + Bruchkanten), Triplanar-Material, Varianten per Seed, Moos-/Schneeauflage per Shader nach Biom.
- Gelände: bleibt Chunk-Mesh (ARCHITECTURE §3). Statt reiner Vertexfarben Materialmischung (Splat) aus kalibrierten Bodentexturen je TerrainType, Zustände Verbrannt/Überflutet/Krater als Overlays. Besitzer: World.
- Wasser: Shader-Graph mit Tiefenfarbe, Wellen und Uferschaum; Tsunami-Welle als eigenes Mesh (§6.6).

### 4.4 Gebäude

Modularer Baukasten je Epoche und Kultur (Wände, Dächer, Fenster, Türen, Schornsteine, Zäune), selbst modelliert bzw. prozedural zusammengesetzt. Kultur-Parameter (Dachform, Materialien, Farben) erzeugen Vielfalt, ohne jedes Haus einzeln zu bauen. Zustände: Baustelle, intakt, beschädigt, brennend, verkohlte Ruine, Trümmer.

### 4.5 Menschen und Einheiten

Schwierigster Bereich (Modellierung + Rig + Animation). Vorschlag: **ein** eigener realistisch proportionierter Grundkörper, Kleidung/Ausrüstung je Epoche als Module, Animationen auf einem gemeinsamen Humanoid-Rig. Möglicher Startpunkt für den Grundkörper: Open-Source-Charaktergeneratoren (z. B. MakeHuman). **Lizenz der Ausgaben nicht geprüft**, vor Nutzung klären. Fahrzeuge: eigene Modelle nach demselben Baukastenprinzip; die Kandidaten aus `PHASE5_MILITARY_ASSETS.md` bleiben nur als Referenz/Notlösung.

### 4.6 Effekte

Unity Particle System, gepoolt (UI_STYLE_GUIDE A6). Feuer, Rauch, Glut, Asche, Funkenflug, Blitz, Regen, Schnee, fliegende Blätter, Staub, Gischt, Überflutung. Flipbook-Texturen möglichst selbst erzeugt oder aus **einer** geprüften CC0-Quelle, damit sie zusammenpassen. Ferne Brände: Glut-Decal + Rauchsäule statt vieler Partikel.

## 5. Lebende Modelle: Zustände jeder Pflanze

Jede Pflanze hat dieselben Zustände. Fast alle kommen **ohne zusätzliches Modell** aus: gleicher Mesh, andere Shader-Werte oder Rotation.

| Zustand | Umsetzung |
|---|---|
| Keimling, jung, ausgewachsen, alt | Skalierung + eigene Seeds für junge Formen (weniger Äste) |
| Jahreszeiten (Austrieb, Sommer, Herbstfärbung, kahl, Schnee) | Shader-Parameter: Laubfarbe, Laubdichte, Schneeauflage |
| Trockenstress / Dürre | Laub gelb-braun, lichter (Blätter nach Hierarchie ausgeblendet) |
| Krank | Flecken, lichtere Krone |
| Tot stehend | kein Laub, graue Rinde, einzelne Äste abgebrochen |
| Brennend | Feuer-Partikel an Ästen, Glühen im Shader, Laub verschwindet nach Brandfortschritt |
| Verkohlt | schwarze Rinde, feine Äste weg, Asche am Boden (Decal) |
| Umgestürzt (Sturm, Tsunami, Fällung) | gleicher Mesh gekippt in Wind- bzw. Wellenrichtung + Wurzelteller-Mesh; Fallrichtung kommt aus der Simulation |
| Stumpf (gefällt) | eigene Generator-Ausgabe (Stamm abgeschnitten) |
| Nachwachsen | Sukzession: Gras → Kräuter → Sträucher → Pionierbäume → Wald (§6.2) |

## 6. Umweltkopplung

### 6.1 Grundsatz

- **Simulation entscheidet, Darstellung zeigt.** Wind, Brand, Tod, Flut sind Simulationsdaten (deterministisch, speicherbar, Chronik-fähig nach VISION V2). Die Darstellung liest nur.
- **Einzelbäume sind Darstellung des Kachelzustands.** Die Simulation speichert pro Kachel Werte (Dichte, Artenmix, Gesundheit, Brand). Welcher einzelne Baum tot ist, folgt deterministisch aus Hash-Rang + Kachelwert (z. B. Gesundheit 0,6 → die 40 % Bäume mit dem niedrigsten Hash sind tot). So bleibt Speichern/Laden exakt, ohne Millionen Bäume zu speichern. Das passt zur vorhandenen Hash-Platzierung im `VegetationLayer`.
- Später (VISION V1, aktive Stufe) können einzelne Bäume nahe beim Spieler zu echten Objekten werden (z. B. Holzfäller fällt genau diesen Baum). Nicht vor M4.
- Grobe Felder statt pro Kachel pro Tick (VISION V9): Wind/Wetter auf einem Regionsraster (Vorschlag 16 × 16 Kacheln = 64 m), Feuer nur auf aktiven Brandkacheln.

### 6.2 Vegetationsfeld (Simulation)

Pro Kachel ein kleiner Datensatz in einem eigenen Array des Umwelt-Bereichs (ändert `TileData` nicht):

| Feld | Bedeutung |
|---|---|
| Bewuchs-Dichte | 0..1 Bäume, Sträucher, Bodendecker getrennt |
| Artenmix | bis zu 3 Gattungs-IDs (aus dem Katalog) mit Anteil |
| Altersklasse | jung / mittel / alt |
| Gesundheit | 0..1 (Dürre, Krankheit, Salz, Rauch senken sie) |
| Trockenheit | 0..1 aus Feuchte, Wetter, Jahreszeit → Brennbarkeit |
| Totholz/Brennstoff | Menge, wächst mit toten Bäumen |
| Sukzessionsstufe | nach Brand oder Rodung zurück auf 0 |

`TerrainType.Forest` und `ResourceType.Wood` bleiben die Wahrheit für World und Civ; das Umweltsystem hält sie über `IWorldGrid` konsistent (z. B. Wald verbrannt → Terrain Grass + Flag Scorched, Holzmenge sinkt). Wer welche Felder schreibt, muss mit World abgestimmt werden.

Baumsterben-Ursachen: Alter, Dürre, Brand, Sturmwurf, Überflutung/Salz (Tsunami), Krankheit (später), Rodung durch Civs, Verschmutzung (VISION V9).

### 6.3 Feuer, Blitz und Feuerwehr

**Blitz:** Gewitterzellen (§6.4) erzeugen Einschläge. Wahrscheinlichkeit steigt mit Zellstärke, Höhe, einzelnen hohen Bäumen und Türmen. Einschlag = Ereignis mit Ort und Zeit (Chronik). Zündet nur, wenn Trockenheit und Brennstoff reichen; Regen kann den Brand gleich wieder löschen.

**Ausbreitung:** pro Brandkachel Intensität und Brennstoff. Ausbreitung auf Nachbarkacheln abhängig von Windrichtung/-stärke, Hangneigung (bergauf schneller), Trockenheit, Brennstoff, Funkenflug bei starkem Wind (Sprünge über Straßen/Flüsse möglich). Brennstoff verbraucht → `Burning` aus, `Scorched` an, Vegetation tot. Gebäude fangen Feuer über dieselbe Regel (Material: Holz brennt, Stein kaum).

**Sichtung („Feuerwehr erst, wenn es jemand sieht“):**
- Jeder Brand ist ein **Brandereignis** mit ID, Startzeit, Fläche, Status `unentdeckt → entdeckt → bekämpft → gelöscht/erloschen`.
- Beobachter: Personen/Einheiten im Freien, Siedlungen, später Wachtürme, Feuerwachen, Flugzeuge, Satelliten (nach Epoche). Jeder hat einen Sichtradius.
- Gesehen wird vor allem die **Rauchsäule**: ihre Höhe und damit die Sichtweite wächst mit der Brandgröße. Nachts sieht man Flammen statt Rauch. Gelände kann die Sicht verdecken (Sichtlinie über das Höhenraster, grob).
- Erst nach Entdeckung: Ereignis `Brand entdeckt` (mit Entdecker). Die betroffene Civ entscheidet über ihre KI, ob und womit sie löscht.
- Löschmittel nach Epoche: Eimerkette der Dorfbewohner → Feuerwehr mit Wagen/Pumpe → Löschfahrzeug → Löschflugzeug/Hubschrauber. Die Kräfte laufen/fahren über normale Pfadsuche und brauchen echte Spielzeit (VISION V7). Unentdeckte Brände brennen weiter.
- Folgen in der Chronik: Einschlag, Entdeckung, Einsatz, Fläche, zerstörte Gebäude, Opfer.

### 6.4 Wind, Sturm, Orkan, Wetter

- Simulation: Windfeld (Richtung, Stärke, Böigkeit) auf dem Regionsraster, angetrieben von Wetterlagen und wandernden Zellen (Regen, Gewitter, Sturm, Orkan, Schnee). Beaufort-ähnliche Stärkestufen.
- Folgen: Austrocknung/Befeuchtung, Brandausbreitung, Sturmwurf (Wahrscheinlichkeit je Baum aus Windstärke, Wuchsform, Gesundheit, Bodennässe), Dachschäden, Schiffe, Ernteschäden.
- Darstellung: globale Shader-Werte (Richtung, Stärke, Böe) + regionaler Wert; Bäume biegen sich nach Hierarchie (Stamm wenig, Äste mehr, Blätter flattern), fliegende Blätter/Staub, Regen/Schnee, Wolken, Wellen auf Wasser. Bei Orkan zusätzlich umstürzende Bäume (Zustand aus der Simulation), Trümmer.

### 6.5 Erdbeben (Auslöser) und Tsunami

- Seebeben-Ereignis (Ort, Stärke) → Welle breitet sich über Wasserkacheln aus; Geschwindigkeit nach Flachwasserformel (≈ √(g·Tiefe)), dadurch realistische Verzögerung bis zur Küste.
- Auflaufen: Flutkacheln abhängig von Höhe über Meeresspiegel und Wellenhöhe, Eindringtiefe ins Land begrenzt.
- Folgen: Gebäude und Einheiten beschädigt/zerstört, Bäume entwurzelt, Salz → Vegetation stirbt, Trümmer (`Wreck`), Flucht, Chronik.
- Darstellung: Wellenfront-Mesh mit Schaum, steigender Wasserspiegel auf Flutkacheln, zurückströmendes Wasser, Treibgut.

### 6.6 Zuständigkeit (Vorschlag)

| Funktion | Vorschlag Besitzer |
|---|---|
| Wetter/Wind, Blitz, Brand + Sichtung, Vegetationsfeld, Erdbeben/Tsunami (Simulation) | **neuer Bereich „Umwelt“** (gibt es in INTEGRATION §3 noch nicht) |
| Kachelraster, Terrain/Wald-Typen, Gelände-Darstellung, `VegetationLayer` | World (wie heute) |
| Löschentscheidung und Feuerwehr-Einheiten | Civ (Einheitentypen, KI) |
| Partikel/VFX-Darstellung | UI (`IVfxService`, INTEGRATION §3) |
| PlantForge-Generator, Asset-Katalog, Stilregeln, Shader für Pflanzen | neuer Bereich „Art/Assets“ (dieser Thread schlägt sich vor) |

## 7. „Jede Pflanze der Erde“ – ehrlich und in Etappen

Ehrlich: Es gibt grob 350 000–400 000 beschriebene Pflanzenarten (häufig genannte Größenordnung, nicht selbst gezählt). Jede Art als eigenes Modell zu bauen ist nicht machbar, und auf Spielkamera-Entfernung sind die meisten Arten nicht voneinander zu unterscheiden. Machbar ist: **jede Wuchsform** und **jede wichtige Gattung**, und darüber beliebig viele Arten als **Datensätze** (Parameter + Blattform + Farbe), ohne neue Modelle.

| Etappe | Inhalt | Umfang | Ergebnis |
|---|---|---|---|
| P1 | Wuchsformen (Archetypen): rundkroniger Laubbaum, Kegel-Nadelbaum, Palme, Säulenkaktus, Gras … | 27 (Katalog §2.1) | jede Pflanze der Erde ist optisch einer Form zugeordnet |
| P2 | Leitgattungen je Biom (Eiche, Buche, Fichte, Kiefer, Akazie, Baobab, Mangrove …) | 104 Gattungen (Katalog §2.2–2.7) | jedes Biom sieht typisch und abwechslungsreich aus |
| P3 | Erweiterung nach Klimazonen (Köppen) und Epochen: Nutzpflanzen, Zierpflanzen, Wasserpflanzen, fehlende Biome (Tropen, Mittelmeer) | ca. 250–400 Gattungen | große Vielfalt, Landwirtschaft je Kultur |
| P4 | Arten als Datensätze in einer Pflanzen-Datenbank (Gattungs-Preset + Abweichungen) | beliebig erweiterbar, Tausende möglich | „jede Pflanze“ im Sinne von: jede Art kann eingetragen werden |

Voraussetzung in der Simulation: Biome **Tropischer Regenwald** und **Mittelmeer/Hartlaub**. Erledigt am 2026-10-26 durch World (`TropicalForest` = 9, `Mediterranean` = 10, 24bd411).

## 8. Etappenplan (Reihenfolge, je Etappe eigener Test)

Regel INTEGRATION §0.6: Nichts aus M2–M6 beginnen, bevor M1 getestet ist. Deshalb ist **jetzt nur Planung + Katalog** dran. Danach:

| Etappe | Inhalt | Voraussetzung | Nachweis |
|---|---|---|---|
| A0 (jetzt) | Katalog, Stilkonzept, Konflikte gemeldet | – | dieses Dokument + Katalog |
| A1 | PlantForge-Grundversion: 3 Wuchsformen (rund Laub, Kegel Nadel, Busch), Wind-Shader, LOD + Impostor; ersetzt P-001/P-002 im `VegetationLayer` (mit World abgestimmt) | M1 getestet, Unity-Projekt im Repo | Style-Board-Screenshot Nah/Mittel/Fern, Bildrate gemessen, Nutzer bestätigt |
| A2 | Alle 27 Wuchsformen + Leitgattungen P2; Felsen; Pflanzen-Zustände (§5) per Shader | A1 abgenommen | Screenshot je Biom, Zustandsreihe |
| A3 | Umwelt E1: Vegetationsfeld, Wachstum/Sterben/Sukzession, Wind/Wetter-Grundlage | M2 (Chronik + Kalender) | dotnet-Test „N Jahre ohne Eingriff“, Speichern/Laden identisch |
| A4 | Umwelt E2: Blitz → Brand → Sichtung → Feuerwehr | A3, Civ-Einheiten | Test: Brand wird erst nach Sichtung bekämpft; Chronik-Einträge |
| A5 | Umwelt E3: Sturm/Orkan mit Sturmwurf und Schäden | A3 | Test + Video/Screenshot |
| A6 | Umwelt E4: Erdbeben/Tsunami, Überflutung | A3 | Test + Screenshot |
| A7 | Gebäude-Baukasten, Menschen-Grundkörper, Fahrzeuge (eigene Stilfamilie) | Style Lock | Style Board laut PROJECT_MASTER §8.3 |
| laufend | Pflanzen P3/P4 als Daten | A2 | Katalog-Zähler |

Nach VISION liegt die Umweltsimulation eigentlich in M5. Vorschlag: Vegetation/Brand/Wind (A3–A5) früher, weil der Nutzer sie ausdrücklich will und sie auf vorhandenen Flags aufbauen. Das ist eine Reihenfolgeänderung und braucht die Zustimmung des Nutzers bzw. des Integrations-Threads.

## 9. Übergabe (Format laut Projektregeln §9)

**Aufgabe:** Zentraler Asset-Katalog + Konzept für eigenen Asset-Stil und Umweltkopplung.

**Ergebnis:** Zwei neue Vorschlagsdokumente (dieses + `ASSET_CATALOG.md`). Kein Code, kein Asset erzeugt oder importiert.

**Geänderte Dateien:** nur neu: `Docs/proposals/ASSETS_STYLE_ENVIRONMENT.md`, `Docs/proposals/ASSET_CATALOG.md`.

**Schnittstellen:** World (VegetationLayer, ProceduralMeshes, Biome-Enum, TileFlags), Civ (Feuerwehr-Einheiten, Lösch-KI), UI (VFX), Save (neue Sektion `environment`), Chronik (M2).

**Abhängigkeiten:** M1-Test; Unity-Projekt im Repo (DevOps); Style Lock (Phase 6); Entscheidung zu KA1–KA4.

**Tests:** keine (reine Dokumentation). Code-Befunde in §1 durch Lesen der genannten Dateien geprüft.

**Offene Probleme:** KA1–KA4 übernommen (70a2d99); offen bleibt die Zustimmung des Nutzers, Umwelt (A3–A5) vor M5 einzuplanen; MakeHuman-Lizenz ungeprüft; Texturquellen-Lizenzen je Datei noch zu prüfen; Polygon-Budgets nur geschätzt.

**Nächste Schritte:** (erledigt: Regeln, Besitzer, Platzhalter-Register, Biome.) Nutzer entscheidet über die frühere Umwelt-Etappe. Nach M1-Test: Etappe A1.

**Dokumentation:** PROJECT_MASTER §8.1, UI_STYLE_GUIDE A1/A3, ARCHITECTURE §16 (Budgets), INTEGRATION §3, ASSET_CREDITS §3/§4, VISION §4 (Reihenfolge Umwelt), CURRENT_STATE.
