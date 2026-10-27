# World Beyond – ASSET-KATALOG (alle Modelle, Pflanzen, Effekte)

Status: **Vorschlag [E]**, gepflegt vom Bereich „Art/Assets“ (Thread „Asset-Katalog für alle Pflanzen & Modelle aufbauen“). Stand: 2026-10-27.
Stil, Herstellung und Umweltkopplung: `Docs/proposals/ASSETS_STYLE_ENVIRONMENT.md`.
Lizenzen importierter Fremddateien stehen weiterhin **nur** in `Assets/ThirdParty/ASSET_CREDITS.md`. Dieser Katalog zählt dagegen **alles auf, was im Spiel sichtbar sein soll**, egal ob selbst gemacht oder importiert.

**Ehrlicher Gesamtstand: Es ist noch kein einziges endgültiges Asset fertig. Sichtbar im Spiel sind nur 3 Ersatzmodelle und Ersatzmaterialien (Platzhalter-Register §9).**

## 0. Regeln

### 0.1 Status (einheitlich, passend zu den Projektregeln §7)

| Status | Bedeutung |
|---|---|
| geplant | nur im Katalog, nichts erzeugt |
| Platzhalter | im Spiel sichtbar, aber Ersatz (steht in §9 mit Ersatzplan) |
| in Arbeit | wird gerade erzeugt/modelliert |
| implementiert | liegt im Projekt und wird im Spiel benutzt, aber nicht abgenommen |
| abgenommen | im Spiel gerendert (Nah/Mittel/Fern, alle Zustände), Stilregeln erfüllt, vom Nutzer per Screenshot bestätigt |
| blockiert | Hindernis benannt in „Bemerkung“ |

### 0.2 IDs

- Pflanzen: `plant.<gattung>` mit dem botanischen Gattungsnamen (stabil, eindeutig), z. B. `plant.quercus`. Arten später als `plant.<gattung>.<art>` (Etappe P4).
- Wuchsformen: `form.W01` … `form.W27`.
- Sonst: `<bereich>.<name>`: `rock.*`, `terrain.*`, `water.*`, `bld.*`, `unit.*`, `veh.*`, `fx.*`, `decal.*`, `prop.*`, `fauna.*`.
- Wo die Simulation einen Inhaltsschlüssel hat (z. B. `building.farm`, `unit.worker`), steht er in der Spalte „Sim-Schlüssel“. Die Asset-ID ersetzt ihn nicht.
- IDs werden nie umbenannt oder wiederverwendet (wie ARCHITECTURE §11.1).

### 0.3 Herstellungsweg (Spalte „Weg“, Standard für Pflanzen = PF)

PF = eigener Generator PlantForge · PR = eigene Prozedur (Fels, Wasser, Gelände) · MB = eigener modularer Baukasten (Gebäude, Fahrzeuge) · CH = eigener Charakter-Grundkörper · PS = Partikelsystem/Shader · T0 = zusätzlich CC0-Fototextur (Lizenz je Datei prüfen, in ASSET_CREDITS eintragen).

### 0.4 Zustände

Jede Pflanze hat die Zustände aus `ASSETS_STYLE_ENVIRONMENT.md` §5 (jung … alt, Jahreszeiten, trocken, krank, tot, brennend, verkohlt, umgestürzt, Stumpf). Gebäude: Baustelle, intakt, beschädigt, brennend, Ruine, Trümmer. Sie werden nicht einzeln gezählt; ein Asset ist erst „abgenommen“, wenn alle seine Zustände abgenommen sind.

### 0.5 Pflege

Wer ein Asset anlegt, ändert, ersetzt oder entfernt, aktualisiert hier sofort die Zeile und die Zähler in §1. Etappen-Kürzel (A1 …, P1 …) stammen aus `ASSETS_STYLE_ENVIRONMENT.md` §7/§8.

### 0.6 Archetypen, unbegrenzte Varianten (Nutzer, 2026-10-26 16:45 UTC)

Der Nutzer will unendlich viele Arten von Möbeln, Häusern usw., und alles soll von den NPCs im Spiel gemacht werden. Für den Katalog heißt das: **Eine Zeile ist ein Archetyp (Bauplan-Familie), kein einzelnes Modell.** Die sichtbaren Varianten entstehen aus
- endlich vielen, sorgfältig gebauten Bauteilen pro Archetyp (z. B. Tischplatte, Beine, Zarge, Schnitzerei; Wand, Fenster, Dach),
- Parametern, die der herstellende NPC festlegt: Maße, Form, Material, Verzierung, Baustil seines Stammes (von der Spiel-KI gewählt) und Epoche, Qualität aus seinem Können,
- einem Seed.
Damit gibt es unbegrenzt viele Kombinationen. Ehrliche Grenze: Jedes Bauteil muss trotzdem einzeln in R1/R2-Qualität gebaut werden. Die Vielfalt wächst mit der Zahl der Bauteile, die wir herstellen.

Jedes von NPCs hergestellte Objekt trägt in der Simulation Hersteller, Qualität und Parameter. Der Katalog zählt nur Archetypen und Bauteil-Sätze. Wie NPCs entwerfen, bauen und handeln, gehört nicht in diesen Katalog, sondern zu den zuständigen Systemen (weitergegeben).

### 0.7 Keine Quatsch-Objekte, Physik muss passen (Nutzer, 2026-10-26 16:57 UTC, verbindlich)

Jede Variante, die ein Generator oder ein NPC aus einem Archetyp erzeugt, muss diese Prüfungen bestehen, sonst wird sie verworfen und nicht angezeigt:
1. **Funktion:** Das Objekt erfüllt seinen Zweck für einen Menschen von 1,5–2 m. Sitzhöhe ca. 40–50 cm, Tischhöhe ca. 70–80 cm, Bett mindestens Körperlänge, Türen mindestens ca. 2 m hoch, Treppenstufen ca. 15–20 cm hoch und ca. 25–30 cm tief. Werte je Epoche und Kultur nur in plausiblen Grenzen.
2. **Statik:** Der Schwerpunkt liegt über der Standfläche. Lastende Teile ruhen auf tragenden Teilen (Tischplatte auf Beinen, Dach auf Wänden, Obergeschoss auf Wand oder Stütze). Nichts schwebt, nichts steckt im Boden oder in einem anderen Objekt.
3. **Material:** Querschnitte passen zum Material und zur Last (keine Bleistift-Beine unter einer Steinplatte, keine Glasbrücke im Mittelalter). Gewicht wird aus Volumen × Materialdichte berechnet und für Physik und Transport mitgegeben.
4. **Epoche:** Nur Materialien und Techniken, die die Civ zu dieser Zeit kennt (Technologien aus `CivContent`). Keine Anachronismen.
5. **Platz:** Das Objekt passt durch Tür und Treppe in den Raum, in dem es steht. Möbel stehen mit Abstand so, dass man daran vorbeigehen kann.
6. **Optik:** R1/R2-Qualität (Konzept §3); keine verzerrten Texturen bei Größenänderung (Texeldichte bleibt gleich).

Die Prüfung läuft automatisch im Generator. Zusätzlich wird für jeden Archetyp eine Stichprobe von Varianten im Spiel-Licht gerendert und vom Nutzer geprüft, bevor der Archetyp auf „abgenommen“ steht. Wie sich Objekte im laufenden Spiel physikalisch verhalten (Umfallen, Einsturz, Trümmer), gehört zum zuständigen Systembereich und wurde weitergegeben. Der Katalog liefert dafür je Objekt Gewicht, Schwerpunkt, Standfläche und Kollisionsform.

## 1. Zähler (Stand 2026-10-27)

