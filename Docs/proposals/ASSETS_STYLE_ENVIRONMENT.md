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
| KA3 | Polygon-Budgets: Bäume ≤ 1 500 (UI_STYLE_GUIDE A3) bzw. ≤ 2 000 (ARCHITECTURE §16). Für runde, detailreiche Bäume in der Nahsicht zu knapp. | LOD-Kette statt Einzelwert (§4.4): LOD0 bis 6 000 Dreiecke (nur nahe Kamera; *Nachtrag 2026-10-26 nach Referenzbild R1, §3.1: Ziel für die wenigen Bäume direkt an der Kamera ca. 15 000–30 000*), LOD1 ≈ 1 500, LOD2 ≈ 300, ab mittlerer Entfernung Impostor (Bild-Ersatz). Endwerte erst nach Messung in Unity. | Integration, nach Messung |
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

### 3.1 Referenzbild R1 (vom Nutzer am 2026-10-26 16:34 UTC geschickt)

Datei: `/mnt/project-files/WorldBeyond/references/R1_baum_laub_nutzer_2026-10-26.jpg` (Projektordner, **nicht** im Repo). Urheber und Lizenz unbekannt: nur Qualitätsmaßstab, wird weder ins Spiel noch ins Repo übernommen und nicht nachgebaut.

Zu sehen: einzelner Laubbaum (ahornähnlich, passt zu `plant.acer`, W01/W02), fotorealistisch, freigestellt auf Weiß.

**Was wir daraus als Pflicht-Merkmale für Bäume ableiten (Abnahme A1/A2):**
1. **Lockere, ovale Krone mit Lücken**, durch die man Himmel und Äste sieht, kein geschlossener Klumpen.
2. **Sichtbares Astgerüst**: gerader Stamm, gabelt sich etwa im unteren Drittel in 2–3 Leittriebe, viele feine Zweige auch innerhalb der Krone.
3. **Einzelne Blätter in der Nahsicht erkennbar** (gelappte Blattform), in Büscheln an den Zweigenden; Blätter aus Fotoscans, nicht einfarbig.
4. **Licht in der Krone**: helle, durchscheinende Blätter auf der Sonnenseite, dunkle im Inneren (Durchscheinen + Selbstschattierung + Ambient Occlusion).
5. **Rinde** fein strukturiert, grau-braun, mit Normal-Map; Stammfuß mit Kontaktschatten.
6. Jedes Exemplar anders (Seed), aber als dieselbe Gattung erkennbar.

**Ehrliche Grenzen:**
- R1 ist sehr wahrscheinlich ein Offline-Render (Raytracing, sehr viele Polygone; nicht geprüft). Ein Spiel muss Tausende Bäume mit flüssiger Bildrate zeigen.
- Erreichbar: Bäume **nah an der Kamera** kommen R1 nahe (hohe LOD0-Dichte, Fotoscan-Blätter, Durchscheinen, gute Schatten/SSAO). Wälder in der Ferne sehen aus der Entfernung ähnlich aus, haben aber über Impostor (vorgerenderte Bilder) weniger Einzeldetails.
- Der größte verbleibende Unterschied ist das Licht: URP hat kein Raytracing-GI. HDRP käme R1 näher, wäre aber eine große Architekturänderung (gesamtes Projekt läuft auf URP) mit höheren Hardwareanforderungen. Vorschlag: bei URP bleiben und beim Style Lock einen Nahvergleich „unser Baum neben R1“ machen. Erst wenn der Abstand zu groß ist, HDRP als Entscheidung des Nutzers vorlegen.

**Abnahme-Zusatz:** Für jeden Baum in A1/A2 gibt es einen Screenshot aus Unity im gleichen Blickwinkel und vor hellem Hintergrund, neben R1 gestellt.

### 3.1b Referenzbild R2 – Häuser (vom Nutzer am 2026-10-26 16:40 UTC geschickt)

Datei: `/mnt/project-files/WorldBeyond/references/R2_haeuser_nutzer_2026-10-26.jpg` (Projektordner, **nicht** im Repo; Urheber/Lizenz unbekannt, wahrscheinlich Vorschaubild einer Modellsammlung, nur Qualitätsmaßstab, wird nicht nachgebaut). Nutzervorgabe: Häuser müssen in Qualität und Licht **zum Baum R1 passen**, kein „AI-Slop“; Blender darf genutzt werden.

