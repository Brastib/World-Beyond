using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Simulation.World
{
    public struct UnitArrivedEvent : ISimEvent
    {
        public EntityId Unit;
        public float X, Z;
    }

    // Kein Weg zum Ziel (Ziel unerreichbar, Wasser, anderes Landstück). Das Bewegungsziel wurde gelöscht.
    public struct UnitMoveFailedEvent : ISimEvent
    {
        public EntityId Unit;
        public float TargetX, TargetZ;
        public PathStatus Reason;
    }

    // Bewegt alle Einheiten mit Bewegungsziel (IUnitStore.SetMoveTarget) über das Gelände.
    // Ablauf je Einheit: Pfad anfordern (Budget pro Tick) → Wegpunkte mit MotionIntegrator abfahren
    // (Wenderate, Beschleunigung, Geländekosten) → am Ziel Bewegungsziel löschen und UnitArrivedEvent senden.
    // Hält den SpatialHash für alle lebenden Einheiten aktuell. Agenten (Ziel, Wegpunkte) und die Pfadanfragen-
    // Warteschlange werden gespeichert (MovementSystem.Save.cs), damit die Simulation nach dem Laden identisch weiterläuft (M1.6).
    public sealed partial class MovementSystem : ISimulationSystem
    {
        // ~1 µs pro Expansion (.NET 8, Cloud); in Unity (Mono/IL2CPP) nicht gemessen. 6000 hält den Tick auch bei vielen
        // gleichzeitigen Anfragen im einstelligen Millisekundenbereich; längere Warteschlangen verteilen sich auf mehrere Ticks.
        public const int DefaultPathBudget = 6000;
        const float SeparationStrength = 0.5f;

        sealed class Agent
        {
            public EntityId Id;
            public float TargetX, TargetZ;
            public int RequestId;
            public bool Pending;
            public readonly List<(int x, int y)> Waypoints = new List<(int x, int y)>();
            public int Next;
            public int NavVersion;
            public long SeenTick;
        }

        readonly IWorldGrid _world;
        readonly GridPathfinder _pathfinder;
        readonly SpatialHash _spatial;
        readonly Dictionary<int, Agent> _agents = new Dictionary<int, Agent>();
        readonly List<EntityId> _ids = new List<EntityId>();
        readonly List<EntityId> _tracked = new List<EntityId>();
        readonly List<EntityId> _neighbors = new List<EntityId>();
        readonly List<int> _removeKeys = new List<int>();

        public int PathBudgetPerTick { get; set; } = DefaultPathBudget;

        public MovementSystem(IWorldGrid world, GridPathfinder pathfinder, SpatialHash spatial)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        }

        public string Name => "Movement";
        public int Order => SystemOrder.Movement;
        public int TickInterval => 1;

        public int ActiveAgentCount => _agents.Count;

        public void Tick(SimContext ctx)
        {
            IUnitStore units = ctx.Units;
            float dt = ctx.Clock.DeltaSeconds;
            long tick = ctx.Clock.Tick;

            _pathfinder.ProcessRequests(PathBudgetPerTick);

            // Tote Einheiten aus dem Raumindex entfernen.
            foreach (EntityId id in _tracked)
            {
                if (!units.Exists(id))
                    _spatial.Remove(id);
            }

            units.CopyAliveIds(_ids);
            _tracked.Clear();
            // Erst alle neuen Einheiten eintragen, damit die Trennung im selben Tick alle Nachbarn sieht
            // (auch direkt nach dem Laden, wenn der Raumindex leer ist).
            foreach (EntityId id in _ids)
            {
                if (!_spatial.Contains(id) && units.TryGet(id, out UnitView v0))
                    _spatial.Insert(id, v0.X, v0.Z);
            }
            foreach (EntityId id in _ids)
            {
                if (!units.TryGet(id, out UnitView view))
                    continue;
                _tracked.Add(id);

                if (!view.HasMoveTarget || view.State == UnitState.Dead)
                {
                    _spatial.Move(id, view.X, view.Z);
                    continue;
                }
                if (!ctx.UnitTypes.TryGet(view.Type, out UnitTypeData type))
                    continue;

                Agent agent = GetAgent(id);
                agent.SeenTick = tick;
                UpdateAgent(ctx, units, agent, in view, type, dt);
            }

            // Agenten von Einheiten ohne Ziel oder ohne Leben aufräumen.
            _removeKeys.Clear();
            foreach (var kv in _agents)
            {
                if (kv.Value.SeenTick != tick)
                    _removeKeys.Add(kv.Key);
            }
            foreach (int key in _removeKeys)
            {
                Agent a = _agents[key];
                if (a.Pending)
                    _pathfinder.Cancel(a.RequestId);
                _agents.Remove(key);
            }
        }

        Agent GetAgent(EntityId id)
        {
            if (_agents.TryGetValue(id.Index, out Agent a) && a.Id.Equals(id))
                return a;
            if (a != null && a.Pending)
                _pathfinder.Cancel(a.RequestId);
            a = new Agent { Id = id, RequestId = 0 };
            _agents[id.Index] = a;
            return a;
        }

        void UpdateAgent(SimContext ctx, IUnitStore units, Agent agent, in UnitView view, UnitTypeData type, float dt)
        {
            MovementClass mc = type.Movement;
            bool newTarget = agent.RequestId == 0 || agent.TargetX != view.MoveTargetX || agent.TargetZ != view.MoveTargetZ;
            bool pathBlocked = !newTarget && !agent.Pending && agent.NavVersion != _world.NavigationVersion && !RemainingPathWalkable(agent, mc);
            if (newTarget || pathBlocked)
            {
                if (agent.Pending)
                    _pathfinder.Cancel(agent.RequestId);
                agent.TargetX = view.MoveTargetX;
                agent.TargetZ = view.MoveTargetZ;
                var (fx, fy) = WorldCoords.WorldToTile(view.X, view.Z);
                var (tx, ty) = WorldCoords.WorldToTile(view.MoveTargetX, view.MoveTargetZ);
                agent.RequestId = _pathfinder.RequestPath(Clamp(fx, _world.Width), Clamp(fy, _world.Height),
                    Clamp(tx, _world.Width), Clamp(ty, _world.Height), mc);
                agent.Pending = true;
                agent.Waypoints.Clear();
                if (view.State == UnitState.Idle)
                    units.SetState(view.Id, UnitState.Moving);
            }

            if (agent.Pending)
            {
                PathStatus status = _pathfinder.TryGetPath(agent.RequestId, agent.Waypoints);
                if (status == PathStatus.Pending)
                    return;
                agent.Pending = false;
                if (status != PathStatus.Found)
                {
                    Fail(ctx, units, agent, in view, status);
                    return;
                }
                agent.NavVersion = _world.NavigationVersion;
                // Wegpunkt 0 ist die Startkachel; direkt zum nächsten fahren.
                agent.Next = agent.Waypoints.Count > 1 ? 1 : 0;
            }

            var state = new MotionState
            {
                X = view.X,
                Z = view.Z,
                Heading = view.Heading * (MathF.PI / 180f),
                Speed = view.Speed,
            };
            var p = new MotionParams
            {
                MovementClass = mc,
                Speed = type.MaxSpeed,
                TurnRateDeg = type.TurnRateDeg,
                Acceleration = type.Acceleration,
            };

            bool final = agent.Next >= agent.Waypoints.Count - 1;
            float wx, wz;
            if (final)
            {
                FinalPoint(agent, out wx, out wz);
            }
            else
            {
                var wp = agent.Waypoints[agent.Next];
                wx = WorldCoords.TileCenterX(wp.x);
                wz = WorldCoords.TileCenterZ(wp.y);
            }

            bool reached = MotionIntegrator.Step(ref state, in p, _world, wx, wz, final, dt);
            if (mc == MovementClass.Foot)
                ApplySeparation(ref state, view.Id, type.Radius, mc);

            var clamped = WorldCoords.ClampToBounds(new System.Numerics.Vector3(state.X, 0f, state.Z), _world.Width, _world.Height);
            state.X = clamped.X;
            state.Z = clamped.Z;
            units.SetPosition(view.Id, state.X, state.Z, state.Heading * (180f / MathF.PI), state.Speed);
            _spatial.Move(view.Id, state.X, state.Z);

            if (!reached)
                return;
            if (!final)
            {
                agent.Next++;
                return;
            }

            units.ClearMoveTarget(view.Id);
            if (view.State == UnitState.Moving || view.State == UnitState.Idle)
                units.SetState(view.Id, UnitState.Idle);
            ctx.Events.Publish(new UnitArrivedEvent { Unit = view.Id, X = state.X, Z = state.Z });
            agent.SeenTick = -1;
        }

        void Fail(SimContext ctx, IUnitStore units, Agent agent, in UnitView view, PathStatus reason)
        {
            units.ClearMoveTarget(view.Id);
            if (view.State == UnitState.Moving)
                units.SetState(view.Id, UnitState.Idle);
            ctx.Events.Publish(new UnitMoveFailedEvent { Unit = view.Id, TargetX = agent.TargetX, TargetZ = agent.TargetZ, Reason = reason });
            agent.SeenTick = -1;
        }

        // Endpunkt: das Bewegungsziel; wurde das Ziel auf eine Nachbarkachel verlegt (Ziel blockiert, z. B. Gebäude),
        // der dem Ziel nächstgelegene Punkt dieser Kachel.
        static void FinalPoint(Agent agent, out float x, out float z)
        {
            x = agent.TargetX;
            z = agent.TargetZ;
            if (agent.Waypoints.Count == 0)
                return;
            var last = agent.Waypoints[agent.Waypoints.Count - 1];
            var (tx, ty) = WorldCoords.WorldToTile(x, z);
            if (tx == last.x && ty == last.y)
                return;
            const float inset = 0.3f;
            float minX = last.x * WorldConstants.TileSize + inset, maxX = (last.x + 1) * WorldConstants.TileSize - inset;
            float minZ = last.y * WorldConstants.TileSize + inset, maxZ = (last.y + 1) * WorldConstants.TileSize - inset;
            x = x < minX ? minX : (x > maxX ? maxX : x);
            z = z < minZ ? minZ : (z > maxZ ? maxZ : z);
        }

        bool RemainingPathWalkable(Agent agent, MovementClass mc)
        {
            for (int i = Math.Max(1, agent.Next); i < agent.Waypoints.Count; i++)
            {
                var wp = agent.Waypoints[i];
                if (!_world.IsWalkable(wp.x, wp.y, mc))
                    return false;
            }
            agent.NavVersion = _world.NavigationVersion;
            return true;
        }

        // Fußtruppen drücken sich leicht auseinander, statt sich auf einem Punkt zu stapeln.
        void ApplySeparation(ref MotionState s, EntityId self, float radius, MovementClass mc)
        {
            float range = Math.Max(0.2f, radius) * 2f;
            _spatial.QueryRadius(s.X, s.Z, range, _neighbors);
            // Feste Reihenfolge, unabhängig von der Einfügereihenfolge im Raumindex (Gleitkomma-Summe deterministisch).
            _neighbors.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Generation.CompareTo(b.Generation));
            float pushX = 0f, pushZ = 0f;
            foreach (EntityId other in _neighbors)
            {
                if (other.Equals(self))
                    continue;
                // Position aus dem Raumindex genügt nicht (nur Zellen), daher kleiner Umweg über die Distanz zur eigenen Position.
                if (!TryGetIndexedPosition(other, out float ox, out float oz))
                    continue;
                float dx = s.X - ox, dz = s.Z - oz;
                float d2 = dx * dx + dz * dz;
                if (d2 >= range * range)
                    continue;
                float d = MathF.Sqrt(d2);
                if (d < 1e-4f)
                {
                    // Exakt gleiche Position: deterministisch nach Index auseinander schieben.
                    dx = self.Index < other.Index ? 1f : -1f;
                    dz = 0f;
                    d = 1f;
                }
                float overlap = (range - d) / range;
                pushX += dx / d * overlap;
                pushZ += dz / d * overlap;
            }
            if (pushX == 0f && pushZ == 0f)
                return;
            float nx = s.X + pushX * SeparationStrength * radius;
            float nz = s.Z + pushZ * SeparationStrength * radius;
            var (tx, ty) = WorldCoords.WorldToTile(nx, nz);
            if (_world.IsWalkable(tx, ty, mc))
            {
                s.X = nx;
                s.Z = nz;
            }
        }

        bool TryGetIndexedPosition(EntityId id, out float x, out float z) => _spatial.TryGetPosition(id, out x, out z);

        static int Clamp(int v, int size) => v < 0 ? 0 : (v >= size ? size - 1 : v);
    }
}
