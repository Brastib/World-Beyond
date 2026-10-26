# World Beyond – SYSTEM_INTERFACES

Stand: 2026-10-25, Phase 1 (Doku). **Alle Schnittstellen sind geplant, keine existiert bisher im Code.**
Phasennummern = Nutzer-Phasen (IMPLEMENTATION_PLAN). Besitzer = Zuständigkeitsregister `Docs/INTEGRATION.md` §3. Verbindliche ID-/Speicherregeln: ARCHITECTURE §11.
Wer in einem Chat eine Schnittstelle implementiert, ändert hier den Status auf `implementiert (Datei, Phase)`. Wer eine Signatur ändern muss, dokumentiert es hier mit Grund und passt alle Aufrufer an.

Namensraum-Präfix: `WorldBeyond.`

## 0. Statusübersicht

| Schnittstelle | Schicht | Phase | Besitzer | Status |
|---|---|---|---|---|
| `EntityId`, `ISimEventBus`, `SimRandom`, `SimClock` | Core/Sim | 1b | Integration | implementiert (Branch `claude/phase1b-fundament`), dotnet-Tests grün, nicht in Unity kompiliert |
| `EntityStore`, `SequentialIdAllocator`, `ContentRegistry` | Core | 1b | Integration | implementiert (1b), dotnet-Tests grün, nicht in Unity kompiliert |
| `ISimulationSystem`, `SimContext`, `SimulationRunner`, `GameComposition`, `GameBootstrap` | Sim/App | 1b | Integration | implementiert (1b), dotnet-Tests grün außer `GameBootstrap` (braucht Unity), nicht in Unity kompiliert |
| `IWorldCommand`, `ICommandQueue`, `CommandProcessingSystem` | Sim | 1b | Integration (Queue), Fachsysteme (Commands) | implementiert (1b), dotnet-Tests grün |
| `ISaveable`, `SaveableRegistry`, `SimCoreSaveable`, `ISaveReferenceCheck`, `LoadReport` | Sim | 1b | Integration (Definition), jedes System (Implementierung) | implementiert (1b), dotnet-Tests grün |
| `ISaveLoadService`, `SaveGameData`, `SaveGameSerializer`, `SaveGameMigrator`, `ISaveMigration`, `ISaveReferenceCheck`, `LoadReport` | App/Sim | 1b Gerüst, 7 vollständig | Save (Phase 7); Migrationsschritte und `ISaveReferenceCheck` implementiert das jeweilige Fachsystem | geplant |
| `IUnitStore`, `IUnitQuery`, `UnitStore`, `UnitTypeData`, `UnitTypeRegistry` | Sim | 1b | Integration (Store); Civ/Military tragen Typen ein | implementiert (1b), dotnet-Tests grün |
| `IWorldGrid`, `IWorldQuery`, `IWorldGenerator`, Koordinaten | Sim | 2 | World | Signaturen präzisiert (§4), kein Code |
| `ISpatialIndex`, `IPathfinder`, `MovementSystem` | Core/Sim | 2 | World | Signaturen präzisiert (§5), kein Code |
| `ICivilizationRegistry`/`Query`, `ISettlementRegistry`/`Query`, `ITerritoryService`/`Query` | Sim | 3 | Civ | geplant |
| `IEconomy`, `ITechTree`, `IRecruitment` | Sim | 3 (Recruitment mit 5) | Civ | geplant |
| `ICivAI` (Rahmen) | Sim | 3 | Civ (Rahmen), War (Kriegsbewerter) | geplant |
| `IDiplomacyService`, `IDiplomacyQuery`, `IWarQuery` | Sim | 4 | War | geplant |
| `ICombatService`, `IProjectileSystem` | Sim | 5 | Military | geplant |
| `IArmyRegistry`, `IArmyQuery`, `IArmyAI` | Sim | 5 | Military | geplant |
| `ISelectionService`, `IGodTool`, `IGameEventFeed`, UI-Presenter | App/Pres | 6 (Auswahl-Grundlage in 2) | UI | geplant |
| `IAudioService`, `IVfxService` | Pres | 6 | UI | geplant |

## 1. Core

```csharp
namespace WorldBeyond.Core
{
    // Stabile ID mit Generation; Index 0 = ungültig.
    public readonly struct EntityId : System.IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly int Generation;
        public static readonly EntityId None;
        public bool IsValid { get; }
    }

    public interface ISimEvent { }

    // Explizite Werte, nur anhängen.
    public enum MovementClass : byte { Foot = 0, Wheeled = 1, Tracked = 2 }

    public interface ISimEventBus
    {
        void Publish<T>(in T evt) where T : struct, ISimEvent;
        void Subscribe<T>(System.Action<T> handler) where T : struct, ISimEvent;
        void Unsubscribe<T>(System.Action<T> handler) where T : struct, ISimEvent;
        // Events werden gesammelt und am Ende jedes Ticks ausgeliefert (kein Reentrancy-Chaos).
        void Flush();
    }

    public sealed class SimRandom          // Seed-basiert, Zustand speicherbar
    {
        public SimRandom(ulong seed);
        public int Range(int minInclusive, int maxExclusive);
        public float Value01();
        public ulong State { get; set; }
    }

    // Verwaltet EntityIds einer Art (Einheiten, Gebäude, Geschosse). Index 0 reserviert.
    // Destroy erhöht die Generation (Überlauf → 1, nie 0) und legt den Index auf die Freiliste.
    public sealed class EntityStore
    {
        public EntityStore(int initialCapacity);
        public EntityId Create();
        public bool Destroy(EntityId id);           // false bei ungültiger/veralteter ID, keine Exception
        public bool Exists(EntityId id);            // Index im Bereich, lebt, Generation stimmt
        public int AliveCount { get; }
        public EntityStoreState CaptureState();
        public bool TryRestoreState(EntityStoreState state, out string error);  // prüft Konsistenz (Längen, Freiliste ↔ Lebend-Markierung, keine Duplikate)
    }

    [System.Serializable]
    public sealed class EntityStoreState             // DTO
    {
        public int[] Generations;                    // je Index
        public bool[] Alive;                         // je Index
        public int[] FreeList;                       // Reihenfolge wird erhalten
    }

    // Fachliche IDs (Civ, Siedlung, Armee, Krieg, Vertrag): fortlaufend, nie wiederverwendet, 0 = ungültig.
    public sealed class SequentialIdAllocator
    {
        public int Next();
        public int LastIssued { get; }
        public bool TryRestore(int lastIssued, out string error);  // < 0 → Fehler
    }

    // Explizite Werte, nur anhängen.
    public enum ContentKind : byte { Unit = 0, Building = 1, Resource = 2, Tech = 3, Biome = 4, Terrain = 5 }

    // String-Schlüssel ↔ Laufzeit-int. Laufzeit-ints sind nur innerhalb einer Sitzung gültig
    // und dürfen nie gespeichert werden. Registrierung beim Start aus den Inhaltstabellen.
    public sealed class ContentRegistry
    {
        public int Register(ContentKind kind, string key);         // doppelter Schlüssel → Exception beim Start (Konfigurationsfehler)
        public bool TryGetId(ContentKind kind, string key, out int runtimeId);
        public string GetKey(ContentKind kind, int runtimeId);
        public bool Contains(ContentKind kind, string key);
    }
}
```

