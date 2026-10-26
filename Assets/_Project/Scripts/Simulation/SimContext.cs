using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.Save;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation
{
    /// <summary>
    /// Zugriff für Systeme, keine globale Statik. Fundament-Dienste sind feste Felder.
    /// Dienste der Fachphasen (IWorldGrid, ICivilizationRegistry, IEconomy, IDiplomacyService, ICombatService …)
    /// werden in der Composition Root mit Register&lt;T&gt; eingetragen und mit Get&lt;T&gt; abgefragt.
    /// Nach Seal() sind keine Registrierungen mehr möglich: Es gibt genau eine Instanz je Dienst.
    /// </summary>
    public sealed class SimContext
    {
        public readonly SimClock Clock;
        public readonly SimRandom Random;
        public readonly ISimEventBus Events;
        public readonly ICommandQueue Commands;
        public readonly ContentRegistry Content;
        public readonly UnitTypeRegistry UnitTypes;
        public readonly IUnitStore Units;
        public readonly SaveableRegistry Saveables;

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        public bool IsSealed { get; private set; }

        public SimContext(SimClock clock, SimRandom random, ISimEventBus events, ICommandQueue commands,
            ContentRegistry content, UnitTypeRegistry unitTypes, IUnitStore units, SaveableRegistry saveables)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            UnitTypes = unitTypes ?? throw new ArgumentNullException(nameof(unitTypes));
            Units = units ?? throw new ArgumentNullException(nameof(units));
            Saveables = saveables ?? throw new ArgumentNullException(nameof(saveables));
        }

        /// <summary>Trägt den einzigen Dienst vom Typ T ein. Zweite Registrierung desselben Typs → Exception.</summary>
        public void Register<T>(T service) where T : class
        {
            if (IsSealed) throw new InvalidOperationException($"SimContext ist versiegelt; {typeof(T).Name} kann nicht mehr registriert werden.");
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (_services.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"Dienst {typeof(T).Name} ist bereits registriert. Es darf nur eine Instanz geben (INTEGRATION.md §3).");
            _services.Add(typeof(T), service);
        }

        /// <summary>Fehlender Dienst → Exception mit Namen (Konfigurationsfehler, kein stiller Rückfall).</summary>
        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new InvalidOperationException($"Dienst {typeof(T).Name} ist nicht registriert. Er muss in GameComposition eingetragen werden.");
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s)) { service = (T)s; return true; }
            service = null;
            return false;
        }

        public void Seal() => IsSealed = true;
    }
}
