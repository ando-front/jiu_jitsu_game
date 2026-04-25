// NUnit EditMode mirror of src/prototype/web/tests/unit/stamina.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class StaminaTest
    {
        // --- helpers -------------------------------------------------------

        static HandFSM Gripped(HandSide side, GripZone zone = GripZone.SleeveR) => new HandFSM
        {
            Side             = side,
            State            = HandState.Gripped,
            Target           = zone,
            StateEnteredMs   = 0,
            ReachDurationMs  = 0,
            LastParriedZone  = GripZone.None,
            LastParriedAtMs  = BJJConst.SentinelTimeMs,
        };

        static HandFSM Reaching(HandSide side) => new HandFSM
        {
            Side             = side,
            State            = HandState.Reaching,
            Target           = GripZone.SleeveR,
            StateEnteredMs   = 0,
            ReachDurationMs  = 275,
            LastParriedZone  = GripZone.None,
            LastParriedAtMs  = BJJConst.SentinelTimeMs,
        };

        static ActorState Actor(
            HandFSM? leftHand  = null,
            HandFSM? rightHand = null,
            Vec2 postureBreak  = default) =>
            new ActorState
            {
                LeftHand          = leftHand  ?? GameStateOps.InitialActorState().LeftHand,
                RightHand         = rightHand ?? GameStateOps.InitialActorState().RightHand,
                LeftFoot          = GameStateOps.InitialActorState().LeftFoot,
                RightFoot         = GameStateOps.InitialActorState().RightFoot,
                PostureBreak      = postureBreak,
                Stamina           = 1f,
                ArmExtractedLeft  = false,
                ArmExtractedRight = false,
            };

        static StaminaInputs Inputs(
            float dtMs        = 1000f,
            ActorState? actor = null,
            float hipPush     = 0f,
            float hipLateral  = 0f,
            float triggerL    = 0f,
            float triggerR    = 0f,
            bool breathPressed = false) =>
            new StaminaInputs
            {
                DtMs          = dtMs,
                Actor         = actor ?? Actor(),
                AttackerHip   = new HipIntent { HipPush = hipPush, HipLateral = hipLateral },
                TriggerL      = triggerL,
                TriggerR      = triggerR,
                BreathPressed = breathPressed,
            };

        // --- drain tests -------------------------------------------------------

        [Test]
        public void ActiveHand_Reaching_Drains_At_HandActiveDrainPerSec()
        {
            float s = StaminaOps.UpdateStamina(1f, Inputs(actor: Actor(leftHand: Reaching(HandSide.L))));
            float expected = 1f - StaminaConfig.Default.HandActiveDrainPerSec;
            Assert.AreEqual(expected, s, 1e-4f);
        }

        [Test]
        public void Gripped_WithHighStrength_Drains()
        {
            float s = StaminaOps.UpdateStamina(1f,
                Inputs(actor: Actor(leftHand: Gripped(HandSide.L)), triggerL: 0.8f));
            Assert.Less(s, 1f);
        }

        [Test]
        public void Gripped_WithLowStrength_DoesNotDrain_FromHandActiveClause()
        {
            float s = StaminaOps.UpdateStamina(1f,
                Inputs(actor: Actor(leftHand: Gripped(HandSide.L)), triggerL: 0.3f));
            Assert.AreEqual(1f, s, 1e-5f);
        }

        [Test]
        public void PostureBreak_AddsDrain_ProportionalToMagnitude()
        {
            // Expected per-second: 0.02 (hand active) + 0.05 × 0.5 (posture) = 0.045
            float s = StaminaOps.UpdateStamina(1f, Inputs(
                actor: Actor(leftHand: Reaching(HandSide.L), postureBreak: new Vec2(0.5f, 0f))));
            float expected = 1f - (StaminaConfig.Default.HandActiveDrainPerSec + StaminaConfig.Default.PostureMaintainDrainPerSec * 0.5f);
            Assert.AreEqual(expected, s, 1e-4f);
        }

        // --- recovery tests -------------------------------------------------------

        [Test]
        public void BtnBreath_NoGrips_StaticHip_Recovers_AtBreathRate()
        {
            float s = StaminaOps.UpdateStamina(0.5f, Inputs(breathPressed: true));
            float expected = 0.5f + StaminaConfig.Default.BreathRecoverPerSec;
            Assert.AreEqual(expected, s, 1e-4f);
        }

        [Test]
        public void BtnBreath_MovingHip_DoesNotRecover()
        {
            float s = StaminaOps.UpdateStamina(0.5f, Inputs(breathPressed: true, hipPush: 0.6f));
            Assert.AreEqual(0.5f, s, 1e-4f);
        }

        [Test]
        public void AllLimbsIdle_NoGrips_Recovers_AtIdleRate()
        {
            float s = StaminaOps.UpdateStamina(0.5f, Inputs());
            float expected = 0.5f + StaminaConfig.Default.IdleRecoverPerSec;
            Assert.AreEqual(expected, s, 1e-4f);
        }

        // --- clamps / thresholds -------------------------------------------------------

        [Test]
        public void Stamina_NeverExceeds_One()
        {
            float s = StaminaOps.UpdateStamina(0.99f, Inputs(breathPressed: true));
            Assert.LessOrEqual(s, 1f);
        }

        [Test]
        public void Stamina_NeverGoesBelow_Zero()
        {
            float s = StaminaOps.UpdateStamina(0.01f, Inputs(
                actor: Actor(
                    leftHand:  Gripped(HandSide.L),
                    rightHand: Gripped(HandSide.R),
                    postureBreak: new Vec2(1f, 0f)),
                triggerL: 1f, triggerR: 1f));
            Assert.GreaterOrEqual(s, 0f);
        }

        [Test]
        public void GripStrengthCeiling_CapAt0_6_WhenStaminaBelow0_2()
        {
            Assert.AreEqual(0.6f, StaminaOps.GripStrengthCeiling(0.1f));
            Assert.AreEqual(1f,   StaminaOps.GripStrengthCeiling(0.5f));
        }

        [Test]
        public void CanStartReach_Rejects_WhenStaminaBelow0_05()
        {
            Assert.IsFalse(StaminaOps.CanStartReach(0.04f));
            Assert.IsTrue( StaminaOps.CanStartReach(0.05f));
        }

        [Test]
        public void ApplyConfirmCost_Deducts_Flat0_1()
        {
            Assert.AreEqual(0.4f, StaminaOps.ApplyConfirmCost(0.5f), 1e-5f);
        }
    }
}
