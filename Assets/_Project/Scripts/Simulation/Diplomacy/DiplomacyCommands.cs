using WorldBeyond.Simulation.Commands;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Befehle für Spieler (UI/God-Modus). Gleicher Weg wie die KI: alles läuft über den DiplomacyService.
    // Die UI erzeugt sie und reicht sie an die ICommandQueue; der Service wird beim Erzeugen übergeben,
    // weil SimContext (Integration) noch kein Diplomatie-Feld hat.

    public sealed class DeclareWarCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _attacker, _defender; readonly bool _force;
        public DeclareWarCommand(DiplomacyService d, int attacker, int defender, bool forceBreakTreaties)
        { _d = d; _attacker = attacker; _defender = defender; _force = forceBreakTreaties; }

        public bool Validate(SimContext ctx, out string reason)
        {
            if (_force)
            {
                reason = null;
                if (_d.AreAtWar(_attacker, _defender)) { reason = "Die beiden befinden sich bereits im Krieg."; return false; }
                if (_attacker == _defender || !_d.World.IsCivAlive(_attacker) || !_d.World.IsCivAlive(_defender)) { reason = "Ungültige Zivilisation."; return false; }
                return true;
            }
            return _d.CanDeclareWar(_attacker, _defender, out reason);
        }

        public void Execute(SimContext ctx)
        {
            if (_force) _d.ForceWar(_attacker, _defender, out _);
            else _d.DeclareWar(_attacker, _defender, CasusBelli.PlayerForced, null, out _);
        }
    }

    // Friedensangebot, das die KI des Empfängers prüft. Mit force = true wird der Frieden ohne Prüfung geschlossen (God-Modus).
    public sealed class ProposePeaceCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _proposer, _receiver; readonly PeaceTerm _term; readonly bool _force;
        public PeaceProposalResult LastResult { get; private set; }

        public ProposePeaceCommand(DiplomacyService d, int proposer, int receiver, PeaceTerm term, bool force)
        { _d = d; _proposer = proposer; _receiver = receiver; _term = term; _force = force; }

        public bool Validate(SimContext ctx, out string reason)
        {
            reason = null;
            if (_d.FindWar(_proposer, _receiver) == null) { reason = "Kein Krieg zwischen diesen Zivilisationen."; return false; }
            return true;
        }

        public void Execute(SimContext ctx)
        {
            if (_force && _term.Type == PeaceTermType.WhitePeace) { _d.MakePeace(_proposer, _receiver); LastResult = new PeaceProposalResult(true, 0f, null); }
            else LastResult = _d.ProposePeace(_proposer, _receiver, _term);
        }
    }

    public sealed class SignTruceCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _a, _b, _ticks;
        public SignTruceCommand(DiplomacyService d, int civA, int civB, int durationTicks) { _d = d; _a = civA; _b = civB; _ticks = durationTicks; }
        public bool Validate(SimContext ctx, out string reason)
        {
            reason = null;
            if (_ticks <= 0) { reason = "Dauer muss positiv sein."; return false; }
            var war = _d.FindWar(_a, _b);
            if (war != null && !((war.AttackerLeader == _a && war.DefenderLeader == _b) || (war.AttackerLeader == _b && war.DefenderLeader == _a)))
            { reason = "Waffenstillstand nur zwischen den Kriegsführern."; return false; }
            return true;
        }
        public void Execute(SimContext ctx) => _d.SignTruce(_a, _b, _ticks);
    }

    public sealed class FormAllianceCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _a, _b;
        public FormAllianceCommand(DiplomacyService d, int civA, int civB) { _d = d; _a = civA; _b = civB; }
        public bool Validate(SimContext ctx, out string reason)
        {
            reason = null;
            if (_d.FindWar(_a, _b) != null) { reason = "Die beiden befinden sich im Krieg."; return false; }
            if (_d.AreAllied(_a, _b)) { reason = "Bündnis besteht bereits."; return false; }
            return true;
        }
        public void Execute(SimContext ctx) => _d.FormAlliance(_a, _b);
    }

    public sealed class BreakTreatyCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _treatyId, _breaker;
        public BreakTreatyCommand(DiplomacyService d, int treatyId, int breakerCiv) { _d = d; _treatyId = treatyId; _breaker = breakerCiv; }
        public bool Validate(SimContext ctx, out string reason)
        {
            foreach (var t in _d.GetTreaties(_breaker)) if (t.Id == _treatyId) { reason = null; return true; }
            reason = "Vertrag nicht gefunden."; return false;
        }
        public void Execute(SimContext ctx) => _d.BreakTreaty(_treatyId, _breaker);
    }

    // God-Modus: Meinung gezielt verschieben (dauerhaft, bis der Spieler sie wieder ändert).
    public sealed class ModifyRelationCommand : IWorldCommand
    {
        readonly DiplomacyService _d; readonly int _a, _b, _delta; readonly bool _bothWays;
        public ModifyRelationCommand(DiplomacyService d, int civA, int civB, int delta, bool bothWays) { _d = d; _a = civA; _b = civB; _delta = delta; _bothWays = bothWays; }
        public bool Validate(SimContext ctx, out string reason)
        {
            reason = null;
            if (_a == _b || !_d.World.IsCivAlive(_a) || !_d.World.IsCivAlive(_b)) { reason = "Ungültige Zivilisation."; return false; }
            return true;
        }
        public void Execute(SimContext ctx)
        {
            _d.ModifyRelation(_a, _b, _delta, RelationReason.PlayerIntervention);
            if (_bothWays) _d.ModifyRelation(_b, _a, _delta, RelationReason.PlayerIntervention);
        }
    }
}
