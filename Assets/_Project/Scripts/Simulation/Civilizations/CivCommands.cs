using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    // Spieler-/God-Befehle der Zivilisationsphase. Laufen an der Tick-Grenze (Order 0) und nutzen nur die
    // normalen Dienste – Ereignisse wirken über Modifikatoren und Lager, nie über direkte Zahlenänderungen am Ergebnis.

    /// <summary>Neue Zivilisation erschaffen. StartX &lt; 0: bester freier Startplatz der Welt.</summary>
    public sealed class CreateCivilizationCommand : IWorldCommand
    {
        public string Name;
        public CivColor Color;
        public ulong CultureSeed;
        public int StartX = -1, StartY = -1;

        public bool Validate(SimContext ctx, out string reason)
        {
            if (StartX >= 0 && !ctx.Get<IWorldQuery>().InBounds(StartX, StartY)) { reason = "Startpunkt außerhalb der Welt."; return false; }
            reason = null;
            return true;
        }

        public void Execute(SimContext ctx)
        {
            var civs = ctx.Get<ICivilizationRegistry>();
            int x = StartX, y = StartY;
            if (x < 0 && !CivBootstrap.TryFindFreeStart(ctx, out x, out y)) throw new InvalidOperationException("Kein freier Startplatz für eine neue Zivilisation.");
            if (!civs.TryCreate(Name, Color, CultureSeed, x, y, out _, out string reason)) throw new InvalidOperationException(reason);
        }
    }

    /// <summary>God-Modus: Siedlung mit 6 Einwohnern an einer Stelle gründen.</summary>
    public sealed class FoundSettlementCommand : IWorldCommand
    {
        public int CivId;
        public int X, Y;

        public bool Validate(SimContext ctx, out string reason) => ctx.Get<ISettlementRegistry>().CanFoundAt(CivId, X, Y, out reason);

        public void Execute(SimContext ctx)
        {
            var reg = (SettlementRegistry)ctx.Get<ISettlementRegistry>();
            if (!reg.TryFound(CivId, X, Y, 6, new ResourceCost { Food = 30, Wood = 20 }, true, out _, out string reason))
                throw new InvalidOperationException(reason);
        }
    }

    /// <summary>Gründung beeinflussen: Die Siedlung schickt ihren nächsten Siedler an diesen Ort (sobald sie es sich leisten kann).</summary>
    public sealed class SetExpansionTargetCommand : IWorldCommand
    {
        public int SettlementId;
        public int X, Y;

        public bool Validate(SimContext ctx, out string reason)
        {
            var q = ctx.Get<ISettlementQuery>();
            if (!q.TryGet(SettlementId, out var st)) { reason = "Siedlung existiert nicht."; return false; }
            return ctx.Get<ISettlementRegistry>().CanFoundAt(st.CivId, X, Y, out reason);
        }

        public void Execute(SimContext ctx)
        {
            var state = ctx.Get<CivState>();
            var st = state.Settlement(SettlementId);
            st.ExpansionTargetX = X;
            st.ExpansionTargetY = Y;
            st.PlayerChoseTarget = true;
        }
    }

    /// <summary>Ereignis in einer Siedlung auslösen (Seuche, Missernte, Babyboom, Brand, Goldfund).</summary>
    public sealed class TriggerCivEventCommand : IWorldCommand
    {
        public int SettlementId;
        public CivEventKind Kind;

        public bool Validate(SimContext ctx, out string reason)
        {
            if (!ctx.Get<ISettlementQuery>().Exists(SettlementId)) { reason = "Siedlung existiert nicht."; return false; }
            if (!Enum.IsDefined(typeof(CivEventKind), Kind)) { reason = "Unbekanntes Ereignis."; return false; }
            reason = null;
            return true;
        }

        public void Execute(SimContext ctx) => CivEvents.Apply(ctx, SettlementId, Kind);
    }

    /// <summary>God-Modus: Epoche einer Zivilisation setzen (schaltet alle Technologien bis dahin frei).</summary>
    public sealed class ForceEpochCommand : IWorldCommand
    {
        public int CivId;
        public Epoch Epoch;

        public bool Validate(SimContext ctx, out string reason)
        {
            if (!ctx.Get<ICivilizationQuery>().Exists(CivId)) { reason = "Zivilisation existiert nicht."; return false; }
            reason = null;
            return true;
        }

        public void Execute(SimContext ctx) => ctx.Get<ITechTree>().ForceEpoch(CivId, Epoch);
    }

    /// <summary>Forschungsziel einer Zivilisation setzen.</summary>
    public sealed class SetResearchCommand : IWorldCommand
    {
        public int CivId;
        public string TechKey;

        public bool Validate(SimContext ctx, out string reason)
        {
            var tech = ctx.Get<ITechTree>();
            if (!ctx.Get<ICivilizationQuery>().Exists(CivId)) { reason = "Zivilisation existiert nicht."; return false; }
            if (!tech.TryGetTech(TechKey, out _)) { reason = $"Unbekannte Technologie '{TechKey}'."; return false; }
            reason = null;
            return true;
        }

        public void Execute(SimContext ctx)
        {
            var tech = ctx.Get<ITechTree>();
            tech.TryGetTech(TechKey, out var t);
            tech.SetResearch(CivId, t);
        }
    }

    public static class CivEvents
    {
        public const int DurationTicks = CivTime.TicksPerYear;

        public static void Apply(SimContext ctx, int settlementId, CivEventKind kind)
        {
            var s = ctx.Get<CivState>();
            var st = s.Settlement(settlementId);
            if (st == null) return;
            long until = ctx.Clock.Tick + DurationTicks;
            switch (kind)
            {
                case CivEventKind.Plague:
                case CivEventKind.BabyBoom:
                    st.Modifiers.Add(new SettlementModifier { Kind = kind, UntilTick = until });
                    break;
                case CivEventKind.Famine:
                    st.Stock[(int)ResourceType.Food] *= 0.4;
                    st.Modifiers.Add(new SettlementModifier { Kind = kind, UntilTick = until });
                    break;
                case CivEventKind.Fire:
                {
                    st.Stock[(int)ResourceType.Wood] *= 0.7;
                    var candidates = new List<EntityId>();
                    foreach (var b in st.Buildings)
                    {
                        var r = s.Buildings.GetRecord(b);
                        if (r != null && !r.Type.Equals(s.Content.TownCenterType)) candidates.Add(b);
                    }
                    if (candidates.Count > 0)
                    {
                        var victim = candidates[ctx.Random.Range(0, candidates.Count)];
                        var r = s.Buildings.GetRecord(victim);
                        int bx = r.X, by = r.Y;
                        ((SettlementRegistry)ctx.Get<ISettlementRegistry>()).DestroyBuilding(victim);
                        if (s.World.InBounds(bx, by)) s.World.SetFlags(bx, by, TileFlags.Scorched, TileFlags.None);
                    }
                    break;
                }
                case CivEventKind.GoldRush:
                    st.Stock[(int)ResourceType.Gold] += 30;
                    st.Adults += 5;   // Zuwanderer
                    break;
            }
            ctx.Events.Publish(new CivEventTriggeredEvent { SettlementId = settlementId, Kind = kind });
        }
    }
}
