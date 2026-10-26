using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Gemeinsamer Zustand der Civ-Schicht (Zivilisationen, Siedlungen, Gebäude). Wird nur von den Civ-Diensten und
    /// -Systemen gehalten; andere Phasen sehen ihn nur über die Interfaces aus SYSTEM_INTERFACES §7.
    /// IDs von Zivilisationen und Siedlungen: SequentialIdAllocator, nie wiederverwendet (ARCHITECTURE §11.1 Regel 2).
    /// </summary>
    public sealed class CivState
    {
        public readonly SimContext Ctx;
        public readonly IWorldGrid World;
        public readonly CivContent Content;
        public readonly BuildingStore Buildings;

        public readonly SequentialIdAllocator CivIds = new SequentialIdAllocator();
        public readonly SequentialIdAllocator SettlementIds = new SequentialIdAllocator();

        internal readonly Dictionary<int, Civilization> Civs = new Dictionary<int, Civilization>();
        internal readonly List<int> CivOrder = new List<int>();
        internal readonly Dictionary<int, Settlement> Settlements = new Dictionary<int, Settlement>();
        internal readonly List<int> SettlementOrder = new List<int>();          // aufsteigende IDs = feste Reihenfolge
        internal readonly Dictionary<int, List<int>> SettlementsByCiv = new Dictionary<int, List<int>>();

        public CivState(SimContext ctx, IWorldGrid world, CivContent content)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Buildings = new BuildingStore(content.Buildings);
        }

        internal Civilization Civ(int id) => Civs.TryGetValue(id, out var c) ? c : null;
        internal Settlement Settlement(int id) => Settlements.TryGetValue(id, out var s) ? s : null;

        internal List<int> SettlementListOf(int civId)
        {
            if (!SettlementsByCiv.TryGetValue(civId, out var list))
            {
                list = new List<int>();
                SettlementsByCiv.Add(civId, list);
            }
            return list;
        }

        internal void AddSettlement(Settlement s)
        {
            Settlements.Add(s.Id, s);
            InsertSorted(SettlementOrder, s.Id);
            InsertSorted(SettlementListOf(s.CivId), s.Id);
        }

        internal void RemoveSettlement(Settlement s)
        {
            Settlements.Remove(s.Id);
            SettlementOrder.Remove(s.Id);
            SettlementListOf(s.CivId).Remove(s.Id);
        }

        internal void MoveSettlement(Settlement s, int newCivId)
        {
            SettlementListOf(s.CivId).Remove(s.Id);
            s.CivId = newCivId;
            InsertSorted(SettlementListOf(newCivId), s.Id);
        }

        internal void ClearAll()
        {
            Civs.Clear(); CivOrder.Clear(); Settlements.Clear(); SettlementOrder.Clear(); SettlementsByCiv.Clear();
        }

        private static void InsertSorted(List<int> list, int value)
        {
            int i = list.BinarySearch(value);
            if (i < 0) list.Insert(~i, value);
        }

        /// <summary>Kachelmittelpunkt in Weltmetern (x, z).</summary>
        internal static (float x, float z) TileCenter(int x, int y) => (WorldCoords.TileCenterX(x), WorldCoords.TileCenterZ(y));

        /// <summary>Mittelpunkt des 2×2-Ortskerns.</summary>
        internal static (float x, float z) SettlementCenter(Settlement s) =>
            ((s.X + 1) * WorldConstants.TileSize, (s.Y + 1) * WorldConstants.TileSize);

        /// <summary>
        /// Begehbarer Ankunfts- und Aufbruchspunkt einer Siedlung: erste freie Kachel in Ringen um den Ortskern
        /// (Gebäudekacheln blockieren die Bewegung, der Ortskern selbst ist also nie erreichbar). Feste Suchreihenfolge,
        /// damit das Ergebnis deterministisch ist. Rückfall: Mitte des Ortskerns.
        /// </summary>
        internal (float x, float z) ArrivalPoint(Settlement s, MovementClass mc)
        {
            int size = SettlementRegistry.CoreSize;
            for (int r = 1; r <= 6; r++)
            {
                for (int y = s.Y - r; y < s.Y + size + r; y++)
                {
                    for (int x = s.X - r; x < s.X + size + r; x++)
                    {
                        bool onRing = x == s.X - r || x == s.X + size + r - 1 || y == s.Y - r || y == s.Y + size + r - 1;
                        if (!onRing || !World.InBounds(x, y) || !World.IsWalkable(x, y, mc)) continue;
                        return (WorldCoords.TileCenterX(x), WorldCoords.TileCenterZ(y));
                    }
                }
            }
            return SettlementCenter(s);
        }

        internal (float x, float z) ArrivalPoint(Settlement s, UnitTypeId type) =>
            ArrivalPoint(s, Ctx.UnitTypes.TryGet(type, out var t) ? t.Movement : MovementClass.Foot);

        internal int TechSlots => Content.Techs.Count + 1;
    }
}
