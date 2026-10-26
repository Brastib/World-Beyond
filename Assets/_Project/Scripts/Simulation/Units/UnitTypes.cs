using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Units
{
    // Explizite Werte, nur anhängen. Grobe Rolle; die feine Militärrolle steht in UnitTypeData.MilitaryRole.
    public enum UnitRole : byte
    {
        Civilian = 0,
        Worker = 1,
        Settler = 2,
        Infantry = 3,
        Vehicle = 4,
        Artillery = 5,
        RocketArtillery = 6,
        Trader = 7,
    }

    /// <summary>Laufzeit-ID eines Einheitentyps (aus der ContentRegistry). Nie speichern, im Spielstand steht der Key.</summary>
    public readonly struct UnitTypeId : IEquatable<UnitTypeId>
    {
        public readonly int Value;
        public UnitTypeId(int value) => Value = value;
        public bool IsValid => Value > 0;
        public bool Equals(UnitTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is UnitTypeId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => $"UnitType({Value})";
    }

    /// <summary>
    /// Reine Daten eines Einheitentyps. Zivil-Typen trägt Civ ein, Militär-Typen Military (INTEGRATION.md §3).
    /// Neue Felder nur nach Absprache mit dem Integrations-Thread.
    /// </summary>
    public sealed class UnitTypeData
    {
        public string Key;                 // stabiler Schlüssel, z. B. "unit.settler", "unit.humvee_mg"
        public string DisplayName;
        public UnitRole Role;
        public byte MilitaryRole;          // Wert aus Military-Enum MilitaryRole (Besitzer Military); 0 = keine
        public MovementClass Movement;
        public float MaxHp = 100f;
        public float MaxSpeed = 1.5f;      // m/s
        public float TurnRateDeg = 360f;   // Grad/s, von MovementSystem (World) genutzt
        public float Acceleration = 10f;   // m/s², von MovementSystem (World) genutzt
        public float Radius = 0.4f;        // m, für Auswahl und Kollision
    }

    /// <summary>Einzige Tabelle aller Einheitentypen. Registrierung nur beim Start.</summary>
    public sealed class UnitTypeRegistry
    {
        private readonly ContentRegistry _content;
        private readonly List<UnitTypeData> _byId = new List<UnitTypeData> { null };

        public UnitTypeRegistry(ContentRegistry content) => _content = content ?? throw new ArgumentNullException(nameof(content));

        public UnitTypeId Register(UnitTypeData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.MaxHp <= 0f) throw new ArgumentException($"Einheitentyp '{data.Key}': MaxHp muss > 0 sein.");
            if (data.MaxSpeed < 0f) throw new ArgumentException($"Einheitentyp '{data.Key}': MaxSpeed darf nicht negativ sein.");
            int id = _content.Register(ContentKind.Unit, data.Key);
            while (_byId.Count <= id) _byId.Add(null);
            _byId[id] = data;
            return new UnitTypeId(id);
        }

        public bool TryGet(UnitTypeId id, out UnitTypeData data)
        {
            data = id.Value > 0 && id.Value < _byId.Count ? _byId[id.Value] : null;
            return data != null;
        }

        public UnitTypeData Get(UnitTypeId id)
        {
            if (TryGet(id, out var d)) return d;
            throw new ArgumentException($"Unbekannter Einheitentyp {id}.");
        }

        public bool TryGetByKey(string key, out UnitTypeId id)
        {
            if (_content.TryGetId(ContentKind.Unit, key, out int v) && v < _byId.Count && _byId[v] != null)
            {
                id = new UnitTypeId(v);
                return true;
            }
            id = default;
            return false;
        }

        public string KeyOf(UnitTypeId id) => TryGet(id, out var d) ? d.Key : null;

        public int Count => _byId.Count - 1;
    }
}