## 2. Simulationstakt

Verbindlich ist der Code; hier die öffentliche Oberfläche. Dateien: `Simulation/SimClock.cs`, `Simulation/ISimulationSystem.cs`, `Simulation/SimContext.cs`, `Application/SimulationRunner.cs`, `Application/GameComposition.cs`, `Application/Unity/GameBootstrap.cs`.

```csharp
namespace WorldBeyond.Simulation
{
    public sealed class SimClock
    {
        public const int TicksPerSecond = 10;
        public long Tick { get; }
        public float DeltaSeconds { get; }        // 0.1
        public double ElapsedSeconds { get; }
        public void Advance();                    // nur SimulationRunner
        public void Restore(long tick);           // nur SimCoreSaveable
    }

    public interface ISimulationSystem
    {
        string Name { get; }
        int Order { get; }                 // Konstanten in SystemOrder (Tabelle unten)
        int TickInterval { get; }          // läuft, wenn Tick % TickInterval == 0
        void Tick(SimContext ctx);
    }

    public sealed class SimContext         // keine globale Statik
    {
        // Fundament, immer vorhanden:
        public readonly SimClock Clock;
        public readonly SimRandom Random;
        public readonly ISimEventBus Events;
        public readonly ICommandQueue Commands;
        public readonly ContentRegistry Content;
        public readonly UnitTypeRegistry UnitTypes;
        public readonly IUnitStore Units;
        public readonly SaveableRegistry Saveables;
        // Fachdienste (IWorldGrid, ICivilizationRegistry, IEconomy, IDiplomacyService, ICombatService …):
        public void Register<T>(T service) where T : class;   // nur in GameComposition; zweite Registrierung → Exception
        public T Get<T>() where T : class;                      // fehlt → Exception mit Namen (Konfigurationsfehler)
        public bool TryGet<T>(out T service) where T : class;
        public bool IsSealed { get; }                            // nach GameComposition.Create true
    }
}

namespace WorldBeyond.Application
{
    public enum GameSpeed { Paused = 0, x1 = 1, x2 = 2, x5 = 5, x10 = 10 }

    public interface ISimulationRunner
    {
        GameSpeed Speed { get; set; }
        float InterpolationAlpha { get; }  // 0..1 zwischen Ticks
        bool IsTicking { get; }            // Speichern nur bei false
        void Register(ISimulationSystem system);
        int Advance(float realDeltaSeconds);   // höchstens MaxTicksPerAdvance (20) Ticks; Rückstand wird verworfen
        void StepOnce();                       // ein Tick, auch bei Pause
        event System.Action<long> TickCompleted;
    }

    public sealed class GameComposition    // Composition Root, reines C#
    {
        public SimContext Context { get; }
        public SimulationRunner Runner { get; }
        public GameStartSettings Settings { get; }   // Seed, WorldWidth, WorldHeight, CivilizationCount
        public static GameComposition Create(GameStartSettings settings, System.Action<SimContext> contentSetup = null);
        // Fachphasen tragen sich in RegisterPhaseServices ein (per PR, INTEGRATION §0).
    }

    public sealed class GameBootstrap : UnityEngine.MonoBehaviour   // einziges zentrales MonoBehaviour
    {
        public GameComposition Composition { get; }
        public event System.Action<GameComposition> CompositionChanged;
        public void Replace(GameComposition composition);   // nach erfolgreichem Laden
    }
}
```

Ablauf eines Ticks (`SimulationRunner`): Systeme nach `Order` (bei Gleichstand Registrierungsreihenfolge) → `Events.Flush()` → `Clock.Advance()` → `TickCompleted`.

| Order (`SystemOrder`) | System |
|---|---|
| 0 `Commands` | CommandProcessingSystem (Spieler/God-Befehle einspielen) |
| 100 `AI` | AI (strategisch/operativ/taktisch, je nach Intervall) |
| 200 `Economy` | Economy & Production |
| 300 `Movement` | Movement & Pathfinding |
| 400 `Combat` | Combat (Zielwahl, Feuern) |
| 500 `Projectiles` | Projectiles & Damage |
| 600 `Cleanup` | Death/Destruction & Cleanup |
| 700 `Settlements` | Settlements, Territory, Population |
| 800 `Diplomacy` | Diplomacy |
| 900 | EventBus.Flush (macht der Runner selbst) |

