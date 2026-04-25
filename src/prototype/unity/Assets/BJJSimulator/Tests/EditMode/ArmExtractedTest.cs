// NUnit EditMode mirror of src/prototype/web/tests/unit/arm_extracted.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class ArmExtractedTest
    {
        static HandFSM GrippedAt(GripZone zone, HandSide side) => new HandFSM
        {
            Side             = side,
            State            = HandState.Gripped,
            Target           = zone,
            StateEnteredMs   = 0,
            ReachDurationMs  = 0,
            LastParriedZone  = GripZone.None,
            LastParriedAtMs  = BJJConst.SentinelTimeMs,
        };

        static HandFSM IdleHand(HandSide side) => new HandFSM
        {
            Side             = side,
            State            = HandState.Idle,
            Target           = GripZone.None,
            StateEnteredMs   = 0,
            ReachDurationMs  = 0,
            LastParriedZone  = GripZone.None,
            LastParriedAtMs  = BJJConst.SentinelTimeMs,
        };

        static ArmExtractedInputs BaseInputs(
            long nowMs          = 0,
            float dtMs          = 16.67f,
            HandFSM? leftHand   = null,
            HandFSM? rightHand  = null,
            float triggerL      = 0f,
            float triggerR      = 0f,
            float hipPush       = 0f,
            bool  defenderBase  = false) =>
            new ArmExtractedInputs
            {
                NowMs            = nowMs,
                DtMs             = dtMs,
                BottomLeftHand   = leftHand  ?? IdleHand(HandSide.L),
                BottomRightHand  = rightHand ?? IdleHand(HandSide.R),
                TriggerL         = triggerL,
                TriggerR         = triggerR,
                AttackerHip      = new HipIntent { HipPush = hipPush },
                DefenderBaseHold = defenderBase,
            };

        // Helper: drive until flag is true via the standard extract sequence.
        ArmExtractedState BuildExtracted(float step, out long setAt)
        {
            var s = ArmExtractedState.Initial;
            int frames = (int)System.Math.Ceiling(ArmExtractedConfig.Default.RequiredSustainMs / step);
            long now   = 0;
            for (int i = 0; i < frames; i++, now = (long)(i * step))
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    now,
                    dtMs:     step,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    triggerL: 0.9f,
                    hipPush: -0.6f));
            }
            setAt = s.LeftSetAtMs;
            return s;
        }

        // -------------------------------------------------------------------------

        [Test]
        public void GripOnSleeveR_WithStrongPull_For1500ms_Sets_RightArmExtracted()
        {
            float step  = 16.67f;
            int   frames = (int)System.Math.Ceiling(ArmExtractedConfig.Default.RequiredSustainMs / step);
            var   s     = ArmExtractedState.Initial;
            for (int i = 0; i < frames; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    (long)(i * step),
                    dtMs:     step,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    triggerL: 0.8f,
                    hipPush: -0.5f));
            }
            Assert.IsTrue(s.Right, "right should be extracted");
            Assert.IsFalse(s.Left, "left should not be extracted");
        }

        [Test]
        public void WeakTrigger_FailsToExtract_EvenWithSustainedTime()
        {
            var s = ArmExtractedState.Initial;
            for (int i = 0; i < 120; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    (long)(i * 16.67f),
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    triggerL: 0.3f,
                    hipPush: -0.5f));
            }
            Assert.IsFalse(s.Right);
        }

        [Test]
        public void NoHipPull_FailsToSustain()
        {
            var s = ArmExtractedState.Initial;
            for (int i = 0; i < 120; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    (long)(i * 16.67f),
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    triggerL: 0.8f,
                    hipPush: 0f));
            }
            Assert.IsFalse(s.Right);
        }

        [Test]
        public void ReleasingGrip_Clears_PreviouslySetFlag()
        {
            long setAt;
            var s = BuildExtracted(16.67f, out setAt);
            Assert.IsTrue(s.Right, "precondition: right was extracted");

            s = ArmExtractedOps.Update(s, BaseInputs(
                nowMs: (long)(s.RightSustainMs + 16.67f),
                leftHand: IdleHand(HandSide.L)));
            Assert.IsFalse(s.Right, "should be cleared after releasing grip");
        }

        [Test]
        public void AutoReset_Clears_FlagAfter5s_EvenWhilePulling()
        {
            float step = 50f;
            long  setAt;
            var   s = BuildExtracted(step, out setAt);
            Assert.IsTrue(s.Right, "precondition: flag is set");
            Assert.AreNotEqual(BJJConst.SentinelTimeMs, s.RightSetAtMs, "setAtMs must not be sentinel");

            long resetTick = s.RightSetAtMs + ArmExtractedConfig.Default.AutoResetAfterMs;
            s = ArmExtractedOps.Update(s, BaseInputs(
                nowMs:    resetTick,
                dtMs:     step,
                leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                triggerL: 0.9f,
                hipPush: -0.6f));
            Assert.IsFalse(s.Right, "auto-reset should have cleared the flag");
        }
    }
}