| Bereich | Einträge | geplant | Platzhalter | in Arbeit | implementiert | abgenommen |
|---|---|---|---|---|---|---|
| Wuchsformen (§2.1) | 27 | 25 | 2 (W01, W05 über P-001/P-002) | 0 | 0 | 0 |
| Bäume (§2.2) | 44 | 44 | 0 | 0 | 0 | 0 |
| Sträucher (§2.3) | 12 | 12 | 0 | 0 | 0 | 0 |
| Kakteen, Sukkulenten (§2.4) | 6 | 6 | 0 | 0 | 0 | 0 |
| Gräser, Kräuter, Farne, Bodendecker (§2.5) | 19 | 19 | 0 | 0 | 0 | 0 |
| Wasserpflanzen (§2.6) | 6 | 6 | 0 | 0 | 0 | 0 |
| Nutzpflanzen (§2.7) | 17 | 17 | 0 | 0 | 0 | 0 |
| Fels, Gelände, Wasser (§3) | 26 | 13 | 13 (über P-003, P-004, P-005) | 0 | 0 | 0 |
| Gebäude (§4) | 59 | 59 | 0 | 0 | 0 | 0 |
| Personen, Einheiten, Fahrzeuge (§5) | 36 | 36 | 0 | 0 | 0 | 0 |
| Tiere (§6) | 10 | 10 | 0 | 0 | 0 | 0 |
| Effekte, Decals (§7) | 26 | 26 | 0 | 0 | 0 | 0 |
| Requisiten (§8) | 14 | 14 | 0 | 0 | 0 | 0 |
| Innenausstattung (§8.1) | 14 | 14 | 0 | 0 | 0 | 0 |
| Werkzeuge, Epochen-Gegenstände (§8.2) | 30 | 30 | 0 | 0 | 0 | 0 |
| **Summe** | **346** | **331** | **15** (5 Platzhalter-Dateien P-001–P-005) | **0** | **0** | **0** |

Pflanzen-Gattungen gesamt (§2.2–2.7): **104** (Etappe P2). Ziel P3: 250–400 Gattungen, P4: Arten als Datensätze.

## 2. Pflanzen

### 2.1 Wuchsformen (Etappe P1) – jede Pflanze der Erde gehört zu einer davon

| ID | Wuchsform | Kronen-/Formhülle | Beispiele | Etappe | Status |
|---|---|---|---|---|---|
| form.W01 | Laubbaum, rundkronig | Kugel/Ei | Eiche, Buche, Linde | A1 | Platzhalter (P-002); Machbarkeitstest v1 in Blender 2026-10-27, nicht im Spiel |
| form.W02 | Laubbaum, hoch/schlank | gestrecktes Ei | Esche, Birke, Erle | A2 | geplant |
| form.W03 | Schirmbaum | flacher Schirm | Akazie, Kapok, Mesquite | A2 | geplant |
| form.W04 | Hängeform | Kugel mit hängenden Ruten | Trauerweide | A2 | geplant |
| form.W05 | Nadelbaum, kegelförmig | Kegel, Quirle | Fichte, Tanne, Lärche | A1 | Platzhalter (P-001) |
| form.W06 | Nadelbaum, locker/schirmartig | unregelmäßig, hoher Stamm | Kiefer, Pinie, Zeder | A2 | geplant |
| form.W07 | Säulenform | schmale Säule | Zypresse, Säulenpappel | A2 | geplant |
| form.W08 | Palme / Riesenstaude | Stamm + Wedelkrone | Kokos-, Dattelpalme, Banane | A2 | geplant |
| form.W09 | Baumfarn | Stamm + Farnwedel | Cyathea | A2 | geplant |
| form.W10 | Bambus/Halmhorst | Halmbündel | Bambus, Zuckerrohr | A2 | geplant |
| form.W11 | Flaschenbaum | dicker Stamm, kleine Krone | Baobab | A2 | geplant |
| form.W12 | Stelzwurzelbaum | Krone auf Wurzelgerüst | Mangrove | A2 | geplant |
| form.W13 | Strauch, rund | Kugel bodennah | Hasel, Holunder | A1 | geplant |
| form.W14 | Zwergstrauch/Polster | flaches Polster | Heide, Heidelbeere | A2 | geplant |
| form.W15 | Säulenkaktus/-wolfsmilch | Säulen mit Armen | Saguaro, Kandelaber-Wolfsmilch | A2 | geplant |
| form.W16 | Kugel-/Scheibenkaktus | Kugeln, Scheibenglieder | Opuntie, Kugelkaktus | A2 | geplant |
| form.W17 | Rosette | Blattrosette | Agave, Yucca, Aloe | A2 | geplant |
| form.W18 | Gras/Horst | Halmbüschel | Wiesengras, Federgras | A2 | geplant |
| form.W19 | Röhricht | hohe Halme | Schilf, Rohrkolben | A2 | geplant |
| form.W20 | Farn | Wedeltrichter | Adlerfarn, Wurmfarn | A2 | geplant |
| form.W21 | Kraut/Blühpflanze | Stängel + Blüte | Mohn, Enzian, Löwenzahn | A2 | geplant |
| form.W22 | Kletterpflanze/Liane | folgt Träger | Efeu, Wein, Lianen | A2 | geplant |
| form.W23 | Schwimmblattpflanze | Blätter auf Wasser | Seerose, Lotus, Wasserlinse | A2 | geplant |
| form.W24 | Unterwasserpflanze | wogende Bänder | Seegras, Tang | A2 | geplant |
| form.W25 | Moos/Flechte | Bodendecke (Shader/Decal) | Torfmoos, Rentierflechte | A2 | geplant |
| form.W26 | Feldbestand | Reihen/Flächen | Getreide, Kartoffel | A2 | geplant |
| form.W27 | Kriechpflanze/Bodendecker | flach, rankend | Klee, Kürbis | A2 | geplant |

### 2.2 Bäume (Etappe P2)

Biome laut `BiomeType`. „Tropen“ = `TropicalForest` (9), „Mittelmeer“ = `Mediterranean` (10), seit 2026-10-26 in der Simulation (World, 24bd411 auf `integration/m1`).

| ID | Name | Wuchsform | Biome | Status |
|---|---|---|---|---|
| plant.quercus | Eiche | W01 | Temperate, Mittelmeer (Steineiche) | geplant |
| plant.fagus | Buche | W01 | Temperate | geplant |
| plant.acer | Ahorn | W01 | Temperate | geplant |
| plant.fraxinus | Esche | W02 | Temperate | geplant |
| plant.tilia | Linde | W01 | Temperate | geplant |
| plant.betula | Birke (auch Zwergbirke) | W02 / W14 | Temperate, Boreal, Tundra | geplant |
| plant.carpinus | Hainbuche | W01 | Temperate | geplant |
| plant.castanea | Edelkastanie | W01 | Temperate, Mittelmeer | geplant |
| plant.juglans | Walnuss | W01 | Temperate | geplant |
| plant.prunus | Kirsche, Pflaume, Wildobst | W01 | Temperate | geplant |
| plant.malus | Apfel | W01 | Temperate (auch Obstgarten) | geplant |
| plant.pyrus | Birne | W01 | Temperate (auch Obstgarten) | geplant |
| plant.sorbus | Eberesche | W02 | Temperate, Boreal | geplant |
| plant.ulmus | Ulme | W01 | Temperate | geplant |
| plant.populus | Pappel, Espe | W07 / W02 | Temperate, Boreal, Wetland | geplant |
| plant.salix | Weide (auch Zwergweide) | W04 / W14 | Wetland, Temperate, Tundra | geplant |
| plant.alnus | Erle | W02 | Wetland | geplant |
| plant.platanus | Platane | W01 | Temperate (Siedlungen) | geplant |
| plant.picea | Fichte | W05 | Boreal, Alpine, Temperate | geplant |
| plant.abies | Tanne | W05 | Boreal, Alpine | geplant |
| plant.pinus | Kiefer, Pinie, Latsche | W06 / W14 | Boreal, Temperate, Coast, Alpine, Mittelmeer | geplant |
| plant.larix | Lärche | W05 | Boreal, Alpine | geplant |
| plant.juniperus | Wacholder | W13 / W07 | Boreal, Tundra, Alpine | geplant |
| plant.taxus | Eibe | W13 | Temperate | geplant |
| plant.cupressus | Zypresse | W07 | Mittelmeer | geplant |
| plant.sequoia | Küstenmammutbaum | W05 | Temperate (Küste) | geplant |
| plant.taxodium | Sumpfzypresse | W05 | Wetland | geplant |
| plant.cedrus | Zeder | W06 | Alpine, Mittelmeer | geplant |
| plant.vachellia | Schirmakazie | W03 | Savanna, Desert | geplant |
| plant.adansonia | Baobab | W11 | Savanna | geplant |
| plant.ficus | Feige, Würgefeige | W01 | Tropen | geplant |
| plant.ceiba | Kapokbaum | W03 | Tropen | geplant |
| plant.swietenia | Mahagoni | W01 | Tropen | geplant |
| plant.dipterocarpus | Flügelfruchtbaum | W02 | Tropen | geplant |
| plant.phoenix | Dattelpalme | W08 | Desert (Oase) | geplant |
| plant.cocos | Kokospalme | W08 | Coast (warm) | geplant |
| plant.rhizophora | Mangrove | W12 | Coast, Wetland (warm) | geplant |
| plant.cyathea | Baumfarn | W09 | Tropen | geplant |
| plant.bambusa | Bambus | W10 | Tropen, Temperate (warm) | geplant |
| plant.olea | Olivenbaum | W01 | Mittelmeer (auch Hain) | geplant |
| plant.eucalyptus | Eukalyptus | W02 | Savanna, Mittelmeer | geplant |
| plant.tamarix | Tamariske | W13 | Desert, Coast | geplant |
| plant.prosopis | Mesquite | W03 | Desert, Savanna | geplant |
| plant.citrus | Zitrus | W01 | Mittelmeer, Tropen (Obstgarten) | geplant |