Zu sehen: ca. 35 freistehende Wohnhäuser (Einfamilienhäuser, Moderne/Industrie-Epoche, europäisch), fotorealistisch: Putz, Holzverschalung, Ziegel, Naturstein; Dächer rot, grau, anthrazit; Satteldach, Walmdach, Gauben, Erker, Balkone, Garagen.

**Pflicht-Merkmale für Gebäude (Abnahme A7):**
1. **Echte Geometrie für Details**: Fensterrahmen mit Tiefe, Fensterbänke, Dachüberstand mit Sparren/Traufe, Regenrinnen und Fallrohre, Schornsteine, Geländer, Sockel. Keine aufgemalten Fenster auf glatten Kästen.
2. **Glaubwürdige Proportionen** (Geschosshöhe ~2,7–3 m, Türen ~2,1 m, Dachneigungen der Epoche) im Maßstab 1 Einheit = 1 m.
3. **Materialmix pro Haus** (2–4 Materialien: Putz + Holz + Ziegeldach + Sockelstein), aus kalibrierten Fotoscans, mit leichter Verwitterung (Schmutz unten, Regenspuren, Moos am Dach).
4. **Vielfalt ohne Zufallsbrei**: Jede Variante folgt einer Bauregel (Grundrissform, Dachform, Fensterraster, Materialpalette der Kultur/Epoche). Zufall wählt nur innerhalb dieser Regeln.
5. **Dasselbe Licht und dieselbe Farbkalibrierung wie R1**: Haus und Baum nebeneinander im Style Board, ein Screenshot für beide.

### 3.2 R1-Niveau gilt für alle Assets (Nutzer, 2026-10-26 16:36 UTC)

Nicht nur Bäume: Gebäude, Felsen, Gelände, Menschen, Fahrzeuge und Effekte bekommen dieselbe Qualitätsstufe und dasselbe Aussehen. Konkret für jedes Asset:
- Materialien aus kalibrierten Fotoscans (§4.2) mit Normal-, Rauheits- und AO-Karten, keine einfarbigen Flächen.
- Feine Details, wo man nah herankommt (Kanten abgerundet, Fugen, Abnutzung, Schmutz an der Unterseite).
- Dieselbe Licht- und Farbkalibrierung, dieselbe Texeldichte (§3).
- Alle Zustände aus §5.1 (nass, Schnee, brennend, verkohlt …) über dieselbe gemeinsame Shader-Schicht.
- Beim Style Lock bekommt jede Kategorie ein eigenes Referenzbild nach dem Vorbild von R1 (Nutzer wählt oder bestätigt), und der Nahvergleich wird dokumentiert.

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

### 4.4 Gebäude: jede Epoche, an jeder Stelle zerstörbar (Nutzer, 2026-10-26 16:36 UTC)

**Epochen:** Jeder Gebäudetyp bekommt ab der Epoche seiner Technologie eine eigene Ausführung je Epoche (`Epoch`: Frühzeit, Antike, Mittelalter, Industrie, Moderne; spätere Epochen werden angehängt). Für die 19 vorhandenen Gebäude sind das 75 Epochen-Ausführungen, mit den 4 neuen Brandschutz-Gebäuden 95 (Katalog §4). Dazu kommen Kultur-Stilfamilien (Dachform, Baumaterial, Farben, Verzierungen), damit nicht jede Civ gleich aussieht.

**Bauweise:** modularer Baukasten aus Bauteilen von etwa 1–2 m: Wandsegment, Ecke, Pfeiler/Stütze, Decke, Dachfeld, Fenster, Tür, Schornstein, Fundament. Ein Haus = Grundriss + Bauteile, prozedural zusammengesetzt. Jedes Bauteil gibt es in den Materialien seiner Epoche (Lehm, Holz, Fachwerk, Naturstein, Ziegel, Stahlbeton, Glas).

