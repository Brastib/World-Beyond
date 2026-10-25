# Phase 5 – Modernes Militärsystem (Entwurf)

Stand: 2026-10-25. Status: **Entwurf, kein Code vorhanden, nichts kompiliert oder getestet.**
Baut auf ARCHITECTURE §4/§7, SYSTEM_INTERFACES §6/§9/§10, INTEGRATION §3 und PHASE4_DIPLOMACY_WAR_DESIGN auf. Legt keine zweiten Stores, Bewegungs- oder Pfadsysteme an.

## 1. Zuständigkeit (nach INTEGRATION §3)

| Military besitzt | Military nutzt (fremd) |
|---|---|
| Militär-Einträge in der gemeinsamen `UnitTypeData`-Tabelle, `WeaponData` | `UnitStore` (Integration) – kein eigener Store |
| `ICombatService` (einziger Weg, HP zu senken), `CombatSystem` (taktisch, jeden Tick) | `MovementSystem`, `IPathfinder`, `ISpatialIndex` (World) |
| `IProjectileSystem` + `ProjectileStore` (Geschosse, Raketen, Granaten) | `IEconomy.TrySpend`, `ITechTree.IsUnlocked`, Gebäude Kaserne/Fabrik (Civ) |
| `IArmyRegistry`, `IArmyAI` (operativ, ~1 s) | `IWarQuery` Kriegsziele + Zielprioritäten (War) |
| Militär-Typen + Produktionsgebäude als Daten; `IMilitaryQuery` (Kampfstärke) | `IRecruitment` (Civ, einziger Produktionsweg für alle Einheiten), `ISelectionService`, HUD, `IVfxService`, `IAudioService` (UI) |

## 2. Einheiten als Daten (erweiterbar)

Jede Einheit = ein `UnitTypeData`-Eintrag (ScriptableObject → reine Sim-Kopie). Neue Einheit = neuer Datensatz, kein neuer Code, solange vorhandene Verhaltensbausteine reichen.

Militär-Felder: `Role`, `MovementClass` (Foot/Wheeled/Tracked), `MaxHp`, `Armor[DamageType]`, `Speed`, `TurnRate`, `Acceleration`, `SightRange`, `Weapons[]` (Slots mit Turm/Rumpf-Montage), `TransportCapacity`, `SupplyRadius`, `Cost`, `BuildTime`, `RequiredTech`, `RequiredBuilding`, `CombatStrength` (für KI-Bewertung), `ModelKey` (Presentation), `Abilities` (Flags).

| # | Typ (Startset) | Bewegung | Rolle / Besonderheit im Spiel |
|---|---|---|---|
| 1 | Infanterie (Schütze) | Foot | billig, Deckungsbonus im Wald, kann in Transporter einsteigen |
| 1b | Infanterie (Panzerabwehr) | Foot | langsam, Rakete mit ArmorPiercing, schwach gegen Infanterie |
| 2 | Aufklärungsfahrzeug | Wheeled | sehr schnell, große Sichtweite, schwach bewaffnet |
| 3 | Humvee (Transport) | Wheeled | trägt 4 Infanterie, unbewaffnet, schnell auf Straße |
| 4 | Humvee mit MG | Wheeled | Dach-MG (HeavyMG) dreht unabhängig, stark gegen Infanterie |
| 5 | Leichter Schützenwagen | Wheeled | Maschinenkanone, trägt 2 Infanterie |
| 6 | Kampfpanzer | Tracked | Turm mit Kanone (ArmorPiercing), Koaxial-MG, hohe Frontpanzerung, langsam am Hang |
| 7 | Artillerie | Tracked | indirektes Feuer, Mindestreichweite, braucht Sicht durch Verbündete, Aufbauzeit |
| 8 | Raketenwerfer-Fahrzeug | Wheeled | Salve, langes Nachladen, Flächenschaden |
| 9 | Versorgungs-LKW | Wheeled | füllt Munition im Radius auf, repariert langsam |
| 10 | weitere über Daten | – | z. B. Flugabwehr, Sanitäter, Pionier |

Verhalten unterscheidet sich tatsächlich über: Bewegungsklasse (Gelände-Kosten verschieden, Raupen langsamer am Hang aber sicher im Wald, Räder schnell auf Straße), Panzerungs-Tabelle, Waffenart (direkt/indirekt), Turm vs. feste Waffe, Aufbau/Abbau, Transport, Versorgung.

## 3. Kampf

- **Zielerfassung** (CombatSystem, jeden Tick, gestaffelt): `ISpatialIndex`-Abfrage im Sichtradius → Bewertung (Bedrohung, Wirksamkeit der eigenen Waffe gegen Panzerung, Entfernung, Befehlsziel hat Vorrang) → Sichtlinie über Kachelhöhen (Bresenham-Strahl, Wald dämpft).
- **Feuern**: Waffe hat `Range`, `MinRange`, `FireInterval`, `MagazineSize`, `ReloadTime`, `AmmoReserve`, `Spread`, `ProjectileKind`. Ohne Munition: Einheit meldet „braucht Versorgung“.
- **Geschosse** (`ProjectileStore`, Arrays + Pool): Hitscan (Kleinwaffen, sofort, aber mit Leuchtspur-Event), Ballistic (Panzergranate, Schwerkraft), ArtilleryShell (hohe Bahn, Streuung), Rocket (eigener Datensatz, s. §4). Treffer: Segment-Kugel-Test gegen Einheiten aus SpatialIndex + Höhenvergleich mit Terrain.
- **Schaden**: `ICombatService.ApplyDamage` → `effektiv = amount × (1 − Armor[type])`, Flächenschaden mit linearem Abfall; HP ≤ 0 → `UnitStore.Despawn` → `UnitDiedEvent`, Wrack als kurzlebiges Effekt-Objekt.

