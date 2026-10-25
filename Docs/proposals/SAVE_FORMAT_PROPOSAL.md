# Vorschlag (Phase 7): Versioniertes Speicherformat und stabile IDs

Status: **Vorschlag**, noch nicht implementiert, nicht getestet. Ergänzt ARCHITECTURE.md §11 und SYSTEM_INTERFACES.md (`EntityId`, `ISaveable`). Ziel: Alle Phasen halten diese Regeln von Anfang an ein, damit Phase 7 nichts umbauen muss.

## A. Regeln für stabile IDs (gelten für alle Phasen)

1. **Einheiten, Gebäude, Geschosse:** `EntityId (Index, Generation)`. Der Store speichert beim Sichern seine Generations-Tabelle und die Freiliste mit, damit alte IDs nach dem Laden weiterhin korrekt als ungültig erkannt werden.
2. **Zivilisationen, Siedlungen, Armeen, Kriege, Verträge:** `int`-IDs aus einem fortlaufenden Zähler, **niemals wiederverwendet**. Der Zählerstand wird gespeichert. 0 = keine/ungültig.
3. **Inhaltstypen** (Einheitentyp, Gebäudetyp, Ressource, Technologie, Biom): Im Spielstand als **String-Schlüssel** (`"unit.archer"`, `"res.iron"`) speichern, nicht als Array-Index. Zur Laufzeit dürfen Integer verwendet werden; Mapping beim Laden über eine Tabelle. Grund: Inhaltslisten ändern sich zwischen Versionen.
4. **Enums mit expliziten Zahlenwerten**, niemals umsortieren; neue Werte nur anhängen.
5. **Querverweise nur über IDs.** In DTOs keine `UnityEngine.Object`, `Transform`, `GameObject`, Delegates, Events, Interfaces oder Klassenreferenzen.
6. **Jeder Zugriff über eine ID prüft `Exists(id)`** (zerstörte Einheit, aufgelöste Siedlung, untergegangene Zivilisation). Ungültige Ziele führen zu einem definierten Rückfall (z. B. Auftrag → `Idle`), nie zu einer Exception.

## B. Dateiformat

```jsonc
{
  "format": "WorldBeyondSave",
  "formatVersion": 1,            // ganzzahlig, steigt bei jeder inkompatiblen Änderung
  "gameVersion": "0.1.0",        // Application.version, nur zur Info
  "createdUtc": "2026-10-25T20:00:00Z",
  "header": {                     // für Ladeliste, ohne den Rest zu parsen
    "name": "...", "worldSeed": 12345, "worldWidth": 256, "worldHeight": 256,
    "simTick": 184200, "civCount": 6, "unitCount": 1840
  },
  "settings": { "simSpeed": 1.0, "paused": true, "...": "relevante Simulationseinstellungen" },
  "sections": {                   // Schlüssel = ISaveable.SaveKey
    "clock":   { "v": 1, "data": { } },
    "random":  { "v": 1, "data": { } },   // SimRandom-Zustand
    "world":   { "v": 1, "data": { } },   // Seed + Kachel-Änderungen (siehe C)
    "civs":    { "v": 1, "data": { } },
    "settlements": { "v": 1, "data": { } },
    "economy": { "v": 1, "data": { } },
    "diplomacy": { "v": 1, "data": { } }, // Beziehungen, Kriege, Verträge
    "units":   { "v": 1, "data": { } },   // Position, HP, Zustand, Auftrag, Ziel-IDs
    "armies":  { "v": 1, "data": { } }
  },
  "checksum": "sha256 über sections"      // erkennt beschädigte Dateien
}
```

- Jede Sektion hat eine eigene Version `v`, damit Systeme unabhängig migrieren können. `formatVersion` betrifft nur den Rahmen.
- Unbekannte Sektionen beim Laden: Warnung, ignorieren. Fehlende Pflichtsektion: kontrollierter Fehler.

## C. Terrain

- Gespeichert werden `worldSeed`, Generator-Einstellungen und eine **Liste geänderter Kacheln** (Index + neue `TileData`) gegenüber der frisch generierten Welt.
- Voraussetzung: Die Weltgenerierung (Phase 2) ist pro Seed **deterministisch** (`SimRandom`, kein `UnityEngine.Random`, keine Abhängigkeit von Framerate).
- Rückfall: Wenn mehr als ca. 30 % der Kacheln geändert sind, wird das komplette Raster komprimiert gespeichert (Flag `"fullGrid": true`).

## D. Speichern

- Nur an einer **Tick-Grenze**: Befehlswarteschlange und Event-Bus vorher leeren (`Flush`).
- Nicht gespeichert (wird beim Laden neu erzeugt): Pfadfindungsanfragen und -caches, Spatial Hash, Chunk-Meshes, Partikel, Audio, UI-Zustand, Objekt-Pools. Einheiten mit laufendem Pfad speichern nur ihr Ziel und fordern den Pfad nach dem Laden neu an.
- **Atomar schreiben:** `name.save.tmp` schreiben → prüfen → alte Datei zu `name.save.bak` → `.tmp` umbenennen. Ein Absturz während des Speicherns zerstört so keinen bestehenden Spielstand.
- Ort: `Application.persistentDataPath/Saves/`.

## E. Laden

1. Datei lesen, JSON parsen → bei Fehler `Corrupted`.
2. `format` und `formatVersion` prüfen → zu neu: `TooNew`; zu alt ohne Migration: `Incompatible`.
3. Prüfsumme prüfen → `Corrupted`.
4. Migrationen pro Sektion ausführen (`ISaveMigration`, Kette v1→v2→…).
5. Leere Simulation erzeugen, Sektionen in `LoadOrder` einspielen.
6. **Referenzprüfung:** Alle gespeicherten IDs gegen die geladenen Stores prüfen. Ungültige Verweise werden bereinigt (Auftrag → Idle, Armee ohne Einheiten → aufgelöst) und als Warnung protokolliert.
7. Presentation komplett neu aufbauen.
- Schlägt ein Schritt fehl, bleibt der vorherige Spielzustand erhalten (Laden in eine neue Instanz, erst bei Erfolg austauschen).

Fehlerarten mit verständlichen Meldungen: `NotFound`, `Corrupted`, `TooNew`, `Incompatible`, `MissingContent` (unbekannter Inhaltsschlüssel), `IoError`.

## F. Was das für die anderen Phasen bedeutet

- Phase 2 (Welt): deterministische Generierung pro Seed; Kachel-Änderungen über `IWorldGrid.Set` laufen lassen, damit sie erfasst werden.
- Phase 3 (Zivilisationen): fortlaufende Civ-/Siedlungs-IDs, Zählerstand speicherbar.
- Phase 4 (Krieg/Diplomatie): Kriege und Verträge als eigene Einträge mit ID, nicht als implizite Zustände.
- Phase 5 (Militär): Aufträge als Daten (Typ + Ziel-ID/Position), keine Coroutines oder Lambdas als Auftrag.
- Phase 6 (UI): keinen Spielzustand in UI-Objekten halten.
