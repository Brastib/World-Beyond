# M1-Prototyp in Unity starten – Schritt für Schritt

Stand: 2026-10-27, Branch `integration/m1`. Alles hier ist **headless (dotnet) geprüft, nicht in Unity**.
Der erste echte Unity-Lauf ist genau der Zweck dieser Liste.

## 0. Voraussetzungen

- Unity **6000.6.0f1**, Projekt im Ordner `C:\Users\Basti\Documents\GitHub\World-Beyond`.
- Pakete (Package Manager): Universal RP 17.6.0, Input System 1.20.0, Test Framework 1.8.0,
  Newtonsoft Json (`com.unity.nuget.newtonsoft-json`) 3.2.1.
- Project Settings → Graphics: ein URP-Asset ist eingetragen.
- Project Settings → Player → Active Input Handling: **Both** (empfohlen) oder Input System Package.

`ProjectSettings/`, `Packages/` und die `.meta`-Dateien liegen seit 2026-10-27 im Repo (DevOps, 03ebda7/8a757c8). `.meta` für neue Dateien erzeugt Unity; DevOps committet sie beim nächsten Lauf.

## 1. Code holen

Im Projektordner (Git Bash oder Terminal):

```
git fetch origin
git status                         # lokale Änderungen vorher committen
git merge origin/integration/m1    # NICHT "checkout": sonst verschwinden lokal committete ProjectSettings
```

## 2. Kompilieren

1. Unity öffnen und warten, bis die Kompilierung durch ist.
2. Console: **0 Fehler** erwartet. Die drei Unity-6-Fehler (Mehrdeutigkeit `EntityId`) sind behoben.
   Simuliert ohne Unity: ohne Korrektur 3 Fehler, mit Korrektur 0.
3. Falls doch Fehler: den Text jeder Fehlermeldung vollständig kopieren (Datei, Zeile, Code CSxxxx) und melden.

## 3. Tests in Unity

Window → General → Test Runner → EditMode → **Run All**.
Headless laufen dieselben Tests grün. Die M1-Sandbox-Tests dauern mehrere Minuten.
Rote Tests bitte mit Namen und Meldung melden.

## 4. Spielen

1. File → New Scene → „Basic (URP)“ oder „Empty“ (auch die Standardszene geht).
2. **Play** drücken. Nichts weiter anlegen: `M1SceneAssembler` baut alles selbst auf, wenn die Szene keinen
   `GameBootstrap` enthält. In der Console steht dann
   „M1SceneAssembler: Szene ohne GameBootstrap, baue Prototyp-Szene zur Laufzeit auf.“
3. Erzeugt werden: `Game` (GameBootstrap + SaveLoadController), `World` (WorldRenderer), Licht mit
   WorldAtmosphere, Kamera mit WorldSelection + StrategyCameraController, `HUD` (GameHud).
4. Prototyp-Welt: Seed 99, 256 × 256 Kacheln, 4 Zivilisationen.

Steuerung:

| Aktion | Taste/Maus |
|---|---|
| Karte schwenken | WASD / Pfeiltasten, rechte Maustaste ziehen |
| Drehen | Q / E, mittlere Maustaste ziehen |
| Zoom | Mausrad |
| Schneller schwenken | Shift |
| Pause | Leertaste |
| Tempo | 1 / 2 / 3 / 4 = 1× / 2× / 5× / 10× |
| Auswahl | Linksklick auf Einheit oder Siedlung |
| Speichern/Laden | Menü im HUD, Esc schließt es |

## 5. Was man sehen sollte (Prüfliste)

- [ ] Welt mit Gelände, Wasser, Bäumen und Felsen (prozedurale Platzhalter-Modelle, nicht die Ziel-Optik R1/R2).
- [ ] HUD: Datum/Tempo oben, Zivilisationsliste, Ereignis-Feed, Infofeld bei Auswahl. Text muss lesbar sein.
- [ ] Bei 10×: Völker starten mit 40 Personen, gründen in den ersten Jahren Tochtersiedlungen (headless: bis Jahr 45 je 3 Siedlungen), Einheiten bewegen sich.
- [ ] Erster Krieg: headless nach rund 45 Spielminuten, bei 10× also nach etwa **4,5 Minuten**. Der Krieg ist
      eine KI-Entscheidung und wird nicht erzwungen. Im Unity-Lauf kann es etwas abweichen, wenn die Bildrate
      das Tempo bremst.
- [ ] Kampf: Einheiten sterben, Feed meldet Kriegserklärung, Frieden oder Eroberung.
- [ ] Speichern, ein paar Sekunden weiterlaufen lassen, Laden: Zustand springt auf den Speicherpunkt zurück.
- [ ] Bildrate bei 10× (Stats-Fenster im Game-View) notieren.

Screenshots: Übersicht, Nahansicht Siedlung, HUD mit Feed, Kampf (falls gesehen), Profiler bei 10×.

## 6. Bekannte Grenzen (ehrlich)

- **Stand 2026-10-27 13:10 (Unity-Lauf 3):** Siedlungen, Gebäude, Einheiten und Gebiete werden noch NICHT gezeichnet (es gibt dafür noch keinen Renderer; Art & UI baut ihn). Laden meldet in Unity „Corrupted“ (Prüfsumme, Save/Perf behebt). Sichtbar sind Gelände, Wasser, Vegetation, HUD und Feed.

- Grafik ist Platzhalter: eigene Modelle nach R1/R2 kommen erst mit dem Asset-Katalog.
- Mehrere Werte sind PLATZHALTER (Kosten, Ressourcenbedarf, Konfliktfaktoren).
- Zeit: 1 Civ-Jahr = 1 Spielminute (K5), Einheiten bewegen sich in Echtzeit. Bekannt: Kriege enden oft,
  bevor Armeen ankommen (an War gegeben).
- Offen bei Civ: Gebäude auf stehenden Einheiten, eingemauerte Siedlungen, Karawanen-Schleife.
- Offen bei War/Military: Aufrüstung ohne Obergrenze im langen Krieg. Nach vielen Spielstunden wird die
  Simulation dadurch langsam (QA: Seed 99, 357 Einheiten).
- Eigene Szene statt Schnellstart: Objekte wie in Abschnitt 4.3 anlegen. Alle Verweise finden sich selbst
  (FindAnyObjectByType). Ohne Schnellstart gilt die Standardwelt (Seed 12345, 2 Civs, headless in
  12 Spielstunden ohne Krieg).
