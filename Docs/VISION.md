# World Beyond – VISION und Ausbaustufen

Stand: 2026-10-26. Quelle: Nutzeranweisungen vom 2026-10-26 („Aktualisierte Projektanweisung“, „Wichtige Entwicklungsregeln“). Dieses Dokument fasst die **langfristige Vision** knapp zusammen und ordnet sie in Ausbaustufen ein. Arbeitsregeln stehen in `INTEGRATION.md` §0, Zuständigkeiten in §3.

## 1. Vision (Nutzervorgabe, verbindlich)

- Riesige, eigenständige Welt, die sich ohne Spieler entwickelt (Sandbox). Start bei Stämmen; über lange Zeiträume entstehen Landwirtschaft, Berufe, Dörfer, Städte, Königreiche, Technologien bis Moderne/Zukunft. Nicht jede Kultur entwickelt sich gleich.
- Spieler = Beobachter, Gestalter, übernatürliche Kraft: kann Menschen, Siedlungen, Länder verändern und Ereignisse auslösen (Katastrophen, gute Taten, Diebstahl, Mord …). Die Welt reagiert eigenständig.
- Individuelle NPCs: Name, Alter, Herkunft, Attribute mit echter Wirkung, Bedürfnisse, Beruf, Besitz, Familie, Beziehungen, Erinnerungen.
- Kriege mit nachvollziehbaren Ursachen und Folgen (Belagerung, Zerstörung, Flucht, Hunger, Grenzverschiebung, Wiederaufbau). Straftaten mit Folgen (Ermittlung, Gericht, Vergeltung). Gewalt nicht unnötig grafisch; sexuelle Gewalt nie explizit oder sexualisiert darstellen, nur nicht-grafische Folgen.
- Zeitsteuerung zentral: Pause, Beschleunigung, Vorspulen um Zeiträume oder **bis zu Bedingungen** („bis zur Moderne“, „bis ein großer Krieg ausbricht“), auch per natürlicher Sprache. Start in gewähltem Zeitalter mit schlüssig erzeugter Vorgeschichte.
- Chronik nach Zeitsprüngen: kurz / ausführlich / Detailansicht, **nur aus tatsächlich simulierten Ereignissen**.
- Medien: Zeitungen, Radio, Fernsehen, Live-Berichte, Sender mit eigener Perspektive, passend zum Technikstand.
- Eigenständige Welt: keine realen Personen, Staaten oder Ereignisse; eigene Namen und fiktive Sprachen je Kultur, sprachlich konsistent. Darstellungssprache Deutsch.
- Grafik: modern, realistisch, atmosphärisch (**nicht Low-Poly**), effizient gerendert (LOD, Instancing, Streaming).

## 2. Zentrale Regeln aus der Vision

1. **Eine Wahrheit:** Alle Systeme (NPCs, Städte, Nachrichten, Chronik) lesen dieselben Simulationsdaten. Wichtige Ereignisse werden mit Ursache und Folgen gespeichert.
2. **KI erfindet nichts:** Sprachmodelle formulieren, verdichten und interpretieren nur gespeicherte Ereignisse. Keine LLM-Aufrufe für Routine (Bewegung, Bedürfnisse, Wirtschaft, Kampf).
3. **Lokale KI bevorzugt**, wenn ausreichend; Qualität und Laufzeit werden gemessen.
4. **Stabilität vor Umfang**, kleine testbare Schritte, Performance messen statt schätzen, feste Seeds für Reproduzierbarkeit, Platzhalter klar kennzeichnen. Ziel: gegenseitige Prüfung der Threads, „99 % fehlerfrei“.
5. **Assets:** kostenlos bevorzugt, möglichst unter 30 €, höchstens 60 € gesamt. Recherche erlaubt, **Kauf nur mit ausdrücklicher Zustimmung**.
6. **Die Spiel-KI entscheidet alles (Nutzer 2026-10-27):** Wer in einen Krieg einbezogen wird, wie stark aufgerüstet wird, wann Frieden geschlossen wird usw. entscheidet die KI der Völker und Personen durch Abwägung (Kosten, Nutzen, Risiko, Bedürfnisse, Persönlichkeit). Keine festen Spielregel-Grenzen, die Entscheidungen ersetzen (z. B. „höchstens 10 % / 80 Einheiten unter Waffen“). Grenzen entstehen aus der Simulation: Unterhalt, Nahrung, fehlende Arbeitskräfte, Versorgung über Wege (V8), Kriegsmüdigkeit. Technische Leistungsgrenzen werden technisch gelöst (Simulationsstufen V1, zusammengefasste Armeen), nicht durch Spielregeln. Übergangswerte sind als PLATZHALTER markiert und werden durch KI-Abwägung ersetzt.

## 3. Architekturentscheidungen dazu (Integration, [E])

