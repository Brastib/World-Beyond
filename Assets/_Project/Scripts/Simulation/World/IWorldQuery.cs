using System.Collections.Generic;
using System.Numerics;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World
{
    // Lesender Zugriff auf die Welt. Vertrag: SYSTEM_INTERFACES §4.
    public interface IWorldQuery
    {
        int Width { get; }
        int Height { get; }
        ulong Seed { get; }
        float SeaLevel { get; }

        // Steigt bei jeder Änderung einer Kachel; für Caches (Darstellung, Kartentextur).
        int Version { get; }

        // Steigt nur bei Änderungen, die Begehbarkeit oder Kosten beeinflussen können (Höhe, Gelände, Flags, Gebäude);
        // nicht bei Ressourcenabbau oder Besitzwechsel. Für Caches der Pfadfindung.
        int NavigationVersion { get; }

        bool InBounds(int x, int y);
        ref readonly TileData Get(int x, int y);

        // Gelände
        float SampleHeight(float worldX, float worldZ);
        Vector3 SampleNormal(float worldX, float worldZ);
        float SlopeDegrees(int x, int y);
        bool IsWater(int x, int y);
        bool HasLineOfSight(Vector3 from, Vector3 to);
        bool Raycast(WorldRay ray, float maxDistance, out int x, out int y, out Vector3 hit);

        // Navigation (SYSTEM_INTERFACES §4.2)
        bool IsWalkable(int x, int y, MovementClass mc);
        float MoveCost(int x, int y, MovementClass mc);

        // Platzierung (SYSTEM_INTERFACES §4.3)
        bool IsBuildable(int x, int y);
        bool CanPlaceFootprint(int x, int y, int sizeX, int sizeY, out PlacementFailure reason);

        // Bereiche, ohne Allokation pro Aufruf
        void ForEachInRect(int minX, int minY, int maxX, int maxY, TileVisitor visitor);
        int FindBestTiles(int cx, int cy, int radius, TileScorer scorer, int maxResults, List<TileScore> results);
        // Startplätze für Zivilisationen; bevorzugt alle auf einem zu Fuß zusammenhängenden Landstück (Sandbox: Civs müssen sich begegnen können).
        int FindStartLocations(int count, int minDistanceTiles, List<TileScore> results);
    }

    public interface IWorldGrid : IWorldQuery
    {
        void Set(int x, int y, in TileData tile);
        void SetHeight(int x, int y, float height);
        void SetTerrain(int x, int y, TerrainType terrain);
        void SetFlags(int x, int y, TileFlags set, TileFlags clear);
        bool TryHarvest(int x, int y, int amount, out int taken);
        void SetResource(int x, int y, ResourceType type, ushort amount);
        void SetOwner(int x, int y, int civId);
        bool SetBuilding(int x, int y, EntityId building);
        void ClearBuilding(int x, int y);
    }

    public sealed class WorldGenSettings
    {
        public ulong Seed;
        public int Width = 256;
        public int Height = 256;
        public float SeaLevel = 20f;
        public float MaxHeight = 120f;
        public float LandRatio = 0.55f;
        public int RiverCount = 12;
        public float ForestDensity = 1f;
        public float ResourceRichness = 1f;

        public WorldGenSettings Clone() => (WorldGenSettings)MemberwiseClone();

        public bool Validate(out string error)
        {
            if (Width < WorldConstants.MinWorldSize || Height < WorldConstants.MinWorldSize ||
                Width > WorldConstants.MaxWorldSize || Height > WorldConstants.MaxWorldSize)
            {
                error = $"Weltgröße {Width}×{Height} liegt außerhalb von {WorldConstants.MinWorldSize}..{WorldConstants.MaxWorldSize}.";
                return false;
            }
            if (!(MaxHeight > SeaLevel) || SeaLevel <= 0f)
            {
                error = "MaxHeight muss größer als SeaLevel sein, SeaLevel größer als 0.";
                return false;
            }
            if (LandRatio < 0.1f || LandRatio > 0.9f)
            {
                error = "LandRatio muss zwischen 0,1 und 0,9 liegen.";
                return false;
            }
            if (RiverCount < 0 || ForestDensity < 0f || ResourceRichness < 0f)
            {
                error = "RiverCount, ForestDensity und ResourceRichness dürfen nicht negativ sein.";
                return false;
            }
            error = null;
            return true;
        }
    }

    public interface IWorldGenerator
    {
        // Reines C#, deterministisch: nur aus settings.Seed abgeleitetes Hash-Rauschen,
        // nicht der laufende SimRandom-Zustand (Neuaufbau beim Laden, ARCHITECTURE §11).
        WorldGrid Generate(WorldGenSettings settings);
    }
}
