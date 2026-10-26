using System;
using System.Collections.Generic;

namespace WorldBeyond.Core
{
    public interface ISimEvent { }

    public interface ISimEventBus
    {
        void Publish<T>(in T evt) where T : struct, ISimEvent;
        void Subscribe<T>(Action<T> handler) where T : struct, ISimEvent;
        void Unsubscribe<T>(Action<T> handler) where T : struct, ISimEvent;
        // Events werden gesammelt und am Ende jedes Ticks ausgeliefert (kein Reentrancy-Chaos).
        void Flush();
    }

    /// <summary>
    /// Einziger Event-Bus der Simulation. Publish sammelt nur; Flush liefert in Veröffentlichungsreihenfolge
    /// je Event-Typ aus. Events, die während Flush veröffentlicht werden, kommen beim nächsten Flush an.
    /// </summary>
    public sealed class SimEventBus : ISimEventBus
    {
        private interface IChannel
        {
            int PendingCount { get; }
            void BeginFlush();
            void Deliver(SimEventBus bus);
            void ClearPending();
        }

        private sealed class Channel<T> : IChannel where T : struct, ISimEvent
        {
            private List<T> _pending = new List<T>();
            private List<T> _delivering = new List<T>();
            private readonly List<Action<T>> _handlers = new List<Action<T>>();
            private Action<T>[] _snapshot = Array.Empty<Action<T>>();
            private bool _handlersDirty;

            public int PendingCount => _pending.Count;

            public void Add(in T evt) => _pending.Add(evt);

            public void ClearPending() => _pending.Clear();

            public void Subscribe(Action<T> handler)
            {
                if (_handlers.Contains(handler)) return;
                _handlers.Add(handler);
                _handlersDirty = true;
            }

            public void Unsubscribe(Action<T> handler)
            {
                if (_handlers.Remove(handler)) _handlersDirty = true;
            }

            // Übernimmt die bis jetzt veröffentlichten Events; spätere landen wieder in _pending.
            public void BeginFlush()
            {
                var swap = _delivering;
                _delivering = _pending;
                _pending = swap;
            }

            public void Deliver(SimEventBus bus)
            {
                if (_delivering.Count == 0) return;

                if (_handlersDirty)
                {
                    _snapshot = _handlers.ToArray();
                    _handlersDirty = false;
                }
                var handlers = _snapshot;
                for (int e = 0; e < _delivering.Count; e++)
                {
                    var evt = _delivering[e];
                    for (int h = 0; h < handlers.Length; h++)
                    {
                        try { handlers[h](evt); }
                        catch (Exception ex) { bus.ReportHandlerFailure(ex); }
                    }
                }
                _delivering.Clear();
            }
        }

        private readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>();
        private readonly List<IChannel> _channelOrder = new List<IChannel>();

        /// <summary>
        /// Wird bei einer Exception in einem Handler aufgerufen; die übrigen Handler laufen weiter.
        /// Ohne Abonnent wird die Exception nach dem Flush erneut geworfen, damit Fehler nicht verschwinden.
        /// </summary>
        public event Action<Exception> HandlerFailed;

        private List<Exception> _unreported;

        public int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _channelOrder.Count; i++) n += _channelOrder[i].PendingCount;
                return n;
            }
        }

        public void Publish<T>(in T evt) where T : struct, ISimEvent => GetChannel<T>().Add(evt);

        public void Subscribe<T>(Action<T> handler) where T : struct, ISimEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            GetChannel<T>().Subscribe(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, ISimEvent
        {
            if (handler == null) return;
            if (_channels.TryGetValue(typeof(T), out var ch)) ((Channel<T>)ch).Unsubscribe(handler);
        }

        public void Flush()
        {
            // Erst alle Kanäle einfrieren, dann ausliefern: Was Handler jetzt veröffentlichen, kommt beim nächsten Flush.
            int count = _channelOrder.Count;
            for (int i = 0; i < count; i++) _channelOrder[i].BeginFlush();
            for (int i = 0; i < count; i++) _channelOrder[i].Deliver(this);

            if (_unreported != null)
            {
                var errors = _unreported;
                _unreported = null;
                throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
            }
        }

        /// <summary>Verwirft alle noch nicht ausgelieferten Events (z. B. beim Ersetzen der Simulation nach dem Laden).</summary>
        public void ClearPending()
        {
            for (int i = 0; i < _channelOrder.Count; i++) _channelOrder[i].ClearPending();
        }

        private Channel<T> GetChannel<T>() where T : struct, ISimEvent
        {
            if (_channels.TryGetValue(typeof(T), out var ch)) return (Channel<T>)ch;
            var created = new Channel<T>();
            _channels.Add(typeof(T), created);
            _channelOrder.Add(created);
            return created;
        }

        private void ReportHandlerFailure(Exception ex)
        {
            var listener = HandlerFailed;
            if (listener != null) listener(ex);
            else (_unreported ??= new List<Exception>()).Add(ex);
        }
    }
}
