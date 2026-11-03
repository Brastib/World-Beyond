# World Beyond – ASSET_CREDITS

Stand: 2026-10-25. **Bisher ist kein einziges Asset heruntergeladen oder in Unity importiert.** Es existiert noch kein Unity-Projekt.

Regeln: PROJECT_MASTER §8 (verbindlich) und ARCHITECTURE §16. Jedes importierte Asset wird **sofort** hier eingetragen. "Lizenz geprüft" bedeutet: Lizenztext auf der Quellseite bzw. in der Lizenzdatei des Downloads gelesen, mit Datum. Ohne geprüfte Lizenz wird nichts importiert.

## 1. Importierte Assets

| ID | Asset | Autor | Quelle (Original-Link) | Lizenz | Namensnennung nötig | Lizenz geprüft am | Speicherort im Projekt | Anpassungen | Verwendet für |
|---|---|---|---|---|---|---|---|---|---|
| – | – | – | – | – | – | – | – | – | Noch keine Assets importiert |

## 2. Pflicht-Namensnennungen (für Credits-Bildschirm im Spiel)

Wird automatisch aus Tabelle 1 abgeleitet (alle Einträge mit "Namensnennung nötig = ja", z. B. CC BY). Derzeit leer.

## 3. Platzhalter-Register

Jeder sichtbare Platzhalter (Primitive, Standardmaterial, einfaches Ersatzmodell) wird hier eingetragen und vor Phase 8 geprüft (TEST_PLAN §5).

| Objekt | Platzhalter | Grund | Ziel-Asset / Ersatzplan | eingetragen von (Phase) | Status |
|---|---|---|---|---|---|
| P-001 Nadelbaum (form.W05) | `ProceduralMeshes.Conifer()`: 3 gestapelte Kegel, flache Normalen, einfarbig | noch kein eigenes Modell | PlantForge W05 (Etappe A1), über `WorldRenderer.coniferMesh` | World (2), erfasst vom Asset-Katalog | Platzhalter |
| P-002 Laubbaum (form.W01) | `ProceduralMeshes.Broadleaf()`: 2 facettierte Kegelstümpfe, einfarbig | dito | PlantForge W01 (A1), über `WorldRenderer.broadleafMesh` | World (2) | Platzhalter |
| P-003 Fels (rock.boulder) | `ProceduralMeshes.Rock()`: verzerrter Doppelkegel | dito | prozedurale Felsen (A2), über `WorldRenderer.rockMesh` | World (2) | Platzhalter |
| P-004 Gelände-Oberflächen | Vertexfarben aus `WorldPalette` + `WorldMaterials.CreateLit("Terrain")` | keine Bodentexturen | Materialmischung mit CC0-Texturen (A2) | World (2) | Platzhalter |
| P-005 Wasser | `CreateLit("Water")`, flache Vierecke | kein Wasser-Shader | Wasser-Shader-Graph (A2) | World (2) | Platzhalter |

## 4. Kandidaten (recherchiert, NICHT importiert)

Zentraler Katalog aller benötigten Assets mit Status: `Docs/proposals/ASSET_CATALOG.md` (PR #9).

Detaillierte Kandidatenlisten werden von den jeweiligen Phasen gepflegt und hier zusammengeführt, sobald importiert wird:

| Bereich | Liste | Stand |
|---|---|---|
| Militär (Fahrzeuge, Infanterie, Effekte) | `Docs/proposals/PHASE5_MILITARY_ASSETS.md` | recherchiert vom Phase-5-Thread, nichts heruntergeladen, Vorschauen nicht visuell geprüft |
| Welt (Terrain-Texturen, Vegetation, Felsen, Wasser) | noch keine Liste (Phase 2) | offen |
| Zivilisation (Gebäude, Mauern, Infrastruktur) | noch keine Liste (Phase 3) | offen |
| UI (Icons, Schriften) | `Docs/UI_STYLE_GUIDE.md` §5 | Quellen genannt, Lizenzen noch nicht je Datei geprüft |

Allgemein zu prüfende Quellen (Lizenz gilt je Asset, nicht pauschal – vor Import einzeln prüfen): Kenney, Quaternius, Poly Haven, ambientCG, poly.pizza, OpenGameArt, Sketchfab (Lizenzfeld und Download-Erlaubnis je Modell), game-icons.net, Google Fonts (SIL OFL).

## 5. Ausgeschlossen

| Asset / Quelle | Grund |
|---|---|
| kostenpflichtige Asset-Store-Pakete | PROJECT_MASTER §2: keine kostenpflichtigen Assets |
| Assets ohne eindeutige Lizenz ("feel free to use" ohne Lizenztext) | Rechtslage unklar |
| Weitere verworfene Militär-Kandidaten | siehe `Docs/proposals/PHASE5_MILITARY_ASSETS.md` "Verworfen" |