**Zerstörung an jeder Stelle:**
1. Jedes Bauteil wird im Editor vorab in Bruchstücke zerlegt (eigenes Bruch-Werkzeug, Voronoi-Zerlegung) mit passendem Innenmaterial (Ziegelbruch, gesplittertes Holz, Beton mit Bewehrung je Epoche).
2. Ein Einschlag (Artillerie, Bombe, Rakete, Feuer, Erdbeben, Tsunami) ist ein Schadensereignis mit Ort, Stärke und Art (VISION V10). Der Schaden pro Bauteil hängt vom Abstand zum Einschlagpunkt und vom Material ab.
3. Bauteil-Zustände: intakt → angeschlagen (Risse, Einschuss- und Brandspuren) → durchbrochen (Loch: die Bruchstücke im Trefferradius fehlen) → zerstört.
4. Vereinfachte Statik: Was keine Verbindung mehr zum Fundament hat, stürzt ein (Decken, Dächer, obere Stockwerke). Daraus entstehen Teil-Einstürze statt „ganz oder gar nicht“.
5. Trümmer: Nahe der Kamera fallen kurz echte Bruchstücke, danach bleiben dauerhafte Trümmerhaufen und Decals liegen, dazu Staubwolke und gegebenenfalls Brand. Die Zerstörung bleibt, bis die Civ das Haus abreißt oder repariert (Wiederaufbau zeigt wieder die Baustelle).
6. Gespeichert wird in der Simulation nur ein kompakter Schadenswert je Bauteil. Das ist deterministisch und lässt sich speichern und laden. Die Darstellung baut daraus das Gebäude. In der Ferne gibt es nur die Stufen intakt, beschädigt und Ruine.

**Ehrliche Grenzen:** Ein Gebäude-Mesh wird nicht in Echtzeit beliebig zerschnitten, weil das zu teuer und zu fehleranfällig wäre. „An jeder Stelle“ heißt deshalb: auf Bauteil-Auflösung (1–2 m) plus vorberechnete Bruchstücke. Das ist fein genug, dass jeder Einschlag ein eigenes Loch an der getroffenen Stelle reißt. Volle Detailstufe gibt es nur für Gebäude nahe der Kamera.

**Häuser nach Bedürfnis und Vermögen (Nutzer, 2026-10-26 16:38 UTC):** Wer baut, bestimmt das Haus. Die Simulation liefert für jeden Bauauftrag einen **Bauwunsch**: Vermögen, Haushaltsgröße, Beruf/Stand, Bedürfnisse (Platz, Sicherheit, Ansehen, Nähe zur Arbeit), Geschmack der Kultur, Epoche, verfügbare Baustoffe und Bauplatz. Daraus erzeugt ein Grundriss-Generator ein eigenes Haus:
- Arm: kleine Hütte oder Kate, ein Raum, einfache Baustoffe (Lehm, Holz, Wellblech je nach Epoche), oft selbst gebaut, Anbauten nach und nach.
- Mittel: Bürgerhaus, 2–3 Zimmer, ein Stockwerk mehr, kleiner Garten oder Werkstatt.
- Reich: Villa, mehrere Flügel und Stockwerke, teure Baustoffe (Naturstein, Glas), Garten, Mauer, Brunnen oder Pool, Verzierungen.
- Sehr reich oder mächtig: Herrenhaus, Palast, Anwesen mit Nebengebäuden.
- Stadt: Reihenhaus, Mietshaus, Hochhaus (je Epoche); Armut zeigt sich als Überbelegung und Verfall, Reichtum als Penthouse oder Stadtvilla.
Häuser verändern sich mit ihren Bewohnern: Anbau bei Familienzuwachs, Renovierung bei Aufstieg, Verfall bei Armut oder Leerstand. Gleicher Bauwunsch + Seed = gleiches Haus, also deterministisch und speicherbar (gespeichert wird der Bauwunsch, nicht das Mesh). Ehrlich: Das braucht individuelle NPCs mit Vermögen und Bedürfnissen (VISION M4). Bis dahin baut die Civ ihre heutigen Typen (Hütte, Steinhaus, Mietshaus), die der Generator schon mit Varianten zeigen kann.

