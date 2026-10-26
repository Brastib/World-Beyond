using System.IO;
using UnityEngine;
using WorldBeyond.Application.Save;

namespace WorldBeyond.Application
{
    /// <summary>
    /// Verbindet den Unity-freien SaveLoadService mit GameBootstrap. Spielstände liegen in
    /// Application.persistentDataPath/Saves und überleben damit einen Neustart der Anwendung.
    /// Die UI ruft Service.Save/Load/ListSlots auf und zeigt Result.UserMessage an.
    /// </summary>
    [RequireComponent(typeof(GameBootstrap))]
    public sealed class SaveLoadController : MonoBehaviour
    {
        [SerializeField] private GameBootstrap bootstrap;

        public ISaveLoadService Service { get; private set; }

        private void Awake()
        {
            if (bootstrap == null) bootstrap = GetComponent<GameBootstrap>();
            var store = new SaveFileStore(Path.Combine(UnityEngine.Application.persistentDataPath, "Saves"));
            // Muss dieselbe Komposition erzeugen wie GameBootstrap.Awake, nur mit den gespeicherten Startparametern.
            Service = new SaveLoadService(store, settings => GameComposition.Create(settings), () => bootstrap.Composition,
                                          bootstrap.Replace, null, UnityEngine.Application.version);
        }
    }
}
