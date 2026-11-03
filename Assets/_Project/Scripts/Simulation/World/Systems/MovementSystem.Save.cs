using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Simulation.World
{
    // Spielstand der Bewegung (M1.6: nach dem Laden identisch weiterrechnen). Gespeichert werden je Agent Ziel,
    // Wegpunkte und Fortschritt sowie die Pfadanfragen in Abarbeitungsreihenfolge samt Fortschritt der aktiven
    // A*-Suche. Beim Laden wird die aktive Suche bis zu diesem Stand deterministisch nachgerechnet.
    // Der Raumindex wird nicht gespeichert; er wird im ersten Tick vollständig aufgebaut und ist reihenfolgeunabhängig.
    public sealed partial class MovementSystem : ISaveable, ISaveReferenceCheck
    {
        public const string Key = "movement";

        public string SaveKey => Key;
        public int LoadOrder => 20;   // nach world (0) und units (10)
        public int SectionVersion => 1;
        public Type StateType => typeof(MovementState);

        readonly List<int> _queueIds = new List<int>();

        public object CaptureState()
        {
            _pathfinder.GetQueueOrder(_queueIds, out int activeExpansions);
            var state = new MovementState
            {
                ActiveExpansions = activeExpansions,
                Requests = new PathRequestRecord[_queueIds.Count],
            };
            var queuePos = new Dictionary<int, int>(_queueIds.Count);
            for (int i = 0; i < _queueIds.Count; i++)
            {
                _pathfinder.TryGetRequest(_queueIds[i], out int fx, out int fy, out int tx, out int ty, out MovementClass mc);
                state.Requests[i] = new PathRequestRecord { FromX = fx, FromY = fy, ToX = tx, ToY = ty, Movement = mc.ToString() };
                queuePos[_queueIds[i]] = i;
            }

            // Feste Reihenfolge nach Einheiten-Index, unabhängig von der Dictionary-Reihenfolge.
            var keys = new List<int>(_agents.Keys);
            keys.Sort();
            var agents = new List<MovementAgentRecord>(keys.Count);
            foreach (int key in keys)
            {
                Agent a = _agents[key];
                var rec = new MovementAgentRecord
                {
                    UnitIndex = a.Id.Index,
                    UnitGeneration = a.Id.Generation,
                    TargetX = a.TargetX,
                    TargetZ = a.TargetZ,
                    HasRequest = a.RequestId != 0,
                    QueueIndex = a.Pending && queuePos.TryGetValue(a.RequestId, out int qi) ? qi : -1,
                    Next = a.Next,
                    Waypoints = new int[a.Waypoints.Count * 2],
                };
                for (int i = 0; i < a.Waypoints.Count; i++)
                {
                    rec.Waypoints[i * 2] = a.Waypoints[i].x;
                    rec.Waypoints[i * 2 + 1] = a.Waypoints[i].y;
                }
                agents.Add(rec);
            }
            state.Agents = agents.ToArray();
            return state;
        }

        public bool TryRestoreState(object dto, out string error)
        {
            error = null;
            if (!(dto is MovementState state))
            {
                error = "Bewegung: falscher Datentyp.";
                return false;
            }
            var queue = new List<(int, int, int, int, MovementClass)>();
            foreach (PathRequestRecord r in state.Requests ?? new PathRequestRecord[0])
            {
                if (r == null || !Enum.TryParse(r.Movement, out MovementClass mc) || !Enum.IsDefined(typeof(MovementClass), mc))
                {
                    error = $"Bewegung: unbekannte Bewegungsart '{r?.Movement}'.";
                    return false;
                }
                if (!_world.InBounds(r.FromX, r.FromY) || !_world.InBounds(r.ToX, r.ToY))
                {
                    error = "Bewegung: Pfadanfrage außerhalb der Welt.";
                    return false;
                }
                queue.Add((r.FromX, r.FromY, r.ToX, r.ToY, mc));
            }
            var restored = new Dictionary<int, Agent>();
            foreach (MovementAgentRecord rec in state.Agents ?? new MovementAgentRecord[0])
            {
                if (rec == null || rec.Waypoints == null || rec.Waypoints.Length % 2 != 0
                    || rec.QueueIndex >= queue.Count || rec.QueueIndex < -1 || restored.ContainsKey(rec.UnitIndex))
                {
                    error = "Bewegung: Agentendaten beschädigt.";
                    return false;
                }
                var a = new Agent
                {
                    Id = new EntityId(rec.UnitIndex, rec.UnitGeneration),
                    TargetX = rec.TargetX,
                    TargetZ = rec.TargetZ,
                    Next = rec.Next,
                    NavVersion = _world.NavigationVersion,
                    Pending = rec.QueueIndex >= 0,
                    RequestId = rec.QueueIndex >= 0 ? -2 - rec.QueueIndex : (rec.HasRequest ? -1 : 0),
                };
                for (int i = 0; i < rec.Waypoints.Length; i += 2)
                {
                    if (!_world.InBounds(rec.Waypoints[i], rec.Waypoints[i + 1]))
                    {
                        error = "Bewegung: Wegpunkt außerhalb der Welt.";
                        return false;
                    }
                    a.Waypoints.Add((rec.Waypoints[i], rec.Waypoints[i + 1]));
                }
                restored[rec.UnitIndex] = a;
            }

            var newIds = new List<int>();
            _pathfinder.RestoreQueue(queue, Math.Max(0, state.ActiveExpansions), newIds);
            _agents.Clear();
            foreach (var kv in restored)
            {
                Agent a = kv.Value;
                if (a.Pending)
                    a.RequestId = newIds[-2 - a.RequestId];
                _agents[kv.Key] = a;
            }
            _spatial.Clear();
            return true;
        }

        public void CheckReferences(SimContext ctx, LoadReport report)
        {
            if (ctx == null)
                return;
            _removeKeys.Clear();
            foreach (var kv in _agents)
            {
                if (!ctx.Units.Exists(kv.Value.Id))
                    _removeKeys.Add(kv.Key);
            }
            _removeKeys.Sort();
            foreach (int key in _removeKeys)
            {
                Agent a = _agents[key];
                if (a.Pending)
                    _pathfinder.Cancel(a.RequestId);
                _agents.Remove(key);
                report?.Warnings.Add($"Bewegung: Agent für nicht vorhandene Einheit {a.Id} entfernt.");
            }
        }
    }

    [Serializable]
    public sealed class MovementState
    {
        public int ActiveExpansions;
        public PathRequestRecord[] Requests = new PathRequestRecord[0];
        public MovementAgentRecord[] Agents = new MovementAgentRecord[0];
    }

    [Serializable]
    public sealed class PathRequestRecord
    {
        public int FromX, FromY, ToX, ToY;
        public string Movement;   // Name der MovementClass
    }

    [Serializable]
    public sealed class MovementAgentRecord
    {
        public int UnitIndex, UnitGeneration;
        public float TargetX, TargetZ;
        public bool HasRequest;
        public int QueueIndex;    // Position in Requests, -1 = keine wartende Anfrage
        public int Next;
        public int[] Waypoints = new int[0];   // x0, y0, x1, y1 …
    }
}
