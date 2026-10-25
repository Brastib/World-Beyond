# Phase 5 – Militär-Assets: Recherche (Stand 2026-10-25)

**Nichts davon ist heruntergeladen oder in Unity importiert.** Die Cloud-Umgebung blockiert Direkt-Downloads (Proxy 403), Import ist nur auf dem PC des Nutzers möglich. "Lizenz geprüft" heißt: auf der Quellseite gelesen. Vorschaubilder wurden NICHT visuell geprüft. Nach dem Import kommen alle Einträge nach `Assets/ThirdParty/ASSET_CREDITS.md`.

Stilziel laut UI_STYLE_GUIDE Teil A: stilisierter Realismus, Low/Mid-Poly, realistische Proportionen, Oliv/Sand/Grau, Civ-Farbe nur als Akzent. Budget Fahrzeug ≤ 5k Tris, Soldat ≤ 1,5k; Texturen für Einheiten/Fahrzeuge bis 1K (PROJECT_MASTER §8.2).

## Hauptkandidat Fahrzeuge: ein Set, ein Autor

| Asset | Quelle | Lizenz | Geprüft | Deckt ab |
|---|---|---|---|---|
| Low Poly Military Vehicles (9 Modelle: Tank, Light Tank, Truck, Jeep, Helicopter, Military Motorbike, Military Boat, Ambulance Car, Bike), FBX + glTF | [poly.pizza Bundle, Zsky](https://poly.pizza/bundle/Low-Poly-Military-Vehicles-lSgBuYh48X) | CC BY 3.0 (Namensnennung "Zsky" Pflicht) | Lizenz auf Seite gelesen | Kampfpanzer, leichter Panzer, Versorgungs-LKW, Jeep → Humvee/Aufklärung, Motorrad → Aufklärung, Sanitäter |

Begründung: ein Autor = einheitlicher Stil für fast alle Fahrzeugklassen.

## Ergänzungen

| Asset | Quelle | Lizenz | Geprüft | Zweck |
|---|---|---|---|---|
| Animated Tanks Pack (4 Panzer, animiert, untexturiert) | [quaternius.com](https://quaternius.com/packs/animatedtanks.html) | CC0 | Lizenz auf Seite gelesen | Alternative/zusätzliche Panzer; Stil (2019) evtl. comichafter – vor Nutzung im Spiel vergleichen |
| Tank (Quaternius) | [poly.pizza](https://poly.pizza/m/FA5daiyZQq) | CC0 | gelesen | Reserve |
| Military Landvehicle Kit 1.2 (modulare Teile: Rumpf, Turm, Waffenring, Winde, Gitterpanzer) | [Sketchfab](https://www.sketchfab.com/3d-models/military-landvehicle-kit-12-cc0-91f495435f624d8b97a768f692aa6ce9) | Autor sagt CC0, Sketchfab zeigt CC-BY (technisch) → als CC BY behandeln | gelesen | Waffenring für Humvee-MG, Teile für Artillerie/Raketenwerfer |
| Toy Tank 3D | [itch.io, Artisau](https://aitordsgn.itch.io/toy-tank-3d) | CC0 | gelesen | eher Spielzeug-Stil, nur Reserve |
| Toon Shooter Game Kit (Charaktere, 17 Animationen) | [quaternius.com](https://quaternius.com/packs/toonshootergamekit.html) | CC0 | gelesen | Infanterie-Animationen; Toon-Stil, passt evtl. nicht zu "realistische Proportionen" |
| Soldier (animiert) | [poly.pizza, KolosStudios](https://poly.pizza/m/XT8jgwSesV) | CC BY 3.0 | gelesen | Infanterie-Kandidat |
| Ultimate Modular Men Pack (Soldat) | quaternius.com | vermutlich CC0 wie alle Quaternius-Packs | **nicht geprüft** (Seite nicht ladbar) | Infanterie-Kandidat |

## Verworfen / nicht nutzbar

| Asset | Grund |
|---|---|
| beenjoygames "Free military vehicle pack" (humee.fbx, T55) | keine formale Lizenz auf der Seite, nur "feel free to use" → unklar, nicht verwenden ohne schriftliche Klärung |
| ITHappy "Military Free" | Lizenzbedingungen auf der Seite nicht genannt, als "testing and evaluation" beschrieben → ungeprüft |
| Sketchfab "Lowpoly Military vehicles" (blaze71643, CC BY 4.0) | 322k Dreiecke, weit über Budget |
| Kenney Tank Pack 80 | nur 2D-Sprites |
| Unity Asset Store Packs (Synty u. a.) | kostenpflichtig, laut PROJECT_MASTER ausgeschlossen |

## Lücken und Plan

| Lücke | Lösung |
|---|---|
| Kein echter Humvee in passendem Stil | Zsky "Jeep" als Basis, auf Humvee-Proportionen skalieren (breiter, flacher), Oliv-Material |
| Humvee mit MG | Jeep + Waffenring (Landvehicle Kit) + MG-Modell aus Quaternius Guns-Pack (CC0, noch prüfen) |
| Artillerie, Raketenwerfer | Zsky Truck/Tank-Chassis + Aufbau aus Landvehicle-Kit-Teilen oder eigenen Unity-Meshes im selben Stil; als Übergangslösung dokumentiert |
| Explosionen/Rauch | Kenney Particle Pack (CC0 laut Kenney, **noch nicht geprüft**) + Unity Particle System |

## Nächste Schritte (auf dem PC)

1. Zsky-Bundle (glTF) und Quaternius Animated Tanks laden, in Unity nebeneinander unter URP-Licht rendern, Stil vergleichen (Screenshot).
2. Maßstab nach PROJECT_MASTER D13: 1 Unity-Einheit = 1 m, 1 Kachel = 4 m, Modelle in Originalgröße (Soldat ≈ 1,8 m, Humvee ≈ 4,6 m lang, Panzer ≈ 9 m). Die alten Werte aus UI_STYLE_GUIDE A2 gelten nicht mehr. Style Lock nach PROJECT_MASTER §8 / ARCHITECTURE §16 vor dem Massenimport. Pivots, Turm als eigenes Child für Rotation, Box-/Kapsel-Collider nur für Auswahl.
3. ASSET_CREDITS.md-Einträge mit Pfad.
