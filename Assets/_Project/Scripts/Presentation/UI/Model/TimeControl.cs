using System.Collections.Generic;
using WorldBeyond.Application;
using WorldBeyond.Simulation.Civilizations;

namespace WorldBeyond.Presentation.UI
{
    /// <summary>
    /// Anzeige der Spielzeit aus dem Tick. Platzhalter bis zum GameCalendar (M2): 1 Jahr = CivTime.TicksPerYear,
    /// das Jahr wird gleichmäßig in 360 Tage geteilt. Jahr und Tag beginnen bei 1.
    /// </summary>
    public static class GameDate
    {
        public const int DaysPerYear = 360;

        public static int Year(long tick) => (int)(tick / CivTime.TicksPerYear) + 1;

        public static int Day(long tick)
        {
            long inYear = tick % CivTime.TicksPerYear;
            return (int)(inYear * DaysPerYear / CivTime.TicksPerYear) + 1;
        }

        public static string Format(long tick) => $"Jahr {Year(tick)}, Tag {Day(tick)}";
    }

    /// <summary>Spielgeschwindigkeit über ISimulationRunner.Speed (einzige Zeitquelle, kein eigener Takt).</summary>
    public static class TimeControl
    {
        public static readonly IReadOnlyList<GameSpeed> Speeds = new[] { GameSpeed.Paused, GameSpeed.x1, GameSpeed.x2, GameSpeed.x5, GameSpeed.x10 };

        public static string Label(GameSpeed s) => s == GameSpeed.Paused ? "Pause" : $"{(int)s}×";

        public static string Tooltip(GameSpeed s) =>
            s == GameSpeed.Paused ? "Simulation anhalten (Leertaste)" : $"{(int)s}-fache Geschwindigkeit";

        /// <summary>Pause ↔ letzte laufende Geschwindigkeit (Leertaste).</summary>
        public static GameSpeed Toggle(GameSpeed current, GameSpeed lastRunning) =>
            current == GameSpeed.Paused ? (lastRunning == GameSpeed.Paused ? GameSpeed.x1 : lastRunning) : GameSpeed.Paused;
    }
}