## 4. Raketen (abstrakte Spielwerte)

Rakete = Projektil mit Zustand: Startphase → Steigflug → Marsch → Endanflug. Werte: `Speed`, `TurnRate`, `Fuel`, `Guidance` (None/Target/Position), `WarheadDamage`, `BlastRadius`, `DamageType`. Rollen: ungelenkte Artillerierakete (Flächenschaden), gelenkte Panzerabwehrrakete (Einzelziel, ArmorPiercing). Bewusst nur Spielwerte, keine realen Einsatzdaten. Explosion → `ExplosionEvent` → gepoolte Partikelsysteme (Blitz, Feuerball, Rauch, Trümmer) + Krater-Flag auf der Kachel.

## 5. Befehle und Steuerung

`IWorldCommand`s (über die eine `ICommandQueue`): `RecruitCommand`, `MoveUnitsCommand` (Wegpunkte, Formation), `AttackTargetCommand`, `AttackMoveCommand`, `DefendAreaCommand`, `RetreatCommand`, `SetStanceCommand` (Aggressiv/Halten/Feuer zurück), `LoadUnloadCommand`. Gleicher Weg für Spieler, KI und God-Modus. Auswahl einzeln/Gruppe/Box über `ISelectionService` (UI).

## 6. Vorschlag für neue/erweiterte Schnittstellen

```csharp
namespace WorldBeyond.Simulation.Military
{
    // IRecruitment wird von Civ implementiert (INTEGRATION.md §5); Military ruft nur Request aus IArmyAI auf
    // und baut KEINE eigene Rekrutierung.
    public interface IMilitaryQuery
    {
        float GetCombatStrength(UnitTypeId type);   // für War-Bewertung
        float GetCivCombatStrength(int civId);      // Summe lebender Einheiten
    }
    // IArmyRegistry (Stand SYSTEM_INTERFACES, Phase 1): int-Armee-IDs mit OrderAttackSettlement/OrderDefend/OrderRetreat.
    // Zusätzlich benötigt: SetStance(army, stance).
}
```

**Festgelegt mit Integration (INTEGRATION.md §5, 2026-10-25):**
- `UnitDiedEvent` bekommt `CivId`, `KillerCivId`, `UnitType` (kein separates UnitKilled-Event).
- `UnitRole` bleibt grob; `UnitTypeData` erhält Feld `MilitaryRole` (Enum gehört Military: Rifleman, AntiTank, Recon, Transport, LightArmed, IFV, MainBattleTank, Artillery, RocketArtillery, Support, …). `IRecruitment.Request` nimmt dann `MilitaryRole` statt `UnitRole`.
- Korrigiert 21:01: `IRecruitment` implementiert Civ für alle Einheiten (Kosten, Bevölkerung, Tech, Spawn über `UnitStore`). Military liefert Militär-Typen und Produktionsgebäude (Kaserne, Fahrzeugfabrik) als Daten und ruft `Request` aus `IArmyAI` auf.
- Bewegungsparameter (Geländekosten pro MovementClass, Wenderate, Beschleunigung) stehen in `UnitTypeData`, ausgewertet von World im `MovementSystem`.

## 6b. Speichern und IDs (verbindlich nach ARCHITECTURE §11, SYSTEM_INTERFACES §1/§11)

- Geschosse/Raketen: `EntityId` (Index + Generation) über einen `EntityStore`; Generationen und Freiliste werden gespeichert.
- Armee-IDs aus dem `SequentialIdAllocator`, nie wiederverwendet.
- Einheiten- und Waffentypen im Save nur als String-Schlüssel über die `ContentRegistry` (z. B. `unit.humvee_mg`, `weapon.mg_heavy`, `projectile.rocket_at`).
- Aufträge als Daten (Typ + Ziel-ID oder Position), keine Objektreferenzen.
- Enums (`DamageType`, `ProjectileKind`, `MilitaryRole`, `Stance`, Auftragstyp) mit expliziten numerischen Werten.
- `ISaveReferenceCheck`: Auftrag auf tote/ungültige Ziel-ID → Idle; leere Armee → auflösen; Geschoss mit ungültigem Lenkziel → ungelenkt weiter bis Treibstoffende.
- Keine Abweichungen geplant.

## 7. Abnahmetest (Plan, nur auf dem PC ausführbar)

PlayMode-Szene auf generierter Karte: Civ A (Infanterie, Humvee mit 4 Infanterie, Humvee-MG, Panzer, Raketenwerfer) gegen Civ B (Infanterie, Panzer). Prüfungen: Humvee fährt Pfad über Hügel/um Wasser; Infanterie steigt ein/aus; MG tötet Infanterie; Panzergranate trifft und senkt HP mit Panzerung; Rakete fliegt, trifft, Flächenschaden; zerstörte Einheit verschwindet, `UnitDiedEvent` mit Civ; IArmyAI greift nach `WarDeclaredEvent` an. Zusätzlich EditMode-Tests für Schaden, Ballistik, Raketenlenkung, Rekrutierungskosten.
