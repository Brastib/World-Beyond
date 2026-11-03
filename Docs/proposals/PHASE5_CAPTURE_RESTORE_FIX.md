# Phase 5 – Besetzungszustand bei abgelehntem Laden erhalten

Stand: 2026-10-28. Basis: `integration/m1-candidate` @35ad486edb5ad18bf8cbf5e84d3a0e6ac849ebd3.
Änderung direkt über GitHub im bestehenden Branch.

## Befund und Änderung

`SettlementCaptureSystem.TryRestoreState` löschte den bestehenden Besetzungsfortschritt vor der Prüfung der Datensätze. Ein später ungültiger oder doppelter Eintrag führte zu `false`, ließ aber einen teilweise übernommenen Zustand zurück. Das ergibt sich direkt aus der Reihenfolge im Code; in dieser Sitzung nicht zur Laufzeit reproduziert.

Die Methode prüft jetzt sämtliche Datensätze einschließlich doppelter Siedlungs-IDs vor dem Austausch. Fehler lassen den alten Zustand erhalten. Gültige und leere Zustände ersetzen ihn weiterhin. Speicherformat, SectionVersion und die bisherige Behandlung von null-Einträgen bleiben gleich.

## Regressionstests

Ergänzt in der bestehenden `SettlementCaptureTests.cs`:
- Später Eintrag mit Siedlungs-ID 0: Ablehnung erhält den vorherigen Fortschritt.
- Später Eintrag mit negativer Haltezeit: gleicher Nachweis.
- Doppelte Siedlungs-ID nach gültigem Eintrag: gleicher Nachweis.
- Gültiger Zustand ersetzt den vorherigen; leerer Zustand löscht ihn.

## Prüfstand

Code und Diff geprüft. **Neue Tests nicht ausgeführt; keine Aussage über erfolgreiche Kompilierung oder Laufzeit.** Unity-Editor hier nicht verbunden. Nächste Prüfung: `dotnet test Tools/SimTests --filter FullyQualifiedName~SettlementCaptureTests`, danach Gesamt-Tests und Unity EditMode. Gemeinsame Status- und Architekturdateien wurden gemäß INTEGRATION §0 nicht geändert.
