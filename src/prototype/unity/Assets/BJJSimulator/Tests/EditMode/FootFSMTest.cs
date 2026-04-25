// NUnit EditMode mirror of src/prototype/web/tests/unit/foot_fsm.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class FootFSMTest
    {
        static FootTickInput Base(long nowMs = 0, bool bumperEdge = false, float posture = 0f) =>
            new FootTickInput
            {
                NowMs                   = nowMs,
                BumperEdge              = bumperEdge,
                OpponentPostureSagittal = posture,
            };

        static (FootFSM Final, List<string> Events) Run(FootFSM start, params FootTickInput[] steps)
        {
            var f      = start;
            var names  = new List<string>();
            var events = new List<FootTickEvent>();
            foreach (var s in steps)
            {
                events.Clear();
                f = FootFSMOps.Tick(f, s, events);
                foreach (var e in events) names.Add(e.Kind.ToString().ToUpper()
                    .Replace("LOCKINGSTARTED", "LOCKING_STARTED")
                    .Replace("LOCKSUCCEEDED", "LOCK_SUCCEEDED")
                    .Replace("LOCKFAILED",    "LOCK_FAILED"));
            }
            return (f, names);
        }

        // Simpler event-kind list using enum names.
        static List<FootEventKind> EventKinds(FootFSM start, params FootTickInput[] steps)
        {
            var f      = start;
            var kinds  = new List<FootEventKind>();
            var events = new List<FootTickEvent>();
            foreach (var s in steps)
            {
                events.Clear();
                f = FootFSMOps.Tick(f, s, events);
                foreach (var e in events) kinds.Add(e.Kind);
            }
            return kinds;
        }

        [Test]
        public void StartsLocked_ByDefault()
        {
            Assert.AreEqual(FootState.Locked, FootFSMOps.Initial(FootSide.L).State);
        }

        [Test]
        public void Locked_To_Unlocked_OnBumperEdge()
        {
            var (final, _) = Run(FootFSMOps.Initial(FootSide.L), Base(0, bumperEdge: true));
            Assert.AreEqual(FootState.Unlocked, final.State);
        }

        [Test]
        public void Locked_To_Unlocked_EmitsUnlockedEvent()
        {
            var kinds = EventKinds(FootFSMOps.Initial(FootSide.L), Base(0, bumperEdge: true));
            Assert.Contains(FootEventKind.Unlocked, kinds);
        }

        [Test]
        public void Unlocked_To_Locking_Succeeds_WhenPostureForwardBroken()
        {
            var f     = FootFSMOps.Initial(FootSide.L);
            var events = new List<FootTickEvent>();
            int lockMs = FootTiming.Default.LockingMs;

            f = FootFSMOps.Tick(f, Base(0,           bumperEdge: true),  events); // → UNLOCKED
            events.Clear();
            f = FootFSMOps.Tick(f, Base(50,          bumperEdge: true),  events); // → LOCKING
            events.Clear();
            f = FootFSMOps.Tick(f, Base(50 + lockMs, posture: FootFSMOps.LockingPostureThreshold + 0.1f), events);

            Assert.AreEqual(FootState.Locked, f.State);
            bool hasLockSucceeded = false;
            foreach (var e in events) if (e.Kind == FootEventKind.LockSucceeded) { hasLockSucceeded = true; break; }
            Assert.IsTrue(hasLockSucceeded, "Expected LOCK_SUCCEEDED event");
        }

        [Test]
        public void Locking_Fails_WhenPostureUpright()
        {
            var f      = FootFSMOps.Initial(FootSide.R);
            var events = new List<FootTickEvent>();
            int lockMs = FootTiming.Default.LockingMs;

            f = FootFSMOps.Tick(f, Base(0,           bumperEdge: true), events);
            events.Clear();
            f = FootFSMOps.Tick(f, Base(50,          bumperEdge: true), events);
            events.Clear();
            f = FootFSMOps.Tick(f, Base(50 + lockMs, posture: 0f),      events);

            Assert.AreEqual(FootState.Unlocked, f.State);
            bool hasLockFailed = false;
            foreach (var e in events) if (e.Kind == FootEventKind.LockFailed) { hasLockFailed = true; break; }
            Assert.IsTrue(hasLockFailed, "Expected LOCK_FAILED event");
        }

        [Test]
        public void Locking_AbortsToUnlocked_OnSecondBumperPress()
        {
            var f      = FootFSMOps.Initial(FootSide.L);
            var events = new List<FootTickEvent>();
            f = FootFSMOps.Tick(f, Base(0,   bumperEdge: true), events);
            f = FootFSMOps.Tick(f, Base(50,  bumperEdge: true), events);
            f = FootFSMOps.Tick(f, Base(100, bumperEdge: true), events); // abort
            Assert.AreEqual(FootState.Unlocked, f.State);
        }

        [Test]
        public void HeldBumper_NoBumperEdge_IsIgnoredInLocked()
        {
            var f      = FootFSMOps.Initial(FootSide.L);
            var events = new List<FootTickEvent>();
            f = FootFSMOps.Tick(f, Base(0),  events);
            f = FootFSMOps.Tick(f, Base(16), events);
            f = FootFSMOps.Tick(f, Base(32), events);
            Assert.AreEqual(FootState.Locked, f.State);
            Assert.AreEqual(0, events.Count);
        }
    }
}
