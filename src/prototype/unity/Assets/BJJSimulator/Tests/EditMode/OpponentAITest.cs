// NUnit EditMode mirror of src/prototype/web/tests/unit/opponent_ai.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/opponent_ai_v1.md.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public static class OpponentAITestHelpers
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

        public static FootFSM Foot(FootSide side, FootState state) => new FootFSM
        {
            Side           = side,
            State          = state,
            StateEnteredMs = 0,
        };

        public static GameState Base() => GameStateOps.InitialGameState(0);

        public static bool DiscreteContains(DefenseDiscreteIntent[] discrete, DefenseDiscreteIntentKind kind)
        {
            if (discrete == null) return false;
            foreach (var d in discrete) if (d.Kind == kind) return true;
            return false;
        }

        public static bool DiscreteContains(DiscreteIntent[] discrete, DiscreteIntentKind kind)
        {
            if (discrete == null) return false;
            foreach (var d in discrete) if (d.Kind == kind) return true;
            return false;
        }

        public static bool DiscreteContains(
            DiscreteIntent[] discrete, DiscreteIntentKind kind, FootSide side)
        {
            if (discrete == null) return false;
            foreach (var d in discrete)
                if (d.Kind == kind && d.FootSide == side) return true;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // describe("Top AI — priority 1 counter window commit")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopAICounterWindowCommitTests
    {
        // it("commits TRIANGLE_EARLY_STACK when candidate is available")
        [Test]
        public void CommitsTriangleEarlyStackWhenCandidateIsAvailable()
        {
            var g = OpponentAITestHelpers.Base();
            g.CounterWindow = new CounterWindow
            {
                State           = CounterWindowState.Open,
                StateEnteredMs  = 0,
                Candidates      = new[] { CounterTechnique.TriangleEarlyStack },
                CooldownUntilMs = BJJConst.SentinelTimeMs,
            };

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.AreEqual(AIOutputRole.Top,                    outAI.Role);
            Assert.AreEqual(1f,                                   outAI.Defense.Hip.WeightForward);
            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(outAI.Defense.Discrete, DefenseDiscreteIntentKind.RecoveryHold));
            Assert.AreEqual(CounterTechnique.TriangleEarlyStack, outAI.ConfirmedCounter);
        }

        // it("commits SCISSOR_COUNTER in opposite direction to sweep")
        [Test]
        public void CommitsScissorCounterInOppositeDirectionToSweep()
        {
            var g = OpponentAITestHelpers.Base();
            g.CounterWindow = new CounterWindow
            {
                State           = CounterWindowState.Open,
                StateEnteredMs  = 0,
                Candidates      = new[] { CounterTechnique.ScissorCounter },
                CooldownUntilMs = BJJConst.SentinelTimeMs,
            };
            g.AttackerSweepLateralSign = 1;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.AreEqual(-1f,                              outAI.Defense.Hip.WeightLateral);
            Assert.AreEqual(CounterTechnique.ScissorCounter,  outAI.ConfirmedCounter);
        }

        // it("leaves confirmedCounter null when counter window is closed")
        [Test]
        public void LeavesConfirmedCounterNullWhenCounterWindowIsClosed()
        {
            var outAI = OpponentAI.OpponentIntentFor(OpponentAITestHelpers.Base(), AIOutputRole.Top);
            Assert.IsNull(outAI.ConfirmedCounter);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Top AI — priority 3 posture recovery")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopAIPostureRecoveryTests
    {
        // it("RECOVERY_HOLD when sagittal break ≥ 0.5")
        [Test]
        public void RecoveryHoldWhenSagittalBreakAtLeastHalf()
        {
            var g = OpponentAITestHelpers.Base();
            var top = GameStateOps.InitialActorState(0);
            top.PostureBreak = new Vec2(0f, 0.7f);
            g.Top = top;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.AreEqual(1f, outAI.Defense.Hip.WeightForward);
            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(outAI.Defense.Discrete, DefenseDiscreteIntentKind.RecoveryHold));
        }
    }

    // -------------------------------------------------------------------------
    // describe("Top AI — priority 4 cut attempt")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopAICutAttemptTests
    {
        // it("fires CUT_ATTEMPT on attacker's GRIPPED hand")
        [Test]
        public void FiresCutAttemptOnAttackersGrippedHand()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftHand = OpponentAITestHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            g.Bottom = bottom;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(outAI.Defense.Discrete, DefenseDiscreteIntentKind.CutAttempt));
        }

        // it("does NOT fire a new CUT_ATTEMPT while both slots are busy")
        [Test]
        public void DoesNotFireANewCutAttemptWhileBothSlotsAreBusy()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftHand = OpponentAITestHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            g.Bottom = bottom;
            g.CutAttempts = new CutAttempts
            {
                Left = new CutAttemptSlot
                {
                    Kind                 = CutSlotKind.InProgress,
                    StartedMs            = 0,
                    TargetAttackerSide   = HandSide.L,
                    TargetZone           = GripZone.SleeveR,
                },
                Right = new CutAttemptSlot
                {
                    Kind                 = CutSlotKind.InProgress,
                    StartedMs            = 0,
                    TargetAttackerSide   = HandSide.L,
                    TargetZone           = GripZone.SleeveR,
                },
            };

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.IsFalse(OpponentAITestHelpers.DiscreteContains(outAI.Defense.Discrete, DefenseDiscreteIntentKind.CutAttempt));
        }
    }

    // -------------------------------------------------------------------------
    // describe("Top AI — priority 7 breath")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopAIBreathTests
    {
        // it("emits BREATH_START when stamina < 0.3 (and no higher-priority trigger)")
        [Test]
        public void EmitsBreathStartWhenStaminaBelowThreshold()
        {
            var g = OpponentAITestHelpers.Base();
            var top = GameStateOps.InitialActorState(0);
            top.Stamina = 0.1f;
            g.Top = top;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(outAI.Defense.Discrete, DefenseDiscreteIntentKind.BreathStart));
        }
    }

    // -------------------------------------------------------------------------
    // describe("Top AI — idle fallback")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopAIIdleFallbackTests
    {
        // it("returns ZERO_DEFENSE_INTENT when nothing interesting is happening")
        [Test]
        public void ReturnsZeroDefenseIntentWhenNothingInterestingIsHappening()
        {
            // No attacker grips (so no cut), no arm_extracted, no posture break,
            // and bottom two feet locked but 0 grips keeps priority 6 active
            // unless we bump top stamina below 0.5 so the pass-prep rule skips.
            // We want to test true idle: both feet UNLOCKED removes the
            // bothFeetLocked condition in priority 6.
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftFoot  = OpponentAITestHelpers.Foot(FootSide.L, FootState.Unlocked);
            bottom.RightFoot = OpponentAITestHelpers.Foot(FootSide.R, FootState.Unlocked);
            g.Bottom = bottom;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Top);

            Assert.IsNotNull(outAI.Defense.Discrete);
            Assert.AreEqual(0, outAI.Defense.Discrete.Length);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — priority 1 commit")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAICommitTests
    {
        // it("commits first candidate when judgment window is OPEN")
        [Test]
        public void CommitsFirstCandidateWhenJudgmentWindowIsOpen()
        {
            var g = OpponentAITestHelpers.Base();
            g.JudgmentWindow = new JudgmentWindow
            {
                State           = JudgmentWindowState.Open,
                StateEnteredMs  = 0,
                Candidates      = new[] { Technique.ScissorSweep },
                CooldownUntilMs = BJJConst.SentinelTimeMs,
                FiredBy         = WindowSide.Bottom,
            };

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            // SCISSOR_SWEEP commit pattern: LS horizontal + L_BUMPER toggle.
            Assert.AreEqual(1f, Math.Abs(outAI.Intent.Hip.HipLateral));
            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(
                outAI.Intent.Discrete, DiscreteIntentKind.FootHookToggle, FootSide.L));
            Assert.AreEqual(Technique.ScissorSweep, outAI.ConfirmedTechnique);
        }

        // it("leaves confirmedTechnique null when judgment window is closed")
        [Test]
        public void LeavesConfirmedTechniqueNullWhenJudgmentWindowIsClosed()
        {
            var outAI = OpponentAI.OpponentIntentFor(OpponentAITestHelpers.Base(), AIOutputRole.Bottom);
            Assert.IsNull(outAI.ConfirmedTechnique);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — priority 2 first grip")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAIFirstGripTests
    {
        // it("reaches for SLEEVE_R with left hand when no grips yet")
        [Test]
        public void ReachesForSleeveRWithLeftHandWhenNoGripsYet()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.Stamina = 0.8f;
            g.Bottom = bottom;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            Assert.AreEqual(GripZone.SleeveR, outAI.Intent.Grip.LHandTarget);
            Assert.Greater(outAI.Intent.Grip.LGripStrength, 0.5f);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — priority 3 second grip")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAISecondGripTests
    {
        // it("with one hand GRIPPED, reaches COLLAR with the free hand")
        [Test]
        public void WithOneHandGrippedReachesCollarWithTheFreeHand()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftHand = OpponentAITestHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            g.Bottom = bottom;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            // Left is occupied → right should be aimed at COLLAR_R.
            Assert.AreEqual(GripZone.CollarR, outAI.Intent.Grip.RHandTarget);
            Assert.AreEqual(GripZone.SleeveR, outAI.Intent.Grip.LHandTarget);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — priority 4 hip push for break")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAIHipPushTests
    {
        // it("with two grips + break < 0.4, pushes hip forward")
        [Test]
        public void WithTwoGripsAndBreakBelowThresholdPushesHipForward()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftHand  = OpponentAITestHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            bottom.RightHand = OpponentAITestHelpers.Gripped(HandSide.R, GripZone.CollarL);
            g.Bottom = bottom;
            var top = GameStateOps.InitialActorState(0);
            top.PostureBreak = new Vec2(0.1f, 0.1f);
            g.Top = top;

            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            Assert.Greater(outAI.Intent.Hip.HipPush, 0.5f);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — priority 5 breath")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAIBreathTests
    {
        // it("emits BREATH_START when stamina low")
        [Test]
        public void EmitsBreathStartWhenStaminaLow()
        {
            var g = OpponentAITestHelpers.Base();
            var bottom = GameStateOps.InitialActorState(0);
            bottom.LeftHand  = OpponentAITestHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            bottom.RightHand = OpponentAITestHelpers.Gripped(HandSide.R, GripZone.CollarL);
            bottom.Stamina   = 0.1f;
            g.Bottom = bottom;
            var top = GameStateOps.InitialActorState(0);
            top.PostureBreak = new Vec2(0.5f, 0.5f);
            g.Top = top;

            // Only priority 4 would trigger, but break is already ≥ 0.4, so we
            // proceed to priority 5. Expect breath.
            var outAI = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            Assert.IsTrue(OpponentAITestHelpers.DiscreteContains(outAI.Intent.Discrete, DiscreteIntentKind.BreathStart));
        }
    }

    // -------------------------------------------------------------------------
    // describe("Bottom AI — determinism")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BottomAIDeterminismTests
    {
        // it("same input → same output")
        [Test]
        public void SameInputProducesSameOutput()
        {
            var g = OpponentAITestHelpers.Base();
            var out1 = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);
            var out2 = OpponentAI.OpponentIntentFor(g, AIOutputRole.Bottom);

            Assert.AreEqual(out1.Role,                              out2.Role);
            Assert.AreEqual(out1.ConfirmedTechnique,                out2.ConfirmedTechnique);
            Assert.AreEqual(out1.Intent.Hip.HipAngleTarget,         out2.Intent.Hip.HipAngleTarget);
            Assert.AreEqual(out1.Intent.Hip.HipPush,                out2.Intent.Hip.HipPush);
            Assert.AreEqual(out1.Intent.Hip.HipLateral,             out2.Intent.Hip.HipLateral);
            Assert.AreEqual(out1.Intent.Grip.LHandTarget,           out2.Intent.Grip.LHandTarget);
            Assert.AreEqual(out1.Intent.Grip.LGripStrength,         out2.Intent.Grip.LGripStrength);
            Assert.AreEqual(out1.Intent.Grip.RHandTarget,           out2.Intent.Grip.RHandTarget);
            Assert.AreEqual(out1.Intent.Grip.RGripStrength,         out2.Intent.Grip.RGripStrength);
            Assert.AreEqual(out1.Intent.Discrete?.Length ?? 0,      out2.Intent.Discrete?.Length ?? 0);
        }
    }

    // -------------------------------------------------------------------------
    // describe("opponentIntent role wiring")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class OpponentIntentRoleWiringTests
    {
        // it("role='Top' produces defense-shaped output")
        [Test]
        public void RoleTopProducesDefenseShapedOutput()
        {
            var outAI = OpponentAI.OpponentIntentFor(OpponentAITestHelpers.Base(), AIOutputRole.Top);
            Assert.AreEqual(AIOutputRole.Top, outAI.Role);
        }

        // it("role='Bottom' produces intent-shaped output")
        [Test]
        public void RoleBottomProducesIntentShapedOutput()
        {
            var outAI = OpponentAI.OpponentIntentFor(OpponentAITestHelpers.Base(), AIOutputRole.Bottom);
            Assert.AreEqual(AIOutputRole.Bottom, outAI.Role);
        }

        // it("idle top helper references")  — unused-import guard in TS
        [Test]
        public void IdleAndFootHelperReferences()
        {
            Assert.AreEqual(HandState.Idle,  OpponentAITestHelpers.Idle(HandSide.L).State);
            Assert.AreEqual(FootState.Locked, OpponentAITestHelpers.Foot(FootSide.L, FootState.Locked).State);
        }
    }
}