## 3. Befehle

Datei: `Simulation/Commands/Commands.cs`.

```csharp
namespace WorldBeyond.Simulation.Commands
{
    public interface IWorldCommand
    {
        bool Validate(SimContext ctx, out string reason);   // false → CommandRejectedEvent mit Grund
        void Execute(SimContext ctx);                       // Exception → CommandRejectedEvent, Simulation läuft weiter
    }

    public interface ICommandQueue
    {
        void Enqueue(IWorldCommand command);   // nur Main Thread; wirkt im nächsten Tick (Order 0)
        int PendingCount { get; }
    }

    public struct CommandRejectedEvent : ISimEvent { public string CommandName; public string Reason; }
    public struct CommandExecutedEvent : ISimEvent { public string CommandName; }
}
```

Während der Ausführung eingereihte Commands laufen im nächsten Tick.

## 4. Welt

Besitzer: Phase 2 World. Status: **präzisiert, noch nicht implementiert** (2026-10-25). Begründung und Generierungsdetails: `Docs/drafts/WORLD_ENGINE_DESIGN_DRAFT.md`.
Änderungen gegenüber Phase-0-Fassung: `Biome` als `BiomeType` (weiterhin 1 Byte); neue Felder `Fertility`, `Moisture`; neue Werte `TerrainType.River`, `TileFlags.Wreck`; Schreibzugriffe für Besitz, Gebäude und Ressourcen als eigene Methoden (gewünscht von Phase 3); `Generate` liefert `WorldGrid` (konkrete Klasse, weil sie zusätzlich `ISaveable` ist).

```csharp
namespace WorldBeyond.Simulation.World
{
    using WorldBeyond.Core;   // EntityId, ISimEvent, MovementClass

    // Explizite Werte, nur anhängen (SAVE_FORMAT_PROPOSAL §A.4).
    public enum TerrainType : byte { DeepWater = 0, ShallowWater = 1, Beach = 2, Grass = 3, Forest = 4, Rock = 5, Mountain = 6, Snow = 7, Desert = 8, Swamp = 9, River = 10 }
    public enum BiomeType   : byte { Ocean = 0, Coast = 1, Temperate = 2, Boreal = 3, Tundra = 4, Desert = 5, Savanna = 6, Wetland = 7, Alpine = 8 }
    // Metal/Tools: nur Lagergüter, nie Kachelvorkommen (INTEGRATION §5). Im Spielstand als Enum-Name gespeichert.
    public enum ResourceType: byte { None = 0, Food = 1, Wood = 2, Stone = 3, Ore = 4, Oil = 5, Gold = 6, Metal = 7, Tools = 8 }
    [System.Flags] public enum TileFlags : ushort { None = 0, Burning = 1, Scorched = 2, Road = 4, Crater = 8, RiverBank = 16, Lake = 32, Wreck = 64 }
    public enum TileChangeKind : byte { Height = 0, Terrain = 1, Resource = 2, Building = 3, Flags = 4, Full = 5 }

    public struct TileData
    {
        public float Height;               // Meter; Meeresspiegel = IWorldQuery.SeaLevel
        public TerrainType Terrain;
        public BiomeType Biome;
        public ResourceType Resource;
        public ushort ResourceAmount;
        public byte Fertility;             // 0..255, Nahrungsertrag
        public byte Moisture;              // 0..255
        public int OwnerCivId;             // 0 = niemand; schreiben nur über SetOwner
        public EntityId Building;          // None = frei; schreiben nur über SetBuilding/ClearBuilding
        public TileFlags Flags;
    }

    public delegate void  TileVisitor(int x, int y, in TileData tile);
    public delegate float TileScorer(int x, int y, in TileData tile);      // <= 0 = ungeeignet
    public struct TileScore { public int X, Y; public float Score; }

    public interface IWorldQuery
    {
        int Width { get; }
        int Height { get; }
        ulong Seed { get; }
        float SeaLevel { get; }
        bool InBounds(int x, int y);
        ref readonly TileData Get(int x, int y);

        // Gelände
        float SampleHeight(float worldX, float worldZ);                    // bilinear zwischen Kachelmitten
        UnityEngine.Vector3 SampleNormal(float worldX, float worldZ);
        float SlopeDegrees(int x, int y);                                  // steilste Nachbarkante
        bool IsWater(int x, int y);                                        // DeepWater, ShallowWater, River (ohne Road = Brücke)
        bool HasLineOfSight(UnityEngine.Vector3 from, UnityEngine.Vector3 to);   // Höhenprofil + Wald (Sichtblocker), Schritt = TileSize/2
        bool Raycast(UnityEngine.Ray ray, float maxDistance, out int x, out int y, out UnityEngine.Vector3 hit);  // Picking ohne Collider

        // Navigation (Tabelle §4.2)
        bool IsWalkable(int x, int y, MovementClass mc);
        float MoveCost(int x, int y, MovementClass mc);                    // >= 0.5; float.PositiveInfinity = gesperrt

        // Platzierung (§4.3)
        bool IsBuildable(int x, int y);
        bool CanPlaceFootprint(int x, int y, int sizeX, int sizeY, out PlacementFailure reason);

        // Bereiche, ohne Allokation pro Aufruf
        void ForEachInRect(int minX, int minY, int maxX, int maxY, TileVisitor visitor);   // an Grenzen geklippt
        int FindBestTiles(int cx, int cy, int radius, TileScorer scorer, int maxResults, System.Collections.Generic.List<TileScore> results);
        int FindStartLocations(int count, int minDistanceTiles, System.Collections.Generic.List<TileScore> results);
    }

    public enum PlacementFailure : byte { None = 0, OutOfBounds = 1, Water = 2, TooSteep = 3, Occupied = 4, Blocked = 5 }

    public interface IWorldGrid : IWorldQuery
    {
        void Set(int x, int y, in TileData tile);                          // God-Tools; TileChangedEvent(Full)
        void SetHeight(int x, int y, float height);                        // Terrain-Tools, Krater
        void SetTerrain(int x, int y, TerrainType terrain);
        void SetFlags(int x, int y, TileFlags set, TileFlags clear);
        bool TryHarvest(int x, int y, int amount, out int taken);          // bei 0: ResourceDepletedEvent, Forest -> Grass
        void SetResource(int x, int y, ResourceType type, ushort amount);  // God-Tools
        void SetOwner(int x, int y, int civId);                            // gebündelt: ein OwnershipChangedEvent pro Tick
        bool SetBuilding(int x, int y, EntityId building);                 // false, wenn belegt oder nicht baubar
        void ClearBuilding(int x, int y);
    }

    public struct TileChangedEvent      : ISimEvent { public int X, Y; public TileChangeKind Kind; }
    public struct ResourceDepletedEvent : ISimEvent { public int X, Y; public ResourceType Resource; }
    public struct OwnershipChangedEvent : ISimEvent { public int MinX, MinY, MaxX, MaxY; }   // Rechteck aller Besitzänderungen dieses Ticks

    public sealed class WorldGenSettings
    {
        public ulong Seed;
        public int Width = 256, Height = 256;
        public float SeaLevel = 20f, MaxHeight = 120f;
        public float LandRatio = 0.55f;
        public int RiverCount = 12;
        public float ForestDensity = 1f, ResourceRichness = 1f;
    }

    public interface IWorldGenerator
    {
        // Reines C#, deterministisch: nur aus settings.Seed abgeleitetes Hash-Rauschen,
        // nicht der laufende SimRandom-Zustand (Neuaufbau beim Laden, SAVE_FORMAT_PROPOSAL §C).
        WorldGrid Generate(WorldGenSettings settings);
    }

    // public sealed class WorldGrid : IWorldGrid, ISaveable  — SaveKey "world", LoadOrder 0
}
```

