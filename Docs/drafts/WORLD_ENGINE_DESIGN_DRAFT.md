# World Engine (Phase 2) – Entwurf

Status: **ENTWURF**, Stand 2026-10-25. Noch kein Code, nichts getestet. Laut IMPLEMENTATION_PLAN darf vor Abschluss von P1 (Fundament, Integrations-Thread) nur unter `Docs/drafts/` gearbeitet werden. Sobald P1 steht, werden die Signaturen aus §3–§6 in SYSTEM_INTERFACES §4/§5 übernommen und dort gepflegt; dieser Entwurf wird dann gelöscht oder als historisch markiert.

Grundlagen: PROJECT_MASTER (D5, D13, D14), ARCHITECTURE §3/§4, SYSTEM_INTERFACES §4/§5, INTEGRATION §3, SAVE_FORMAT_PROPOSAL §C, Wünsche aus CIVILIZATION_DESIGN_DRAFT §8.

## 1. Zuständigkeit (aus INTEGRATION §3)

| Teil | Besitzer |
|---|---|
| `IWorldGrid`/`IWorldQuery`/`TileData`, `IWorldGenerator`, `WorldGrid : ISaveable` | World (dieser Thread) |
| `IPathfinder`, `ISpatialIndex`, `MovementSystem` | World |
| Chunk-Terrain, Wasser, Vegetations-Instancing, Ressourcen-Darstellung, Kachel-Hervorhebung | World (Presentation) |
| `EntityId`, `SimRandom`, EventBus, `SimContext`, `GameBootstrap`, `UnitStore` | Integration – World programmiert nur dagegen |
| Kamera | World (festgelegt INTEGRATION §5, 2026-10-25); `GameBootstrap` erzeugt sie nur |
| Auswahl (`ISelectionService`), Minimap-UI | UI. World liefert Picking (Bildschirm → Kachel/Entity) und die Minimap-Textur-Quelle |

## 2. Koordinatensystem und Weltgrenzen

- **Maßstab (D13):** 1 Unity-Einheit = 1 m. 1 Kachel = `WorldConstants.TileSize` = 4 m.
- **Achsen:** Unity linkshändig, Y nach oben. Kachel `(x, y)` ↔ Weltposition `X = x · 4`, `Z = y · 4`. Kachel-Index im Array: `i = y · Width + x`.
- **Ursprung:** Weltecke (0, 0, 0) = untere linke Ecke von Kachel (0, 0). Kachelmitte = `((x + 0.5) · 4, h, (y + 0.5) · 4)`.
- **Grenzen:** Kacheln `0 ≤ x < Width`, `0 ≤ y < Height`. Startgröße 256 × 256 (= 1 024 m × 1 024 m), Ziel bis 512 × 512. Der Rand (2 Kacheln) ist immer Tiefwasser, damit kein Objekt die Welt verlassen kann; `InBounds` liefert false außerhalb, `ClampToBounds` existiert für Kamera/Befehle.
- **Höhe:** `TileData.Height` in Metern über Meeresgrund 0. Meeresspiegel `SeaLevel` (Standard 20 m), Gipfel bis ca. 120 m. `SampleHeight(worldX, worldZ)` interpoliert bilinear zwischen Kachelmitten, damit Einheiten nicht in Stufen laufen.
- **Umrechnung (statisch, `WorldCoords`):** `TileToWorld(x, y)`, `WorldToTile(worldX, worldZ, out x, out y)` (floor), `TileCenter(x, y)`.

## 3. Kacheldaten (Erweiterung von SYSTEM_INTERFACES §4)

