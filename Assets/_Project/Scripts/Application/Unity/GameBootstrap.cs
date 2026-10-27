using System;
using UnityEngine;

namespace WorldBeyond.Application
{
    /// <summary>
    /// Einziges zentrales MonoBehaviour (ARCHITECTURE §1 Prinzip 6). Erzeugt die Komposition und treibt den Runner.
    /// Andere Komponenten referenzieren diesen Bootstrap per Inspector und hören auf CompositionChanged;
    /// es gibt keine statische Instanz.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameStartSettings startSettings = new GameStartSettings();

        /// <summary>Starteinstellungen; wirken nur, wenn sie vor Awake gesetzt werden (GameObject dafür inaktiv anlegen).</summary>
        public GameStartSettings StartSettings
        {
            get => startSettings;
            set => startSettings = value ?? new GameStartSettings();
        }

        public GameComposition Composition { get; private set; }

        /// <summary>Neue Komposition (Start, Neues Spiel, nach erfolgreichem Laden). Presentation baut sich daraufhin neu auf.</summary>
        public event Action<GameComposition> CompositionChanged;

        private void Awake()
        {
            Replace(GameComposition.Create(startSettings));
        }

        private void Update()
        {
            Composition?.Runner.Advance(Time.unscaledDeltaTime);
        }

        /// <summary>Ersetzt die laufende Simulation, z. B. nach erfolgreichem Laden (ARCHITECTURE §11.5 Schritt 6).</summary>
        public void Replace(GameComposition composition)
        {
            Composition = composition ?? throw new ArgumentNullException(nameof(composition));
            CompositionChanged?.Invoke(composition);
        }
    }
}