### 4.1 Koordinatensystem und Weltgrenzen

- Linkshändiges Unity-System: X = Osten, Z = Norden, Y = Höhe. 1 Einheit = 1 m.
- Kachel (x, y): `x` entlang Welt-X, `y` entlang Welt-Z. Kachel (0,0) liegt mit ihrer Ecke im Weltursprung.
- `WorldConstants.TileSize = 4f`. Kachelmittelpunkt: `world = ((x + 0.5) * TileSize, height, (y + 0.5) * TileSize)`.
- Weltgrenzen: `0 ≤ worldX < Width * TileSize`, `0 ≤ worldZ < Height * TileSize`. Startgröße 256 × 256 Kacheln (1 024 m), Ziel bis 512 × 512. Außerhalb: `InBounds` = false, nicht begehbar, Kamera wird begrenzt. Die äußersten 2 Kachelreihen sind immer `DeepWater`.
- Höhe in Metern; Meeresspiegel Standard 20 m, Gipfel bis ca. 120 m.
- Linearer Kachelindex für Arrays und Spielstände: `index = y * Width + x`.
- Umrechnung über genau eine Stelle (`WorldBeyond.Simulation.World.WorldCoords`): `WorldToTile(float worldX, float worldZ) → (int x, int y)` (abgerundet), `TileToWorld(int x, int y) → Vector3` (Mittelpunkt, Höhe aus Kachel), `ClampToBounds(Vector3) → Vector3`.

### 4.2 Navigation und Hindernisse

`MovementClass` (in `WorldBeyond.Core`): Foot, Wheeled, Tracked. Kosten sind Multiplikatoren auf die Grundzeit pro Kachel; `MovementSystem` fährt mit `Speed / MoveCost`.

| Gelände / Zustand | Foot | Wheeled | Tracked |
|---|---|---|---|
| Grass, Beach | 1 | 1 | 1 |
| Desert | 1,2 | 1,3 | 1,1 |
| Forest | 1,5 | gesperrt | 2 |
| Swamp | 2 | gesperrt | 2,5 |
| Rock | 2 | 2,5 | 1,8 |
| Mountain | 3 | gesperrt | gesperrt |
| Snow | 2 | 2,5 | 1,8 |
| ShallowWater, DeepWater, River | gesperrt | gesperrt | gesperrt |
| River/Water mit `Road` (Brücke) | 1 | 1 | 1 |
| `Road` auf Land | 0,7 | 0,5 | 0,6 |
| `Crater` | +1 | +2 | +1 |
| `Wreck` | +1 | gesperrt | +2 |
| Gebäude belegt (`Building != None`) | gesperrt | gesperrt | gesperrt |
| max. Hang | 45° | 20° | 30° |

Hindernisse: Gebäude (Kachelbelegung), Wasser ohne Brücke, zu steiler Hang, Wald für Wheeled, Wracks (Flag, von Combat gesetzt). Einzelne Bäume sind keine eigenen Hindernisse; Wald gilt als Kachelzustand. Einheiten blockieren keine Kacheln (Abstand halten über `ISpatialIndex` im `MovementSystem`).

Änderungen an Begehbarkeit (God-Tools, Bauen, Krater) lösen `TileChangedEvent` aus; `IPathfinder` aktualisiert daraufhin seine Regionen (Erreichbarkeits-Komponenten) inkrementell.

### 4.3 Platzierungsprüfung

`CanPlaceFootprint(x, y, sizeX, sizeY, out reason)` prüft das Rechteck mit Ecke (x, y): alle Kacheln in Grenzen, kein Wasser, `SlopeDegrees ≤ 15°`, keine Gebäude, kein `Wreck`/`Burning`. Wald ist erlaubt (wird beim Bau gerodet, Holz geht an den Bauherrn – Aufrufer erledigt das über `TryHarvest`). Besitzrechte (fremdes Territorium) prüft der Aufrufer (Civ), nicht die Welt. `SetBuilding` prüft die einzelne Kachel erneut und liefert false bei Konflikt.

