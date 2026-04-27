// NUnit EditMode mirror of src/prototype/web/tests/unit/cut_attempt.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/state_machines_v1.md §4.2 (defender cut-attempt FSM)
//            docs/design/input_system_defense_v1.md §B.4.1 (target picking).
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors the gripped() / idle() / inputs() factories in TS).
    // -------------------------------------------------------------------------

    public static class CutAttemptTestHelpers
    {
        public static HandFSM Gripped(HandSide side, GripZone zone) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = zone,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        public static HandFSM Idle(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Idle,
            Target          = GripZone.None,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        // Builds a CutTickInput with sensible defaults; pass overrides as named args.
        public static CutTickInput Inputs(
            long       nowMs            = 0,
            CutCommit? leftCommit       = null,
            CutCommit? rightCommit      = null,
            HandFSM?   attackerLeft     = null,
            HandFSM?   attackerRight    = null,
            float      attackerTriggerL = 0f,
            float      attackerTriggerR = 0f) =>
            new CutTickInput
            {
                NowMs            = nowMs,
                LeftCommit       = leftCommit,
                RightCommit      = rightCommit,
                AttackerLeft     = attackerLeft  ?? Idle(HandSide.L),
                AttackerRight    = attackerRight ?? Idle(HandSide.R),
                AttackerTriggerL = attackerTriggerL,
                AttackerTriggerR = attackerTriggerR,
            };
    }

    // -------------------------------------------------------------------------
    // describe("pickCutTarget (§B.4.1)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class PickCutTargetTests
    {
        // it("returns null when no attacker hand is GRIPPED")
        [Test]
        public void ReturnsNullWhenNoAttackerHandIsGripped()
        {
            bool ok = CutAttemptOps.PickCutTarget(
                new Vec2 { X = 0f, Y = 0f },
                CutAttemptTestHelpers.Idle(HandSide.L),
                CutAttemptTestHelpers.Idle(HandSide.R),
                out _, out _);

            Assert.IsFalse(ok);
        }

        // it("returns the only GRIPPED hand regardless of RS")
        [Test]
        public void ReturnsTheOnlyGrippedHandRegardlessOfRs()
        {
            bool ok = CutAttemptOps.PickCutTarget(
                new Vec2 { X = 0.5f, Y = 0.5f },
                CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                CutAttemptTestHelpers.Idle(HandSide.R),
                out var side, out var zone);

            Assert.IsTrue(ok);
            Assert.AreEqual(HandSide.L,      side);
            Assert.AreEqual(GripZone.SleeveR, zone);
        }

        // it("prefers attacker L when RS points left")
        [Test]
        public void PrefersAttackerLWhenRsPointsLeft()
        {
            bool ok = CutAttemptOps.PickCutTarget(
                new Vec2 { X = -1f, Y = 0f },
                CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                CutAttemptTestHelpers.Gripped(HandSide.R, GripZone.SleeveL),
                out var side, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(HandSide.L, side);
        }

        // it("prefers attacker R when RS points right")
        [Test]
        public void PrefersAttackerRWhenRsPointsRight()
        {
            bool ok = CutAttemptOps.PickCutTarget(
                new Vec2 { X = 1f, Y = 0f },
                CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                CutAttemptTestHelpers.Gripped(HandSide.R, GripZone.SleeveL),
                out var side, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(HandSide.R, side);
        }
    }

    // -------------------------------------------------------------------------
    // describe("tickCutAttempts lifecycle")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TickCutAttemptsLifecycleTests
    {
        // it("commits start IN_PROGRESS with a target")
        [Test]
        public void CommitsStartInProgressWithATarget()
        {
            var events = new List<CutTickEvent>();
            var next = CutAttemptOps.Tick(
                CutAttempts.Initial,
                CutAttemptTestHelpers.Inputs(
                    nowMs:        0,
                    leftCommit:   new CutCommit { Rs = new Vec2 { X = 0f, Y = 0f } },
                    attackerLeft: CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                events);

            Assert.AreEqual(CutSlotKind.InProgress, next.Left.Kind);
            Assert.IsTrue(events.Exists(e => e.Kind == CutEventKind.CutStarted));
        }

        // it("silent drop when no GRIPPED hand available")
        [Test]
        public void SilentDropWhenNoGrippedHandAvailable()
        {
            var events = new List<CutTickEvent>();
            var next = CutAttemptOps.Tick(
                CutAttempts.Initial,
                CutAttemptTestHelpers.Inputs(
                    leftCommit: new CutCommit { Rs = new Vec2 { X = 1f, Y = 0f } }),
                events);

            Assert.AreEqual(CutSlotKind.Idle, next.Left.Kind);
            Assert.AreEqual(0, events.Count);
        }

        // it("resolution SUCCEEDS when attacker trigger < 0.5 at expiry")
        [Test]
        public void ResolutionSucceedsWhenAttackerTriggerBelowHalfAtExpiry()
        {
            var s = CutAttempts.Initial;
            var events = new List<CutTickEvent>();

            // Tick 1: attacker is gripping hard (0.8) but defender starts the cut.
            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:            0,
                    leftCommit:       new CutCommit { Rs = new Vec2 { X = 0f, Y = 0f } },
                    attackerLeft:     CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                    attackerTriggerL: 0.8f),
                events);
            events.Clear();

            // Tick 2: at expiry, attacker has let go (0.3 < 0.5) → cut succeeds.
            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:            CutTiming.Default.AttemptMs,
                    attackerLeft:     CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                    attackerTriggerL: 0.3f),
                events);

            Assert.IsTrue(events.Exists(e =>
                e.Kind == CutEventKind.CutSucceeded && e.AttackerSide == HandSide.L));
            Assert.AreEqual(CutSlotKind.Idle, s.Left.Kind);
        }

        // it("resolution FAILS when attacker trigger ≥ 0.5 at expiry")
        [Test]
        public void ResolutionFailsWhenAttackerTriggerAtLeastHalfAtExpiry()
        {
            var s = CutAttempts.Initial;
            var events = new List<CutTickEvent>();

            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:            0,
                    rightCommit:      new CutCommit { Rs = new Vec2 { X = 0f, Y = 0f } },
                    attackerRight:    CutAttemptTestHelpers.Gripped(HandSide.R, GripZone.CollarL),
                    attackerTriggerR: 0.4f),
                events);
            events.Clear();

            // Attacker re-asserted grip during the 1500ms attempt.
            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:            CutTiming.Default.AttemptMs,
                    attackerRight:    CutAttemptTestHelpers.Gripped(HandSide.R, GripZone.CollarL),
                    attackerTriggerR: 0.9f),
                events);

            Assert.IsTrue(events.Exists(e =>
                e.Kind == CutEventKind.CutFailed && e.DefenderSide == HandSide.R));
            Assert.AreEqual(CutSlotKind.Idle, s.Right.Kind);
        }

        // it("mid-attempt second commit is ignored")
        [Test]
        public void MidAttemptSecondCommitIsIgnored()
        {
            var s = CutAttempts.Initial;
            var events = new List<CutTickEvent>();

            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:        0,
                    leftCommit:   new CutCommit { Rs = new Vec2 { X = 0f, Y = 0f } },
                    attackerLeft: CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                events);
            events.Clear();

            // Second commit mid-attempt at t=500 (< 1500 expiry).
            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:        500,
                    leftCommit:   new CutCommit { Rs = new Vec2 { X = 0f, Y = 0f } },
                    attackerLeft: CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                events);

            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(CutSlotKind.InProgress, s.Left.Kind);
        }

        // it("both defender slots can run in parallel")
        [Test]
        public void BothDefenderSlotsCanRunInParallel()
        {
            var s = CutAttempts.Initial;
            var events = new List<CutTickEvent>();

            s = CutAttemptOps.Tick(
                s,
                CutAttemptTestHelpers.Inputs(
                    nowMs:         0,
                    leftCommit:    new CutCommit { Rs = new Vec2 { X = -1f, Y = 0f } },
                    rightCommit:   new CutCommit { Rs = new Vec2 { X =  1f, Y = 0f } },
                    attackerLeft:  CutAttemptTestHelpers.Gripped(HandSide.L, GripZone.SleeveR),
                    attackerRight: CutAttemptTestHelpers.Gripped(HandSide.R, GripZone.SleeveL)),
                events);

            Assert.AreEqual(CutSlotKind.InProgress, s.Left.Kind);
            Assert.AreEqual(CutSlotKind.InProgress, s.Right.Kind);
        }
    }
}
