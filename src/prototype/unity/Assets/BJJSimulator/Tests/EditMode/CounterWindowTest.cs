// NUnit EditMode mirror of src/prototype/web/tests/unit/counter_window.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class CounterWindowTest
    {
        private static CounterTickInput ClosedInput(long nowMs = 0) => new CounterTickInput
        {
            NowMs              = nowMs,
            OpenAttackerWindow = false,
            OpeningSeed        = new List<CounterTechnique>(),
            ConfirmedCounter   = null,
            DismissRequested   = false,
        };

        // ------------------------------------------------------------------
        // counterCandidatesFor mapping
        // ------------------------------------------------------------------

        [Test]
        public void ScissorSweepMapsToScissorCounter()
        {
            var out_ = new List<CounterTechnique>();
            CounterWindowOps.CounterCandidatesFor(
                new List<Technique> { Technique.ScissorSweep }, out_);
            Assert.AreEqual(1, out_.Count);
            Assert.AreEqual(CounterTechnique.ScissorCounter, out_[0]);
        }

        [Test]
        public void TriangleMapsToTriangleEarlyStack()
        {
            var out_ = new List<CounterTechnique>();
            CounterWindowOps.CounterCandidatesFor(
                new List<Technique> { Technique.Triangle }, out_);
            Assert.AreEqual(1, out_.Count);
            Assert.AreEqual(CounterTechnique.TriangleEarlyStack, out_[0]);
        }

        [Test]
        public void TechniquesWithNoCounterYieldEmpty()
        {
            var out_ = new List<CounterTechnique>();
            CounterWindowOps.CounterCandidatesFor(
                new List<Technique> { Technique.FlowerSweep, Technique.HipBump }, out_);
            Assert.AreEqual(0, out_.Count);
        }

        [Test]
        public void DedupeWhenMultipleTechniquesShareACounter()
        {
            var out_ = new List<CounterTechnique>();
            CounterWindowOps.CounterCandidatesFor(
                new List<Technique> { Technique.ScissorSweep, Technique.Triangle }, out_);
            Assert.IsTrue(out_.Contains(CounterTechnique.ScissorCounter));
            Assert.IsTrue(out_.Contains(CounterTechnique.TriangleEarlyStack));
        }

        // ------------------------------------------------------------------
        // Counter window lifecycle
        // ------------------------------------------------------------------

        [Test]
        public void StaysClosed_WhenNoCounterableTechniques()
        {
            var ev = new List<CounterTickEvent>();
            var (n, _) = CounterWindowOps.Tick(CounterWindowOps.Initial, new CounterTickInput
            {
                NowMs              = 0,
                OpenAttackerWindow = true,
                OpeningSeed        = new List<CounterTechnique>(), // empty
                ConfirmedCounter   = null,
                DismissRequested   = false,
            }, ev);
            Assert.AreEqual(CounterWindowState.Closed, n.State);
            Assert.AreEqual(0, ev.Count);
        }

        [Test]
        public void Opens_WhenGivenNonEmptySeed()
        {
            var ev = new List<CounterTickEvent>();
            var (n, _) = CounterWindowOps.Tick(CounterWindowOps.Initial, new CounterTickInput
            {
                NowMs              = 0,
                OpenAttackerWindow = true,
                OpeningSeed        = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
                ConfirmedCounter   = null,
                DismissRequested   = false,
            }, ev);
            Assert.AreEqual(CounterWindowState.Opening, n.State);
            Assert.AreEqual(CounterEventKind.CounterWindowOpening, ev[0].Kind);
        }

        [Test]
        public void ProgressesOpeningToOpenAfter200ms()
        {
            var w = CounterWindowOps.Initial;
            var ev = new List<CounterTickEvent>();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            Assert.AreEqual(CounterWindowState.Open, w.State);
        }

        [Test]
        public void Confirm_TransitionsOpenToClosingAndEmitsConfirmedEvent()
        {
            var w = CounterWindowOps.Initial;
            var ev = new List<CounterTickEvent>();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            ev.Clear();
            CounterWindow n;
            (n, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 300, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
                ConfirmedCounter = CounterTechnique.ScissorCounter,
            }, ev);
            Assert.AreEqual(CounterWindowState.Closing, n.State);
            bool hasConfirm = false;
            foreach (var e in ev)
                if (e.Kind == CounterEventKind.CounterConfirmed) hasConfirm = true;
            Assert.IsTrue(hasConfirm, "COUNTER_CONFIRMED emitted");
        }

        [Test]
        public void Aborts_WhenAttackerWindowClosesBeforeReachingOpen()
        {
            var w = CounterWindowOps.Initial;
            var ev = new List<CounterTickEvent>();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            ev.Clear();
            CounterWindow n;
            (n, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 50, OpenAttackerWindow = false, // attacker window gone
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            Assert.AreEqual(CounterWindowState.Closing, n.State);
            bool hasAttackerClosed = false;
            foreach (var e in ev)
                if (e.Kind == CounterEventKind.CounterWindowClosing &&
                    e.CloseReason == CounterCloseReason.AttackerClosed)
                    hasAttackerClosed = true;
            Assert.IsTrue(hasAttackerClosed, "ATTACKER_CLOSED reason emitted");
        }

        [Test]
        public void TimesOut_AfterOpenMaxMs()
        {
            var w = CounterWindowOps.Initial;
            var ev = new List<CounterTickEvent>();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            ev.Clear();
            CounterWindow n;
            (n, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 200 + WindowTiming.Default.OpenMaxMs,
                OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            Assert.AreEqual(CounterWindowState.Closing, n.State);
            bool hasTimeout = false;
            foreach (var e in ev)
                if (e.Kind == CounterEventKind.CounterWindowClosing &&
                    e.CloseReason == CounterCloseReason.TimedOut)
                    hasTimeout = true;
            Assert.IsTrue(hasTimeout, "TIMED_OUT reason emitted");
        }

        [Test]
        public void HonoursCooldownAfterClosed()
        {
            var w = CounterWindowOps.Initial;
            var ev = new List<CounterTickEvent>();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 0, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 200, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = 300, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique>(),
                ConfirmedCounter = CounterTechnique.ScissorCounter,
            }, ev);
            // CLOSING → CLOSED.
            long closeAt = 300 + WindowTiming.Default.ClosingMs;
            ev.Clear();
            (w, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = closeAt, OpenAttackerWindow = false,
                OpeningSeed = new List<CounterTechnique>(),
            }, ev);
            Assert.AreEqual(CounterWindowState.Closed, w.State);
            Assert.AreEqual(closeAt + WindowTiming.Default.CooldownMs, w.CooldownUntilMs);

            // Mid-cooldown: openingSeed present but window stays CLOSED.
            ev.Clear();
            CounterWindow mid;
            (mid, _) = CounterWindowOps.Tick(w, new CounterTickInput
            {
                NowMs = closeAt + 100, OpenAttackerWindow = true,
                OpeningSeed = new List<CounterTechnique> { CounterTechnique.ScissorCounter },
            }, ev);
            Assert.AreEqual(CounterWindowState.Closed, mid.State);
        }
    }
}
