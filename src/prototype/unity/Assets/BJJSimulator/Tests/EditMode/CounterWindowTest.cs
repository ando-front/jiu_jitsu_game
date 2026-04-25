// NUnit EditMode mirror of src/prototype/web/tests/unit/counter_window.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class CounterWindowTest
    {
        static readonly CounterTechnique[] Empty = System.Array.Empty<CounterTechnique>();
        static readonly CounterTechnique[] ScissorCounterOnly = { CounterTechnique.ScissorCounter };

        static (CounterWindow Next, List<CounterTickEvent> Events) Tick(
            CounterWindow w, CounterTickInput inp)
        {
            var events = new List<CounterTickEvent>();
            float ts;
            var next = CounterWindowOps.Tick(w, inp, events, out ts);
            return (next, events);
        }

        // -----------------------------------------------------------------------

        [Test]
        public void CounterCandidatesFor_ScissorSweep_Returns_ScissorCounter()
        {
            var r = CounterWindowOps.CounterCandidatesFor(new[] { Technique.ScissorSweep });
            Assert.AreEqual(1, r.Length);
            Assert.AreEqual(CounterTechnique.ScissorCounter, r[0]);
        }

        [Test]
        public void CounterCandidatesFor_Triangle_Returns_TriangleEarlyStack()
        {
            var r = CounterWindowOps.CounterCandidatesFor(new[] { Technique.Triangle });
            Assert.AreEqual(1, r.Length);
            Assert.AreEqual(CounterTechnique.TriangleEarlyStack, r[0]);
        }

        [Test]
        public void CounterCandidatesFor_TechniquesWithNoCounter_ReturnsEmpty()
        {
            var r = CounterWindowOps.CounterCandidatesFor(new[] { Technique.FlowerSweep, Technique.HipBump });
            Assert.AreEqual(0, r.Length);
        }

        [Test]
        public void CounterCandidatesFor_Deduplicates()
        {
            var r = CounterWindowOps.CounterCandidatesFor(new[] { Technique.ScissorSweep, Technique.Triangle });
            bool hasScissor = false, hasTriangle = false;
            foreach (var c in r)
            {
                if (c == CounterTechnique.ScissorCounter)     hasScissor  = true;
                if (c == CounterTechnique.TriangleEarlyStack) hasTriangle = true;
            }
            Assert.IsTrue(hasScissor);
            Assert.IsTrue(hasTriangle);
        }

        [Test]
        public void StaysClosed_WhenNoCounterableTechniquesInSeed()
        {
            var (next, events) = Tick(CounterWindow.Initial, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Closed, next.State);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void Opens_WhenGivenNonEmptyOpeningSeed()
        {
            var (next, events) = Tick(CounterWindow.Initial, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Opening, next.State);
            bool hasOpening = false;
            foreach (var e in events) if (e.Kind == CounterEventKind.CounterWindowOpening) { hasOpening = true; break; }
            Assert.IsTrue(hasOpening);
        }

        [Test]
        public void Opening_To_Open_After200ms()
        {
            var w = CounterWindow.Initial;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            var (next, _) = Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Open, next.State);
        }

        [Test]
        public void Confirm_Transitions_Open_To_Closing_And_Emits_CounterConfirmed()
        {
            var w = CounterWindow.Initial;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            var (next, events) = Tick(w, new CounterTickInput
            {
                NowMs = 300, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = CounterTechnique.ScissorCounter, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Closing, next.State);
            bool hasConfirmed = false;
            foreach (var e in events)
                if (e.Kind == CounterEventKind.CounterConfirmed) { hasConfirmed = true; break; }
            Assert.IsTrue(hasConfirmed);
        }

        [Test]
        public void Aborts_WhenAttackerWindowCloses_BeforeOpen()
        {
            var w = CounterWindow.Initial;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            var (next, events) = Tick(w, new CounterTickInput
            {
                NowMs = 50, OpenAttackerWindow = false, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Closing, next.State);
            bool hasAttackerClosed = false;
            foreach (var e in events)
                if (e.Kind == CounterEventKind.CounterWindowClosing && e.CloseReason == CounterCloseReason.AttackerClosed)
                    { hasAttackerClosed = true; break; }
            Assert.IsTrue(hasAttackerClosed);
        }

        [Test]
        public void TimesOut_AfterOpenMaxMs()
        {
            var w = CounterWindow.Initial;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            w = Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            }).Next;
            long t = 200 + JudgmentWindowTiming.Default.OpenMaxMs;
            var (next, events) = Tick(w, new CounterTickInput
            {
                NowMs = t, OpenAttackerWindow = true, OpeningSeed = Empty,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Closing, next.State);
            bool hasTimedOut = false;
            foreach (var e in events)
                if (e.Kind == CounterEventKind.CounterWindowClosing && e.CloseReason == CounterCloseReason.TimedOut)
                    { hasTimedOut = true; break; }
            Assert.IsTrue(hasTimedOut);
        }

        [Test]
        public void HonoursCooldown_AfterClosed()
        {
            var w = CounterWindow.Initial;
            // Drive all the way through to CLOSED.
            w = Tick(w, new CounterTickInput { NowMs = 0, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly, ConfirmedCounter = null, DismissRequested = false }).Next;
            w = Tick(w, new CounterTickInput { NowMs = 200, OpenAttackerWindow = true, OpeningSeed = Empty, ConfirmedCounter = null, DismissRequested = false }).Next;
            w = Tick(w, new CounterTickInput { NowMs = 300, OpenAttackerWindow = true, OpeningSeed = Empty, ConfirmedCounter = CounterTechnique.ScissorCounter, DismissRequested = false }).Next;
            long closeAt = 300 + JudgmentWindowTiming.Default.ClosingMs;
            w = Tick(w, new CounterTickInput { NowMs = closeAt, OpenAttackerWindow = false, OpeningSeed = Empty, ConfirmedCounter = null, DismissRequested = false }).Next;
            Assert.AreEqual(CounterWindowState.Closed, w.State);
            Assert.AreEqual(closeAt + JudgmentWindowTiming.Default.CooldownMs, w.CooldownUntilMs);

            // Cooldown in effect.
            var (mid, _) = Tick(w, new CounterTickInput
            {
                NowMs = closeAt + 100, OpenAttackerWindow = true, OpeningSeed = ScissorCounterOnly,
                ConfirmedCounter = null, DismissRequested = false,
            });
            Assert.AreEqual(CounterWindowState.Closed, mid.State);
        }
    }
}
