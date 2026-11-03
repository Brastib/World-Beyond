using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Unity-frei: welche Bodentextur (Art/Terrain/Resources/WorldBeyondTerrain, README) auf einer Kachel liegt.
    // Nutzung (Weg, Gebäude, Krater) überlagert die Grundschicht aus Gelände und Biom. Reihenfolge = Index im Texturfeld.
    public enum GroundLayer : byte { Grass = 0, Earth = 1, Path = 2, Field = 3, Forest = 4, Sand = 5, Rock = 6, Dry = 7, Wet = 8, Snow = 9 }

    public static class TerrainLayers
    {
        public const int Count = 10;
        public static readonly string[] Names = { "Grass", "Earth", "Path", "Field", "Forest", "Sand", "Rock", "Dry", "Wet", "Snow" };
        // Empfohlene Wiederholung in Metern (README der Bodentexturen).
        public static readonly float[] RepeatMeters = { 5f, 4f, 4.5f, 6f, 4.6f, 4f, 6f, 4f, 4.7f, 4f };

        public static GroundLayer LayerFor(in TileData t)
        {
            if ((t.Flags & TileFlags.Road) != 0) return GroundLayer.Path;
            if ((t.Flags & (TileFlags.Crater | TileFlags.Scorched)) != 0) return GroundLayer.Earth;
            if (t.Building.IsValid) return GroundLayer.Earth;
            switch (t.Terrain)
            {
                case TerrainType.DeepWater:
                case TerrainType.ShallowWater:
                case TerrainType.Beach: return GroundLayer.Sand;
                case TerrainType.River:
                case TerrainType.Swamp: return GroundLayer.Wet;
                case TerrainType.Forest: return GroundLayer.Forest;
                case TerrainType.Rock:
                case TerrainType.Mountain: return GroundLayer.Rock;
                case TerrainType.Snow: return GroundLayer.Snow;
                case TerrainType.Desert: return t.Biome == BiomeType.Savanna || t.Biome == BiomeType.Tundra ? GroundLayer.Dry : GroundLayer.Sand;
            }
            // Grass und Unbekanntes: nach Biom
            if ((t.Flags & TileFlags.RiverBank) != 0) return GroundLayer.Wet;
            switch (t.Biome)
            {
                case BiomeType.Ocean:
                case BiomeType.Coast: return GroundLayer.Sand;
                // Savanne/Tundra mit Grasbewuchs bleiben Gras (Lauf 8: rissiger Trockenboden wirkte auf bewachsenem
                // Land falsch); „Dry“ nur noch für Wüstenkacheln in diesen Biomen.
                case BiomeType.Desert: return GroundLayer.Sand;
                case BiomeType.Wetland: return GroundLayer.Wet;
                case BiomeType.Alpine: return GroundLayer.Rock;
                default: return GroundLayer.Grass;   // Temperate, Boreal, TropicalForest, Mediterranean
            }
        }

        /// <summary>Schreibt die Kachel als Ein-Hot-Gewicht in drei RGBA-Steuerkarten (12 Kanäle, 10 genutzt).
        /// rgba: 3 Felder mit je Breite × Höhe × 4 Bytes. Bilineare Filterung im Shader ergibt die Überblendung.</summary>
        public static void Write(byte[][] rgba, int width, int x, int y, GroundLayer layer)
        {
            int p = (y * width + x) * 4;
            for (int m = 0; m < 3; m++)
            {
                byte[] a = rgba[m];
                a[p] = a[p + 1] = a[p + 2] = a[p + 3] = 0;
            }
            int l = (int)layer;
            rgba[l / 4][p + l % 4] = 255;
        }
    }
}