### 2.3 Sträucher und Zwergsträucher

| ID | Name | Wuchsform | Biome | Status |
|---|---|---|---|---|
| plant.corylus | Hasel | W13 | Temperate | geplant |
| plant.sambucus | Holunder | W13 | Temperate | geplant |
| plant.crataegus | Weißdorn | W13 | Temperate | geplant |
| plant.rubus | Brombeere, Himbeere | W13 | Temperate, Boreal | geplant |
| plant.rosa | Wildrose | W13 | Temperate | geplant |
| plant.vaccinium | Heidelbeere, Preiselbeere | W14 | Boreal, Tundra | geplant |
| plant.calluna | Heidekraut | W14 | Temperate, Boreal | geplant |
| plant.rhododendron | Alpenrose | W13 | Alpine | geplant |
| plant.larrea | Kreosotbusch | W13 | Desert | geplant |
| plant.lavandula | Lavendel | W14 | Mittelmeer | geplant |
| plant.cistus | Zistrose | W14 | Mittelmeer | geplant |
| plant.dryas | Silberwurz | W14 | Tundra, Alpine | geplant |

### 2.4 Kakteen, Sukkulenten, Rosetten

| ID | Name | Wuchsform | Biome | Status |
|---|---|---|---|---|
| plant.carnegiea | Saguaro (Säulenkaktus) | W15 | Desert | geplant |
| plant.euphorbia | Kandelaber-Wolfsmilch | W15 | Savanna, Desert | geplant |
| plant.opuntia | Feigenkaktus | W16 | Desert, Mittelmeer | geplant |
| plant.agave | Agave | W17 | Desert | geplant |
| plant.yucca | Palmlilie | W17 | Desert | geplant |
| plant.aloe | Aloe | W17 | Desert, Savanna | geplant |

### 2.5 Gräser, Kräuter, Farne, Bodendecker

| ID | Name | Wuchsform | Biome | Status | Bemerkung |
|---|---|---|---|---|---|
| plant.festuca | Schwingel (Wiesen-/Trockengras) | W18 | Temperate, Alpine | geplant | |
| plant.stipa | Federgras | W18 | Savanna, Desert, Temperate (trocken) | geplant | Steppe gibt es nicht als Biom (s. u.) |
| plant.themeda | Savannengras | W18 | Savanna | geplant | |
| plant.ammophila | Strandhafer | W18 | Coast | geplant | |
| plant.eriophorum | Wollgras | W18 | Tundra, Wetland | geplant | |
| plant.carex | Segge | W18 | Wetland, Tundra | geplant | |
| plant.phragmites | Schilf | W19 | Wetland, Ufer | geplant | |
| plant.typha | Rohrkolben | W19 | Wetland | geplant | |
| plant.pteridium | Adlerfarn | W20 | Temperate | geplant | |
| plant.dryopteris | Wurmfarn | W20 | Temperate, Boreal | geplant | |
| plant.trifolium | Klee | W27 | Temperate | geplant | |
| plant.taraxacum | Löwenzahn | W21 | Temperate | geplant | |
| plant.papaver | Mohn | W21 | Temperate, Mittelmeer | geplant | |
| plant.gentiana | Enzian | W21 | Alpine | geplant | |
| plant.monstera | Fensterblatt (Unterwuchs) | W21 | Tropen | geplant | |
| plant.hedera | Efeu | W22 | Temperate | geplant | |
| plant.lianen | Lianen (Sammelgruppe) | W22 | Tropen | geplant | Gruppe, keine Gattung |
| plant.sphagnum | Torfmoos | W25 | Wetland, Boreal | geplant | |
| plant.cladonia | Rentierflechte | W25 | Tundra, Boreal | geplant | Flechte, botanisch keine Pflanze |

Hinweis Prüfung 2026-10-27: Die Simulation kennt kein eigenes Steppen- oder Graslandbiom (gemäßigtes Grasland, Prärie). Federgras, Wildrinder und Pferde stehen deshalb bei Savanna/Temperate. Ein Biom `Grassland` wäre eine Anfrage an World (Enum anhängen); offen, nicht dringend.

### 2.6 Wasserpflanzen

| ID | Name | Wuchsform | Ort | Status | Bemerkung |
|---|---|---|---|---|---|
| plant.nymphaea | Seerose | W23 | See, Sumpf | geplant | |
| plant.nelumbo | Lotus | W23 | See (warm) | geplant | |
| plant.lemna | Wasserlinse | W23 | Teich | geplant | als Oberflächen-Shader |
| plant.potamogeton | Laichkraut | W24 | Fluss, See | geplant | |
| plant.zostera | Seegras | W24 | Flachwasser Meer | geplant | |
| plant.laminaria | Kelp/Tang | W24 | Küste kalt | geplant | Alge, botanisch keine Pflanze |

### 2.7 Nutzpflanzen (Felder, Plantagen; Bezug zu `building.farm`)

| ID | Name | Wuchsform | Klima | Status |
|---|---|---|---|---|
| plant.triticum | Weizen | W26 | gemäßigt | geplant |
| plant.hordeum | Gerste | W26 | gemäßigt, kühl | geplant |
| plant.secale | Roggen | W26 | kühl | geplant |
| plant.oryza | Reis (Nassfeld) | W26 | warm, feucht | geplant |
| plant.zea | Mais | W26 | warm | geplant |
| plant.solanum | Kartoffel | W26 | gemäßigt | geplant |
| plant.brassica | Kohl | W26 | gemäßigt | geplant |
| plant.beta | Rübe | W26 | gemäßigt | geplant |
| plant.linum | Flachs | W26 | gemäßigt | geplant |
| plant.gossypium | Baumwolle | W26 | warm | geplant |
| plant.saccharum | Zuckerrohr | W10 | tropisch | geplant |
| plant.musa | Banane | W08 | tropisch | geplant |
| plant.coffea | Kaffee | W13 | tropisch | geplant |
| plant.camellia | Tee | W13 | subtropisch | geplant |
| plant.vitis | Weinrebe | W22 | Mittelmeer, gemäßigt | geplant |
| plant.cucurbita | Kürbis | W27 | warm-gemäßigt | geplant |
| plant.capsicum | Paprika/Chili | W21 | warm | geplant |

## 3. Fels, Gelände, Wasser