### 4.4 Kamera, Picking, Kartenübersicht (Presentation, Besitzer World)

- `StrategyCameraController`: Verschieben (WASD/Pfeile/Kantenscrollen), Zoom (Mausrad, 15–400 m Höhe, zoomabhängige Neigung), Drehen (Q/E, mittlere Maustaste), Fokus auf Kachel/Entity. Bleibt über `SampleHeight` und innerhalb `ClampToBounds`. Erzeugt von `GameBootstrap`.
- Picking: `IWorldQuery.Raycast` (kein Collider nötig); Entities über `ISpatialIndex.QueryRadius` am Trefferpunkt. `ISelectionService` (UI) nutzt beides.
- Kartenübersicht: `WorldMapTextureBuilder` liefert eine `Texture2D` (1 Pixel = 1 Kachel; Ebenen Gelände, Besitz, Ressourcen), gebündelt aktualisiert über `TileChangedEvent`/`OwnershipChangedEvent`. Die Minimap-UI baut Phase UI.

### 4.5 Speichern der Welt (nach ARCHITECTURE §11)

`WorldGrid : ISaveable` mit `SaveKey = "world"`, `LoadOrder = 0`, `SectionVersion = 1`. DTO: Seed, `WorldGenSettings`, Liste geänderter Kacheln gegenüber der frisch generierten Welt (Kachelindex `y * Width + x` + Kachel-DTO); ab > 30 % Änderungen komplettes Raster komprimiert (`fullGrid = true`). Im Kachel-DTO stehen Gelände, Biom und Ressource als String-Schlüssel über `ContentRegistry` (`ContentKind.Terrain` z. B. `"terrain.grass"`, `ContentKind.Biome` z. B. `"biome.temperate"`, `ContentKind.Resource` z. B. `"res.wood"`), `Building` als `EntityId` (Index + Generation), `OwnerCivId` als fachliche ID. Laden: aus Seed neu generieren → Deltas anwenden → Regionen, Chunks, Kartentextur neu aufbauen. Nicht gespeichert: Pfadanfragen, Regionen, Spatial Hash, Meshes. `WorldGrid` implementiert `ISaveReferenceCheck`: `Building`-IDs, die nicht mehr existieren, werden freigegeben (Warnung), `OwnerCivId` ohne existierende Zivilisation → 0 (Warnung).

## 5. Räumliche Abfragen und Pfadfindung

Besitzer: Phase 2 World. Status: **präzisiert, noch nicht implementiert**.

```csharp
namespace WorldBeyond.Core
{
    public interface ISpatialIndex
    {
        // Spatial Hash, Zellgröße 32 m (8 × 8 Kacheln).
        void Insert(EntityId id, float x, float z);
        void Move(EntityId id, float x, float z);
        void Remove(EntityId id);
        // Füllt Ergebnisliste, keine Allokation pro Aufruf.
        void QueryRadius(float x, float z, float radius, System.Collections.Generic.List<EntityId> results);
        void QueryRect(float minX, float minZ, float maxX, float maxZ, System.Collections.Generic.List<EntityId> results);
    }

    public interface IPathfinder
    {
        // Asynchron über Budget (Knoten-Expansionen pro Tick): Anfrage stellen, später Ergebnis abholen.
        int RequestPath(int fromX, int fromY, int toX, int toY, MovementClass mc);
        PathStatus TryGetPath(int requestId, System.Collections.Generic.List<(int x, int y)> path);   // geglätteter Pfad
        void Cancel(int requestId);
        // Synchron, günstig: für Standortwahl und KI-Bewertung.
        bool AreConnected(int ax, int ay, int bx, int by, MovementClass mc);       // O(1) über Regionen
        float EstimateCost(int fromX, int fromY, int toX, int toY, MovementClass mc);   // Heuristik; +∞ wenn nicht verbunden
    }

    public enum PathStatus { Pending, Found, NotFound, Invalid }
}
```

Implementierung: A* auf dem Kachelraster, 8-Nachbarschaft ohne Eckenschneiden, Kosten aus `IWorldQuery.MoveCost`, binärer Heap, wiederverwendete Arrays. Nicht verbundene Ziele → sofort `NotFound`. Pfadanfragen und Regionen werden nicht gespeichert (SAVE_FORMAT_PROPOSAL §D). Flow Fields für große Verbände folgen später hinter derselben Schnittstelle.

`MovementSystem` (Order 300) liest `MovementClass`, `Speed`, `TurnRate`, `Acceleration` aus `UnitTypeData` (Werte von Civ/Military), bewegt entlang des Pfads mit Kurvenfahrt und Beschleunigung für Fahrzeuge, Hangfaktor und Höhe aus `SampleHeight`.

## 6. Einheiten

Dateien: `Simulation/Units/UnitTypes.cs`, `UnitModel.cs`, `IUnitStore.cs`, `UnitStore.cs`.

