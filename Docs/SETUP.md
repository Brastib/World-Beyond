# World Beyond – Einrichtung auf dem PC

Stand: 2026-10-26. Status: **Anleitung, nicht im Editor geprüft.**

## Voraussetzungen

- Windows-PC mit Unity Hub und Unity 6 LTS (genaue Version wird nach Prüfung in `CURRENT_STATE.md` eingetragen).
- Git und Git LFS (`git lfs install` einmalig).

## Repository holen

```bash
git lfs install
git clone https://github.com/Brastib/World-Beyond.git
```

## Unity-Projekt öffnen (erstes Mal)

Das Repository enthält noch keine `ProjectSettings/` und kein `Packages/manifest.json`, weil die Unity-Version noch nicht feststeht. Beim ersten Mal:

1. Unity Hub → **Add** → **Add project from disk** → den Ordner `World-Beyond` wählen, Unity 6 LTS auswählen.
2. Unity legt `ProjectSettings/` und `Packages/` an und importiert `Assets/`.
3. Pakete über den Package Manager installieren: Universal RP, Input System, Newtonsoft Json (`com.unity.nuget.newtonsoft-json`), Test Framework.
4. `ProjectSettings/` und `Packages/manifest.json` committen (der Integrations-Thread übernimmt das per Remote Control).

## Tests

- In Unity: **Window → General → Test Runner → EditMode → Run All**.
- Ohne Editor (Kommandozeile):
  `Unity.exe -batchmode -projectPath <Pfad> -runTests -testPlatform EditMode -testResults results.xml -logFile -`
- Reine C#-Logik ohne Unity (auch in der Cloud): `dotnet test Tools/SimTests`