| ID | Name | Weg | Sim-Bezug | Etappe | Status | Bemerkung |
|---|---|---|---|---|---|---|
| rock.boulder | Findling | PR+T0 | `TerrainType.Rock/Mountain` | A2 | Platzhalter (P-003) | |
| rock.outcrop | Felsnase | PR+T0 | Rock | A2 | geplant | |
| rock.cliff | Felswand | PR+T0 | Mountain, steile Hänge | A2 | geplant | |
| rock.scree | Geröll | PR+T0 | Mountain | A2 | geplant | |
| rock.pebbles | Kiesel (Ufer) | PR+T0 | River, Beach | A2 | geplant | |
| terrain.grass | Gras-Boden | PR+T0 | `TerrainType.Grass` | A2 | Platzhalter (P-004) | |
| terrain.forest_floor | Waldboden (Laub, Nadeln) | PR+T0 | Forest | A2 | Platzhalter (P-004) | |
| terrain.beach | Sand | PR+T0 | Beach | A2 | Platzhalter (P-004) | |
| terrain.rock | Fels-Boden | PR+T0 | Rock | A2 | Platzhalter (P-004) | |
| terrain.mountain | Gebirge | PR+T0 | Mountain | A2 | Platzhalter (P-004) | |
| terrain.snow | Schnee | PR+T0 | Snow | A2 | Platzhalter (P-004) | |
| terrain.desert | Wüste | PR+T0 | Desert | A2 | Platzhalter (P-004) | |
| terrain.swamp | Sumpf/Schlamm | PR+T0 | Swamp | A2 | Platzhalter (P-004) | |
| terrain.tundra | Tundra-Boden | PR+T0 | Biome Tundra | A2 | geplant | |
| terrain.field | Ackerboden | PR+T0 | Farm-Einzugsgebiet | A2 | geplant | |
| terrain.road | Weg/Straße (nach Epoche) | PR+T0 | `TileFlags.Road` | A2 | geplant | |
| terrain.scorched | verbrannter Boden, Asche | PR | `TileFlags.Scorched` | A4 | geplant | heute: Kachel ohne Bewuchs |
| terrain.flooded | überfluteter Boden | PR | Tsunami/Flut | A6 | geplant | |
| terrain.crater | Krater | PR | `TileFlags.Crater` | A7 | geplant | |
| water.ocean_deep | Tiefsee | PR (Shader) | DeepWater | A2 | Platzhalter (P-005) | |
| water.ocean_shallow | Flachwasser/Küste | PR (Shader) | ShallowWater | A2 | Platzhalter (P-005) | |
| water.river | Fluss (Strömung) | PR (Shader) | River | A2 | Platzhalter (P-005) | |
| water.lake | See | PR (Shader) | `TileFlags.Lake` | A2 | Platzhalter (P-005) | |
| water.shore_foam | Uferschaum/Brandung | PS | Küstenkacheln | A2 | geplant | |
| water.ice | Eis (Winter) | PR (Shader) | Jahreszeit | A3 | geplant | |
| water.tsunami_wave | Tsunami-Wellenfront | PR+PS | Tsunami-Ereignis | A6 | geplant | |

Hinweis: Alle Terrain-Zeilen mit „Platzhalter“ teilen sich P-004, alle Wasser-Zeilen P-005.

## 4. Gebäude (Sim-Schlüssel aus `CivContent.cs`, `integration/m1`)

Derzeit werden Gebäude **gar nicht dargestellt**. Weg: MB (+T0). Jedes Gebäude gibt es **ab seiner Epoche in jeder späteren Epoche** in eigener Ausführung (Spalte „ab Epoche“, aus der benötigten Technologie in `CivContent.cs`; Frühzeit F, Antike A, Mittelalter M, Industrie I, Moderne Mo). **Wie ein Gebäude aussieht, entscheidet die Spiel-KI des jeweiligen Stammes** (Nutzer 2026-10-27, siehe „Baustil je Stamm“ unten); der Katalog liefert nur Vorgaben und Bauteil-Sätze. Alle Gebäude sind auf Bauteil-Ebene an jeder Stelle zerstörbar (Beschuss, Brand, Naturgewalt, aber auch Einsturz wegen schlechter Bauqualität) und haben die Zustände aus §0.4 sowie nass, Schnee und Brand (`ASSETS_STYLE_ENVIRONMENT.md` §4.4, §5.1). Epochen-Ausführungen: 75 für die 19 vorhandenen Gebäude, 95 mit den 4 neuen.

| ID | Name | Sim-Schlüssel | Größe (Kacheln) | ab Epoche | Etappe | Status | Bemerkung |
|---|---|---|---|---|---|---|---|
| bld.town_center | Ortskern | building.town_center | 2×2 | F | A7 | geplant | |
| bld.hut | Hütte | building.hut | 1×1 | F | A7 | geplant | |
| bld.house | Steinhaus | building.house | 1×1 | M | A7 | geplant | |
| bld.apartment | Mietshaus | building.apartment | 2×2 | I | A7 | geplant | |
| bld.farm | Farm | building.farm | 2×2 | F | A7 | geplant | Felder: §2.7 |
| bld.fishery | Fischerhütte | building.fishery | 1×1 | F | A7 | geplant | |
| bld.woodcutter | Holzfäller | building.woodcutter | 1×1 | F | A7 | geplant | erzeugt Stümpfe (§0.4) |
| bld.quarry | Steinbruch | building.quarry | 1×1 | F | A7 | geplant | |
| bld.mine | Mine | building.mine | 1×1 | A | A7 | geplant | |
| bld.gold_mine | Goldmine | building.gold_mine | 1×1 | A | A7 | geplant | |
| bld.smelter | Schmelze | building.smelter | 1×1 | A | A7 | geplant | Rauch-Effekt |
| bld.workshop | Werkstatt | building.workshop | 1×1 | M | A7 | geplant | |
| bld.oil_well | Ölförderung | building.oil_well | 1×1 | Mo | A7 | geplant | |
| bld.storehouse | Lagerhaus | building.storehouse | 1×1 | F | A7 | geplant | |
| bld.warehouse | Großlager | building.warehouse | 2×2 | M | A7 | geplant | |
| bld.library | Bibliothek | building.library | 1×1 | A | A7 | geplant | |
| bld.palisade | Palisade | building.palisade | 1×1 | F | A7 | geplant | |
| bld.stone_wall | Steinmauer | building.stone_wall | 2×1 | M | A7 | geplant | |
| bld.barracks | Kaserne | building.barracks | 2×2 | A | A7 | geplant | |
| bld.fire_station | Feuerwache (nach Epoche) | – (neu, Civ) | – | F | A4 | geplant | für Brandbekämpfung |
| bld.watchtower | Wachturm / Feuerturm | – (neu, Civ) | – | F | A4 | geplant | erhöht Sichtung |
| bld.well | Brunnen / Löschteich | – (neu, Civ) | – | F | A4 | geplant | Löschwasser |
| bld.construction_site | Baustelle (generisch) | – | – | F | A7 | geplant | |

**Wohnhaus-Klassen nach Vermögen und Bedürfnis** (Grundriss-Generator, `ASSETS_STYLE_ENVIRONMENT.md` §4.4; braucht individuelle NPCs aus M4). Die Klassen sind Größen- und Ausstattungsvorgaben, keine Bauformen: Wie das Haus aussieht, kommt aus dem Baustil des Stammes, jedes Haus wird aus dem Bauwunsch des Bauherrn erzeugt und ist dadurch einzigartig.

| ID | Klasse | Bauherr | Beispiele je Epoche | ab Epoche | Etappe | Status |
|---|---|---|---|---|---|---|
| bld.home.shack | Notunterkunft, Elendshütte | sehr arm | Laubhütte, Bretterbude, Wellblechhütte | F | A7 / M4 | geplant |
| bld.home.cottage | einfaches Haus, Kate | arm | Lehmhaus, Kate, Arbeiterhäuschen | F | A7 / M4 | geplant (Machbarkeitstest v1 in Blender 2026-10-27, nicht im Spiel) |
| bld.home.townhouse | Bürgerhaus | Mittelschicht | Fachwerkhaus, Bürgerhaus, Einfamilienhaus | A | A7 / M4 | geplant (Machbarkeitstest v1 in Blender 2026-10-27, nicht im Spiel) |
| bld.home.rowhouse | Reihen-, Miets-, Hochhaus | Stadtbevölkerung | Insula, Mietskaserne, Hochhaus | A | A7 / M4 | geplant |
| bld.home.villa | Villa | reich | Römische Villa, Stadtpalais, moderne Villa | A | A7 / M4 | geplant (Machbarkeitstest v1 in Blender 2026-10-27, nicht im Spiel) |
| bld.home.manor | Herrenhaus, Palast, Anwesen | sehr reich, Herrscher | Gutshof, Burg/Schloss, Anwesen | A | A7 / M4 | geplant |

