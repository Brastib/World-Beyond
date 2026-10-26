using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Simulation.World
{
    public sealed partial class WorldGrid
    {
        // Maximale Hangneigung für Gebäude: tan(15°).
        const float BuildableSlopeTan = 0.2679f;
        // Höhe eines Waldes als Sichtblocker in m.
        const float ForestCanopyHeight = 10f;
        // Brücken liegen so weit über der Wasseroberfläche.
        const float BridgeDeckHeight = 0.6f;

        // ---- Oberfläche ----

        // Höhe, auf der eine Einheit auf dieser Kachel steht: Gelände, bei Brücken das Brückendeck.
        public float SurfaceHeightAt(int x, int y)
        {
            ref readonly TileData t = ref _tiles[y * Width + x];
            if ((t.Flags & TileFlags.Road) != 0 && t.WaterLevel > t.Height)
                return t.WaterLevel + BridgeDeckHeight;
            return t.Height;
        }

        // Höhe der Oberfläche inklusive Wasser; Grundlage für Hangberechnung, damit Küsten und Ufer nicht als Steilhang gelten.
        float TopHeightAt(int x, int y)
        {
            ref readonly TileData t = ref _tiles[y * Width + x];
            float h = SurfaceHeightAt(x, y);
            return t.WaterLevel > h ? t.WaterLevel : h;
        }

        // Dreiecksinterpolation zwischen Kachelmittelpunkten, Diagonale von (i, j) nach (i+1, j+1).
        // Der Terrain-Renderer trianguliert identisch, damit Einheiten exakt auf dem sichtbaren Boden stehen.
        public float SampleHeight(float worldX, float worldZ)
        {
            SampleCell(worldX, worldZ, out int i0, out int j0, out float fu, out float fv);
            int i1 = i0 + 1 < Width ? i0 + 1 : i0;
            int j1 = j0 + 1 < Height ? j0 + 1 : j0;
            float h00 = SurfaceHeightAt(i0, j0);
            float h10 = SurfaceHeightAt(i1, j0);
            float h01 = SurfaceHeightAt(i0, j1);
            float h11 = SurfaceHeightAt(i1, j1);
            if (fu >= fv)
                return h00 + fu * (h10 - h00) + fv * (h11 - h10);
            return h00 + fv * (h01 - h00) + fu * (h11 - h01);
        }

        public Vector3 SampleNormal(float worldX, float worldZ)
        {
            SampleCell(worldX, worldZ, out int i0, out int j0, out float fu, out float fv);
            int i1 = i0 + 1 < Width ? i0 + 1 : i0;
            int j1 = j0 + 1 < Height ? j0 + 1 : j0;
            float h00 = SurfaceHeightAt(i0, j0);
            float h10 = SurfaceHeightAt(i1, j0);
            float h01 = SurfaceHeightAt(i0, j1);
            float h11 = SurfaceHeightAt(i1, j1);
            float dhdu, dhdv;
            if (fu >= fv)
            {
                dhdu = h10 - h00;
                dhdv = h11 - h10;
            }
            else
            {
                dhdu = h11 - h01;
                dhdv = h01 - h00;
            }
            float nx = -dhdu * WorldConstants.InvTileSize;
            float nz = -dhdv * WorldConstants.InvTileSize;
            float len = MathF.Sqrt(nx * nx + 1f + nz * nz);
            return new Vector3(nx / len, 1f / len, nz / len);
        }

        void SampleCell(float worldX, float worldZ, out int i0, out int j0, out float fu, out float fv)
        {
            float u = worldX * WorldConstants.InvTileSize - 0.5f;
            float v = worldZ * WorldConstants.InvTileSize - 0.5f;
            float maxU = Width - 1;
            float maxV = Height - 1;
            u = u < 0f ? 0f : (u > maxU ? maxU : u);
            v = v < 0f ? 0f : (v > maxV ? maxV : v);
            i0 = (int)u;
            j0 = (int)v;
            if (i0 >= Width - 1) i0 = Math.Max(0, Width - 2);
            if (j0 >= Height - 1) j0 = Math.Max(0, Height - 2);
            fu = u - i0;
            fv = v - j0;
        }

        // Betrag des Geländegradienten (Höhe pro Meter) aus zentralen Differenzen der Oberfläche.
        public float SlopeTangent(int x, int y)
        {
            int xl = x > 0 ? x - 1 : x;
            int xr = x < Width - 1 ? x + 1 : x;
            int yd = y > 0 ? y - 1 : y;
            int yu = y < Height - 1 ? y + 1 : y;
            float dx = (TopHeightAt(xr, y) - TopHeightAt(xl, y)) / ((xr - xl) * WorldConstants.TileSize);
            float dz = (TopHeightAt(x, yu) - TopHeightAt(x, yd)) / ((yu - yd) * WorldConstants.TileSize);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public float SlopeDegrees(int x, int y)
        {
            if (!InBounds(x, y))
                return 0f;
            return MathF.Atan(SlopeTangent(x, y)) * (180f / MathF.PI);
        }

        public bool IsWater(int x, int y)
        {
            if (!InBounds(x, y))
                return false;
            return MovementRules.IsWaterTerrain(_tiles[y * Width + x].Terrain);
        }

        // ---- Navigation ----

        public float MoveCost(int x, int y, MovementClass mc)
        {
            if (!InBounds(x, y))
                return MovementRules.Blocked;
            float cost = MovementRules.TileCost(in _tiles[y * Width + x], mc);
            if (float.IsInfinity(cost))
                return cost;
            float tan = SlopeTangent(x, y);
            float maxTan = MovementRules.MaxStep(mc) * WorldConstants.InvTileSize;
            if (tan > maxTan)
                return MovementRules.Blocked;
            // Hang verteuert die Bewegung: bis zu +100 % an der Steigungsgrenze.
            return cost * (1f + tan / maxTan);
        }

        public bool IsWalkable(int x, int y, MovementClass mc) => !float.IsInfinity(MoveCost(x, y, mc));

        // ---- Platzierung ----

        public bool IsBuildable(int x, int y)
        {
            return CheckBuildable(x, y) == PlacementFailure.None;
        }

        PlacementFailure CheckBuildable(int x, int y)
        {
            if (!InBounds(x, y))
                return PlacementFailure.OutOfBounds;
            ref readonly TileData t = ref _tiles[y * Width + x];
            if (MovementRules.IsWaterTerrain(t.Terrain))
                return PlacementFailure.Water;
            if (t.Building.IsValid)
                return PlacementFailure.Occupied;
            if (t.Terrain == TerrainType.Mountain || (t.Flags & (TileFlags.Wreck | TileFlags.Burning)) != 0)
                return PlacementFailure.Blocked;
            if (SlopeTangent(x, y) > BuildableSlopeTan)
                return PlacementFailure.TooSteep;
            return PlacementFailure.None;
        }

        public bool CanPlaceFootprint(int x, int y, int sizeX, int sizeY, out PlacementFailure reason)
        {
            if (sizeX <= 0 || sizeY <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeX), "Grundfläche muss mindestens 1×1 Kachel groß sein.");
            for (int yy = y; yy < y + sizeY; yy++)
            {
                for (int xx = x; xx < x + sizeX; xx++)
                {
                    reason = CheckBuildable(xx, yy);
                    if (reason != PlacementFailure.None)
                        return false;
                }
            }
            reason = PlacementFailure.None;
            return true;
        }

        // ---- Sicht und Picking ----

        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            const float step = WorldConstants.TileSize * 0.5f;
            if (dist <= step)
                return true;
            var (fx, fy) = WorldCoords.WorldToTile(from.X, from.Z);
            var (tx, ty) = WorldCoords.WorldToTile(to.X, to.Z);
            int steps = (int)(dist / step);
            for (int s = 1; s < steps; s++)
            {
                float t = (s * step) / dist;
                float px = from.X + dx * t;
                float pz = from.Z + dz * t;
                float lineY = from.Y + (to.Y - from.Y) * t;
                float ground = SampleHeight(px, pz);
                var (cx, cy) = WorldCoords.WorldToTile(px, pz);
                if (InBounds(cx, cy) && !(cx == fx && cy == fy) && !(cx == tx && cy == ty) &&
                    _tiles[cy * Width + cx].Terrain == TerrainType.Forest)
                {
                    ground += ForestCanopyHeight;
                }
                if (ground > lineY)
                    return false;
            }
            return true;
        }

        // Schnittpunkt eines Strahls mit Gelände oder Wasseroberfläche (Picking ohne Collider).
        public bool Raycast(WorldRay ray, float maxDistance, out int x, out int y, out Vector3 hit)
        {
            x = y = -1;
            hit = default;
            Vector3 dir = ray.Direction;
            float len = MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            if (len < 1e-6f || maxDistance <= 0f)
                return false;
            dir = new Vector3(dir.X / len, dir.Y / len, dir.Z / len);

            // Strahl auf das Weltvolumen beschneiden, damit eine Kamera hoch über der Karte trotzdem trifft.
            float worldMaxX = Width * WorldConstants.TileSize;
            float worldMaxZ = Height * WorldConstants.TileSize;
            if (!ClipToBox(ray.Origin, dir, worldMaxX, worldMaxZ, maxDistance, out float tStart, out float tEnd))
                return false;

            const float step = WorldConstants.TileSize * 0.25f;
            float prevT = tStart;
            float startDelta = HeightAbove(ray.Origin, dir, tStart);
            if (startDelta <= 0f)
                return Finish(ray.Origin, dir, tStart, out x, out y, out hit);
            for (float t = tStart + step; t <= tEnd + step; t += step)
            {
                float tc = t > tEnd ? tEnd : t;
                float delta = HeightAbove(ray.Origin, dir, tc);
                if (delta <= 0f)
                {
                    // Feinsuche zwischen letztem Punkt über und erstem Punkt unter der Oberfläche.
                    float lo = prevT, hi = tc;
                    for (int k = 0; k < 12; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (HeightAbove(ray.Origin, dir, mid) > 0f)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    return Finish(ray.Origin, dir, hi, out x, out y, out hit);
                }
                prevT = tc;
                if (tc >= tEnd)
                    break;
            }
            return false;
        }

        float HeightAbove(Vector3 origin, Vector3 dir, float t)
        {
            float px = origin.X + dir.X * t;
            float pz = origin.Z + dir.Z * t;
            float ground = SampleHeight(px, pz);
            var (cx, cy) = WorldCoords.WorldToTile(px, pz);
            if (InBounds(cx, cy))
            {
                float water = _tiles[cy * Width + cx].WaterLevel;
                if (water > ground)
                    ground = water;
            }
            return origin.Y + dir.Y * t - ground;
        }

        bool Finish(Vector3 origin, Vector3 dir, float t, out int x, out int y, out Vector3 hit)
        {
            hit = new Vector3(origin.X + dir.X * t, origin.Y + dir.Y * t, origin.Z + dir.Z * t);
            (x, y) = WorldCoords.WorldToTile(hit.X, hit.Z);
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= Width) x = Width - 1;
            if (y >= Height) y = Height - 1;
            return true;
        }

        static bool ClipToBox(Vector3 o, Vector3 d, float maxX, float maxZ, float maxDistance, out float tMin, out float tMax)
        {
            tMin = 0f;
            tMax = maxDistance;
            if (!ClipAxis(o.X, d.X, 0f, maxX, ref tMin, ref tMax))
                return false;
            if (!ClipAxis(o.Z, d.Z, 0f, maxZ, ref tMin, ref tMax))
                return false;
            return tMin <= tMax;
        }

        static bool ClipAxis(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(d) < 1e-8f)
                return o >= min && o <= max;
            float t1 = (min - o) / d;
            float t2 = (max - o) / d;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }

        // ---- Bereiche ----

        public void ForEachInRect(int minX, int minY, int maxX, int maxY, TileVisitor visitor)
        {
            if (visitor == null)
                throw new ArgumentNullException(nameof(visitor));
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX >= Width) maxX = Width - 1;
            if (maxY >= Height) maxY = Height - 1;
            for (int y = minY; y <= maxY; y++)
            {
                int row = y * Width;
                for (int x = minX; x <= maxX; x++)
                    visitor(x, y, in _tiles[row + x]);
            }
        }

        // Liefert die besten Kacheln im Kreis, absteigend nach Bewertung (bei Gleichstand kleinerer Kachelindex zuerst).
        public int FindBestTiles(int cx, int cy, int radius, TileScorer scorer, int maxResults, List<TileScore> results)
        {
            if (scorer == null)
                throw new ArgumentNullException(nameof(scorer));
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            results.Clear();
            if (maxResults <= 0 || radius < 0)
                return 0;

            int r2 = radius * radius;
            int minY = Math.Max(0, cy - radius), maxY = Math.Min(Height - 1, cy + radius);
            int minX = Math.Max(0, cx - radius), maxX = Math.Min(Width - 1, cx + radius);
            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - cy;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy * dy > r2)
                        continue;
                    float score = scorer(x, y, in _tiles[y * Width + x]);
                    if (!(score > 0f))
                        continue;
                    PushBounded(results, new TileScore(x, y, score), maxResults);
                }
            }
            SortDescending(results);
            return results.Count;
        }

        // Gut verteilte Startplätze für Zivilisationen: baubar, Nahrung, Holz, Stein und Süßwasser in der Nähe.
        public int FindStartLocations(int count, int minDistanceTiles, List<TileScore> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            results.Clear();
            if (count <= 0)
                return 0;

            const int radius = 6;
            var candidates = new List<TileScore>();
            for (int y = WorldConstants.OceanBorder + radius; y < Height - WorldConstants.OceanBorder - radius; y += 2)
            {
                for (int x = WorldConstants.OceanBorder + radius; x < Width - WorldConstants.OceanBorder - radius; x += 2)
                {
                    if (!CanPlaceFootprint(x - 1, y - 1, 3, 3, out _))
                        continue;
                    float score = ScoreStartArea(x, y, radius);
                    if (score > 0f)
                        candidates.Add(new TileScore(x, y, score));
                }
            }
            SortDescending(candidates);

            // Sandbox: Zivilisationen sollen sich ohne Spielereingriff begegnen können. Deshalb zuerst alle Starts
            // auf demselben zu Fuß zusammenhängenden Landstück suchen (größtes zuerst); erst wenn keines genügend
            // Platz hat, über alle Landstücke verteilen.
            var regions = new Navigation.NavRegions(this);
            var regionSize = new Dictionary<int, int>();
            foreach (TileScore c in candidates)
            {
                int r = regions.RegionOf(c.X, c.Y, MovementClass.Foot);
                regionSize[r] = regionSize.TryGetValue(r, out int n) ? n + 1 : 1;
            }
            var order = new List<KeyValuePair<int, int>>(regionSize);
            order.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
            foreach (var region in order)
            {
                if (region.Key == 0 || region.Value < count)
                    continue;
                PickSpread(candidates, count, minDistanceTiles, results, regions, region.Key);
                if (results.Count >= count)
                    return results.Count;
                results.Clear();
            }
            PickSpread(candidates, count, minDistanceTiles, results, null, 0);
            return results.Count;
        }

        static void PickSpread(List<TileScore> candidates, int count, int minDistanceTiles, List<TileScore> results,
            Navigation.NavRegions regions, int region)
        {
            int minD2 = minDistanceTiles * minDistanceTiles;
            foreach (TileScore c in candidates)
            {
                if (regions != null && regions.RegionOf(c.X, c.Y, MovementClass.Foot) != region)
                    continue;
                bool farEnough = true;
                foreach (TileScore r in results)
                {
                    int dx = r.X - c.X, dy = r.Y - c.Y;
                    if (dx * dx + dy * dy < minD2)
                    {
                        farEnough = false;
                        break;
                    }
                }
                if (!farEnough)
                    continue;
                results.Add(c);
                if (results.Count >= count)
                    break;
            }
        }

        float ScoreStartArea(int cx, int cy, int radius)
        {
            float food = 0f;
            int wood = 0, stone = 0, freshWater = 0, land = 0, ocean = 0;
            int r2 = radius * radius;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2)
                        continue;
                    ref readonly TileData t = ref _tiles[y * Width + x];
                    if (t.Terrain == TerrainType.River || (t.Flags & TileFlags.Lake) != 0)
                        freshWater++;
                    else if (t.Terrain == TerrainType.DeepWater || t.Terrain == TerrainType.ShallowWater)
                        ocean++;
                    else
                        land++;
                    food += t.Fertility;
                    if (t.Resource == ResourceType.Wood) wood++;
                    else if (t.Resource == ResourceType.Stone || t.Resource == ResourceType.Ore) stone++;
                }
            }
            // Zu wenig Land (Halbinselspitze, kleine Insel) ist kein guter Start.
            if (land < r2 * 2)
                return 0f;
            float score = food / 255f;
            score += Math.Min(wood, 15) * 1.5f;
            score += Math.Min(stone, 6) * 2f;
            score += freshWater > 0 ? 25f : (ocean > 0 ? 8f : 0f);
            if (wood == 0 || stone == 0)
                score *= 0.4f;
            return score;
        }

        static bool IsWorse(in TileScore a, in TileScore b, int width)
        {
            if (a.Score != b.Score)
                return a.Score < b.Score;
            return a.Y * width + a.X > b.Y * width + b.X;
        }

        // Min-Heap der Größe maxResults: Wurzel ist das schlechteste behaltene Ergebnis.
        void PushBounded(List<TileScore> heap, TileScore item, int maxResults)
        {
            if (heap.Count < maxResults)
            {
                heap.Add(item);
                int i = heap.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!IsWorse(heap[i], heap[p], Width))
                        break;
                    (heap[i], heap[p]) = (heap[p], heap[i]);
                    i = p;
                }
                return;
            }
            if (!IsWorse(heap[0], item, Width))
                return;
            heap[0] = item;
            int n = heap.Count, k = 0;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < n && IsWorse(heap[l], heap[m], Width)) m = l;
                if (r < n && IsWorse(heap[r], heap[m], Width)) m = r;
                if (m == k)
                    break;
                (heap[k], heap[m]) = (heap[m], heap[k]);
                k = m;
            }
        }

        void SortDescending(List<TileScore> list)
        {
            int w = Width;
            list.Sort((a, b) =>
            {
                if (a.Score != b.Score)
                    return b.Score.CompareTo(a.Score);
                return (a.Y * w + a.X).CompareTo(b.Y * w + b.X);
            });
        }
    }
}
