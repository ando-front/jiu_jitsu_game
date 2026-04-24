// NUnit EditMode mirror of src/prototype/web/tests/unit/foot_fsm.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class FootFSMTest
    {
        private static FootFSM Initial(FootSide side = FootSide.L) =>
            FootFSMOps.Initial(side, nowMs: 0);

        private static FootTickInput Base() => new FootTickInput
        {
            NowMs                   = 0,
            BumperEdge              = false,
            OpponentPostureSagittal = 0f,
        };

        private struct RunResult
        {
            public FootFSM        Final;
            public List<string>   EventKinds;
        }

        private static RunResult Run(FootFSM start, params FootTickInput[] steps)
        {
            var foot   = start;
            var kinds  = new List<string>();
            var events = new List<FootTickEvent>();
            foreach (var step in steps)
            {
                events.Clear();
                foot = FootFSMOps.Tick(foot, step, events);
                foreach (var e in events) kinds.Add(e.Kind.ToString());
            }
            return new RunResult { Final = foot, EventKinds = kinds };
        }

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        [Test]
        public void StartsLockedByDefault()
        {
            Assert.AreEqual(FootState.Locked, Initial().State);
        }

        [Test]
        public void LockedToUnlockedOnBumperEdge()
        {
            var r = Run(Initial(), new FootTickInput { NowMs = 0, BumperEdge = true });
            Assert.AreEqual(FootState.Unlocked, r.Final.State);
            Assert.AreEqual(new List<string> { "Unlocked" }, r.EventKinds);
        }

        [Test]
        public void UnlockedToLockingSucceedsWhenPostureForwardBroken()
        {
            var r = Run(Initial(),
                new FootTickInput { NowMs = 0, BumperEdge = true },  // LOCKED → UNLOCKED
                new FootTickInput { NowMs = 50, BumperEdge = true }, // UNLOCKED → LOCKING
                new FootTickInput
                {
                    NowMs                   = 50 + FootTiming.Default.LockingMs,
                    BumperEdge              = false,
                    OpponentPostureSagittal = FootConst.LockingPostureThreshold + 0.1f,
                });
            Assert.AreEqual(FootState.Locked, r.Final.State);
            Assert.AreEqual(new List<string> { "Unlocked", "LockingStarted", "LockSucceeded" }, r.EventKinds);
        }

        [Test]
        public void LockingFailsAndDropsToUnlockedWhenPostureUpright()
        {
            var r = Run(FootFSMOps.Initial(FootSide.R),
                new FootTickInput { NowMs = 0, BumperEdge = true },
                new FootTickInput { NowMs = 50, BumperEdge = true },
                new FootTickInput
                {
                    NowMs                   = 50 + FootTiming.Default.LockingMs,
                    BumperEdge              = false,
                    OpponentPostureSagittal = 0f,
                });
            Assert.AreEqual(FootState.Unlocked, r.Final.State);
            Assert.AreEqual(new List<string> { "Unlocked", "LockingStarted", "LockFailed" }, r.EventKinds);
        }

        [Test]
        public void LockingAbortsToUnlockedIfBumperPressedAgain()
        {
            var r = Run(Initial(),
                new FootTickInput { NowMs = 0,   BumperEdge = true },
                new FootTickInput { NowMs = 50,  BumperEdge = true },
                new FootTickInput { NowMs = 100, BumperEdge = true }); // abort mid-LOCKING
            Assert.AreEqual(FootState.Unlocked, r.Final.State);
        }

        [Test]
        public void HeldBumperNoEdgeIsIgnoredInLocked()
        {
            var r = Run(Initial(),
                new FootTickInput { NowMs = 0  },
                new FootTickInput { NowMs = 16 },
                new FootTickInput { NowMs = 32 });
            Assert.AreEqual(FootState.Locked, r.Final.State);
            Assert.AreEqual(0, r.EventKinds.Count);
        }
    }
}
