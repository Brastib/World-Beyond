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
        // Groll an gemeinsamer Grenze wächst über die Zeit (Sandbox: auch gleich starke Nachbarn geraten irgendwann in Konflikt).
        public float GrievancePerEdgePerSecond = -0.0005f;  // Grenze 80 → ≈ -0.04/s → Maximum nach ≈ 21 min
        public float GrievanceMax = -50f;
        public float GrievanceDecayPerSecond = 0.05f;       // ohne Grenze baut sich Groll ab
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
        // Kriegsende an das Kampfgeschehen koppeln (Integration 2026-10-27, Werte sind Platzhalter):
        // Frieden erst nach erster Feindberührung oder nach PeaceMinWarTicks, je nachdem was zuerst kommt.
        public long PeaceMinWarTicks = 9000;              // 15 Spielminuten
        // Zeit-Erschöpfung vor der Feindberührung nur mit PreContactExhaustionFactor; nach WarNoContactMaxTicks
        // ohne Berührung wieder voll, damit ein Krieg ohne Kontakt trotzdem endet.
        public float PreContactExhaustionFactor = 0.5f;
        public long WarNoContactMaxTicks = 36000;         // 60 Spielminuten

        // Frieden
        public float PeaceBaseline = 10f;
        public float PeaceScoreWeight = 0.6f;
        public float PeaceExhaustionWeight = 0.5f;
        public float PeaceOtherWarBonus = 10f;
        // Ein klar überlegener, noch frischer Empfänger nimmt keinen weißen Frieden an (sonst Krieg/Frieden-Ping-Pong).
        // Abzug = Gewicht × Überlegenheit (0 bei Gleichstand, 1 ab FullStrengthRatio) × (1 − Erschöpfung/Grenze).
        public float DominancePeaceWeight = 30f;
        public float DominanceExhaustionLimit = 60f;      // ab dieser Erschöpfung zählt Überlegenheit nicht mehr

        // KI
        public float WarThreshold = 0.25f;
        public float StrengthWeight = 0.35f;              // Anteil der Stärke an der Kriegsbewertung, Rest = Motiv
        public float MinStrengthRatio = 0.8f;             // darunter nie Angriff (deutlich schwächer)
        public float FullStrengthRatio = 2.0f;
        // Krieg nur mit echtem Grund: Stärke und Aggressivität allein reichen nicht.
        public float MinWarReason = 0.1f;                 // Mindestanteil aus Feindseligkeit + Ressourcenbedarf
        public float MotiveForFullStrength = 0.3f;        // ab diesem Motiv zählt der Stärkevorteil voll
        // Kriegsmüdigkeit nach Frieden: jeder beendete Krieg gegen dieselbe Civ senkt den Kriegsnutzen und
        // das Nachwachsen von Grenzgroll und klingt linear ab. 36000 Ticks = 60 Min. = 60 Jahre bei CivTime.TicksPerYear 600 (Stand integration/m1).
        public long WarWearinessTicks = 36000;
        public float WarWearinessPerWar = 0.8f;           // 1 = direkt nach dem Frieden gar kein Krieg möglich            // ab hier voller Stärkefaktor
        public float AllianceThreshold = 0.5f;
        public float RecruitThreshold = 0.4f;
        public float MaxReachTiles = 60f;                 // Reichweite für Kriegsziele und Zielprioritäten
        public float ContactReachTiles = 48f;             // Abstand der Gebietsränder, ab dem Nachbarn sich als Rivalen sehen
        public float RivalryEquivalentEdges = 160f;       // Groll ohne Grenze wächst wie an einer Grenze dieser Länge × Nähe
        public int WarCooldownTicks = 3000;
        public int PeaceProposalCooldownTicks = 300;
        public int AllianceCooldownTicks = 1200;
        public int RecruitCooldownTicks = 600;
        public float PeaceArmyStrengthPerPop = 0.05f;     // Grundarmee: Bevölkerung 1000, Haltung 0,5 → Stärke 50
        public float AssumedUnitStrength = 5f;            // Schätzwert pro Einheit für die Anzahl einer Anfrage
        public int MaxRecruitPerRequest = 5;
        // Genau eine Stelle rekrutiert: false, sobald Military über ArmyEnvironment.RecruitWillingness
        // (IWarQuery.GetRecruitWillingness) selbst bestellt. Die Bereitschaft wird weiter berechnet.
        public bool SelfRecruit = true;
        // Keine feste Armeegrenze (Nutzerentscheidung 2026-10-27): Die KI wägt jeden Rekruten gegen seine Kosten ab.
        // Kosten = Arbeitskraft: a = Armee / (Armee + Bevölkerung), weil die Bevölkerung Soldaten nicht mitzählt.
        //          LaborCostWeight × a/(1−a) + LaborSteepWeight × ((a − LaborSteepFrom)/(1−a))² ab der Schwelle (steil, ohne harte Grenze)
        //        + Nahrung/Ressourcenmangel (FoodCostWeight × ResourceNeed) + Kriegsmüdigkeit (ExhaustionCostWeight × Erschöpfung/100).
        public float LaborCostWeight = 2f;
        public float LaborSteepFrom = 0.06f;              // ab etwa 6 % unter Waffen steigen die Kosten steil
        public float LaborSteepWeight = 150f;             // 10 % → ≈ 0,5, 12 % → ≈ 1,0, 15 % → ≈ 2,0 (mehr als jeder Nutzen, max. 1,75)
        public float FoodCostWeight = 0.5f;
        public float ExhaustionCostWeight = 0.5f;
        // Bis der Adapter eingereihte Aufträge mitzählt: angenommene Anfragen zählen so lange selbst als „unterwegs“.
        // 0 schaltet das ab (wenn GetArmyUnitCount die Warteschlange enthält).
        public long RecruitPendingTicks = 1200;
        // Aussichtslose Lage: Gegner mindestens so viel stärker → früh Frieden mit Zugeständnis suchen.
        public float HopelessThreat = 4f;
        public float HopelessPeaceExhaustion = 20f;
        public int MaxAlliancesPerCiv = 2;
        public float TargetDistanceScale = 64f;
        public int StrategicPointRadius = 4;
        public int DecisionLogSize = 64;
    }
}