**Jedes Haus ist begehbar (Nutzer, 2026-10-26 16:42 UTC):** Gebäude sind keine leeren Hüllen. Gelesen als: NPCs gehen wirklich hinein und leben darin, und der Spieler kann in jedes Haus hineinsehen und den Bewohnern folgen. Eine eigene Ego-Perspektive zum Herumlaufen wäre ein späterer Zusatz und ist nicht geplant, solange der Nutzer sie nicht ausdrücklich will.
- **Innenräume aus demselben Grundriss:** Der Grundriss-Generator legt Räume an (Wohnen, Schlafen, Küche/Herd, Bad, Werkstatt, Lager; je Epoche und Vermögen). Daraus entstehen Innenwände, Innentüren, Treppen und Decken aus demselben Bauteil-Baukasten. Außen und innen passen also immer zusammen, und ein Artillerieloch zeigt den echten Raum dahinter.
- **Möblierung nach Bewohner:** Möbel und Gegenstände je Raumtyp, Epoche, Kultur und Vermögen (Katalog §8.1). Ein armer Haushalt hat Strohlager und Feuerstelle, ein reicher Bett, Kamin, Teppiche und Bilder. Gespeichert werden nur Bauwunsch und Seed; Besitztümer, die die Simulation kennt (VISION: Besitz der NPCs), werden an passender Stelle gezeigt.
- **Hineinsehen:** Nähert sich die Kamera oder ist ein Bewohner ausgewählt, werden Dach und obere Stockwerke ausgeblendet und die Wände zur Kamera hin abgeschnitten (Schnittansicht). Licht innen: Fenster, Kerzen, Lampen je Epoche; nachts leuchten bewohnte Häuser.
- **Wege im Haus:** Das Weltraster hat 4-m-Kacheln, für Räume zu grob. Jedes Gebäude bekommt deshalb ein eigenes, feines Wegenetz (Räume, Türen, Treppen als Graph plus feines Raster im Raum), das an den Haustüren an die Welt-Pfadsuche anschließt. Das betrifft World (Besitzer der Pfadsuche) und muss mit World abgestimmt werden.
- **Leistung:** Innenräume werden nur für Häuser in Kameranähe oder mit ausgewählten Bewohnern erzeugt und angezeigt. Alle anderen Häuser rechnen ihre Bewohner nur als Daten (VISION V1, Simulationsstufen).
- **Stufe:** Innenräume brauchen individuelle NPCs (M4). Den Baukasten mit Innenteilen kann Art/Assets zusammen mit A7 vorbereiten.

**Bauqualität und Bauschäden („der Architekt hat es versaut“):** Jedes Bauteil bekommt beim Bau einen Qualitätswert. Er ergibt sich aus Können und Sorgfalt von Architekt und Bauleuten (NPC-Attribute, M4), Baustoffqualität, Zeitdruck und Geld (Pfusch beim Sparen) sowie dem Baugrund (Hang, Sumpf, Erdbebengebiet). Schlechte Qualität senkt die Tragfähigkeit in der Statik (Punkt 4 oben). Folgen zeigen sich mit der Zeit:
- sichtbare Risse, durchhängende Decken, schiefe Wände, undichte Dächer (Wasserflecken, Pfützen im Haus);
- Einsturz bei Belastung: Schneelast auf dem Dach, Starkregen und unterspülter Boden, Sturm, Erdbeben, Überfüllung, Alter;
- Ereignis in der Chronik mit Ursache (zum Beispiel „Dach eingestürzt, Ursache: Pfusch von Architekt X“), Opfer, Folgen wie Ermittlung, Gericht oder Rufschaden des Architekten (VISION: Straftaten mit Folgen).
Die Darstellung nutzt dafür denselben Bruch-Baukasten wie bei Artillerie, nur langsam (Risse, Absacken) oder plötzlich (Einsturz).

**Zuständigkeit und Stufe:** Gebäudedaten und Bauteil-Schaden liegen bei Civ, Schadensereignisse bei Military (`ICombatService`), Baukasten und Bruch-Werkzeug bei Art/Assets, der Zusammenbau in der Darstellung bei Presentation. Die bleibende Zerstörung gehört laut VISION zu V10/M7, Epochen-Gebäude setzen das Zeitalter-Modell aus M3 voraus. Den Baukasten mit Bruchstücken kann Art/Assets nach dem Style Lock (A7) vorbereiten.

**Werkzeug Blender für Gebäude (Nutzer erlaubt, 2026-10-26):** Der Baukasten wird in **Blender** gebaut (kostenlos, GPL; die damit erstellten Modelle gehören uns):
- Bauteile mit echter Detailgeometrie, abgerundeten Kanten (Bevel) und sauberen UVs modelliert, je Bauteil hoch- und niedrigaufgelöste Fassung; Normal- und AO-Karten von hoch nach niedrig gebacken.
- Bauteil-Regeln (Raster 1–2 m, Anschlusspunkte, Pivot) sind fest, damit Unity jedes Haus aus dem Bauwunsch zusammensetzen kann (§4.4). Der Grundriss-Generator läuft in Unity, nicht in Blender, weil die Simulation zur Laufzeit neue Häuser bestellt.
- Bruchstücke für die Zerstörung werden ebenfalls in Blender vorberechnet (Voronoi-Zerlegung je Bauteil) und als eigene Meshes exportiert.
- Export FBX, Import nach ARCHITECTURE §16 (Maßstab, Pivot, URP-Material, LODs).
- Blender lässt sich per Python-Skript ohne Oberfläche steuern. Damit sind wiederholbare Bauteil-Erzeugung und Vorschau-Renders möglich. In der Cloud ist Blender 4.0.2 über apt installierbar (am 2026-10-26 nur die Paketverfügbarkeit geprüft, nicht installiert oder getestet), auf dem PC des Nutzers die aktuelle Version.
- Gegen „AI-Slop“: Jedes Bauteil wird gezielt gebaut und im Spiel-Licht neben R1/R2 geprüft. Kein Massenexport ungeprüfter, generierter Modelle. Was den Vergleich nicht besteht, bleibt im Katalog auf „in Arbeit“.

