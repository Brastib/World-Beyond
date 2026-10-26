using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World.Navigation
{
    // Kostentabelle aus SYSTEM_INTERFACES §4.2. Einzige Stelle, an der Begehbarkeit festgelegt wird.
    public static class MovementRules
    {
        public const float Blocked = float.PositiveInfinity;

        // Höhendifferenz zur Nachbarkachel (m), ab der der Hang zu steil ist: tan(maxGrad) * TileSize.
        // Foot 45°, Wheeled 20°, Tracked 30°.
        static readonly float[] MaxStepHeight =
        {
            1.0000f * WorldConstants.TileSize,
            0.3640f * WorldConstants.TileSize,
            0.5774f * WorldConstants.TileSize,
        };

        static readonly float[] MaxSlopeDeg = { 45f, 20f, 30f };

        // Index = (int)TerrainType, Spalten Foot, Wheeled, Tracked.
        static readonly float[,] TerrainCost =
        {
            /* DeepWater    */ { Blocked, Blocked, Blocked },
            /* ShallowWater */ { Blocked, Blocked, Blocked },
            /* Beach        */ { 1f, 1f, 1f },
            /* Grass        */ { 1f, 1f, 1f },
            /* Forest       */ { 1.5f, Blocked, 2f },
            /* Rock         */ { 2f, 2.5f, 1.8f },
            /* Mountain     */ { 3f, Blocked, Blocked },
            /* Snow         */ { 2f, 2.5f, 1.8f },
            /* Desert       */ { 1.2f, 1.3f, 1.1f },
            /* Swamp        */ { 2f, Blocked, 2.5f },
            /* River (Furt) */ { 3f, Blocked, 3f },
        };

        static readonly float[] RoadCost = { 0.7f, 0.5f, 0.6f };
        static readonly float[] BridgeCost = { 1f, 1f, 1f };
        static readonly float[] CraterExtra = { 1f, 2f, 1f };
        static readonly float[] WreckExtra = { 1f, Blocked, 2f };

        // Kleinster möglicher Kostenwert je Klasse (für eine zulässige A*-Heuristik).
        public static float MinCost(MovementClass mc) => RoadCost[(int)mc];

        public static float MaxSlopeDegrees(MovementClass mc) => MaxSlopeDeg[(int)mc];

        public static float MaxStep(MovementClass mc) => MaxStepHeight[(int)mc];

        // Kosten nur aus den Kacheldaten, ohne Hangprüfung (die braucht Nachbarn, siehe WorldGrid.MoveCost).
        public static float TileCost(in TileData tile, MovementClass mc)
        {
            int m = (int)mc;
            if (tile.Building.IsValid)
                return Blocked;

            bool road = (tile.Flags & TileFlags.Road) != 0;
            float cost;
            if (IsWaterTerrain(tile.Terrain))
            {
                if (road)
                    cost = BridgeCost[m];
                else
                    cost = TerrainCost[(int)tile.Terrain, m];
            }
            else
            {
                cost = road ? RoadCost[m] : TerrainCost[(int)tile.Terrain, m];
            }

            if (float.IsInfinity(cost))
                return Blocked;
            if ((tile.Flags & TileFlags.Crater) != 0)
                cost += CraterExtra[m];
            if ((tile.Flags & TileFlags.Wreck) != 0)
                cost += WreckExtra[m];
            return cost;
        }

        public static bool IsWaterTerrain(TerrainType t) =>
            t == TerrainType.DeepWater || t == TerrainType.ShallowWater || t == TerrainType.River;
    }
}
