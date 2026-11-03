using System.Numerics;

namespace WorldBeyond.Simulation.World
{
    public static class WorldConstants
    {
        // D13: 1 Unity-Einheit = 1 m, 1 Kachel = 4 m.
        public const float TileSize = 4f;
        public const float InvTileSize = 1f / TileSize;

        // Äußerste Kachelreihen, die immer Tiefwasser sind.
        public const int OceanBorder = 2;

        public const int ChunkSize = 32;

        // Mindestgröße, damit Rand, Kontinente und Chunks sinnvoll sind.
        public const int MinWorldSize = 32;
        public const int MaxWorldSize = 1024;
    }

    // Einzige Stelle für Umrechnungen Welt ↔ Kachel (SYSTEM_INTERFACES §4.1).
    public static class WorldCoords
    {
        public static (int x, int y) WorldToTile(float worldX, float worldZ)
        {
            int x = (int)System.MathF.Floor(worldX * WorldConstants.InvTileSize);
            int y = (int)System.MathF.Floor(worldZ * WorldConstants.InvTileSize);
            return (x, y);
        }

        public static float TileCenterX(int x) => (x + 0.5f) * WorldConstants.TileSize;

        public static float TileCenterZ(int y) => (y + 0.5f) * WorldConstants.TileSize;

        // Kachelmittelpunkt ohne Höhe.
        public static Vector3 TileToWorld(int x, int y) => new Vector3(TileCenterX(x), 0f, TileCenterZ(y));

        // Kachelmittelpunkt mit Geländehöhe.
        public static Vector3 TileToWorld(IWorldQuery world, int x, int y)
        {
            float wx = TileCenterX(x);
            float wz = TileCenterZ(y);
            return new Vector3(wx, world.SampleHeight(wx, wz), wz);
        }

        public static int ToIndex(int x, int y, int width) => y * width + x;

        public static Vector3 ClampToBounds(Vector3 position, int width, int height)
        {
            float maxX = width * WorldConstants.TileSize - 0.001f;
            float maxZ = height * WorldConstants.TileSize - 0.001f;
            position.X = position.X < 0f ? 0f : (position.X > maxX ? maxX : position.X);
            position.Z = position.Z < 0f ? 0f : (position.Z > maxZ ? maxZ : position.Z);
            return position;
        }
    }
}