| # | Entscheidung | Begründung |
|---|---|---|
| V1 | **Mehrstufige Simulation** auf dem bestehenden Tick-Fundament: *Aktiv* (volle Systeme, Spielernähe), *Regional* (aggregierte Systeme mit großem `TickInterval`), *Global/Zeitsprung* (ereignisbasierte Aggregatmodelle für Bevölkerung, Ressourcen, Technik, Politik, Krieg). Gleiche Daten, verschiedene Detailstufen. | Erweitert vorhandenen Runner statt neuer Architektur; testbar mit festen Seeds |
| V2 | **Chronik-Log** als eine maßgebliche Ereignisliste (`ChronicleLog`, Fundament): jedes bedeutende Ereignis mit ID, Zeit, Beteiligten (IDs), Ursache-IDs, Folgen; speicherbar. Chronik, Nachrichten und KI-Texte lesen nur hieraus. | Regel „KI erfindet nichts“ technisch erzwingen |
| V3 | **Vorspulen bis Bedingung** als Runner-Funktion (`RunUntil(predicate, maxTicks)`), später auf Zeitsprung-Stufe umschaltbar. Natürliche Sprache wird in der Application-Schicht in solche Ziele übersetzt. | Simulation bleibt deterministisch und ohne LLM |
| V4 | **LLM-Anbindung nur in Application/Presentation** über eine schmale Schnittstelle (Eingabe: Ereignisse aus dem Chronik-Log). Simulation kennt keine KI-Modelle. | Trennung, austauschbar lokal/Cloud |
| V5 | **Grafikrichtung realistisch mit URP** (ersetzt die Low-Poly-Arbeitshypothese von D12). Konkrete Assets nach Recherche und Freigabe. | Nutzervorgabe |
| V6 | **Namens-/Sprachsystem** zunächst als Silben-/Lautregeln je Kultur (Seed), später erweiterbar (Wandel, Lehnwörter). | Pragmatischer Start, kulturell konsistent |
| V7 | **Realistische Zeit (Nutzervorgabe 2026-10-26):** Ein Tick bleibt 0,1 s Spielzeit; Geschwindigkeit 1× = Echtzeit. Ein `GameCalendar` (Fundament) rechnet Ticks in Tag/Monat/Jahr um. Beschleunigen = mehr Ticks pro Frame bis zur CPU-Grenze, darüber Zeitsprung-Stufe (V1) bzw. `RunUntil` (V3). Alle Dauern (Reise, Bau, Forschung, Mobilisierung, Politik, Wiederaufbau) werden in Spielzeit gerechnet und hängen von Entfernung, Technik, Infrastruktur, Ressourcen, Personen ab. Zeitsprünge rechnen dieselben Dauern aggregiert, überspringen sie nie. *Bekannte Abweichung bis M2:* Civ rechnet heute 1 Jahr = 60 s bei 1× (CURRENT_STATE K5). | Keine „Teleport“-Effekte; Zeitsprung bleibt konsistent |
| V8 | **Transport/Infrastruktur** als eigenes System: Wege (Pfad, Straße, Schiene, Hafen, Seeweg) sind gebaute Objekte mit Kapazität und Geschwindigkeitsfaktor; Armeen brauchen Mobilisierung + Versorgung über diese Wege. | Begrenzt Macht realistisch |
| V9 | **Umwelt als Feldsimulation** auf grobem Regionsraster (nicht pro Zelle/Tick): Luft (CO₂/Schadstoffe), Wasser, Boden, Wald, Biodiversität, Strahlung. Quellen (Fabrik, Energie, Verkehr, Bergbau, Landwirtschaft) melden Emissionen abhängig von Technik und Produktion; Senken: Regeneration, Filter, Gesetze, Aufforstung. Folgen fließen als Modifikatoren in Gesundheit, Ernte, Migration, Wirtschaft, Politik. Wetter/Klima/Katastrophen als Ereignisse ins Chronik-Log. | Messbare Werte, effizient |
| V10 | **Dauerhafte Zerstörung:** Explosionen/Waffen erzeugen Schadensereignisse mit Typ, Stärke, Ort; lokal detailliert (Gebäude, Einheiten), großräumig/langfristig über V9-Felder (Kontamination, Trümmer, Brand) und Aggregatfolgen (Flucht, Wirtschaft, Diplomatie). Waffen nur über Forschung + Ressourcen + Infrastruktur. **Gebäude (Nutzer 2026-10-26):** je Epoche eigene Ausführung; an jeder Stelle zerstörbar auf Bauteil-Ebene (ab ca. 1–2 m) mit vorgebrochenen Bauteilen, Schaden je Bauteil in der Simulation (gespeichert, Civ) und vereinfachter Statik; Auslöser sind Schadensereignisse mit Ort und Stärke (Military, `ICombatService`). Konzept: ASSETS_STYLE_ENVIRONMENT §4.4, Katalog §4. | Folgen abhängig von Ereignis, Region, Technik |
| V11 | **Personen-Identität:** jede Person hat eine nie wiederverwendete `PersonId` (sequenziell), unabhängig von Wohnort/Beruf/Amt. Favoriten und Beobachtungslisten (Person, Familie, Stadt, Nation) speichern nur IDs; Ansicht liest Lebenslauf aus Chronik-Log. | Favoriten bleiben stabil |
| V12 | **NPC-Aufträge per Sprache, dreistufig:** *Auftrag* (Spielertext, einmal von der KI übersetzt) → *Ziel* (strukturiert, z. B. `BecomeRole(Scientist)`, `FoundCompany`, `Migrate(region)`) → *Handlung* (normale Simulationsaktion). Nur Ziele/Handlungen laufen über die Command-Queue und werden von der Simulation geprüft (Voraussetzungen, Ressourcen, Zeit). Unmögliches → Ablehnung mit Begründung + Alternativvorschlag. Ändern/Abbrechen/Ergänzen jederzeit. KI-Rückmeldung nur aus Chronik-Log. | KI vermittelt, Simulation entscheidet; kein LLM pro Tick |
| V13 | **Gebäude und Gegenstände von NPCs (Nutzer 2026-10-26):** Häuser entstehen aus dem Bauwunsch des Bauherrn (Vermögen, Haushalt, Stand, Bedürfnisse, Kultur, Epoche) über einen Grundriss-Generator, von der Hütte bis zum Palast; gespeichert wird der Bauwunsch, nicht das Mesh. Jedes Haus ist begehbar (Innenräume aus dem Grundriss, feines Wegenetz an die Welt-Pfadsuche angeschlossen, Schnittansicht). NPCs richten selbst ein; Möbel und Gegenstände sind Besitz, werden durch echte Handlungen beschafft oder von Handwerkern/Architekten entworfen und gebaut: Archetyp + Parameter (Maße, Material, Stil, Qualität aus dem Können), jedes Objekt trägt Hersteller und Qualität. Keine unsinnigen Objekte. Bauqualität je Bauteil (Können, Baustoff, Zeitdruck, Baugrund); Pfusch führt bei Schnee, Regen, Sturm, Erdbeben zu Rissen/Einsturz mit Chronik-Eintrag und Folgen. Konzept: ASSETS_STYLE_ENVIRONMENT §4.4, Katalog §0.6/§4 (PR #9). | Vielfalt aus Regeln statt Handarbeit; alles aus Simulationsdaten |
| V14 | **Physikdaten als Inhaltsdaten:** jedes Objekt-Archetyp liefert Masse, Schwerpunkt, Standfläche, Kollisionsform (Katalog §0.7). Die Simulation nutzt sie vereinfacht (Tragen/Transport nach Gewicht, Standsicherheit, Statik/Einsturz, Trümmer), keine Echtzeit-Rigidbody-Physik in der Simulation; Unity-Physik nur für Darstellung von Trümmern. Schnittstelle: `ObjectPhysicsData` in der ContentRegistry (Besitz Daten: Art/Assets; Nutzung: Civ/NPC/Military). | Deterministisch und speicherbar |

## 4. Ausbaustufen (Reihenfolge, jeweils erst nach Test der vorherigen)

| Stufe | Inhalt | Status |
|---|---|---|
| M1 | Technischer Durchstich (INTEGRATION §4): Welt, 2 Civs wachsen, Bewegung, autonomer Krieg + Kampf, UI, Speichern/Laden | in Arbeit |
| M2 | Chronik-Log (V2) + Kalender/Zeitsteuerung (V7) + Vorspulen bis Bedingung (V3) + einfache Chronik-Ansicht (Text, Deutsch) | geplant |
| M3 | Stämme → Dörfer, Zeitalter-Modell (Epochen je Civ), Start in gewähltem Zeitalter, Gebäudedaten je Epoche; Wege/Transport + Mobilisierung/Versorgung (V8) | geplant |
| M4 | Individuelle NPCs (Attribute, Bedürfnisse, Vermögen, Familie, PersonId) im aktiven Bereich, Aggregat außerhalb (V1); Favoriten/Beobachtungslisten (V11); Bauwunsch-Häuser, begehbare Innenräume, Einrichtung und Handwerk (V13), Gewicht beim Tragen (V14) | geplant |
| M5 | Regionale/globale Simulationsstufe und Zeitsprung-Rechnung (V1); Umweltfelder + Folgen (V9) | geplant |
| M6 | Medien (Text-Zeitung zuerst) und KI-Texte aus dem Chronik-Log (V4); NPC-Aufträge per Sprache (V12) | geplant |
| M7 | Forschung → Waffen bis Atomwaffen, dauerhafte Zerstörung/Kontamination, Abschreckung, Bauteil-Zerstörung von Gebäuden (V10), Statik/Einsturz und Bauqualität (V13/V14) | geplant |
| laufend | Grafik (Qualität R1 für alle Assets, URP; Zustands-Shader Nässe/Schnee/Brand), Performance-Messung, Assets im Budget | – |