**Baustil je Stamm: die Spiel-KI entscheidet, wir geben nur Vorgaben (Nutzer 2026-10-27 13:05 UTC, verbindlich).** Jeder Stamm hat eigene Häuser. Es gibt im Katalog deshalb **keine festen Haustypen** (Langhaus, Jurte, Fachwerkhaus usw. sind nur Beispiele dafür, was dabei herauskommen *kann*). Ein Haus entsteht in drei Ebenen:
1. **Harte Vorgaben (von uns, gelten für alle):** Physik und Maße nach §0.7; Klima des Bauplatzes (Schneelast → Mindest-Dachneigung, Regen → Dachüberstand, Hitze → Wanddicke/Fenstergröße); nur Baustoffe, die es im Gebiet gibt (Biom, Rohstoffe, Handel); nur Techniken, die der Stamm kennt (Epoche/Technologie); Bedürfnisse des Bauwunsches (Fläche je Person, Beruf, Vermögen).
2. **Baustil des Stammes (entscheidet die Spiel-KI):** ein Parametersatz je Stamm, z. B. bevorzugte Grundform (rechteckig, rund, lang, um einen Hof, auf Pfählen, eingetieft), Dachform und Neigung, Wand- und Dachbaustoff, Farben, Fensterform und -größe, Läden, Schmuck. Der Stil entsteht bei der Gründung aus Umwelt und Zufall, ändert sich mit Technik, Kontakt zu anderen Stämmen und Geschmack, und spaltet sich, wenn ein Stamm sich teilt. Wer ihn in der Simulation hält und wie die KI wählt, gehört zum Systembereich Civ/NPC (weitergegeben 2026-10-27).
3. **Einzelhaus (entscheidet der NPC-Bauherr):** Größe, Anbauten und Ausstattung aus seinem Bauwunsch, innerhalb des Stammes-Stils.

Der Katalog zählt deshalb nur, was wir wirklich bauen müssen: **Bauteil-Sätze** in R1/R2-Qualität, aus denen jeder Stil zusammengesetzt werden kann. Je mehr Sätze, desto verschiedener die Stämme.

| ID | Bauteil-Satz | Inhalt | ab Epoche | Etappe | Status |
|---|---|---|---|---|---|
| bld.kit.wall.pole | Stangen, Flechtwerk, Lehmbewurf | Pfosten, Flechtwände, Lehmputz | F | A7 | geplant |
| bld.kit.wall.log | Blockbau | liegende Stämme, Eckverbindungen | F | A7 | geplant (Probe in Haus v2/v3) |
| bld.kit.wall.textile | Fell, Filz, Plane über Gerüst | Gerüststangen, Bahnen, Seile | F | A7 | geplant |
| bld.kit.wall.adobe | Lehm, Adobe, Stampflehm | Wände, Gesimse, Wasserspeier | F | A7 | geplant (Probe in Haus v2/v3) |
| bld.kit.wall.timberframe | Fachwerk | Ständer, Riegel, Streben, Gefache | A | A7 | geplant |
| bld.kit.wall.stone | Bruch- und Quaderstein | Mauerwerk, Ecksteine, Stürze | A | A7 | geplant |
| bld.kit.wall.brick | Ziegelmauerwerk, Putz | Mauerwerk, Putzflächen, Faschen | A | A7 | geplant (Probe in Haus v2/v3) |
| bld.kit.wall.modern | Beton, Stahl, Glas | Fertigteile, Fassadenplatten, Glasfronten | I | A7 | geplant |
| bld.kit.roof.thatch | Reet, Stroh, Soden | Deckung, First, Traufe | F | A7 | geplant (Probe in Haus v3) |
| bld.kit.roof.shingle | Holzschindel, Rinde | Deckung, Ortgang | F | A7 | geplant |
| bld.kit.roof.tile | Ziegel, Schiefer | Deckung, First, Gauben | A | A7 | geplant (Probe in Haus v1–v3) |
| bld.kit.roof.metal | Blech, Wellblech | Deckung, Rinnen, Fallrohre | I | A7 | geplant (Probe in Haus v2) |
| bld.kit.roof.flat | Flachdach, Attika | Lehm- oder Betondach, Brüstung, Abfluss | F | A7 | geplant (Probe in Haus v2/v3) |
| bld.kit.opening | Öffnungen | Türen, Fenster, Bögen, Läden, Gitter, Vorhänge je Baustoff | F | A7 | geplant (Probe in Haus v1–v3) |
| bld.kit.support | Tragwerk | Pfähle, Stützen, Säulen, Balken, Plattformen, Stege | F | A7 | geplant |
| bld.kit.ornament | Schmuck | Schnitzerei, Bemalung, Giebelzier, Farbflächen (Muster und Farben setzt der Stil) | F | A7 | geplant |
| bld.kit.wall.green | Holz-Hybrid, begrünte Fassade, Solardach | Holzfassade, Pflanzflächen, Solarmodule | NZ | A7 | geplant (Zielqualität R3) |
| bld.kit.wall.composite | Metall- und Verbundpaneele | Paneele, Schleusen, Rundfenster, Kuppelsegmente | RF | A7 | geplant (Zielqualität R3) |
| bld.kit.wall.regolith | gedruckte oder gestampfte Planetenerde | Schichtwände, Sonnensegel, Druckschleusen | PL | A7 | geplant (Zielqualität R3) |

**Baustil-Parameter: verbindliche Namen für Civ (`CivStyle`) und Presentation** (Stand 2026-10-27; Civ übernimmt die Namen 1:1, nur anhängen, nie umbenennen). Der Stil eines Stammes ist ein Seed plus diese Werte. Presentation setzt daraus mit den Bauteil-Sätzen oben das Haus zusammen.

| Parameter | erlaubte Werte | Bauteil-Satz |
|---|---|---|
| `WallMaterial` | `Pole`, `Log`, `Textile`, `Adobe`, `TimberFrame`, `Stone`, `Brick`, `Modern`; vorgemerkt für späte Epochen (§4.3): `Green`, `Composite`, `Regolith` | bld.kit.wall.pole, .log, .textile, .adobe, .timberframe, .stone, .brick, .modern, .green, .composite, .regolith |
| `WallFinish` | `Raw`, `Plaster`, `Painted` | Teil von bld.kit.wall.* |
| `RoofMaterial` | `Thatch`, `Sod`, `Hide`, `Shingle`, `Tile`, `Slate`, `Metal`, `FlatEarth`, `Concrete` | bld.kit.roof.thatch (Thatch, Sod), .wall.textile (Hide), .shingle, .tile (Tile, Slate), .metal, .flat (FlatEarth, Concrete) |
| `RoofShape` | `Gable`, `Hip`, `Shed`, `Flat`, `Cone`, `Dome` | bld.kit.roof.* |
| `RoofPitchDeg` | Zahl 0–60, Grenzen siehe Regeln unten | – |
| `OverhangM` | Zahl 0–1,2 | – |
| `FloorPlan` | `Rect`, `Long`, `Round`, `Courtyard`, `LShape` | – |
| `Elevation` | `Ground`, `Sunken`, `Stilt` | bld.kit.support (Stilt) |
| `WindowShape` | `Small`, `Square`, `Tall`, `Wide`, `Arched`, `Round` | bld.kit.opening |
| `WindowClosure` | `Open`, `Shutters`, `Glazed` | bld.kit.opening |
| `Ornament` | `None`, `Carving`, `Painting`, `GableDecor`, `Pattern`, `Relief` | bld.kit.ornament |
| `WallColor`, `AccentColor` | RGB aus der Stammes-Palette (Seed), Sättigung höchstens mittel (Stil §3) | – |