Blender kann auch für Pflanzen helfen (Blattatlanten rendern, Impostor-Bilder backen). Der Baumgenerator selbst bleibt aber PlantForge in Unity (§4.1), weil Zustände und Wind dort zusammenhängen.

### 4.5 Menschen und Einheiten

Schwierigster Bereich (Modellierung + Rig + Animation). Vorschlag: **ein** eigener realistisch proportionierter Grundkörper, Kleidung/Ausrüstung je Epoche als Module, Animationen auf einem gemeinsamen Humanoid-Rig. Möglicher Startpunkt für den Grundkörper: Open-Source-Charaktergeneratoren (z. B. MakeHuman). **Lizenz der Ausgaben nicht geprüft**, vor Nutzung klären. Fahrzeuge: eigene Modelle nach demselben Baukastenprinzip; die Kandidaten aus `PHASE5_MILITARY_ASSETS.md` bleiben nur als Referenz/Notlösung.

### 4.6 Effekte

Unity Particle System, gepoolt (UI_STYLE_GUIDE A6). Feuer, Rauch, Glut, Asche, Funkenflug, Blitz, Regen, Schnee, fliegende Blätter, Staub, Gischt, Überflutung. Flipbook-Texturen möglichst selbst erzeugt oder aus **einer** geprüften CC0-Quelle, damit sie zusammenpassen. Ferne Brände: Glut-Decal + Rauchsäule statt vieler Partikel.

## 5. Lebende Modelle: Zustände jeder Pflanze

Jede Pflanze hat dieselben Zustände. Fast alle kommen **ohne zusätzliches Modell** aus: gleicher Mesh, andere Shader-Werte oder Rotation.

| Zustand | Umsetzung |
|---|---|
| Keimling, jung, ausgewachsen, alt | Skalierung + eigene Seeds für junge Formen (weniger Äste) |
| Jahreszeiten (Austrieb, Sommer, Herbstfärbung, kahl) | Shader-Parameter: Laubfarbe, Laubdichte; Schnee, Nässe und Frost über §5.1 |
| Trockenstress / Dürre | Laub gelb-braun, lichter (Blätter nach Hierarchie ausgeblendet) |
| Krank | Flecken, lichtere Krone |
| Tot stehend | kein Laub, graue Rinde, einzelne Äste abgebrochen |
| Brennend | Feuer-Partikel an Ästen, Glühen im Shader, Laub verschwindet nach Brandfortschritt |
| Verkohlt | schwarze Rinde, feine Äste weg, Asche am Boden (Decal) |
| Umgestürzt (Sturm, Tsunami, Fällung) | gleicher Mesh gekippt in Wind- bzw. Wellenrichtung + Wurzelteller-Mesh; Fallrichtung kommt aus der Simulation |
| Stumpf (gefällt) | eigene Generator-Ausgabe (Stamm abgeschnitten) |
| Nachwachsen | Sukzession: Gras → Kräuter → Sträucher → Pionierbäume → Wald (§6.2) |

### 5.1 Wetter- und Oberflächenzustände für **alle** Assets (Nutzer, 2026-10-26 16:36 UTC)

Alle Materialien im Spiel (Pflanzen, Gebäude, Felsen, Gelände, Fahrzeuge, Menschen) nutzen **eine gemeinsame Shader-Schicht**. Sie wird nur einmal gebaut, deshalb sehen die Zustände überall gleich aus. Zustände lassen sich kombinieren, zum Beispiel nass und verschneit oder Schnee über Glut (schmilzt und dampft).

