using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Application.Save;
using WorldBeyond.Presentation.UI;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;

namespace WorldBeyond.UiTests
{
    // UI-Modelle gegen die echte GameComposition (alle Phasen). Kriege werden hier über IDiplomacyService ausgelöst,
    // weil die KI sie erst nach langer Laufzeit erklärt; der Feed prüft nur, dass er die echten Events richtig wiedergibt.
    public class UiModelTests
    {
        private static GameComposition NewGame(ulong seed = 7) =>
            GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 });

        private static void Run(GameComposition c, int ticks)
        {
            for (int i = 0; i < ticks; i++) c.Runner.StepOnce();
        }

        [Test]
        public void GameDate_StartsAtYear1Day1_AndRollsOverEachYear()
        {
            Assert.AreEqual("Jahr 1, Tag 1", GameDate.Format(0));
            Assert.AreEqual(1, GameDate.Year(CivTime.TicksPerYear - 1));
            Assert.AreEqual(GameDate.DaysPerYear, GameDate.Day(CivTime.TicksPerYear - 1));
            Assert.AreEqual("Jahr 2, Tag 1", GameDate.Format(CivTime.TicksPerYear));
        }

        [Test]
        public void TimeControl_SpeedChangesTicksPerAdvance_AndPauseStopsSimulation()
        {
            var c = NewGame();
            c.Runner.Speed = GameSpeed.x1;
            int normal = c.Runner.Advance(1f);
            c.Runner.Speed = GameSpeed.x5;
            int fast = c.Runner.Advance(1f);
            c.Runner.Speed = GameSpeed.Paused;
            long before = c.Context.Clock.Tick;
            Assert.AreEqual(0, c.Runner.Advance(1f));
            Assert.AreEqual(before, c.Context.Clock.Tick, "Pause darf keine Ticks laufen lassen");
            Assert.AreEqual(10, normal);
            Assert.AreEqual(20, fast, "5× = 50 Ticks, aber MaxTicksPerAdvance begrenzt auf 20");
            Assert.AreEqual(GameSpeed.Paused, TimeControl.Toggle(GameSpeed.x2, GameSpeed.x2));
            Assert.AreEqual(GameSpeed.x2, TimeControl.Toggle(GameSpeed.Paused, GameSpeed.x2));
            Assert.AreEqual(GameSpeed.x1, TimeControl.Toggle(GameSpeed.Paused, GameSpeed.Paused));
            CollectionAssert.AreEqual(new[] { "Pause", "1×", "2×", "5×", "10×" }, TimeControl.Speeds.Select(TimeControl.Label).ToArray());
        }

        [Test]
        public void CivOverview_ShowsLiveValues_AndWarStatusFollowsDiplomacy()
        {
            var c = NewGame();
            Run(c, 200);
            var civs = c.Context.Get<ICivilizationQuery>();
            var wars = c.Context.Get<IWarQuery>();
            var model = new CivOverviewModel();
            model.Refresh(civs, wars);

            Assert.AreEqual(2, model.Count);
            for (int i = 0; i < model.Count; i++)
            {
                var row = model[i];
                var v = civs.Get(row.CivId);
                Assert.AreEqual(v.Name, row.Name);
                Assert.AreEqual(v.Population, row.Population);
                Assert.AreEqual(v.SettlementCount, row.Settlements);
                Assert.AreEqual(v.MilitaryStrength, row.MilitaryStrength);
                Assert.AreEqual(UiText.Epoch(v.Epoch), row.Epoch);
                Assert.AreEqual("Frieden", row.WarStatus);
                Assert.IsFalse(row.AtWar);
            }

            int a = model[0].CivId, b = model[1].CivId;
            Assert.IsTrue(c.Context.Get<IDiplomacyService>().DeclareWar(a, b));
            Run(c, 1);
            model.Refresh(civs, wars);
            Assert.IsTrue(model[0].AtWar);
            Assert.AreEqual("Krieg mit " + civs.Get(b).Name, model[0].WarStatus);
            Assert.AreEqual("Krieg mit " + civs.Get(a).Name, model[1].WarStatus);
        }

        [Test]
        public void EventFeed_ReportsWarPeaceCaptureAndCollapse_InGerman_WithLocation()
        {
            var c = NewGame();
            var feed = new EventFeed();
            feed.Attach(c.Context);
            Run(c, 50);
            var ctx = c.Context;
            var civs = ctx.Get<ICivilizationQuery>();
            int a = civs.AllCivIds[0], b = civs.AllCivIds[1];
            string nameA = civs.Get(a).Name, nameB = civs.Get(b).Name;
            var diplomacy = ctx.Get<IDiplomacyService>();

            Assert.IsTrue(diplomacy.DeclareWar(a, b));
            Run(c, 1);
            var war = feed.Entries.Last();
            Assert.AreEqual(FeedKind.War, war.Kind);
            StringAssert.StartsWith($"{nameA} erklärt {nameB} den Krieg", war.Text);
            Assert.IsTrue(war.HasLocation, "Krieg verweist auf die Hauptstadt des Verteidigers");

            Assert.IsTrue(diplomacy.MakePeace(a, b));
            Run(c, 1);
            Assert.IsTrue(feed.Entries.Any(e => e.Kind == FeedKind.Peace && e.Text.StartsWith($"Frieden zwischen {nameA} und {nameB}")),
                string.Join(" | ", feed.Entries.Select(e => e.Text)));

            int capitalB = civs.Get(b).CapitalSettlementId;
            ctx.Get<ISettlementRegistry>().TransferSettlement(capitalB, a);
            Run(c, 1);
            var capture = feed.Entries.Last(e => e.Kind == FeedKind.Conquest);
            StringAssert.StartsWith($"{nameA} erobert ", capture.Text);
            StringAssert.EndsWith($" von {nameB}.", capture.Text);
            Assert.IsTrue(capture.HasLocation);

            ctx.Get<ICivilizationRegistry>().Destroy(b);
            Run(c, 1);
            Assert.IsTrue(feed.Entries.Any(e => e.Kind == FeedKind.Collapse && e.Text == $"{nameB} ist zerfallen."),
                "Name der zerfallenen Civ muss aus dem Zwischenspeicher kommen: " + string.Join(" | ", feed.Entries.Select(e => e.Text)));
            Assert.AreEqual(0, c.Runner.FaultCount);
        }

        [Test]
        public void EventFeed_Detach_StopsListening_AndCapacityIsBounded()
        {
            var c = NewGame();
            var feed = new EventFeed(capacity: 3);
            feed.Attach(c.Context);
            var civs = c.Context.Get<ICivilizationQuery>();
            int a = civs.AllCivIds[0], b = civs.AllCivIds[1];
            var diplomacy = c.Context.Get<IDiplomacyService>();
            for (int i = 0; i < 3; i++)
            {
                diplomacy.DeclareWar(a, b);
                Run(c, 1);
                diplomacy.MakePeace(a, b);
                Run(c, 1);
            }
            Assert.LessOrEqual(feed.Entries.Count, 3);

            int version = feed.Version;
            feed.Detach();
            diplomacy.DeclareWar(a, b);
            Run(c, 1);
            Assert.AreEqual(version, feed.Version, "nach Detach keine neuen Einträge");
        }

        [Test]
        public void SelectionInfo_Settlement_Unit_AndInvalidIdAfterDeath()
        {
            var c = NewGame();
            Run(c, 100);
            var ctx = c.Context;
            var settlements = ctx.Get<ISettlementQuery>();
            var s = settlements.Get(settlements.AllSettlementIds[0]);
            var info = new SelectionInfoModel();

            Assert.IsTrue(info.RefreshTile(ctx, s.X, s.Y));
            Assert.AreEqual(s.Name, info.Title);
            Assert.AreEqual(s.Id, info.SettlementId);
            Assert.IsTrue(info.Lines.Any(l => l.Label == "Einwohner" && l.Value.StartsWith(s.Population.ToString())));

            // Einheiten entstehen erst durch Rekrutierung/Siedler; bis zu 3 Spieljahre warten.
            var ids = new System.Collections.Generic.List<Core.EntityId>();
            for (int i = 0; i < 3 * CivTime.TicksPerYear && ids.Count == 0; i += 50)
            {
                Run(c, 50);
                ctx.Units.CopyAliveIds(ids);
            }
            Assert.Greater(ids.Count, 0, "keine Einheit nach 3 Spieljahren");
            Assert.IsTrue(info.RefreshUnit(ctx, ids[0]));
            Assert.IsTrue(info.Lines.Any(l => l.Label == "Lebenspunkte"));

            Assert.IsFalse(info.RefreshUnit(ctx, Core.EntityId.None), "ungültige ID → kein Inhalt, keine Exception");
            Assert.IsFalse(info.HasContent);
        }

        [Test]
        public void SaveMenu_SaveThenLoad_UsesServiceMessages_AndListsSlot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "wb-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameComposition current = NewGame();
                Run(current, 30);
                var service = new SaveLoadService(new SaveFileStore(dir), s => GameComposition.Create(s), () => current, c => current = c, null, "test");
                var menu = new SaveMenuModel();
                menu.Refresh(service);
                Assert.IsTrue(menu.Rows.All(r => !r.Exists && r.Detail == "leer"));

                Assert.IsTrue(menu.Save(service, SaveMenuModel.SlotName(2)), menu.LastDetail);
                Assert.AreEqual(SaveMessages.ForSave(SaveLoadError.None), menu.LastMessage);
                var row = menu.Rows[1];
                Assert.IsTrue(row.Exists && row.CanLoad);
                StringAssert.StartsWith(GameDate.Format(30), row.Detail);

                var before = current;
                Assert.IsTrue(menu.Load(service, SaveMenuModel.SlotName(2)), menu.LastDetail);
                Assert.AreNotSame(before, current, "Laden ersetzt die Komposition");
                Assert.AreEqual(30, current.Context.Clock.Tick);

                Assert.IsFalse(menu.Load(service, SaveMenuModel.SlotName(4)));
                Assert.AreEqual(SaveMessages.ForLoad(SaveLoadError.NotFound), menu.LastMessage);
                Assert.IsTrue(menu.LastWasError);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