**Regeln, die jeder Stil einhalten muss** (Prüfung im Generator; ein Stil, der sie bricht, wird von Civ nicht gewählt):
- *Technik:* `Stone`, `Brick`, `Tile`, `Slate`, `Relief` erst mit `tech.masonry`; `TimberFrame` und `Glazed` erst mit `tech.construction`; `Metal`, `Concrete`, `Modern` erst mit `tech.industrialization`. Alles andere ab `Primitive`.
- *Formen:* `Round` nur mit `Cone` oder `Dome`; `Dome` nur mit `Adobe`, `Stone`, `Brick`, `Modern` oder `Textile`; `Courtyard` erst ab `Ancient`; `Flat` nur mit `FlatEarth` oder `Concrete`.
- *Mindestneigung je Dachdeckung:* `Thatch` 40°, `Hide` 35°, `Shingle` 30°, `Tile` und `Slate` 25°, `Sod` 15–30°, `Metal` 8°, `FlatEarth` und `Concrete` 0–5°.
- *Klima (aus dem Biom):* Schnee (`Boreal`, `Tundra`, `Alpine`): kein `Flat`, Neigung +10° über der Mindestneigung (außer `Sod`). Heiß und trocken (`Desert`, `Savanna`, `Mediterranean`): Fenster bevorzugt `Small`, Wände dicker. Nass und warm (`Wetland`, `TropicalForest`): `OverhangM` ≥ 0,6; `Stilt`, wenn der Bauplatz überflutet werden kann.

**Baustoffe je Biom** (was ohne Handel vor Ort wächst oder liegt; über Handel oder ab `tech.industrialization` sind alle Baustoffe überall möglich):

| Biom (`BiomeType`) | Wände | Dächer |
|---|---|---|
| `Coast` | Pole, Log, Adobe, Stone, Brick | Thatch, Shingle, Tile, Slate |
| `Temperate` | Pole, Log, TimberFrame, Stone, Brick | Thatch, Shingle, Tile, Slate |
| `Boreal` | Log, Pole, Stone | Sod, Shingle, Hide, Slate |
| `Tundra` | Textile, Stone | Hide, Sod |
| `Desert` | Adobe, Stone, Textile | FlatEarth, Hide |
| `Savanna` | Pole, Adobe, Textile | Thatch, FlatEarth, Hide |
| `Wetland` | Pole, Log | Thatch, Shingle |
| `Alpine` | Stone, Log | Slate, Shingle, Sod |
| `TropicalForest` | Pole, Log | Thatch, Shingle |
| `Mediterranean` | Stone, Adobe, Brick, TimberFrame | Tile, FlatEarth, Thatch |
| `Ocean` | – (kein Bauplatz) | – |

**Ab wann was gilt:** Wohnhaus-Klassen `bld.home.*` siehe Spalte „ab Epoche“ oben (es gibt **keine** Haustypen nach Beruf mehr; der Beruf wirkt nur über `bld.part.*`). Bauteil-Sätze siehe Tabelle oben und die Technik-Regeln. Bedürfnis-Bausteine: alle ab `Primitive`, außer `bld.part.shopfront` ab `Ancient` und Glasfronten in `bld.part.terrace` erst mit `tech.industrialization`.

**Bedürfnis-Bausteine** (werden an jedes Haus angebaut, wenn der Bauherr sie braucht und bezahlen kann; ein Haus kann später wachsen, wenn die Familie wächst):

| ID | Baustein | ausgelöst durch | Etappe | Status |
|---|---|---|---|---|
| bld.part.room | zusätzliches Zimmer, Geschoss | Haushaltsgröße (Fläche je Person nach Vermögen) | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.extension | späterer Anbau | Familie wächst, Vermögen steigt | A7 / M4 | geplant |
| bld.part.workshop | Werkstatt-Anbau | Handwerksberuf | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.shopfront | Laden-Erdgeschoss, Schaufenster, Markise | Handel | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.barn | Stall, Scheune | Vieh, Ernte | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.cellar | Keller, Vorratsraum | Vorräte, kaltes Klima | A7 / M4 | geplant |
| bld.part.yard | Garten, Zaun, Hofmauer | Selbstversorgung, Sicherheit, Wohlstand | A7 / M4 | geplant |
| bld.part.water | Brunnen, Zisterne | kein Wasser in der Nähe | A7 / M4 | geplant |
| bld.part.terrace | Terrasse, Wintergarten, Anbau mit Glasfront | Wohlstand | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.climate | Klima-Teile: steiles Dach, Flachdach mit Attika, dicke Wände, Fensterläden, Vordach | Klima des Bauplatzes | A7 / M4 | geplant (Bauregel in Haus v2) |
| bld.part.makeshift | Notbau-Teile: Äste, Bretter, Rinde, Plane, Pultdach (Wellblech erst mit `tech.industrialization`) | sehr wenig Geld, Notlage, Flucht | A7 / M4 | geplant (Bauregel in Haus v2) |

### 4.3 Zielqualität je Epoche, Referenzen R3/R4 (Nutzer 2026-10-27 16:34 UTC)

Der Nutzer hat zwei Übersichten geschickt: R3 zeigt Häuser, Mauern und Gegenstände, R4 Fahrzeuge, Belagerungsgeräte und Werkzeuge, jeweils für Frühzeit, Antike, Mittelalter, Industrie, Moderne, Nahe Zukunft, Raumfahrt, Planeten und Galaxie. Die Dateien liegen nur in `/mnt/project-files/WorldBeyond/references/` (Urheber und Lizenz unbekannt, wirken KI-erzeugt, nicht ins Repo und nicht ins Spiel). Sie gelten als **Zielqualität**: realistische Materialien, Abnutzung, Details, warmes, weiches Licht wie R1/R2. Sie sind **keine Vorlage für feste Modelle**: Häuser setzt die Spiel-KI des Stammes aus den Bauteil-Sätzen zusammen (§4), Gegenstände sind Archetypen (§0.6).

Abgleich mit dem Katalog: Alle Häuser und Mauern aus R3 lassen sich mit den Bauteil-Sätzen `bld.kit.*` beschreiben (Flechtwerk und Reet, Stein und Ziegel, Fachwerk, Ziegelmauerwerk, Putz und Beton). Für die späten Epochen sind drei Sätze neu (`bld.kit.wall.green`, `.composite`, `.regolith`). Gegenstände und Fahrzeuge aus R3/R4, die noch fehlten, stehen jetzt in §5 und §8.2.

**Logik-Lücke (weitergegeben 2026-10-27):** Die Simulation kennt nur die Epochen `Primitive` bis `Modern` (`Epoch`-Enum). Nahe Zukunft (NZ), Raumfahrt (RF), Planeten (PL) und Galaxie (GX) gibt es dort noch nicht. Die Katalogzeilen mit diesen Epochen sind deshalb nur vorgemerkt und werden erst gebaut, wenn Civ/Integration die Epochen anlegen.

## 5. Personen, Einheiten, Fahrzeuge

