# World Beyond – CURRENT_STATE

Letzte Aktualisierung: 2026-10-25, Ende Phase 1 (Projektanalyse und Kerndokumentation).
Regel: Nur Tatsachen. Jede Phase aktualisiert diese Datei am Ende. Nicht im Unity-Editor Geprüftes wird als solches markiert.

## 1. Projektanalyse (geprüft am 2026-10-25)

| Prüfung | Ergebnis | Wie geprüft |
|---|---|---|
| Unity-Projekt in GitHub-Repos des Nutzers | **keins vorhanden**. Repos: `Brastib/Games` (nur README.md, 1 Commit), `Seniorenheim`, `Rogue-Core-ConfiguratorBastiu.Tom`, `Flappy-Bird`, `Flappy-Bird-` | Repo-Liste abgefragt; `Brastib/Games` geklont und durchsucht (kein `ProjectSettings/ProjectVersion.txt`, kein `Packages/manifest.json`). Die anderen Repos wurden nur nach Namen beurteilt, nicht geöffnet |
| Unity-Projekt auf dem PC des Nutzers | **unbekannt** – kein Gerät mit dieser Sitzung verbunden | Geräteliste abgefragt: leer |
| Installierte Unity-Version | **unbekannt** | nicht prüfbar ohne Gerätezugriff |
| Installierte Pakete | **unbekannt** (kein Projekt) | – |
| Unity in der Cloud-Umgebung | nicht verfügbar | Arbeitsumgebung |

## 2. Was tatsächlich existiert

Nur Dokumentation, kein Code, keine Szenen, keine Assets.

| Datei | Inhalt | Erstellt von |
|---|---|---|
| `Docs/PROJECT_MASTER.md` | Vision, Rahmen, Entscheidungen D1–D15, Art Direction & Asset-Regeln (§8), Definition "fertig" | Phase 1 |
| `Docs/ARCHITECTURE.md` | Schichten, Weltmodell, Einheiten, Wirtschaft, Diplomatie, Kampf, KI, UI, Speichern (inkl. ID-Regeln), Performance-Ziele, Ordner, Pakete, Asset-Pipeline (§16) | Phase 1 |
| `Docs/SYSTEM_INTERFACES.md` | Geplante C#-Schnittstellen mit Besitzer und Status, Koordinatensystem §4.1 | Phase 1 |
| `Docs/IMPLEMENTATION_PLAN.md` | Phasen 1–8 (Nutzer-Nummerierung) + 1b Fundament, Durchstich-Reihenfolge, Abnahmen | Phase 1 |
| `Docs/TEST_PLAN.md` | Testarten, Tests je Phase (alle "nicht ausgeführt"), Endabnahme E-01…E-15 | Phase 1 |
| `Docs/CURRENT_STATE.md` | diese Datei | Phase 1 |
| `Assets/ThirdParty/ASSET_CREDITS.md` | leere Import-Tabelle, Platzhalter-Register, Kandidaten-Verweise | Phase 1 |
| `Docs/INTEGRATION.md` | Zuständigkeitsregister, Integrationsplan | Integrations-Thread |
| `Docs/UI_STYLE_GUIDE.md` | Entwurf Grafikstil (Teil A) und UI (Teil B) | Phase 6 (Entwurf) |
| `Docs/PHASE4_DIPLOMACY_WAR_DESIGN.md` | Entwurf Diplomatie/Krieg | Phase 4 (Entwurf) |
| `Docs/drafts/WORLD_ENGINE_DESIGN_DRAFT.md` | Entwurf Welt | Phase 2 (Entwurf) |
| `Docs/drafts/CIVILIZATION_DESIGN_DRAFT.md` | Entwurf Zivilisation | Phase 3 (Entwurf) |
| `Docs/proposals/SAVE_FORMAT_PROPOSAL.md` | Speicherformat und ID-Regeln (A und F übernommen, D14) | Phase 7 (Vorschlag) |
| `Docs/proposals/PHASE5_MILITARY_DESIGN.md`, `PHASE5_MILITARY_ASSETS.md` | Entwurf Militär, Asset-Recherche | Phase 5 (Entwurf) |

Alle Dateien liegen vorläufig im Projektordner `/mnt/project-files/WorldBeyond/` (ohne Git), bis ein Repository existiert.

## 3. Implementierte Schnittstellen

Keine. Alle Schnittstellen in SYSTEM_INTERFACES haben Status "geplant".

Speicherformat und stabile IDs (Nutzervorgabe 2026-10-25): **nur dokumentiert** in ARCHITECTURE §11, SYSTEM_INTERFACES §1/§11, TEST_PLAN T-7-08 bis T-7-20. Kein Code, kein Test ausgeführt.

## 4. Ausgeführte Tests

Keine. Es gibt nichts Ausführbares.

## 5. Bekannte Probleme / Widersprüche

| # | Problem | Status |
|---|---|---|
| K1 | `UI_STYLE_GUIDE.md` Teil A2 nennt noch "1 Unity-Einheit = 1 Kachel ≈ 10 m" mit verkleinerten Referenzgrößen (Soldat 0,45). Gültig ist D13: 1 Einheit = 1 m, 1 Kachel = 4 m, Modelle in Originalgröße. `PHASE5_MILITARY_ASSETS.md` übernimmt die alten Werte. | an Phase 6 und Phase 5 gemeldet, Korrektur durch die Besitzer offen |
| K2 | Stilrichtung ist nur Arbeitshypothese (stilisiert-realistisch, Low/Mid-Poly). Style Lock (PROJECT_MASTER §8.3) braucht Unity und Bestätigung durch den Nutzer. | offen |
| K3 | Asset-Downloads aus der Cloud werden laut Phase 5 vom Proxy blockiert (403). Import ist realistisch nur auf dem PC möglich. | nicht von Phase 1 selbst geprüft, übernommen aus Phase-5-Notiz |

## 6. Fehlende Abhängigkeiten (blockieren Phase 1b und damit alle Code-Phasen)

1. **Projektort:** GitHub-Repository (Vorschlag `Brastib/WorldBeyond` mit Git LFS) oder Ordner auf dem PC. Nur der Nutzer kann ein Repo anlegen.
2. **Unity auf dem PC:** installierte Version prüfen (Unity Hub). Falls keine: aktuelle Unity 6 LTS installieren.
3. **Gerätezugriff:** Remote Control oder Ordnerzugriff, damit Claude Unity-Version und Pakete prüfen, kompilieren und Tests ausführen kann.

## 7. Nächste Aufgabe

Phase 1b (Fundament) durch den Integrations-Thread, sobald 1–3 aus §6 geklärt sind. Bis dahin dürfen die anderen Phasen nur Entwürfe und Asset-Recherche machen.
