using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Presentation.UI
{
    /// <summary>Deutsche Anzeigetexte für Enums der Simulation. Unity-frei, damit Tools/UiTests sie prüfen kann.</summary>
    public static class UiText
    {
        public static string Epoch(Epoch e)
        {
            switch (e)
            {
                case Simulation.Civilizations.Epoch.Primitive: return "Frühzeit";
                case Simulation.Civilizations.Epoch.Ancient: return "Antike";
                case Simulation.Civilizations.Epoch.Medieval: return "Mittelalter";
                case Simulation.Civilizations.Epoch.Industrial: return "Industriezeit";
                case Simulation.Civilizations.Epoch.Modern: return "Moderne";
                default: return "Unbekannte Epoche";
            }
        }

        public static string Tier(SettlementTier t)
        {
            switch (t)
            {
                case SettlementTier.Camp: return "Lager";
                case SettlementTier.Village: return "Dorf";
                case SettlementTier.Town: return "Kleinstadt";
                case SettlementTier.City: return "Stadt";
                case SettlementTier.Metropolis: return "Großstadt";
                default: return "Siedlung";
            }
        }

        public static string UnitState(UnitState s)
        {
            switch (s)
            {
                case Simulation.Units.UnitState.Idle: return "wartet";
                case Simulation.Units.UnitState.Moving: return "unterwegs";
                case Simulation.Units.UnitState.Working: return "arbeitet";
                case Simulation.Units.UnitState.Fighting: return "kämpft";
                case Simulation.Units.UnitState.Fleeing: return "flieht";
                case Simulation.Units.UnitState.Dead: return "gefallen";
                default: return "unbekannt";
            }
        }

        public static string UnitRole(UnitRole r)
        {
            switch (r)
            {
                case Simulation.Units.UnitRole.Civilian: return "Zivilist";
                case Simulation.Units.UnitRole.Worker: return "Arbeiter";
                case Simulation.Units.UnitRole.Settler: return "Siedler";
                case Simulation.Units.UnitRole.Infantry: return "Infanterie";
                case Simulation.Units.UnitRole.Vehicle: return "Fahrzeug";
                case Simulation.Units.UnitRole.Artillery: return "Artillerie";
                case Simulation.Units.UnitRole.RocketArtillery: return "Raketenartillerie";
                case Simulation.Units.UnitRole.Trader: return "Händler";
                default: return "Einheit";
            }
        }

        public static string CasusBelli(CasusBelli c)
        {
            switch (c)
            {
                case Simulation.Diplomacy.CasusBelli.BorderDispute: return "Grenzstreit";
                case Simulation.Diplomacy.CasusBelli.ResourceCompetition: return "Streit um Rohstoffe";
                case Simulation.Diplomacy.CasusBelli.Hostility: return "Feindseligkeit";
                case Simulation.Diplomacy.CasusBelli.Expansion: return "Expansion";
                case Simulation.Diplomacy.CasusBelli.DefendAlly: return "Bündnispflicht";
                case Simulation.Diplomacy.CasusBelli.PlayerForced: return "Eingriff des Spielers";
                default: return null;
            }
        }

        public static string PeaceTerm(PeaceTermType t)
        {
            switch (t)
            {
                case PeaceTermType.WhitePeace: return "ohne Gebietsabtretung";
                case PeaceTermType.CedeSettlement: return "mit Abtretung einer Siedlung";
                case PeaceTermType.Tribute: return "gegen Tribut";
                default: return null;
            }
        }
    }
}