| Zustand | Aussehen | Umsetzung |
|---|---|---|
| Nass | dunklere Farben, glänzende Oberflächen, Tropfen auf Blättern und Glas, Pfützen am Boden, Rinnsale an Wänden; Trocknen von oben nach unten | Nässe-Wert: Albedo dunkler, Glanz höher, Tropfen-Normalmap; Pfützen als Gelände-Maske |
| Schnee | Schnee auf allem, was nach oben zeigt: Blätter, Äste, Nadeln, Dächer, Fensterbänke, Fahrzeuge, Felsen; Nadelbäume halten mehr Schnee; Schneehöhe wächst und taut | Schneemenge × Ausrichtung der Fläche nach oben × Bewuchs-Abschattung; ab hoher Last biegen sich Zweige (Wind-Daten, §4.1) |
| Raureif, Eis | feiner weißer Belag, vereiste Pfützen und Gewässer | Frost-Wert, eigene Detailtextur |
| Brennend | Flammen an Ästen, Dachbalken und Fenstern, Glühen der Oberfläche, Rauch | Brand-Wert pro Objekt: Glüh-Maske im Shader + Partikel an im Asset hinterlegten Brandpunkten |
| Verkohlt, glimmend | schwarz, rissig, Glutnester, Asche | Verkohlungs-Wert, Ruß-Detailtextur |
| Staub, Ruß, Schlamm | nach Explosionen, Bränden, Fluten | Schmutz-Werte nach Art |

Boden und Gebäude ausdrücklich eingeschlossen: geschlossene Schneedecke auf dem Gelände mit Fußspuren und Fahrspuren, Schneewehen an Hauswänden, Schnee auf Dächern, Mauern, Zäunen und Fensterbänken, Eiszapfen an Traufen; bei Nässe Pfützen auf Wegen, Plätzen und in Senken, die sich mit Regen füllen und wieder austrocknen, nasse Fassaden mit Wasserlaufspuren, Matsch auf unbefestigten Wegen. Schneelast und Nässe wirken zurück auf die Bau-Statik (§4.4, Bauqualität).

Woher die Werte kommen: Bis M5 setzen Jahreszeit und Testregler die Werte, damit die Zustände im Style Board geprüft werden können. Ab M5 liefert die Wetter- und Umweltsimulation die Werte (Regen, Schneehöhe, Temperatur, Brand). Brand und Schaden pro Objekt kommen aus dem Brand- und Schadenssystem.

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

**Entscheidung des Nutzers (2026-10-26 16:28 UTC): Umwelt bleibt bei M5.** A3–A6 (Vegetationsfeld, Blitz/Brand/Feuerwehr, Sturm, Tsunami) beginnen erst mit M5. Nach dem M1-Test kommen nur A1/A2 (neue Bäume, Pflanzen, Felsen, Zustände per Shader) und später A7.

## 9. Übergabe (Format laut Projektregeln §9)

**Aufgabe:** Zentraler Asset-Katalog + Konzept für eigenen Asset-Stil und Umweltkopplung.

**Ergebnis:** Zwei neue Vorschlagsdokumente (dieses + `ASSET_CATALOG.md`). Kein Code, kein Asset erzeugt oder importiert.

**Geänderte Dateien:** nur neu: `Docs/proposals/ASSETS_STYLE_ENVIRONMENT.md`, `Docs/proposals/ASSET_CATALOG.md`.

**Schnittstellen:** World (VegetationLayer, ProceduralMeshes, Biome-Enum, TileFlags), Civ (Feuerwehr-Einheiten, Lösch-KI), UI (VFX), Save (neue Sektion `environment`), Chronik (M2).

**Abhängigkeiten:** M1-Test; Unity-Projekt im Repo (DevOps); Style Lock (Phase 6); Entscheidung zu KA1–KA4.

**Tests:** keine (reine Dokumentation). Code-Befunde in §1 durch Lesen der genannten Dateien geprüft.

**Offene Probleme:** KA1–KA4 übernommen (70a2d99); Umwelt bleibt laut Nutzer bei M5; MakeHuman-Lizenz ungeprüft; Texturquellen-Lizenzen je Datei noch zu prüfen; Polygon-Budgets nur geschätzt.

**Nächste Schritte:** (erledigt: Regeln, Besitzer, Platzhalter-Register, Biome.) Umwelt bleibt bei M5 (Nutzer). Nach M1-Test: Etappe A1.

**Dokumentation:** PROJECT_MASTER §8.1, UI_STYLE_GUIDE A1/A3, ARCHITECTURE §16 (Budgets), INTEGRATION §3, ASSET_CREDITS §3/§4, VISION §4 (Reihenfolge Umwelt), CURRENT_STATE.