```csharp
public enum TerrainType : byte { DeepWater = 0, ShallowWater = 1, Beach = 2, Grass = 3, Forest = 4, Rock = 5, Mountain = 6, Snow = 7, Desert = 8, Swamp = 9, River = 10 }
public enum BiomeType   : byte { Ocean = 0, Coast = 1, Temperate = 2, Boreal = 3, Tundra = 4, Desert = 5, Savanna = 6, Wetland = 7, Alpine = 8 }
public enum ResourceType: byte { None = 0, Food = 1, Wood = 2, Stone = 3, Ore = 4, Oil = 5, Gold = 6 }
[System.Flags] public enum TileFlags : ushort { None = 0, Burning = 1, Scorched = 2, Road = 4, Crater = 8, RiverBank = 16, Lake = 32 }

public struct TileData
{
    public float Height;
    public TerrainType Terrain;
    public BiomeType Biome;            // statt "byte Biome" in SYSTEM_INTERFACES, Wert bleibt 1 Byte
    public ResourceType Resource;
    public ushort ResourceAmount;
    public byte Fertility;             // 0..255, Nahrungsertrag (Wunsch Phase 3)
    public byte Moisture;              // 0..255, aus Generierung, für Brand/Biom-Logik
    public int OwnerCivId;             // 0 = niemand; Schreiben nur über SetOwner
    public EntityId Building;          // EntityId.None = frei; Schreiben nur über SetBuilding/ClearBuilding
    public TileFlags Flags;
}
```

Enum-Werte explizit nummeriert, neue Werte nur anhängen (SAVE_FORMAT_PROPOSAL §A.4).

## 4. Abfragen und Änderungen

```csharp
public interface IWorldQuery
{
    int Width { get; }  int Height { get; }
    ulong Seed { get; }  float SeaLevel { get; }
    bool InBounds(int x, int y);
    ref readonly TileData Get(int x, int y);
    float SampleHeight(float worldX, float worldZ);              // bilinear, Meter
    UnityEngine.Vector3 SampleNormal(float worldX, float worldZ);
    float Slope(int x, int y);                                   // max. Höhendifferenz zu Nachbarn / TileSize
    bool IsWater(int x, int y);                                  // DeepWater, ShallowWater, River
    bool IsWalkable(int x, int y, MovementClass mc);             // siehe §6
    float MoveCost(int x, int y, MovementClass mc);              // ≥ 1, +∞ = unpassierbar
    bool IsBuildable(int x, int y);                              // begehbar zu Fuß, Hang < Grenze, kein Gebäude, kein Wasser
    void ForEachInRect(int minX, int minY, int maxX, int maxY, TileVisitor visitor);   // geklippt an Grenzen
    int FindBestTiles(int cx, int cy, int radius, TileScorer scorer, int maxResults, System.Collections.Generic.List<TileScore> results);
    bool Raycast(UnityEngine.Ray ray, float maxDistance, out int x, out int y, out UnityEngine.Vector3 hit);  // Picking ohne Collider
}
public delegate void  TileVisitor(int x, int y, in TileData tile);
public delegate float TileScorer(int x, int y, in TileData tile);   // ≤ 0 = ungeeignet
public struct TileScore { public int X, Y; public float Score; }

public interface IWorldGrid : IWorldQuery
{
    void Set(int x, int y, in TileData tile);                    // God-Tools; TileChangedEvent
    void SetHeight(int x, int y, float height);                  // Terrain-Tools, Krater; TileChangedEvent
    bool TryHarvest(int x, int y, int amount, out int taken);    // ResourceDepletedEvent bei 0; Forest → Grass
    void SetOwner(int x, int y, int civId);                      // gebündelt: OwnershipChangedEvent einmal pro Tick mit Rechteck
    bool SetBuilding(int x, int y, EntityId building);           // false wenn belegt/nicht baubar
    void ClearBuilding(int x, int y);
    void SetFlags(int x, int y, TileFlags set, TileFlags clear);
}

public struct TileChangedEvent       : ISimEvent { public int X, Y; public TileChangeKind Kind; }   // Kind: Height, Terrain, Resource, Building, Flags
public struct ResourceDepletedEvent  : ISimEvent { public int X, Y; public ResourceType Resource; }
public struct OwnershipChangedEvent  : ISimEvent { public int MinX, MinY, MaxX, MaxY; }          // ein Event pro Tick, Rechteck aller Änderungen
```

`FindBestTiles` sammelt in eine vom Aufrufer übergebene Liste (keine Allokation pro Aufruf) und nutzt eine feste Heap-Größe `maxResults`.

## 5. Generierung (`IWorldGenerator`, deterministisch)

