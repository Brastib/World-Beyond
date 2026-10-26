namespace WorldBeyond.Simulation.World.Generation
{
    // Temperatur und Feuchte → Biom → Geländetyp. Wälder entstehen hier als Geländetyp Forest; Holz setzt ResourcePlacer.
    internal sealed class Climate
    {
        readonly int _w, _h, _n;

        public readonly TerrainType[] Terrain;
        public readonly BiomeType[] Biome;
        public readonly byte[] Moisture;
        // 0 (kalt) .. 1 (heiß)
        public readonly float[] Temperature;
        // 0 (Meeresspiegel) .. 1 (MaxHeight)
        public readonly float[] Altitude;
        public readonly float[] SlopeTan;

        public Climate(int width, int height)
        {
            _w = width;
            _h = height;
            _n = width * height;
            Terrain = new TerrainType[_n];
            Biome = new BiomeType[_n];
            Moisture = new byte[_n];
            Temperature = new float[_n];
            Altitude = new float[_n];
            SlopeTan = new float[_n];
        }

        public void Run(WorldGenSettings s, float[] height, bool[] ocean, Hydrology hydro)
        {
            ulong seedT = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepTemperature);
            ulong seedM = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepMoisture);
            ulong seedF = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepForest);
            ulong seedJ = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepForestJitter);
            float landRange = s.MaxHeight - s.SeaLevel;

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    float alt = HashNoise.Clamp01((height[i] - s.SeaLevel) / landRange);
                    Altitude[i] = alt;
                    SlopeTan[i] = Slope(height, hydro, x, y);

                    // Süden warm, Norden kalt, Höhe kühlt ab.
                    float lat = _h > 1 ? y / (float)(_h - 1) : 0.5f;
                    float t = 0.8f - 0.55f * lat - alt * 0.7f + HashNoise.Fbm(seedT, x / 90f, y / 90f, 3) * 0.12f;
                    Temperature[i] = HashNoise.Clamp01(t);

                    float m = HashNoise.Fbm(seedM, x / 70f, y / 70f, 4) * 0.5f + 0.5f;
                    float nearWater = 1f - hydro.WaterDistance[i] / 12f;
                    m = HashNoise.Clamp01(m * 0.75f + nearWater * 0.35f - 0.05f);
                    Moisture[i] = (byte)(m * 255f + 0.5f);
                }
            }

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x;
                    Classify(s, i, x, y, height, ocean, hydro, seedF, seedJ);
                }
            }
        }

        // Gradient der Oberfläche (Wasser zählt mit Wasserspiegel), Höhe pro Meter.
        float Slope(float[] height, Hydrology hydro, int x, int y)
        {
            int xl = x > 0 ? x - 1 : x, xr = x < _w - 1 ? x + 1 : x;
            int yd = y > 0 ? y - 1 : y, yu = y < _h - 1 ? y + 1 : y;
            float dx = (Top(height, hydro, xr, y) - Top(height, hydro, xl, y)) / ((xr - xl) * WorldConstants.TileSize);
            float dz = (Top(height, hydro, x, yu) - Top(height, hydro, x, yd)) / ((yu - yd) * WorldConstants.TileSize);
            return System.MathF.Sqrt(dx * dx + dz * dz);
        }

        float Top(float[] height, Hydrology hydro, int x, int y)
        {
            int i = y * _w + x;
            float h = height[i];
            float wl = hydro.WaterLevel[i];
            return wl > h ? wl : h;
        }

        void Classify(WorldGenSettings s, int i, int x, int y, float[] height, bool[] ocean, Hydrology hydro, ulong seedF, ulong seedJ)
        {
            if (ocean[i])
            {
                bool deep = s.SeaLevel - height[i] > 4f;
                Terrain[i] = deep ? TerrainType.DeepWater : TerrainType.ShallowWater;
                Biome[i] = deep ? BiomeType.Ocean : BiomeType.Coast;
                return;
            }
            if (hydro.Lake[i])
            {
                Terrain[i] = TerrainType.ShallowWater;
                Biome[i] = BiomeType.Wetland;
                return;
            }

            float alt = Altitude[i];
            float temp = Temperature[i];
            float moist = Moisture[i] / 255f;
            float slope = SlopeTan[i];

            BiomeType biome;
            if (alt > 0.62f)
                biome = BiomeType.Alpine;
            else if (temp < 0.18f)
                biome = BiomeType.Tundra;
            else if (temp < 0.36f)
                biome = BiomeType.Boreal;
            else if (moist > 0.78f && alt < 0.12f && slope < 0.1f)
                biome = BiomeType.Wetland;
            else if (temp > 0.62f && moist < 0.38f)
                biome = BiomeType.Desert;
            else if (temp > 0.58f && moist < 0.55f)
                biome = BiomeType.Savanna;
            else
                biome = BiomeType.Temperate;

            if (hydro.River[i])
            {
                Terrain[i] = TerrainType.River;
                Biome[i] = biome;
                return;
            }

            bool coastal = alt < 0.03f && IsNextToOcean(ocean, x, y);
            if (coastal && biome != BiomeType.Wetland)
            {
                Terrain[i] = TerrainType.Beach;
                Biome[i] = BiomeType.Coast;
                return;
            }

            TerrainType terrain;
            if (alt > 0.8f || (biome == BiomeType.Alpine && temp < 0.08f))
                terrain = TerrainType.Snow;
            else if (alt > 0.55f && slope > 0.55f)
                terrain = TerrainType.Mountain;
            else if (slope > 0.7f || biome == BiomeType.Alpine)
                terrain = TerrainType.Rock;
            else if (biome == BiomeType.Desert)
                terrain = TerrainType.Desert;
            else if (biome == BiomeType.Wetland)
                terrain = TerrainType.Swamp;
            else
                terrain = TerrainType.Grass;

            if (terrain == TerrainType.Grass || terrain == TerrainType.Swamp)
            {
                float density = biome switch
                {
                    BiomeType.Temperate => 0.5f,
                    BiomeType.Boreal => 0.66f,
                    BiomeType.Savanna => 0.14f,
                    BiomeType.Wetland => 0.28f,
                    BiomeType.Tundra => 0.06f,
                    _ => 0f,
                };
                density *= s.ForestDensity;
                // Waldflächen statt Einzelbäume: großräumiges Rauschen, Feuchte begünstigt, leichte Körnung an den Rändern.
                float f = HashNoise.Fbm(seedF, x / 26f, y / 26f, 4) * 0.5f + 0.5f;
                f = f * 0.8f + moist * 0.25f + (HashNoise.Value01(seedJ, x, y) - 0.5f) * 0.12f;
                if (density > 0f && f > 1f - density * 0.75f && slope < 0.6f)
                    terrain = TerrainType.Forest;
            }

            Terrain[i] = terrain;
            Biome[i] = biome;
        }

        bool IsNextToOcean(bool[] ocean, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx < (uint)_w && (uint)ny < (uint)_h && ocean[ny * _w + nx])
                        return true;
                }
            }
            return false;
        }
    }
}
