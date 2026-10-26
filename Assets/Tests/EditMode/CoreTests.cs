using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;

namespace WorldBeyond.Tests
{
    public class EntityStoreTests
    {
        [Test]
        public void Create_StartsAtIndexOne_WithGenerationOne()
        {
            var s = new EntityStore(4);
            var a = s.Create();
            Assert.AreEqual(1, a.Index);
            Assert.AreEqual(1, a.Generation);
            Assert.IsTrue(s.Exists(a));
            Assert.IsFalse(s.Exists(EntityId.None));
        }

        [Test]
        public void Destroy_InvalidatesOldId_AndReuseGetsNewGeneration()
        {
            var s = new EntityStore(1);
            var a = s.Create();
            Assert.IsTrue(s.Destroy(a));
            Assert.IsFalse(s.Exists(a));
            Assert.IsFalse(s.Destroy(a), "zweites Destroy muss false liefern");
            var b = s.Create();
            Assert.AreEqual(a.Index, b.Index);
            Assert.AreEqual(a.Generation + 1, b.Generation);
            Assert.IsFalse(s.Exists(a));
            Assert.IsTrue(s.Exists(b));
        }

        [Test]
        public void Grows_BeyondInitialCapacity()
        {
            var s = new EntityStore(1);
            var ids = new List<EntityId>();
            for (int i = 0; i < 100; i++) ids.Add(s.Create());
            Assert.AreEqual(100, s.AliveCount);
            foreach (var id in ids) Assert.IsTrue(s.Exists(id));
        }

        [Test]
        public void CaptureRestore_PreservesGenerationsAndFreeListOrder()
        {
            var s = new EntityStore(4);
            var a = s.Create(); var b = s.Create(); var c = s.Create();
            s.Destroy(c); s.Destroy(a);
            var state = s.CaptureState();

            var r = new EntityStore(1);
            Assert.IsTrue(r.TryRestoreState(state, out var err), err);
            Assert.IsFalse(r.Exists(a));
            Assert.IsTrue(r.Exists(b));
            Assert.IsFalse(r.Exists(c));
            Assert.AreEqual(1, r.AliveCount);
            var next = r.Create();
            Assert.AreEqual(c.Index, next.Index, "Freiliste FIFO: zuerst c");
            Assert.AreEqual(c.Generation + 1, next.Generation);
        }

        [Test]
        public void Restore_RejectsInconsistentState_AndKeepsOldState()
        {
            var s = new EntityStore(2);
            var a = s.Create();
            var bad = new EntityStoreState { Generations = new[] { 0, 1, 1 }, Alive = new[] { false, true, false }, FreeList = new int[0] };
            Assert.IsFalse(s.TryRestoreState(bad, out var err));
            StringAssert.Contains("weder lebend noch frei", err);
            Assert.IsTrue(s.Exists(a), "alter Zustand muss erhalten bleiben");

            var dup = new EntityStoreState { Generations = new[] { 0, 1, 1 }, Alive = new[] { false, false, false }, FreeList = new[] { 1, 1, 2 } };
            Assert.IsFalse(s.TryRestoreState(dup, out _));
            var aliveAndFree = new EntityStoreState { Generations = new[] { 0, 1 }, Alive = new[] { false, true }, FreeList = new[] { 1 } };
            Assert.IsFalse(s.TryRestoreState(aliveAndFree, out _));
            var zeroGen = new EntityStoreState { Generations = new[] { 0, 0 }, Alive = new[] { false, false }, FreeList = new[] { 1 } };
            Assert.IsFalse(s.TryRestoreState(zeroGen, out _));
        }
    }

    public class SequentialIdAllocatorTests
    {
        [Test]
        public void NeverReuses_AndRestoresCounter()
        {
            var a = new SequentialIdAllocator();
            Assert.AreEqual(1, a.Next());
            Assert.AreEqual(2, a.Next());
            var b = new SequentialIdAllocator();
            Assert.IsTrue(b.TryRestore(a.LastIssued, out _));
            Assert.AreEqual(3, b.Next());
            Assert.IsFalse(b.TryRestore(-1, out _));
            Assert.AreEqual(3, b.LastIssued);
        }
    }

    public class ContentRegistryTests
    {
        [Test]
        public void RegisterAndLookup_PerKind()
        {
            var c = new ContentRegistry();
            int u = c.Register(ContentKind.Unit, "unit.settler");
            int t = c.Register(ContentKind.Tech, "unit.settler");
            Assert.AreEqual(1, u);
            Assert.AreEqual(1, t, "Arten haben getrennte Nummernräume");
            Assert.IsTrue(c.TryGetId(ContentKind.Unit, "unit.settler", out var id));
            Assert.AreEqual(u, id);
            Assert.AreEqual("unit.settler", c.GetKey(ContentKind.Unit, u));
            Assert.IsNull(c.GetKey(ContentKind.Unit, 99));
            Assert.IsFalse(c.Contains(ContentKind.Building, "unit.settler"));
        }

