using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>Laufzeitzustand einer Zivilisation. Nur innerhalb der Civ-Schicht veränderbar; nach außen über CivilizationView.</summary>
    public sealed class Civilization
    {
        public int Id;
        public string Name;
        public CivColor Color;
        public ulong CultureSeed;
        public CivTraits Traits;
        public bool Alive = true;
        public int CapitalSettlementId;
        public long CreatedTick;
        public int NameCounter;                       // für deterministische Siedlungsnamen

        // Forschung
        public bool[] Researched = Array.Empty<bool>();   // Index = TechId.Value (Laufzeit), gespeichert als Keys
        public TechId CurrentResearch;
        public double ResearchPoints;                 // Fortschritt auf CurrentResearch
        public Epoch Epoch = Epoch.Primitive;

        // KI
        public long NextAiTick;
        public int ExpansionCooldownUntilYear;
    }

    /// <summary>Befristeter Modifikator durch ein Ereignis (Seuche, Missernte …). Wirkt nur über die normalen Formeln.</summary>
    public struct SettlementModifier
    {
        public CivEventKind Kind;
        public long UntilTick;
    }

    /// <summary>Eine Produktionsanfrage über IRecruitment.</summary>
    public sealed class RecruitOrder
    {
        public string UnitTypeKey;
        public int Remaining;
        public double ProgressYears;      // Fortschritt der aktuellen Einheit
        public float YearsPerUnit;
        public int TargetX = -1, TargetY = -1;   // Siedler: Zielkachel
        public int CargoTargetSettlementId;      // Karawane: Zielsiedlung
        public ResourceType CargoResource;       // Karawane: Ladung (beim Start aus dem Lager entnommen)
        public int CargoAmount;
    }

    /// <summary>Laufzeitzustand einer Siedlung. Bevölkerung in Kohorten, Lager pro Gut, Arbeitsplätze pro JobKind.</summary>
    public sealed class Settlement
    {
        public int Id;
        public int CivId;
        public string Name;
        public int X, Y;                              // Kachel des Ortskerns (linke untere Ecke des 2×2-Kerns)
        public SettlementTier Tier;
        public long FoundedTick;

        // Bevölkerung (abstrahiert, double für exakte Fortschreibung ohne Rundungsdrift)
        public double Children, Adults, Elders;
        public double Population => Children + Adults + Elders;

        // Lager (Index = (int)ResourceType)
        public double[] Stock = new double[Resources.SlotCount];
        public int StorageCapacity;
        public int HousingCapacity;
        public float Defense;

        // Gebäude
        public readonly List<EntityId> Buildings = new List<EntityId>();

        // Arbeit (Ergebnis der letzten Verteilung, Index = (int)JobKind)
        public int[] Workers = new int[JobKindCount];
        public int Unemployed;

        // Letzte Bilanzen pro Jahr (für UI und KI)
        public float FoodBalancePerYear;
        public float BirthsPerYear, DeathsPerYear;
        public float[] ProductionPerYear = new float[Resources.SlotCount];
        public float[] ConsumptionPerYear = new float[Resources.SlotCount];
        public float ResearchPerYear;

        // Simulation
        public long LastUpdateTick;
        public long NextUpdateTick;
        public bool ActiveLod;
        public readonly List<SettlementModifier> Modifiers = new List<SettlementModifier>();
        public readonly List<RecruitOrder> RecruitQueue = new List<RecruitOrder>();

        // Expansion: geplanter Ort für den nächsten Siedler (−1 = keiner)
        public int ExpansionTargetX = -1, ExpansionTargetY = -1;
        public bool PlayerChoseTarget;

        public const int JobKindCount = 12;

        public bool HasModifier(CivEventKind kind, long now)
        {
            for (int i = 0; i < Modifiers.Count; i++)
                if (Modifiers[i].Kind == kind && Modifiers[i].UntilTick > now) return true;
            return false;
        }

        public double StockOf(ResourceType r) => Stock[(int)r];
    }
}
