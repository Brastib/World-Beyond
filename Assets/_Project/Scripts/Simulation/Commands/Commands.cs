using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Commands
{
    /// <summary>
    /// Einziger Weg, wie Spieler, God-Tools und UI den Zustand ändern (ARCHITECTURE §9).
    /// Jedes Fachsystem definiert seine eigenen Commands.
    /// </summary>
    public interface IWorldCommand
    {
        // Prüft gegen aktuellen Zustand; false = abgelehnt mit Grund (für UI-Fehlermeldung).
        bool Validate(SimContext ctx, out string reason);
        void Execute(SimContext ctx);
    }

    public interface ICommandQueue
    {
        void Enqueue(IWorldCommand command);   // nur Main Thread
        int PendingCount { get; }
    }

    /// <summary>Wird veröffentlicht, wenn Validate false liefert oder Execute eine Exception wirft.</summary>
    public struct CommandRejectedEvent : ISimEvent
    {
        public string CommandName;
        public string Reason;
    }

    public struct CommandExecutedEvent : ISimEvent
    {
        public string CommandName;
    }

    public sealed class CommandQueue : ICommandQueue
    {
        private List<IWorldCommand> _pending = new List<IWorldCommand>();
        private List<IWorldCommand> _processing = new List<IWorldCommand>();

        public int PendingCount => _pending.Count;

        public void Enqueue(IWorldCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            _pending.Add(command);
        }

        /// <summary>Übergibt alle bis jetzt eingereihten Commands. Während der Ausführung eingereihte laufen im nächsten Tick.</summary>
        internal List<IWorldCommand> TakeAll()
        {
            var swap = _processing;
            swap.Clear();
            _processing = _pending;
            _pending = swap;
            return _processing;
        }

        public void Clear() => _pending.Clear();
    }

    /// <summary>Spielt Commands an der Tick-Grenze ein (Order 0).</summary>
    public sealed class CommandProcessingSystem : ISimulationSystem
    {
        private readonly CommandQueue _queue;

        public CommandProcessingSystem(CommandQueue queue) => _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        public string Name => "Commands";
        public int Order => SystemOrder.Commands;
        public int TickInterval => 1;

        public void Tick(SimContext ctx)
        {
            var batch = _queue.TakeAll();
            for (int i = 0; i < batch.Count; i++)
            {
                var cmd = batch[i];
                string name = cmd.GetType().Name;
                bool valid;
                string reason;
                try { valid = cmd.Validate(ctx, out reason); }
                catch (Exception ex)
                {
                    ctx.Events.Publish(new CommandRejectedEvent { CommandName = name, Reason = "Fehler bei Prüfung: " + ex.Message });
                    continue;
                }
                if (!valid)
                {
                    ctx.Events.Publish(new CommandRejectedEvent { CommandName = name, Reason = reason ?? "abgelehnt" });
                    continue;
                }
                try
                {
                    cmd.Execute(ctx);
                    ctx.Events.Publish(new CommandExecutedEvent { CommandName = name });
                }
                catch (Exception ex)
                {
                    ctx.Events.Publish(new CommandRejectedEvent { CommandName = name, Reason = "Fehler bei Ausführung: " + ex.Message });
                }
            }
            batch.Clear();
        }
    }
}
