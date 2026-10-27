using System.Runtime.CompilerServices;

// Tests dürfen interne Hilfen der Simulation nutzen (z. B. DiplomacyService.GetMemory). In Unity sind Simulation und
// Tests getrennte Assemblies; headless bildet Tools/SimTests das mit eigenen Projekten pro asmdef nach.
[assembly: InternalsVisibleTo("WorldBeyond.Tests.EditMode")]