| ID | Name | Sim-Schlüssel | Weg | Etappe | Status | Bemerkung |
|---|---|---|---|---|---|---|
| unit.worker | Arbeiter | unit.worker | CH | A7 | geplant | heute keine Darstellung |
| unit.settler | Siedler | unit.settler | CH | A7 | geplant | |
| unit.caravan | Karawane | unit.caravan | CH+MB | A7 | geplant | Lasttier/Wagen je Epoche |
| unit.rifleman | Schütze | unit.rifleman | CH | A7 | geplant | Militär (Phase 5) |
| unit.firefighter_bucket | Löschhelfer (Eimerkette) | – (neu, Civ) | CH | A4 | geplant | frühe Epochen |
| unit.firefighter | Feuerwehrmann | – (neu, Civ) | CH | A4 | geplant | |
| veh.fire_cart | Feuerwehrwagen mit Pumpe (Pferd) | – (neu) | MB | A4 | geplant | |
| veh.fire_engine | Löschfahrzeug | – (neu) | MB | A4 | geplant | |
| veh.firefighting_aircraft | Löschflugzeug/-hubschrauber | – (neu) | MB | A4 | geplant | Moderne |
| veh.cart | Ochsen-/Pferdekarren | – | MB | A7 | geplant | |
| veh.boat_fishing | Fischerboot | – | MB | A7 | geplant | |
| veh.humvee | Geländewagen (Humvee-Klasse) | (Phase 5) | MB | A7 | geplant | Referenz: PHASE5_MILITARY_ASSETS |
| veh.humvee_mg | Geländewagen mit MG | (Phase 5) | MB | A7 | geplant | |
| veh.tank_main | Kampfpanzer | (Phase 5) | MB | A7 | geplant | |
| veh.tank_light | leichter Panzer | (Phase 5) | MB | A7 | geplant | |
| veh.truck | Versorgungs-LKW | (Phase 5) | MB | A7 | geplant | |
| veh.artillery | Artillerie | (Phase 5) | MB | A7 | geplant | |
| veh.rocket_launcher | Raketenwerfer | (Phase 5) | MB | A7 | geplant | |
| veh.helicopter | Hubschrauber | (Phase 5) | MB | A7 | geplant | |
| veh.boat_military | Militärboot | (Phase 5) | MB | A7 | geplant | |
| veh.ambulance | Sanitätsfahrzeug | (Phase 5) | MB | A7 | geplant | |
| veh.covered_wagon | Planwagen | – (neu, ab M) | MB | A7 | geplant | Zielqualität R4 |
| veh.catapult | Katapult (Torsion) | – (neu, ab A) | MB | A7 | geplant | Zielqualität R4 |
| veh.trebuchet | Tribok | – (neu, ab M) | MB | A7 | geplant | Zielqualität R4 |
| veh.traction_engine | Dampf-Zugmaschine | – (neu, ab I) | MB | A7 | geplant | Zielqualität R4 |
| veh.cannon | Feldkanone | – (neu, ab I) | MB | A7 | geplant | Zielqualität R4 |
| veh.pickup | Pick-up, Kleintransporter | – (neu, ab Mo) | MB | A7 | geplant | Zielqualität R4 |
| veh.bicycle | Fahrrad | – (neu, ab I) | MB | A7 | geplant | Zielqualität R4 |
| veh.rover_drone | unbemannter Geländeroboter | – (neu, ab NZ) | MB | A7 | geplant | Zielqualität R4 |
| veh.turret_rocket | Raketen-Abwehrturm | – (neu, ab NZ) | MB | A7 | geplant | Zielqualität R4 |
| veh.planet_rover | Planeten-Rover (bemannt) | – (neu, ab RF) | MB | A7 | geplant | Zielqualität R4 |
| veh.defense_dome | Abwehrturm auf Sockel | – (neu, ab RF) | MB | A7 | geplant | Zielqualität R4 |
| veh.ore_hauler | Erz-Muldenkipper | – (neu, ab PL) | MB | A7 | geplant | Zielqualität R4 |
| veh.planet_artillery | Planeten-Geschütz auf Fahrgestell | – (neu, ab PL) | MB | A7 | geplant | Zielqualität R4 |
| veh.starship | Raumschiff (Transporter) | – (neu, ab GX) | MB | A7 | geplant | Zielqualität R4 |
| veh.defense_pylon | Abwehrpylon | – (neu, ab GX) | MB | A7 | geplant | Zielqualität R4 |

## 6. Tiere (später, Bezug Wildnahrung `ResourceType.Food`)

| ID | Name | Biome | Etappe | Status |
|---|---|---|---|---|
| fauna.deer | Hirsch/Reh | Temperate, Boreal | P3 | geplant |
| fauna.boar | Wildschwein | Temperate | P3 | geplant |
| fauna.wolf | Wolf | Boreal, Tundra | P3 | geplant |
| fauna.bear | Bär | Boreal, Alpine | P3 | geplant |
| fauna.bison | Wildrind | Savanna, Temperate | P3 | geplant |
| fauna.camel | Kamel | Desert | P3 | geplant |
| fauna.birds | Vogelschwärme | alle | P3 | geplant |
| fauna.fish | Fischschwärme | Wasser | P3 | geplant |
| fauna.livestock | Nutztiere (Rind, Schaf, Huhn) | Siedlungen | P3 | geplant |
| fauna.horse | Pferd | Temperate, Savanna | P3 | geplant |

## 7. Effekte und Decals (Weg PS, Darstellung über `IVfxService`, Besitzer UI)

| ID | Effekt | Auslöser (Simulation) | Etappe | Status |
|---|---|---|---|---|
| fx.fire.ground | Bodenfeuer | Brandkachel, geringe Intensität | A4 | geplant |
| fx.fire.crown | Kronenfeuer | Brandkachel, hohe Intensität | A4 | geplant |
| fx.fire.building | Gebäudebrand | brennendes Gebäude | A4 | geplant |
| fx.smoke.column | Rauchsäule (Höhe = Brandgröße, Grundlage der Sichtung) | Brandereignis | A4 | geplant |
| fx.embers | Glut, Funkenflug | Brand + starker Wind | A4 | geplant |
| fx.ash_fall | Ascheregen | großer Brand | A4 | geplant |
| fx.extinguish | Löschwasser, Dampf | Löscheinsatz | A4 | geplant |
| fx.lightning.bolt | Blitz (Mesh) | Blitzeinschlag | A4 | geplant |
| fx.lightning.flash | Blitzlicht + Donner (Verzögerung nach Entfernung) | Blitzeinschlag | A4 | geplant |
| fx.rain | Regen | Wetterzelle | A3 | geplant |
| fx.snow | Schneefall | Wetterzelle | A3 | geplant |
| fx.clouds | Wolken, Wolkenschatten | Wetterlage | A3 | geplant |
| fx.fog | Nebel, Dunst | Wetter, Tageszeit | A3 | geplant |
| fx.wind.leaves | fliegende Blätter | Wind, Herbst | A3 | geplant |
| fx.wind.dust | Staub-/Sandverwehung | Wind, Desert | A3 | geplant |
| fx.storm.debris | Sturmtrümmer (Äste, Dachteile) | Sturm/Orkan | A5 | geplant |
| fx.tree_fall | umstürzender Baum (Animation) | Sturmwurf, Fällung | A5 | geplant |
| fx.tsunami.spray | Gischt an der Wellenfront | Tsunami | A6 | geplant |
| fx.flood | ansteigendes/abfließendes Wasser, Treibgut | Flut | A6 | geplant |
| fx.quake.dust | Erdbebenstaub | Erdbeben | A6 | geplant |
| fx.explosion | Explosion | Kampf (Phase 5) | A7 | geplant |
| fx.muzzle_flash | Mündungsfeuer | Kampf | A7 | geplant |
| fx.impact_dust | Einschlagstaub | Kampf | A7 | geplant |
| decal.scorch | Brandfleck | `Scorched` | A4 | geplant |
| decal.ash | Ascheschicht unter verkohlten Bäumen | verkohlte Vegetation | A4 | geplant |
| decal.crater | Krater | `Crater` | A7 | geplant |

## 8. Requisiten

| ID | Name | Etappe | Status |
|---|---|---|---|
| prop.bridge | Brücke (Holz/Stein/Stahl) | A7 | geplant |
| prop.fence | Zaun | A7 | geplant |
| prop.flag | Fahne in Civ-Farbe | A7 | geplant |
| prop.woodpile | Holzstapel | A7 | geplant |
| prop.stonepile | Steinhaufen | A7 | geplant |
| prop.crates | Kisten, Säcke (Lager) | A7 | geplant |
| prop.well_bucket | Eimer, Brunnenwinde | A4 | geplant |
| prop.stump | Baumstumpf | A2 | geplant |
| prop.fallen_log | liegender Stamm | A2 | geplant |
| prop.root_plate | Wurzelteller (umgestürzter Baum) | A5 | geplant |
| prop.wreck | Wrack/Trümmer | A6 | geplant |
| prop.debris_flood | Treibgut | A6 | geplant |
| prop.grave | Grab/Gedenkstein | A7 | geplant |
| prop.market_stall | Marktstand | A7 | geplant |