```csharp
namespace WorldBeyond.Simulation.Units
{
    // Explizite Werte, nur anhängen.
    public enum UnitRole : byte { Civilian = 0, Worker = 1, Settler = 2, Infantry = 3, Vehicle = 4, Artillery = 5, RocketArtillery = 6, Trader = 7 }
    public enum UnitState : byte { Idle = 0, Moving = 1, Working = 2, Fighting = 3, Fleeing = 4, Dead = 5 }
    public enum UnitTaskKind : byte { None = 0, MoveTo = 1, Gather = 2, Build = 3, Attack = 4, Settle = 5, Garrison = 6, Flee = 7 }

    public readonly struct UnitTypeId { public readonly int Value; public bool IsValid { get; } }  // Laufzeit, nie speichern

    public sealed class UnitTypeData    // Civ trägt Zivil-, Military Militär-Typen ein; neue Felder nur über Integration
    {
        public string Key;              // "unit.settler", "unit.humvee_mg"
        public string DisplayName;
        public UnitRole Role;
        public byte MilitaryRole;       // Wert aus Military-Enum MilitaryRole; 0 = keine
        public MovementClass Movement;
        public float MaxHp, MaxSpeed, TurnRateDeg, Acceleration, Radius;
    }

    public sealed class UnitTypeRegistry
    {
        public UnitTypeId Register(UnitTypeData data);       // nur beim Start (contentSetup)
        public bool TryGet(UnitTypeId id, out UnitTypeData data);
        public UnitTypeData Get(UnitTypeId id);
        public bool TryGetByKey(string key, out UnitTypeId id);
        public string KeyOf(UnitTypeId id);
    }

    [System.Serializable] public struct UnitTask
    {
        public UnitTaskKind Kind;
        public EntityId TargetEntity;   // vor Nutzung Exists prüfen
        public int TargetRef;           // fachliche ID (Siedlung, Armee) oder 0
        public float TargetX, TargetZ;
    }

    public readonly struct UnitView { Id, Type, CivId, X, Z, Heading, Speed, Hp, MaxHp, State, Task, HasMoveTarget, MoveTargetX, MoveTargetZ }

    public interface IUnitQuery
    {
        int Count { get; }
        bool Exists(EntityId id);
        bool TryGet(EntityId id, out UnitView view);
        UnitView Get(EntityId id);                      // ungültig → default
        void ForEachAlive(System.Action<EntityId> visitor);
        void CopyAliveIds(System.Collections.Generic.List<EntityId> results);
    }

    // Einziger Speicher für alle Einheiten. Ungültige IDs → false, keine Exception.
    public interface IUnitStore : IUnitQuery
    {
        EntityId Spawn(UnitTypeId type, int civId, float x, float z);   // UnitSpawnedEvent; unbekannter Typ → None
        bool Despawn(EntityId id, EntityId killer = default, int killerCivId = 0);   // UnitDiedEvent
        bool SetMoveTarget(EntityId id, float x, float z);
        bool ClearMoveTarget(EntityId id);
        bool SetTask(EntityId id, UnitTask task);
        bool SetState(EntityId id, UnitState state);     // Dead nur über Despawn
        bool SetPosition(EntityId id, float x, float z, float heading, float speed);   // nur MovementSystem (World)
        bool SetHp(EntityId id, float hp);               // nur ICombatService (Military); auf 0..MaxHp begrenzt
    }

    public struct UnitSpawnedEvent : ISimEvent { public EntityId Unit; public UnitTypeId UnitType; public int CivId; public float X, Z; }
    public struct UnitDiedEvent    : ISimEvent { public EntityId Unit; public EntityId Killer; public int CivId; public int KillerCivId; public UnitTypeId UnitType; public float X, Z; }

    // UnitStore : IUnitStore, ISaveable — SaveKey "units", LoadOrder 10, SectionVersion 1, StateType UnitStoreState
    // (EntityStoreState + UnitRecord[] mit TypeKey als String).
}
```

## 7. Zivilisationen, Siedlungen, Territorium, Wirtschaft, Technik

```csharp
namespace WorldBeyond.Simulation.Civilizations
{
    public interface ICivilizationRegistry
    {
        int Create(string name, UnityEngine.Color32 color, ulong cultureSeed, int startX, int startY);
        bool Exists(int civId);
        CivilizationView Get(int civId);
        System.Collections.Generic.IReadOnlyList<int> AllCivIds { get; }
        void Destroy(int civId);                    // CivilizationCollapsedEvent
    }

    public interface ISettlementRegistry
    {
        // Siedlungs-IDs sind int aus fortlaufendem Zähler, nie wiederverwendet (ARCHITECTURE §11).
        int Found(int civId, int x, int y);              // SettlementFoundedEvent
        bool Exists(int settlementId);
        SettlementView Get(int settlementId);
        System.Collections.Generic.IReadOnlyList<int> GetByCiv(int civId);
        void TransferSettlement(int settlementId, int newCivId); // SettlementCapturedEvent (Eroberung, Friedensvertrag)
    }

    public interface ITerritoryService
    {
        // Einzige Wahrheit: TileData.OwnerCivId. Geschrieben nur hier, über IWorldGrid.Set.
        int OwnerAt(int x, int y);
        int SharedBorderLength(int civA, int civB);  // für Diplomatie (Grenzkontakt)
        void MarkDirty(int settlementId);            // gebündelte Neuberechnung
    }

    public interface IEconomy
    {
        int GetStock(int civId, ResourceType r);
        bool TrySpend(int civId, in ResourceCost cost);   // = TryConsume aus Phase-4-Entwurf
        void Add(int civId, ResourceType r, int amount);
    }

    public interface ITechTree
    {
        bool IsUnlocked(int civId, TechId tech);
        Epoch CurrentEpoch(int civId);
        void SetResearch(int civId, TechId tech);
        void ForceEpoch(int civId, Epoch epoch);    // God-Modus
    }

    // Lesesichten für KI/UI (CivilizationView enthält u. a. Bevölkerung, Militärstärke, Hauptstadt-ID, Ressourcen).
    public interface ICivilizationQuery { bool Exists(int civId); CivilizationView Get(int civId); System.Collections.Generic.IReadOnlyList<int> AllCivIds { get; } }
    public interface ISettlementQuery   { bool Exists(int settlementId); SettlementView Get(int settlementId); System.Collections.Generic.IReadOnlyList<int> GetByCiv(int civId); }

    // Rekrutierung: von Civ bereitgestellt (Kosten/Bevölkerung), Military liefert Militär-Typen und Produktionsgebäude.
    public enum RecruitResult { Queued, NotEnoughResources, NotEnoughPopulation, TechMissing, NoProductionBuilding }
    public interface IRecruitment
    {
        RecruitResult Request(int civId, string unitTypeKey, int count, int settlementId);
    }
}
```