        [Test]
        public void DuplicateKey_Throws()
        {
            var c = new ContentRegistry();
            c.Register(ContentKind.Unit, "unit.a");
            Assert.Throws<System.InvalidOperationException>(() => c.Register(ContentKind.Unit, "unit.a"));
            Assert.Throws<System.ArgumentException>(() => c.Register(ContentKind.Unit, " "));
        }
    }

    public class SimRandomTests
    {
        [Test]
        public void SameSeed_SameSequence()
        {
            var a = new SimRandom(42); var b = new SimRandom(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void RestoredState_ContinuesIdentically()
        {
            var a = new SimRandom(7);
            for (int i = 0; i < 10; i++) a.NextULong();
            var b = new SimRandom(999) { State = a.State };
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Range(0, 1000), b.Range(0, 1000));
        }

        [Test]
        public void Range_StaysInBounds_AndCoversAllValues()
        {
            var r = new SimRandom(1);
            var seen = new bool[6];
            for (int i = 0; i < 10000; i++)
            {
                int v = r.Range(-3, 3);
                Assert.That(v, Is.InRange(-3, 2));
                seen[v + 3] = true;
            }
            CollectionAssert.DoesNotContain(seen, false);
            for (int i = 0; i < 10000; i++) Assert.That(r.Value01(), Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => r.Range(5, 5));
            Assert.Throws<System.ArgumentException>(() => r.State = 0);
        }

        [Test]
        public void SeedZero_IsUsable()
        {
            var r = new SimRandom(0);
            Assert.AreNotEqual(0UL, r.State);
            Assert.AreNotEqual(r.NextULong(), r.NextULong());
        }
    }

    public class SimEventBusTests
    {
        private struct Ping : ISimEvent { public int Value; }
        private struct Pong : ISimEvent { public int Value; }

        [Test]
        public void Publish_IsDeferredUntilFlush_InOrder()
        {
            var bus = new SimEventBus();
            var got = new List<int>();
            bus.Subscribe<Ping>(p => got.Add(p.Value));
            bus.Publish(new Ping { Value = 1 });
            bus.Publish(new Ping { Value = 2 });
            Assert.IsEmpty(got);
            bus.Flush();
            CollectionAssert.AreEqual(new[] { 1, 2 }, got);
            bus.Flush();
            Assert.AreEqual(2, got.Count, "kein doppeltes Ausliefern");
        }

        [Test]
        public void EventsPublishedDuringFlush_ArriveNextFlush()
        {
            var bus = new SimEventBus();
            var pongs = new List<int>();
            bus.Subscribe<Ping>(p => bus.Publish(new Pong { Value = p.Value * 10 }));
            bus.Subscribe<Pong>(p => pongs.Add(p.Value));
            bus.Publish(new Ping { Value = 3 });
            bus.Flush();
            Assert.IsEmpty(pongs);
            bus.Flush();
            CollectionAssert.AreEqual(new[] { 30 }, pongs);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new SimEventBus();
            int n = 0;
            System.Action<Ping> h = _ => n++;
            bus.Subscribe(h);
            bus.Subscribe(h);
            bus.Publish(new Ping());
            bus.Flush();
            Assert.AreEqual(1, n, "doppeltes Abonnieren zählt einmal");
            bus.Unsubscribe(h);
            bus.Publish(new Ping());
            bus.Flush();
            Assert.AreEqual(1, n);
        }

        [Test]
        public void HandlerException_DoesNotStopOthers_AndIsReported()
        {
            var bus = new SimEventBus();
            int delivered = 0;
            bus.Subscribe<Ping>(_ => throw new System.InvalidOperationException("kaputt"));
            bus.Subscribe<Ping>(_ => delivered++);
            bus.Publish(new Ping());
            Assert.Throws<System.InvalidOperationException>(() => bus.Flush());
            Assert.AreEqual(1, delivered);

            var reported = new List<System.Exception>();
            bus.HandlerFailed += reported.Add;
            bus.Publish(new Ping());
            Assert.DoesNotThrow(() => bus.Flush());
            Assert.AreEqual(1, reported.Count);
            Assert.AreEqual(2, delivered);
        }

        [Test]
        public void ClearPending_DropsUndelivered()
        {
            var bus = new SimEventBus();
            int n = 0;
            bus.Subscribe<Ping>(_ => n++);
            bus.Publish(new Ping());
            Assert.AreEqual(1, bus.PendingCount);
            bus.ClearPending();
            bus.Flush();
            Assert.AreEqual(0, n);
        }
    }
}
