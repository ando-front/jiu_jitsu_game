// NUnit EditMode mirror of src/prototype/web/tests/unit/arm_extracted.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class ArmExtractedTest
    {
        private static HandFSM GrippedAt(GripZone zone, HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = zone,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static HandFSM IdleHand(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Idle,
            Target          = GripZone.None,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static ArmExtractedInputs BaseInputs(
            float nowMs         = 0f,
            float dtMs          = 16.67f,
            HandFSM? leftHand   = null,
            HandFSM? rightHand  = null,
            float trigL         = 0f,
            float trigR         = 0f,
            float hipPush       = 0f,
            bool defBaseHold    = false)
        {
            return new ArmExtractedInputs
            {
                NowMs            = (long)nowMs,
                DtMs             = dtMs,
                BottomLeftHand   = leftHand  ?? IdleHand(HandSide.L),
                BottomRightHand  = rightHand ?? IdleHand(HandSide.R),
                TriggerL         = trigL,
                TriggerR         = trigR,
                AttackerHip      = new HipIntent { HipPush = hipPush },
                DefenderBaseHold = defBaseHold,
            };
        }

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        [Test]
        public void GripOnSleeveRWithStrongPullFor1p5s_ExtractsRightArm()
        {
            var s = ArmExtractedOps.Initial;
            int frames = (int)System.Math.Ceiling(
                ArmExtractedConfig.Default.RequiredSustainMs / 16.67f);

            for (int i = 0; i < frames; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    i * 16.67f,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    trigL:    0.8f,
                    hipPush:  -0.5f));
            }
            Assert.IsTrue (s.Right, "right arm extracted");
            Assert.IsFalse(s.Left,  "left arm not extracted");
        }

        [Test]
        public void WeakTriggerFailsToExtractEvenWithSustainedTime()
        {
            var s = ArmExtractedOps.Initial;
            for (int i = 0; i < 120; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    i * 16.67f,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    trigL:    0.3f,
                    hipPush:  -0.5f));
            }
            Assert.IsFalse(s.Right);
        }

        [Test]
        public void NoHipPullFailsToSustain()
        {
            var s = ArmExtractedOps.Initial;
            for (int i = 0; i < 120; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    i * 16.67f,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    trigL:    0.8f,
                    hipPush:  0f)); // no pull
            }
            Assert.IsFalse(s.Right);
        }

        [Test]
        public void ReleasingGripClearsPreviouslySetFlag()
        {
            var s = ArmExtractedOps.Initial;
            int buildFrames = (int)System.Math.Ceiling(
                ArmExtractedConfig.Default.RequiredSustainMs / 16.67f);

            for (int i = 0; i < buildFrames; i++)
            {
                s = ArmExtractedOps.Update(s, BaseInputs(
                    nowMs:    i * 16.67f,
                    leftHand: GrippedAt(GripZone.SleeveR, HandSide.L),
                    trigL:    0.9f,
                    hipPush:  -0.6f));
            }
            Assert.IsTrue(s.Right, "right arm extracted after build-up");

            // Release — hand back to IDLE.
            s = ArmExtractedOps.Update(s, BaseInputs(
                nowMs:    buildFrames * 16.67f + 16.67f,
                leftHand: IdleHand(HandSide.L)));
            Assert.IsFalse(s.Right, "flag cleared after grip release");
        }

        [Test]
        public void FiveSecondAutoResetClearsFlag()
        {
            var s    = ArmExtractedOps.Initial;
            float step = 50f;

            // Build up to true.
            int buildFrames = (int)System.Math.Ceiling(
                ArmExtractedConfig.Default.RequiredSustainMs / step);
            for (int i = 0; i < buildFrames; i++)
            {
                s = ArmExtractedOps.Update(s, new ArmExtractedInputs
                {
                    NowMs            = (long)(i * step),
                    DtMs             = step,
                    BottomLeftHand   = GrippedAt(GripZone.SleeveR, HandSide.L),
                    BottomRightHand  = IdleHand(HandSide.R),
                    TriggerL         = 0.9f,
                    TriggerR         = 0f,
                    AttackerHip      = new HipIntent { HipPush = -0.6f },
                    DefenderBaseHold = false,
                });
            }
            Assert.IsTrue(s.Right, "right arm extracted before auto-reset");
            long setAt = s.RightSetAtMs;

            // Advance to just past the auto-reset boundary.
            long resetTick = setAt + (long)ArmExtractedConfig.Default.AutoResetAfterMs;
            s = ArmExtractedOps.Update(s, new ArmExtractedInputs
            {
                NowMs            = resetTick,
                DtMs             = step,
                BottomLeftHand   = GrippedAt(GripZone.SleeveR, HandSide.L),
                BottomRightHand  = IdleHand(HandSide.R),
                TriggerL         = 0.9f,
                TriggerR         = 0f,
                AttackerHip      = new HipIntent { HipPush = -0.6f },
                DefenderBaseHold = false,
            });
            Assert.IsFalse(s.Right, "flag cleared after 5s auto-reset");
        }
    }
}