## 8. Diplomatie

```csharp
namespace WorldBeyond.Simulation.Diplomacy
{
    // Explizite Werte, nur anhängen (ARCHITECTURE §11).
    public enum DiplomaticStatus : byte { Neutral = 0, Tension = 1, Trade = 2, Alliance = 3, War = 4, Truce = 5 }

    public interface IDiplomacyService
    {
        int GetRelation(int civA, int civB);        // −100 … +100
        DiplomaticStatus GetStatus(int civA, int civB);
        void ModifyRelation(int civA, int civB, int delta, RelationReason reason);
        bool DeclareWar(int attacker, int defender);    // WarDeclaredEvent
        bool MakePeace(int civA, int civB);             // PeaceSignedEvent
        bool FormAlliance(int civA, int civB);
        bool SignTruce(int civA, int civB, int durationTicks); // TruceSignedEvent
    }

    // Lesesichten, Details siehe Docs/PHASE4_DIPLOMACY_WAR_DESIGN.md §8 (Phase 4 präzisiert).
    public interface IDiplomacyQuery
    {
        float GetOpinion(int civA, int civB);
        DiplomaticStatus GetStatus(int civA, int civB);
        System.Collections.Generic.IReadOnlyList<OpinionModifier> GetModifiers(int civA, int civB);
        System.Collections.Generic.IReadOnlyList<Treaty> GetTreaties(int civId);   // Treaty.Id: int, nie wiederverwendet
    }

    public interface IWarQuery
    {
        System.Collections.Generic.IReadOnlyList<War> ActiveWars { get; }          // War.Id: int, nie wiederverwendet
        War GetWar(int warId);
        bool AreAtWar(int civA, int civB);
    }

    // Events: WarDeclaredEvent, PeaceSignedEvent, TruceSignedEvent, AllianceFormedEvent, TreatyBrokenEvent,
    //         StrategicPointCapturedEvent. SettlementCapturedEvent kommt aus Civ, UnitDiedEvent aus dem UnitStore.
}
```

## 9. Militär, Kampf, Geschosse

```csharp
namespace WorldBeyond.Simulation.Combat
{
    public enum DamageType : byte { SmallArms, HeavyMG, Explosive, ArmorPiercing, Fire }
    public enum ProjectileKind : byte { Hitscan, Ballistic, Rocket, ArtilleryShell }

    public interface ICombatService
    {
        // Schaden immer über diese Funktion, damit Panzerung/Events konsistent sind.
        void ApplyDamage(EntityId target, float amount, DamageType type, EntityId source);
        void ApplyAreaDamage(float x, float z, float radius, float amount, DamageType type, EntityId source);
    }

    public interface IProjectileSystem
    {
        EntityId Fire(in ProjectileSpawn spawn);    // ProjectileFiredEvent → VFX/Audio
        int ActiveCount { get; }
    }

    public struct ExplosionEvent : WorldBeyond.Core.ISimEvent { public float X, Z, Radius; public DamageType Type; }
}

namespace WorldBeyond.Simulation.Military
{
    public interface IArmyRegistry
    {
        // Armee-IDs: int, nie wiederverwendet. Aufträge sind Daten (ArmyOrder: Typ + Ziel-ID/Position).
        int CreateArmy(int civId, System.Collections.Generic.IReadOnlyList<EntityId> units);
        void OrderMove(int armyId, float x, float z);
        void OrderAttack(int armyId, EntityId targetUnit);
        void OrderAttackSettlement(int armyId, int settlementId);
        void OrderDefend(int armyId, int settlementId);
        void OrderRetreat(int armyId);
        void Disband(int armyId);
    }

    public interface IArmyQuery
    {
        bool Exists(int armyId);
        System.Collections.Generic.IReadOnlyList<ArmyView> GetArmies(int civId);
    }
}
```

## 10. KI

```csharp
namespace WorldBeyond.Simulation.AI
{
    public interface ICivAI : ISimulationSystem { }      // TickInterval ≈ 50
    public interface IArmyAI : ISimulationSystem { }     // TickInterval ≈ 10
    // Taktisches Einheitenverhalten liegt im CombatSystem (jeden Tick, billig).
}
```

## 11. Speichern/Laden

Verbindliche Regeln und Ablauf: ARCHITECTURE §11. Status: geplant, nichts implementiert.