```csharp
public sealed class WorldGenSettings
{
    public ulong Seed; public int Width = 256, Height = 256;
    public float SeaLevel = 20f, MaxHeight = 120f;
    public float LandRatio = 0.55f;          // Anteil Land, gesteuert über Meeresspiegel-Perzentil
    public int RiverCount = 12; public float ForestDensity = 1f; public float ResourceRichness = 1f;
}
public interface IWorldGenerator { WorldGrid Generate(WorldGenSettings s); }
```

Determinismus: Die Generierung nutzt **nicht** den laufenden `SimRandom`-Zustand, sondern ein eigenes, nur aus `Seed` abgeleitetes zustandsloses Hash-Rauschen (Integer-Hash → Gradient-Noise, eigene Implementierung, kein `Mathf.PerlinNoise`, keine `float`-Abhängigkeit von Systembibliotheken außer Grundrechenarten). Grund: Die Welt muss beim Laden aus dem Seed exakt neu erzeugbar sein (SAVE_FORMAT_PROPOSAL §C), unabhängig davon, wie oft `SimRandom` vorher benutzt wurde. Zufallsentscheidungen innerhalb der Generierung (Flussquellen, Ressourcen) nutzen eine lokale `SimRandom`-Instanz mit abgeleitetem Seed (`Seed ^ Konstante pro Schritt`).

Pipeline (jeder Schritt einzeln testbar):
1. **Kontinente:** fBm-Rauschen (5 Oktaven) × radiale Abschwächung zum Rand → Inseln/Küsten statt Rechteck-Land.
2. **Gebirge:** Ridged-Noise, maskiert, damit Bergketten statt verstreuter Spitzen entstehen; Hügel aus mittlerer Oktave.
3. **Meeresspiegel:** Perzentil der Höhen, sodass `LandRatio` erreicht wird; Rand erzwungen Tiefwasser.
4. **Senken füllen** (Priority-Flood) → abflusslose Senken über Schwelle werden **Seen** (Flag `Lake`).
5. **Flüsse:** Abflussrichtung (D8) + Akkumulation; Kacheln mit Akkumulation > Schwelle werden `River`, Ufer `RiverBank`. Fluss gräbt Bett 1–2 m ein. Quelle: Akkumulation, nicht Zufall → plausible Verläufe zum Meer/See.
6. **Temperatur** (Breite + Höhe) und **Feuchte** (Rauschen + Nähe zu Wasser/Fluss) → **Biom**-Tabelle (Whittaker-artig).
7. **Geländetyp** aus Biom, Höhe, Hang (Strand an Küsten mit flachem Hang, Fels bei steilem Hang, Schnee über Schneegrenze).
8. **Wälder:** Poisson-artige Verteilung über Rauschmaske × Feuchte × Biom; Wald = `Forest` + Ressource `Wood` (Menge nach Dichte).
9. **Ressourcen:** Stein an Fels/Hängen, Erz/Gold geclustert in Gebirgsnähe, Öl in Wüste/Sumpf/Küste (selten), Nahrung über `Fertility` (Grasland, Flussufer hoch). Mindestgarantie: in jedem Landkörper > 2 000 Kacheln existieren erreichbare Holz-, Stein- und Erzvorkommen (Test).
10. **Startplatz-Kandidaten:** `FindStartLocations(count)` liefert gut verteilte, baubare Plätze mit Wasser/Holz/Nahrung im Umkreis (für Phase 3).

Zielwerte (aus P2-Abnahme): 256 × 256 in < 3 s, 512 × 512 in < 10 s (zu messen).

## 6. Begehbarkeit und Navigation

| MovementClass | begehbar | Kosten (Basis 1) |
|---|---|---|
| Foot | alles außer Tiefwasser/Flachwasser/Fluss ohne Brücke; Hang < 45° | Wald 1,5 · Sumpf 2 · Fels 2 · Berg 3 · Schnee 2 · Straße 0,7 |
| Wheeled | wie Foot, aber kein Wald, kein Sumpf, kein Berg; Hang < 20° | Gras 1 · Wüste 1,3 · Straße 0,5 |
| Tracked | wie Wheeled + Wald erlaubt (Kosten 2); Hang < 30° | Gras 1 · Wald 2 · Straße 0,6 |

Gebäude-Kacheln sind für alle Klassen gesperrt (außer eigene Straßen/Tore, später). Brücken = Flag `Road` auf Flusskacheln.