### 8.1 Innenausstattung (jedes Haus ist begehbar, `ASSETS_STYLE_ENVIRONMENT.md` §4.4)

Jeden Eintrag gibt es je Epoche und je Vermögensklasse (arm / mittel / reich) in eigener Ausführung. Die Gegenstände sind Besitz der Bewohner: Die NPCs beschaffen und stellen sie nach ihren Bedürfnissen und ihrem Geld selbst auf (Konzept §4.4). Weg: MB in Blender (+T0). Stufe: M4 (NPCs), Baukasten ab A7.

| ID | Gegenstand | Beispiele arm → reich | Etappe | Status |
|---|---|---|---|---|
| int.stairs | Treppe, Leiter | Leiter → Holztreppe → Steintreppe mit Geländer | A7 / M4 | geplant |
| int.door | Innentür | Vorhang → Brettertür → Kassettentür | A7 / M4 | geplant |
| int.bed | Schlafplatz | Strohlager → Holzbett → Himmelbett | A7 / M4 | geplant |
| int.table | Tisch | Brett auf Böcken → Esstisch → Tafel | A7 / M4 | geplant |
| int.chair | Sitzplatz | Hocker → Stuhl → Polstersessel, Sofa | A7 / M4 | geplant |
| int.hearth | Feuerstelle, Herd, Kamin | offene Feuerstelle → Kachelofen/Herd → Kamin, Küchenzeile | A7 / M4 | geplant |
| int.storage | Aufbewahrung | Korb, Truhe → Schrank → Kleiderkammer | A7 / M4 | geplant |
| int.shelf | Regal, Vorräte | Wandbrett → Regal → Vitrine | A7 / M4 | geplant |
| int.light | Licht | Feuerschein, Kienspan → Öllampe, Kerze → Kronleuchter, elektrisch | A7 / M4 | geplant |
| int.wash | Waschen, Bad | Wassereimer → Waschzuber → Badezimmer | A7 / M4 | geplant |
| int.kitchen | Küchengerät, Geschirr | Tontopf → Küchenbord → Küche | A7 / M4 | geplant |
| int.workbench | Arbeitsplatz je Beruf | Werkbank, Webstuhl, Schreibtisch | A7 / M4 | geplant |
| int.decor | Schmuck, Wohlstand | Felle → Teppich, Wandbehang → Bilder, Statuen | A7 / M4 | geplant |
| int.debris | Innentrümmer | eingestürzte Decke, umgestürzte Möbel, Brandreste | A7 / M7 | geplant |


### 8.2 Werkzeuge und Gegenstände je Epoche (Zielqualität R3/R4, Nutzer 2026-10-27)

Jede Zeile ist ein Archetyp (§0.6), den NPCs in ihrer Epoche und mit den Baustoffen ihres Stammes herstellen; Aussehen und Abnutzung folgen dem Stamm, nicht einem festen Modell. Was es schon als Innenausstattung gibt (Truhe, Lampe, Hocker, Ofen), steht in §8.1. Epochen NZ, RF, PL, GX siehe §4.3.

| ID | Gegenstand | Entwicklung | ab Epoche | Etappe | Status |
|---|---|---|---|---|---|
| tool.axe | Beil, Axt | Steinbeil → Bronzeaxt → Stahlaxt | F | A7 | geplant |
| tool.spear | Speer, Spieß | Holz-/Knochenspitze → Eisenspitze | F | A7 | geplant |
| tool.hammer | Hammer | Steinhammer → Holzhammer → Schmiedehammer | F | A7 | geplant |
| tool.sickle | Sichel, Sense | Bronzesichel → Sense | A | A7 | geplant |
| tool.shovel | Grabstock, Spaten, Schaufel | Grabstock → Eisenspaten | F | A7 | geplant |
| tool.pickaxe | Spitzhacke | Eisen → Stahl | M | A7 | geplant |
| tool.wrench | Rohrzange, Schraubenschlüssel | geschmiedet → Chrom-Vanadium | I | A7 | geplant |
| tool.toolbox | Werkzeugkiste, -koffer | Holzkiste → Metallkoffer → Kunststoffkoffer | I | A7 | geplant |
| tool.power_drill | Akku-Bohrschrauber | – | Mo | A7 | geplant |
| tool.scanner | Handscanner, Diagnosegerät | – | NZ | A7 | geplant |
| tool.tablet | Tablet, Datenpad | – | Mo | A7 | geplant |
| tool.mining_drill | Bohr- und Schneidgerät (Bergbau) | Pressluft → Plasma | RF | A7 | geplant |
| prop.pottery | Topf, Amphore, Krug | – | F | A7 | geplant |
| prop.basket | Korb, Flechtkorb | – | F | A7 | geplant |
| prop.barrel | Fass | – | M | A7 | geplant |
| prop.palisade | Palisade, Flechtzaun | – | F | A7 | geplant |
| prop.wall_segment | freistehende Mauer je Baustoff (Bauteil-Satz) | – | F | A7 | geplant |
| prop.bench | Sitzbank | – | I | A7 | geplant |
| prop.trash_bin | Mülltonne | – | Mo | A7 | geplant |
| prop.gas_cylinder | Gasflasche | – | I | A7 | geplant |
| prop.ibc_tank | Tankbehälter (IBC), Wassertank | – | I | A7 | geplant |
| prop.planter | Pflanzkübel, Hochbeet | – | A | A7 | geplant |
| prop.delivery_robot | Lieferroboter | – | NZ | A7 | geplant |
| prop.battery_unit | Hausbatterie, Energiespeicher | – | NZ | A7 | geplant |
| prop.fabricator | 3D-Drucker, Fabrikator | – | NZ | A7 | geplant |
| prop.cargo_case | Transportkiste (Raumfahrt) | – | RF | A7 | geplant |
| prop.canister | Kanister, Energiezelle | – | RF | A7 | geplant |
| prop.greenhouse_box | Pflanzkasten mit Haube | – | PL | A7 | geplant |
| prop.cargo_cart | Lastkarren mit Rädern | – | PL | A7 | geplant |
| prop.service_drone | Service-Drohne, Roboter | – | GX | A7 | geplant |

UI-Icons und Schriften: siehe `Docs/UI_STYLE_GUIDE.md` B §2/§5 (nicht hier gezählt).

## 9. Platzhalter-Register (Vorschlag zur Übernahme in ASSET_CREDITS §3)

| Nr. | Objekt | Platzhalter (Datei) | Grund | Ersatzplan | eingetragen von | Status |
|---|---|---|---|---|---|---|
| P-001 | Nadelbaum (form.W05) | `ProceduralMeshes.Conifer()` – 3 gestapelte 7-seitige Kegel, flache Normalen, einfarbig | noch kein eigenes Modell | PlantForge W05 (Etappe A1), über `WorldRenderer.coniferMesh` einsetzbar | World (Phase 2), hier erfasst | Platzhalter |
| P-002 | Laubbaum (form.W01) | `ProceduralMeshes.Broadleaf()` – 2 facettierte Kegelstümpfe, flache Normalen, einfarbig | dito | PlantForge W01 (A1), über `WorldRenderer.broadleafMesh` | World | Platzhalter |
| P-003 | Fels (rock.boulder) | `ProceduralMeshes.Rock()` – verzerrter Doppelkegel | dito | prozedurale Felsen (A2), über `WorldRenderer.rockMesh` | World | Platzhalter |
| P-004 | Gelände-Oberflächen | Vertexfarben aus `WorldPalette` + Ersatzmaterial `WorldMaterials.CreateLit("Terrain")` | keine Bodentexturen | Materialmischung mit kalibrierten CC0-Texturen (A2), Besitzer World | World | Platzhalter |
| P-005 | Wasser | Ersatzmaterial `CreateLit("Water")`, flache Vierecke | kein Wasser-Shader | Wasser-Shader-Graph (A2), Besitzer World | World | Platzhalter |

Quelle aller fünf: Branch `phase2-world-engine` @921fdbb, `Assets/_Project/Scripts/Presentation/WorldView/` (am 2026-10-26 gelesen, nicht in Unity angesehen).