```csharp
namespace WorldBeyond.Simulation
{
    public interface ISaveable
    {
        string SaveKey { get; }           // eindeutig, z. B. "world", "units", "civs"
        int LoadOrder { get; }            // world vor units vor civs …
        int SectionVersion { get; }       // Version dieser Sektion; SaveLoadService migriert alte DTOs vor RestoreState (INTEGRATION.md §5)
        System.Type StateType { get; }    // DTO-Typ; SaveLoadService (Application) wandelt JToken → StateType. Simulation referenziert kein Newtonsoft (INTEGRATION.md §5)
        object CaptureState();            // reines DTO vom Typ StateType, serialisierbar mit Newtonsoft
        bool TryRestoreState(object dto, out string error);  // dto ist bereits migriert und vom Typ StateType; false = harte Inkonsistenz → LoadResult Corrupted
    }

    // Optional, für Systeme mit Querverweisen. Wird nach dem Einspielen ALLER Sektionen aufgerufen.
    // Weiche Fehler: Rückfall anwenden (ARCHITECTURE §11.1 Regel 6) und als Warnung melden.
    // Harte Fehler (Datenmodell widersprüchlich): als Fehler melden → Laden schlägt fehl.
    public interface ISaveReferenceCheck
    {
        void CheckReferences(SimContext ctx, LoadReport report);
    }

    // Liegt in Simulation, weil ISaveReferenceCheck es braucht und Simulation nicht auf Application zugreifen darf.
    public sealed class LoadReport
    {
        public System.Collections.Generic.List<string> Warnings;     // weiche Inkonsistenzen mit angewendetem Rückfall
        public System.Collections.Generic.List<string> Errors;       // harte Inkonsistenzen → Laden schlägt fehl
        public void Warn(string message);
        public void Fail(string message);
    }
}

namespace WorldBeyond.Application.Save
{
    public enum SaveLoadError { None = 0, NotFound = 1, IoError = 2, Corrupted = 3, TooNew = 4, Unsupported = 5, MissingContent = 6, InvalidReferences = 7 }

    public sealed class SaveGameData                  // Wurzel-Datenmodell
    {
        public const string FormatName = "WorldBeyondSave";
        public const int CurrentFormatVersion = 1;
        public string Format;
        public int FormatVersion;
        public string GameVersion;                    // nur Info
        public string CreatedUtc;                     // ISO 8601
        public SaveHeader Header;                     // Name, WorldSeed, Breite, Höhe, SimTick, CivCount, UnitCount
        public SimSettingsDto Settings;               // Geschwindigkeit, Pause, relevante Simulationseinstellungen
        public System.Collections.Generic.Dictionary<string, SaveSection> Sections;
        public string Checksum;                       // SHA-256 über kanonisch serialisierte Sections
    }

    public sealed class SaveSection
    {
        public int Version;                           // = ISaveable.SectionVersion beim Speichern
        public Newtonsoft.Json.Linq.JToken Data;      // roh, damit Migrationen ohne DTO-Typ arbeiten können
    }

    public sealed class SaveGameSerializer
    {
        public string Serialize(SaveGameData data);                    // setzt Checksum
        public bool TryDeserialize(string json, out SaveGameData data, out SaveLoadError error, out string detail);
                                                                       // prüft JSON, Format, Pflichtfelder, Checksum; keine Exception nach außen
        public static string ComputeChecksum(SaveGameData data);
    }

    // Ein Schritt v → v+1. SectionKey "$frame" = Rahmen (formatVersion).
    public interface ISaveMigration
    {
        string SectionKey { get; }
        int FromVersion { get; }                      // ToVersion = FromVersion + 1
        Newtonsoft.Json.Linq.JToken Migrate(Newtonsoft.Json.Linq.JToken data);
    }

    public sealed class SaveGameMigrator
    {
        public SaveGameMigrator(System.Collections.Generic.IEnumerable<ISaveMigration> migrations);
        // Arbeitet auf einer tiefen Kopie. currentVersions = SectionVersion je SaveKey der laufenden Version.
        // Ergebnis: TooNew bei höherer Version, Unsupported bei Lücke in der Kette, sonst migrierte Kopie.
        public bool TryMigrate(SaveGameData source, System.Collections.Generic.IReadOnlyDictionary<string, int> currentVersions,
                               out SaveGameData migrated, out SaveLoadError error, out string detail);
    }

    public readonly struct SaveResult { public readonly bool Success; public readonly SaveLoadError Error; public readonly string UserMessage; public readonly string Detail; }
    public readonly struct LoadResult { public readonly bool Success; public readonly SaveLoadError Error; public readonly string UserMessage; public readonly string Detail; public readonly LoadReport Report; }
}

namespace WorldBeyond.Application
{
    public interface ISaveLoadService
    {
        Save.SaveResult Save(string slotName);        // nur an der Tick-Grenze, atomar (.tmp → .bak → final)
        Save.LoadResult Load(string slotName);        // Ablauf ARCHITECTURE §11.5; bei Fehler bleibt der bisherige Zustand
        System.Collections.Generic.IReadOnlyList<SaveSlotInfo> ListSlots();   // liest nur den Header
    }
}
```

## 12. Application / Presentation

```csharp
namespace WorldBeyond.Application
{
    public interface ISelectionService
    {
        System.Collections.Generic.IReadOnlyList<EntityId> Selected { get; }
        event System.Action SelectionChanged;
        void Select(EntityId id, bool additive);
        void SelectInRect(UnityEngine.Rect screenRect, bool additive);
        void Clear();
    }

    public interface IGodTool
    {
        string Id { get; }
        string DisplayName { get; }
        void OnPointerDown(UnityEngine.Vector3 worldPos);
        void OnPointerDrag(UnityEngine.Vector3 worldPos);
        void OnPointerUp(UnityEngine.Vector3 worldPos);
        // Erzeugt ausschließlich IWorldCommands, ändert nie direkt den Zustand.
    }

    public interface IGameEventFeed
    {
        event System.Action<GameMessage> MessagePosted;   // "Krieg erklärt", "Stadt gegründet" …
        System.Collections.Generic.IReadOnlyList<GameMessage> Recent { get; }
    }
}

namespace WorldBeyond.Presentation
{
    public interface IAudioService { void PlayAt(AudioCue cue, UnityEngine.Vector3 pos); void PlayUi(AudioCue cue); }
    public interface IVfxService   { void Spawn(VfxCue cue, UnityEngine.Vector3 pos, UnityEngine.Quaternion rot); }
}
```

## 13. Regeln

- Presentation hört auf Events (`UnitDiedEvent`, `ExplosionEvent`, `TileChangedEvent` …) und liest Query-Interfaces. Sie schreibt nie direkt in Stores.
- UI und God-Tools ändern Zustand nur über `IWorldCommand`.
- Neue Events: hier eintragen (Name, Felder, Sender, Empfänger).
- Signaturen dürfen in der implementierenden Phase präzisiert werden (z. B. `View`-Structs), müssen dann aber hier nachgetragen werden.
