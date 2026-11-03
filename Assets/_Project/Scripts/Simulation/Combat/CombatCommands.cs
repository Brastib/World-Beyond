using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Combat
{
    /// <summary>Befohlener Angriff auf eine Einheit (Spieler, UI, Armee-KI). Gilt nur für eigene Militäreinheiten.</summary>
    public sealed class AttackUnitCommand : IWorldCommand
    {
        public readonly List<EntityId> Units = new List<EntityId>();
        public EntityId Target;

        public bool Validate(SimContext ctx, out string reason)
        {
            if (!ctx.Units.Exists(Target)) { reason = "Ziel existiert nicht."; return false; }
            var catalog = ctx.Get<MilitaryCatalog>();
            foreach (var id in Units)
                if (ctx.Units.TryGet(id, out var u) && catalog.TryGet(u.Type, out _)) { reason = null; return true; }
            reason = "Keine Militäreinheit ausgewählt.";
            return false;
        }

        public void Execute(SimContext ctx)
        {
            var catalog = ctx.Get<MilitaryCatalog>();
            var task = new UnitTask { Kind = UnitTaskKind.Attack, TargetEntity = Target };
            foreach (var id in Units)
                if (ctx.Units.TryGet(id, out var u) && catalog.TryGet(u.Type, out _))
                    ctx.Units.SetTask(id, task);
        }
    }

    public sealed class SetStanceCommand : IWorldCommand
    {
        public readonly List<EntityId> Units = new List<EntityId>();
        public CombatStance Stance;

        public bool Validate(SimContext ctx, out string reason)
        {
            if (Units.Count == 0) { reason = "Keine Einheit ausgewählt."; return false; }
            reason = null;
            return true;
        }

        public void Execute(SimContext ctx)
        {
            var combat = ctx.Get<CombatSystem>();
            foreach (var id in Units) combat.SetStance(id, Stance);
        }
    }
}
