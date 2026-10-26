namespace WorldBeyond.Simulation.Diplomacy
{
    // Stellschrauben an einem Ort. Werte sind Startwerte und werden mit laufender Simulation abgestimmt.
    public sealed class DiplomacySettings
    {
        // Meinung
        public float WarDeclaredOnUsValue = -50f;
        public float WarDeclaredOnUsDecay = 0.04f;        // pro Sekunde, ≈ 21 min bis 0
        public float AttackedAllyValue = -25f;
        public float TreatyBrokenValue = -40f;
        public float TreatyBrokenObserverValue = -10f;
        public float TreatyBrokenDecay = 0.01f;           // ≈ 1 h
        public float RefusedCallToArmsValue = -15f;
        public float CommonEnemyValue = 15f;
        public float AllianceFormedValue = 10f;
        public float PeaceSignedValue = 10f;
        public float TerritoryCededValue = -30f;
        public float DefaultEventDecay = 0.02f;
        public float BorderFrictionPerEdge = -0.25f;
        public float BorderFrictionMax = -25f;
        public float ResourceCompetitionMax = -30f;
        public float TensionThreshold = -30f;
        public int FactorRefreshTicks = 50;               // Grenz-/Ressourcenfaktoren alle 5 s

        // Verträge
        public int PeaceCooldownTicks = 3000;             // 5 min kein neuer Krieg nach Frieden
        public int DefaultTruceTicks = 1200;              // 2 min

        // Krieg
        public float ConquestWeight = 50f;
        public float CasualtyWeight = 25f;
        public float PointsWeight = 10f;
        public float GoalWeightEach = 5f;
        public float GoalWeightMax = 15f;
        public float ExhaustionPerMinute = 0.5f;

        // Frieden
        public float PeaceBaseline = 10f;
        public float PeaceScoreWeight = 0.6f;
        public float PeaceExhaustionWeight = 0.5f;
        public float PeaceOtherWarBonus = 10f;

        // KI
        public float WarThreshold = 0.35f;
        public float AllianceThreshold = 0.5f;
        public float RecruitThreshold = 0.4f;
        public float MaxReachTiles = 60f;
        public int WarCooldownTicks = 3000;
        public int PeaceProposalCooldownTicks = 300;
        public int AllianceCooldownTicks = 1200;
        public int RecruitCooldownTicks = 600;
        public int MaxAlliancesPerCiv = 2;
        public float TargetDistanceScale = 64f;
        public int StrategicPointRadius = 4;
        public int DecisionLogSize = 64;
    }
}