```csharp
public interface IPathfinder
{
    int RequestPath(int fromX, int fromY, int toX, int toY, MovementClass mc);  // Budget pro Tick
    PathStatus TryGetPath(int requestId, System.Collections.Generic.List<(int x, int y)> path);
    void Cancel(int requestId);
    float EstimateCost(int fromX, int fromY, int toX, int toY, MovementClass mc);  // synchron, Oktil-Heuristik × Regions-Check
    bool AreConnected(int ax, int ay, int bx, int by, MovementClass mc);           // O(1) über Regionen-IDs
}
```

- A* mit binärem Heap, wiederverwendete Arrays (keine Allokation pro Anfrage), 8-Nachbarschaft ohne Eckenschneiden.
- **Regionen** pro MovementClass (Flood-Fill, inkrementell bei `TileChangedEvent` neu): unerreichbare Ziele werden sofort `NotFound` statt ganze Karte zu durchsuchen.
- Budget: max. N Knoten-Expansionen pro Tick (Start 20 000), Rest in Folgeticks.
- Gruppen/Armeen: Flow Fields später (ARCHITECTURE §4); Schnittstelle bleibt gleich.
- `MovementSystem` (Order 300) bewegt Einheiten aus `UnitStore` entlang Pfad, Höhe aus `SampleHeight`. Liest aus `UnitTypeData` (Werte von Civ/Military, Wunsch aus PHASE5_MILITARY_DESIGN): `MovementClass`, `Speed` (m/s), `TurnRate` (°/s), `Acceleration` (m/s²). Infanterie dreht praktisch sofort; Fahrzeuge drehen mit `TurnRate`, fahren Kurven statt auf der Stelle zu wenden und beschleunigen/bremsen mit `Acceleration` (Bremsweg vor Wegpunkten). Zielgeschwindigkeit = `Speed` / `MoveCost(Kachel, mc)`, zusätzlich Hang-Faktor (bergauf langsamer, für Wheeled stärker als Tracked). Ausrichtung des Fahrzeugs an der Geländenormalen (`SampleNormal`) für die Darstellung. Pfad-Glättung (String-Pulling über Sichtlinie auf dem Raster), damit Fahrzeuge nicht im Zickzack fahren.
- `ISpatialIndex`: Spatial Hash, Zellen 8 × 8 Kacheln (32 m), wie SYSTEM_INTERFACES §5.

## 7. Darstellung (Presentation)

- **TerrainChunkRenderer:** Chunks 32 × 32 Kacheln, ein Mesh pro Chunk mit geteilten Eckhöhen (glatt) und Vertex-Farben + Biom-Texturen (Splat über Vertex-Farbe), Normalen berechnet. Nur Chunks mit `TileChangedEvent` werden im nächsten Frame neu gebaut (Ziel < 5 ms pro Chunk). Frustum-Culling durch Unity pro Chunk-Renderer. LOD (halbe Auflösung) für Chunks in großer Entfernung.
- **Wasser:** eine Meeresebene + Fluss/See-Meshes, URP-Shader-Graph-Material (Tiefe → Farbe, leichte Wellen-Normalen).
- **Vegetation/Felsen:** `Graphics.RenderMeshInstanced` pro Chunk und Modelltyp, Matrizen beim Chunk-Rebuild berechnet, Distanz-Cutoff.
- **Licht/Atmosphäre:** ein Directional Light mit Schatten (Cascades), URP-Nebel, Skybox, Post-Processing (Tonemapping, leichte Farbkorrektur) – einheitlich mit UI_STYLE_GUIDE abstimmen.
- **Assets:** CC0 bevorzugt, z. B. Quaternius „Stylized Nature MegaKit“ / „Ultimate Nature Pack“ (Bäume, Felsen), ambientCG / Poly Haven (Boden-Texturen), Kenney „Nature Kit“. Lizenz und Download vor Import prüfen, Eintrag in `Assets/ThirdParty/ASSET_CREDITS.md`. Ein Stil: Low-Poly (D12). Fallback: eigene prozedurale Meshes (Kegel-/Kugel-Bäume, Ikosaeder-Felsen) mit gleicher Palette.
- **Kachel-Hervorhebung:** `TileHighlightRenderer` (Overlay-Mesh/Decal) für Hover, Auswahl, Bauvorschau, Territoriumsgrenzen.
- **Minimap-Quelle:** `WorldMapTextureBuilder` erzeugt eine `Texture2D` (1 Pixel = 1 Kachel; Ebenen: Gelände, Besitz, Ressourcen) und aktualisiert nur geänderte Bereiche gebündelt. Die Minimap-UI selbst baut der UI-Thread.

