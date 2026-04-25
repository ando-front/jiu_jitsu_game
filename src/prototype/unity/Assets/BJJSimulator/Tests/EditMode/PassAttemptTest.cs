// NUnit EditMode mirror of src/prototype/web/tests/unit/pass_attempt.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Reference: docs/design/input_system_defense_v1.md §B.7
//            (eligibility predicate + pass-attempt FSM lifecycle).
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors the foot() / actor() factories in TS).
    // -------------------------------------------------------------------------

    public static class PassAttemptTestHelpers
    {
        public static FootFSM Foot(FootSide side, FootState state) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = 0 };

        public static ActorState Actor(
            FootFSM? leftFoot  = null,
            FootFSM? rightFoot = null)
        {
            var a = GameStateOps.InitialActorState(0);
            if (leftFoot.HasValue)  a.LeftFoot  = leftFoot.Value;
            if (rightFoot.HasValue) a.RightFoot = rightFoot.Value;
            return a;
        }
    }

    // -------------------------------------------------------------------------
    // describe("isPassEligible (§B.7.1)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class IsPassEligibleTests
    {
        // it("accepts a well-formed commit")
        [Test]
        public void AcceptsAWellFormedCommit()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Unlocked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.5f,
                LeftBasePressure  = 0.7f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.BicepL,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = -0.8f,
                Guard             = GuardState.Closed,
            });

            Assert.IsTrue(ok);
        }

        // it("rejects when both feet are LOCKED")
        [Test]
        public void RejectsWhenBothFeetAreLocked()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Locked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.5f,
                LeftBasePressure  = 0.7f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.BicepL,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = -0.8f,
                Guard             = GuardState.Closed,
            });

            Assert.IsFalse(ok);
        }

        // it("rejects when stamina < 0.2")
        [Test]
        public void RejectsWhenStaminaBelowTwoTenths()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Unlocked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.1f,
                LeftBasePressure  = 0.7f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.BicepL,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = -0.8f,
                Guard             = GuardState.Closed,
            });

            Assert.IsFalse(ok);
        }

        // it("rejects when a hand is on CHEST (not a control zone)")
        [Test]
        public void RejectsWhenAHandIsOnChestNotAControlZone()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Unlocked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.5f,
                LeftBasePressure  = 0.7f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.Chest,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = -0.8f,
                Guard             = GuardState.Closed,
            });

            Assert.IsFalse(ok);
        }

        // it("rejects when pressure is below 0.5")
        [Test]
        public void RejectsWhenPressureIsBelowHalf()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Unlocked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.5f,
                LeftBasePressure  = 0.3f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.BicepL,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = -0.8f,
                Guard             = GuardState.Closed,
            });

            Assert.IsFalse(ok);
        }

        // it("rejects when RS isn't pointing downward")
        [Test]
        public void RejectsWhenRsIsNotPointingDownward()
        {
            bool ok = PassAttemptOps.IsPassEligible(new PassEligibilityParams
            {
                Bottom            = PassAttemptTestHelpers.Actor(
                                        leftFoot:  PassAttemptTestHelpers.Foot(FootSide.L, FootState.Unlocked),
                                        rightFoot: PassAttemptTestHelpers.Foot(FootSide.R, FootState.Locked)),
                Top               = PassAttemptTestHelpers.Actor(),
                DefenderStamina   = 0.5f,
                LeftBasePressure  = 0.7f,
                RightBasePressure = 0.8f,
                LeftBaseZone      = BaseZone.BicepL,
                RightBaseZone     = BaseZone.KneeR,
                RsY               = 0.5f,
                Guard             = GuardState.Closed,
            });

            Assert.IsFalse(ok);
        }
    }

    // -------------------------------------------------------------------------
    // describe("tickPassAttempt lifecycle (§B.7.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TickPassAttemptLifecycleTests
    {
        // it("commit + eligible → IN_PROGRESS and PASS_STARTED event")
        [Test]
        public void CommitPlusEligibleEntersInProgressAndEmitsPassStarted()
        {
            var events = new List<PassTickEvent>();
            var next = PassAttemptOps.Tick(
                PassAttemptState.Idle,
                new PassTickInput
                {
                    NowMs                              = 0,
                    CommitRequested                    = true,
                    EligibleNow                        = true,
                    AttackerTriangleConfirmedThisTick = false,
                },
                events);

            Assert.AreEqual(PassAttemptKind.InProgress, next.Kind);
            Assert.AreEqual(1,                          events.Count);
            Assert.AreEqual(PassEventKind.PassStarted,  events[0].Kind);
        }

        // it("ineligible commit is silently ignored")
        [Test]
        public void IneligibleCommitIsSilentlyIgnored()
        {
            var events = new List<PassTickEvent>();
            var next = PassAttemptOps.Tick(
                PassAttemptState.Idle,
                new PassTickInput
                {
                    NowMs                              = 0,
                    CommitRequested                    = true,
                    EligibleNow                        = false,
                    AttackerTriangleConfirmedThisTick = false,
                },
                events);

            Assert.AreEqual(PassAttemptKind.Idle, next.Kind);
            Assert.AreEqual(0,                    events.Count);
        }

        // it("triangle confirm during progress → PASS_FAILED and back to IDLE")
        [Test]
        public void TriangleConfirmDuringProgressFailsAndReturnsToIdle()
        {
            var prev = new PassAttemptState
            {
                Kind      = PassAttemptKind.InProgress,
                StartedMs = 0,
            };

            var events = new List<PassTickEvent>();
            var next = PassAttemptOps.Tick(
                prev,
                new PassTickInput
                {
                    NowMs                              = 1000,
                    CommitRequested                    = false,
                    EligibleNow                        = true,
                    AttackerTriangleConfirmedThisTick = true,
                },
                events);

            Assert.AreEqual(PassAttemptKind.Idle, next.Kind);
            Assert.IsTrue(events.Exists(e => e.Kind == PassEventKind.PassFailed));
        }

        // it("window elapses without triangle → PASS_SUCCEEDED and back to IDLE")
        [Test]
        public void WindowElapsesWithoutTriangleSucceedsAndReturnsToIdle()
        {
            var prev = new PassAttemptState
            {
                Kind      = PassAttemptKind.InProgress,
                StartedMs = 0,
            };

            var events = new List<PassTickEvent>();
            var next = PassAttemptOps.Tick(
                prev,
                new PassTickInput
                {
                    NowMs                              = PassTiming.Default.WindowMs + 1,
                    CommitRequested                    = false,
                    EligibleNow                        = true,
                    AttackerTriangleConfirmedThisTick = false,
                },
                events);

            Assert.AreEqual(PassAttemptKind.Idle, next.Kind);
            Assert.IsTrue(events.Exists(e => e.Kind == PassEventKind.PassSucceeded));
        }

        // it("in-progress with time remaining does nothing")
        [Test]
        public void InProgressWithTimeRemainingDoesNothing()
        {
            var prev = new PassAttemptState
            {
                Kind      = PassAttemptKind.InProgress,
                StartedMs = 0,
            };

            var events = new List<PassTickEvent>();
            var next = PassAttemptOps.Tick(
                prev,
                new PassTickInput
                {
                    NowMs                              = 1000,
                    CommitRequested                    = false,
                    EligibleNow                        = true,
                    AttackerTriangleConfirmedThisTick = false,
                },
                events);

            Assert.AreEqual(PassAttemptKind.InProgress, next.Kind);
            Assert.AreEqual(0,                          events.Count);
        }
    }
}
