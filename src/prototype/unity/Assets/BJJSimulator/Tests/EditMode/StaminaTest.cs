// NUnit EditMode mirror of src/prototype/web/tests/unit/stamina.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class StaminaTest
    {
        private static HandFSM GrippedHand(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = GripZone.SleeveR,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static HandFSM ReachingHand(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Reaching,
            Target          = GripZone.SleeveR,
            StateEnteredMs  = 0,
            ReachDurationMs = 275,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static ActorState Actor(
            HandFSM? leftHand  = null,
            HandFSM? rightHand = null,
            Vec2?    postureBreak = null)
        {
            var a = ActorState.Initial();
            if (leftHand.HasValue)   a.LeftHand    = leftHand.Value;
            if (rightHand.HasValue)  a.RightHand   = rightHand.Value;
            if (postureBreak.HasValue) a.PostureBreak = postureBreak.Value;
            return a;
        }

        private static StaminaInputs Inputs(
            ActorState? actor   = null,
            HipIntent?  hip     = null,
            float       trigL   = 0f,
            float       trigR   = 0f,
            bool        breath  = false,
            float       dtMs    = 1000f)
        {
            return new StaminaInputs
            {
                DtMs          = dtMs,
                Actor         = actor  ?? Actor(),
                AttackerHip   = hip    ?? HipIntent.Zero,
                TriggerL      = trigL,
                TriggerR      = trigR,
                BreathPressed = breath,
            };
        }

        // ------------------------------------------------------------------
        // Drain
        // ------------------------------------------------------------------

        [Test]
        public void ActiveHandReachingDrainsAt002PerSec()
        {
            float s = StaminaOps.Update(1f, Inputs(actor: Actor(leftHand: ReachingHand(HandSide.L))));
            Assert.AreEqual(1f - StaminaConfig.Default.HandActiveDrainPerSec, s, 1e-4f);
        }

        [Test]
        public void GrippedWithHighStrengthDrains()
        {
            float s = StaminaOps.Update(1f, Inputs(actor: Actor(leftHand: GrippedHand(HandSide.L)), trigL: 0.8f));
            Assert.Less(s, 1f);
        }

        [Test]
        public void GrippedWithLowStrengthDoesNotDrainFromHandClause()
        {
            float s = StaminaOps.Update(1f, Inputs(actor: Actor(leftHand: GrippedHand(HandSide.L)), trigL: 0.3f));
            Assert.AreEqual(1f, s, 1e-5f);
        }

        [Test]
        public void PostureBreakAddsProportionalDrain()
        {
            // Expected: 0.02 (hand) + 0.05 × 0.5 (posture) = 0.045 per second.
            float s = StaminaOps.Update(1f, Inputs(
                actor: Actor(leftHand: ReachingHand(HandSide.L), postureBreak: new Vec2(0.5f, 0f))));
            float expected = 1f - (StaminaConfig.Default.HandActiveDrainPerSec + 0.025f);
            Assert.AreEqual(expected, s, 1e-4f);
        }

        // ------------------------------------------------------------------
        // Recovery
        // ------------------------------------------------------------------

        [Test]
        public void BtnBreathPlusNoGripsPlusStaticHipRecovers()
        {
            float s = StaminaOps.Update(0.5f, Inputs(breath: true));
            Assert.AreEqual(0.5f + StaminaConfig.Default.BreathRecoverPerSec, s, 1e-4f);
        }

        [Test]
        public void BtnBreathFailsToRecoverIfHipIsMoving()
        {
            float s = StaminaOps.Update(0.5f, Inputs(
                breath: true,
                hip: new HipIntent { HipPush = 0.6f }));
            Assert.AreEqual(0.5f, s, 1e-4f);
        }

        [Test]
        public void AllLimbsIdleRecoversAt003PerSec()
        {
            float s = StaminaOps.Update(0.5f, Inputs());
            Assert.AreEqual(0.5f + StaminaConfig.Default.IdleRecoverPerSec, s, 1e-4f);
        }

        // ------------------------------------------------------------------
        // Clamps and thresholds
        // ------------------------------------------------------------------

        [Test]
        public void NeverExceedsOne()
        {
            float s = StaminaOps.Update(0.99f, Inputs(breath: true));
            Assert.LessOrEqual(s, 1f);
        }

        [Test]
        public void NeverGoesBelowZero()
        {
            float s = StaminaOps.Update(0.01f, Inputs(
                actor: Actor(
                    leftHand:    GrippedHand(HandSide.L),
                    rightHand:   GrippedHand(HandSide.R),
                    postureBreak: new Vec2(1f, 0f)),
                trigL: 1f, trigR: 1f));
            Assert.GreaterOrEqual(s, 0f);
        }

        [Test]
        public void GripStrengthCeilingCapsAt06WhenStaminaLow()
        {
            Assert.AreEqual(StaminaConfig.Default.LowGripCap, StaminaOps.GripStrengthCeiling(0.1f), 1e-6f);
            Assert.AreEqual(1f,                               StaminaOps.GripStrengthCeiling(0.5f), 1e-6f);
        }

        [Test]
        public void CanStartReachRejectsWhenStaminaTooLow()
        {
            Assert.IsFalse(StaminaOps.CanStartReach(0.04f));
            Assert.IsTrue (StaminaOps.CanStartReach(0.05f));
        }

        [Test]
        public void ApplyConfirmCostDeductsPoint1()
        {
            Assert.AreEqual(0.4f, StaminaOps.ApplyConfirmCost(0.5f), 1e-5f);
        }
    }
}