## 8. Kamera (World, festgelegt in INTEGRATION §5)

`StrategyCameraController` (Presentation, Input System), erzeugt von `GameBootstrap`: WASD/Pfeiltasten und Kantenscrollen zum Verschieben, Mausrad-Zoom 15–400 m Höhe mit zoomabhängiger Neigung, Q/E bzw. mittlere Maustaste zum Drehen, Doppelklick/Taste F fokussiert Kachel oder Entity. Bleibt über dem Gelände (`SampleHeight`) und innerhalb der Weltgrenzen (`ClampToBounds`). Geschwindigkeit skaliert mit Zoomhöhe, unabhängig von der Spielgeschwindigkeit (nutzt unskalierte Zeit).

## 9. Speichern

`WorldGrid : ISaveable`, `SaveKey = "world"`, `LoadOrder = 0`. DTO: `Seed`, `WorldGenSettings`, Liste geänderter Kacheln (Index + TileData-DTO mit String-Schlüsseln für Terrain/Biom/Ressource laut SAVE_FORMAT_PROPOSAL §A.3), ab > 30 % Änderungen komplettes Raster komprimiert (`fullGrid`). Laden: neu generieren aus Seed → Änderungen anwenden → Regionen und Chunks neu aufbauen.

## 10. Tests (geplant, nichts davon ausgeführt)

EditMode (reines C#):
- Gleicher Seed → identische Kachel-Arrays (Byte-Vergleich, Hash); anderer Seed → abweichend.
- Rand ist Tiefwasser; Landanteil ±5 % um `LandRatio`; jeder Fluss endet in Meer/See; Seen sind abflusslose Senken.
- Jedes Biom und jeder Geländetyp, die für die Klimazone erwartet werden, kommen vor; Ressourcen-Mindestgarantie pro Landkörper.
- Koordinaten-Roundtrip Welt ↔ Kachel; `SampleHeight` stetig.
- `IsWalkable`/`MoveCost` pro MovementClass laut Tabelle §6.
- A*: findet Pfad auf bekannten Karten, meldet `NotFound` bei getrennten Inseln (über Regionen, ohne Vollsuche), Pfad ist optimal auf kleinen Testkarten.
- `TryHarvest`, `SetOwner`, `SetBuilding` lösen die richtigen Events aus; Ownership-Event gebündelt einmal pro Tick.
- Save → Load → identischer Zustand (inkl. geänderter Kacheln).
- Performance: Generierung 256² < 3 s, 512² < 10 s; 1 000 Pfadanfragen über 256²-Karte, Zeit pro Tick im Budget.

PlayMode (nur auf dem PC des Nutzers ausführbar):
- Welt sichtbar, Chunk-Rebuild < 5 ms nach Kachel-Änderung, FPS bei 256² / 512² messen.
- Kamera: Zoom/Drehen/Verschieben, bleibt innerhalb Grenzen und über dem Gelände.
- Picking: Klick auf Kachel liefert richtige Koordinate; Hervorhebung sichtbar.

## 11. Offene Punkte

- [O] Projektort (Repo oder PC-Ordner) – blockiert alles (INTEGRATION §4 #1).
- [B] Kamera gehört World (INTEGRATION §5).
- [B] Maßstab 4 m pro Kachel (D13), mit Integration abgestimmt; ARCHITECTURE §3 wird von Phase 0 nachgezogen.
- [O] `TileData.Biome` als `BiomeType` statt `byte` und neue Felder `Fertility`, `Moisture` – Änderung an SYSTEM_INTERFACES §4, wird nach P1 eingetragen.
- [B] `MovementClass` liegt in `WorldBeyond.Core`, angelegt vom Integrations-Thread im Fundament.
