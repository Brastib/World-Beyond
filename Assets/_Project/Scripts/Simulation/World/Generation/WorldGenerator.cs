using System;

namespace WorldBeyond.Simulation.World.Generation
{
    // Erzeugt die Welt aus WorldGenSettings. Deterministisch: gleiche Einstellungen → identische Kacheldaten.
    // Pipeline (Docs/drafts/WORLD_ENGINE_DESIGN_DRAFT.md §5): Kontinente → Gebirge/Hügel → Meeresspiegel →
    // Senken/Seen → Flüsse → Klima/Biome → Geländetyp → Wälder → Ressourcen → Mindestgarantien.
    public sealed class WorldGenerator : IWorldGenerator
    {
        // Seeds der einzelnen Schritte (DeriveSeed). Nicht umnummerieren, sonst ändern sich alle Welten.
        internal const uint StepContinent = 1, StepMountainMask = 2, StepRidges = 3, StepHills = 4, StepDetail = 5,
            StepTemperature = 6, StepMoisture = 7, StepForest = 8, StepForestJitter = 9, StepResources = 10,
            StepOre = 11, StepOil = 12, StepGold = 13, StepWildFood = 14, StepAmounts = 15;

        public WorldGrid Generate(WorldGenSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.Validate(out string error))
                throw new ArgumentException(error, nameof(settings));

            var grid = new WorldGrid(settings);
            var s = grid.GenSettings;
            int w = s.Width, h = s.Height, n = w * h;

            float[] elevation = new float[n];
            GenerateElevation(s, elevation);

            float[] height = new float[n];
            bool[] ocean = new bool[n];
            ApplySeaLevel(s, elevation, height);

            var hydro = new Hydrology(w, h);
            hydro.Run(s, height, ocean);

            var climate = new Climate(w, h);
            climate.Run(s, height, ocean, hydro);

            // Kacheln schreiben (ohne Events, ohne Änderungsmarkierung).
            for (int i = 0; i < n; i++)
            {
                ref TileData t = ref grid.RawRef(i);
                t.Height = height[i];
                t.WaterLevel = hydro.WaterLevel[i];
                t.Terrain = climate.Terrain[i];
                t.Biome = climate.Biome[i];
                t.Moisture = climate.Moisture[i];
                t.Flags = hydro.Flags[i];
            }

            ResourcePlacer.Run(grid, climate);
            grid.ResetChangeTracking();
            return grid;
        }

        static void GenerateElevation(WorldGenSettings s, float[] elevation)
        {
            int w = s.Width, h = s.Height;
            ulong seedC = HashNoise.DeriveSeed(s.Seed, StepContinent);
            ulong seedM = HashNoise.DeriveSeed(s.Seed, StepMountainMask);
            ulong seedR = HashNoise.DeriveSeed(s.Seed, StepRidges);
            ulong seedH = HashNoise.DeriveSeed(s.Seed, StepHills);
            ulong seedD = HashNoise.DeriveSeed(s.Seed, StepDetail);

            // Wellenlängen in Kacheln. Feste Werte statt Kartenbruchteile: größere Karten haben mehr Landmassen statt gedehnter.
            const float continentScale = 1f / 150f;
            const float mountainMaskScale = 1f / 190f;
            const float ridgeScale = 1f / 64f;
            const float hillScale = 1f / 22f;
            const float detailScale = 1f / 7f;

            float edgeBand = Math.Min(w, h) * 0.16f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float c = HashNoise.Fbm(seedC, x * continentScale, y * continentScale, 4) * 0.5f + 0.5f;

                    // Zum Kartenrand hin absinken, damit Küsten statt abgeschnittener Kontinente entstehen.
                    float edge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y)) / edgeBand;
                    float falloff = HashNoise.SmoothStep(0f, 1f, edge);
                    float e = c * falloff - (1f - falloff) * 0.35f;

                    // Gebirgsketten nur in Teilregionen und nur auf Land.
                    float mask = HashNoise.SmoothStep(0.05f, 0.45f, HashNoise.Fbm(seedM, x * mountainMaskScale, y * mountainMaskScale, 3));
                    float landness = HashNoise.SmoothStep(0.42f, 0.6f, e);
                    float ridge = HashNoise.Ridged(seedR, x * ridgeScale, y * ridgeScale, 4);
                    e += ridge * ridge * mask * landness * 0.55f;

                    e += HashNoise.Fbm(seedH, x * hillScale, y * hillScale, 3) * 0.06f * (0.3f + landness);
                    e += HashNoise.Gradient(seedD, x * detailScale, y * detailScale) * 0.012f;
                    elevation[y * w + x] = e;
                }
            }
        }

        // Legt den Meeresspiegel so, dass LandRatio erreicht wird, und rechnet in Meter um.
        static void ApplySeaLevel(WorldGenSettings s, float[] elevation, float[] height)
        {
            int w = s.Width, h = s.Height, n = w * h;
            int border = WorldConstants.OceanBorder;

            float[] sorted = new float[n];
            Array.Copy(elevation, sorted, n);
            Array.Sort(sorted);
            int idx = (int)((1f - s.LandRatio) * (n - 1));
            float threshold = sorted[idx];
            float minE = sorted[0];
            float maxE = sorted[n - 1];
            float below = Math.Max(threshold - minE, 1e-6f);
            float above = Math.Max(maxE - threshold, 1e-6f);

            float landRange = s.MaxHeight - s.SeaLevel;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float e = elevation[i];
                    float m;
                    if (x < border || y < border || x >= w - border || y >= h - border)
                    {
                        m = s.SeaLevel * 0.25f;
                    }
                    else if (e < threshold)
                    {
                        // Meeresboden zwischen 10 % und knapp unter dem Meeresspiegel.
                        float d = (threshold - e) / below;
                        m = s.SeaLevel - 0.5f - d * s.SeaLevel * 0.85f;
                    }
                    else
                    {
                        // Potenzkurve: weite Ebenen, steile Gipfel.
                        float a = (e - threshold) / above;
                        float curved = a * a * (0.35f + 0.65f * a);
                        m = s.SeaLevel + 0.4f + curved * (landRange - 0.4f);
                    }
                    height[i] = m;
                }
            }
        }
    }
}
