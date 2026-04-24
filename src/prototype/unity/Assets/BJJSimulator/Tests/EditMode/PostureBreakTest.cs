// NUnit EditMode mirror of src/prototype/web/tests/unit/posture_break.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class PostureBreakTest
    {
        private static PostureBreakInputs DefaultInputs(float dtMs = 16.67f) =>
            new PostureBreakInputs
            {
                DtMs              = dtMs,
                AttackerHip       = HipIntent.Zero,
                GripPulls         = new List<Vec2>(),
                DefenderRecovery  = Vec2.Zero,
            };

        // ------------------------------------------------------------------
        // Decay (§3.3 bullet 1)
        // ------------------------------------------------------------------

        [Test]
        public void ZeroInputZeroStateStaysZero()
        {
            var out_ = PostureBreakOps.Update(Vec2.Zero, DefaultInputs());
            Assert.AreEqual(0f, out_.X, 1e-9f);
            Assert.AreEqual(0f, out_.Y, 1e-9f);
        }

        [Test]
        public void After800msWithNoInputMagnitudeFallsToAboutOneOverE()
        {
            var v    = new Vec2(0.8f, 0f);
            float step = 16.67f;
            for (float t = 0f; t < PostureBreakConfig.Default.DecayTauMs; t += step)
                v = PostureBreakOps.Update(v, DefaultInputs(step));

            // e^-1 ≈ 0.3679; allow small integration slack.
            Assert.Greater(v.X, 0.8f * 0.35f, "decay lower bound");
            Assert.Less   (v.X, 0.8f * 0.40f, "decay upper bound");
        }

        // ------------------------------------------------------------------
        // Attacker hip contribution (§3.3 bullet 2)
        // ------------------------------------------------------------------

        [Test]
        public void ForwardHipPushAccumulatesSagittalBreak()
        {
            var v = Vec2.Zero;
            for (float t = 0f; t < 1000f; t += 16.67f)
            {
                v = PostureBreakOps.Update(v, new PostureBreakInputs
                {
                    DtMs              = 16.67f,
                    AttackerHip       = new HipIntent { HipAngleTarget = 0f, HipPush = 1f, HipLateral = 0f },
                    GripPulls         = new List<Vec2>(),
                    DefenderRecovery  = Vec2.Zero,
                });
            }
            Assert.Greater(v.Y, 0.3f, "sagittal break meaningful");
            Assert.LessOrEqual(v.Y, 1f,  "sagittal break clamped");
            Assert.AreEqual(0f, v.X, 1e-4f, "lateral unaffected");
        }

        [Test]
        public void LateralHipDrivesLateralAxis()
        {
            var v = Vec2.Zero;
            for (float t = 0f; t < 500f; t += 16.67f)
            {
                v = PostureBreakOps.Update(v, new PostureBreakInputs
                {
                    DtMs              = 16.67f,
                    AttackerHip       = new HipIntent { HipAngleTarget = 0f, HipPush = 0f, HipLateral = -1f },
                    GripPulls         = new List<Vec2>(),
                    DefenderRecovery  = Vec2.Zero,
                });
            }
            Assert.Less(v.X, -0.1f, "lateral break negative");
        }

        // ------------------------------------------------------------------
        // Grip pulls (§3.3 bullet 3)
        // ------------------------------------------------------------------

        [Test]
        public void SleeveGripAddsBothForwardAndSideBreak()
        {
            var pull = PostureBreakOps.GripPullVector(GripZone.SleeveR, 1f);
            Assert.Greater(pull.X, 0f, "sleeve right has positive lateral");
            Assert.Greater(pull.Y, 0f, "sleeve right has positive sagittal");

            var v = Vec2.Zero;
            for (float t = 0f; t < 500f; t += 16.67f)
            {
                v = PostureBreakOps.Update(v, new PostureBreakInputs
                {
                    DtMs              = 16.67f,
                    AttackerHip       = HipIntent.Zero,
                    GripPulls         = new List<Vec2> { pull },
                    DefenderRecovery  = Vec2.Zero,
                });
            }
            Assert.Greater(v.X, 0f);
            Assert.Greater(v.Y, 0f);
        }

        [Test]
        public void ZeroStrengthGripContributesNothing()
        {
            var pull = PostureBreakOps.GripPullVector(GripZone.SleeveR, 0f);
            Assert.AreEqual(0f, pull.X);
            Assert.AreEqual(0f, pull.Y);
        }

        // ------------------------------------------------------------------
        // Defender recovery (§3.3 bullet 4)
        // ------------------------------------------------------------------

        [Test]
        public void RecoveryInputOpposesExistingBreak()
        {
            var v = new Vec2(0f, 0.5f);
            for (float t = 0f; t < 500f; t += 16.67f)
            {
                v = PostureBreakOps.Update(v, new PostureBreakInputs
                {
                    DtMs              = 16.67f,
                    AttackerHip       = HipIntent.Zero,
                    GripPulls         = new List<Vec2>(),
                    DefenderRecovery  = new Vec2(0f, 1f),
                });
            }
            Assert.Less(v.Y, 0.5f, "recovery reduces sagittal break");
        }

        // ------------------------------------------------------------------
        // Magnitude clamp
        // ------------------------------------------------------------------

        [Test]
        public void ClampsToUnitDiscUnderSustainedLargeInput()
        {
            var v = Vec2.Zero;
            for (float t = 0f; t < 5000f; t += 16.67f)
            {
                v = PostureBreakOps.Update(v, new PostureBreakInputs
                {
                    DtMs              = 16.67f,
                    AttackerHip       = new HipIntent { HipAngleTarget = 0f, HipPush = 1f, HipLateral = 1f },
                    GripPulls         = new List<Vec2>
                    {
                        PostureBreakOps.GripPullVector(GripZone.CollarL, 1f),
                        PostureBreakOps.GripPullVector(GripZone.SleeveR, 1f),
                    },
                    DefenderRecovery  = Vec2.Zero,
                });
            }
            float mag = PostureBreakOps.Magnitude(v);
            Assert.LessOrEqual(mag, PostureBreakConfig.Default.MaxMagnitude + 1e-6f, "magnitude clamped to unit disc");
        }

        // ------------------------------------------------------------------
        // Paper-proto quantization (§3.4)
        // ------------------------------------------------------------------

        [Test] public void Bucket_BelowPoint1_IsZero()       => Assert.AreEqual(0, PostureBreakOps.Bucket(new Vec2(0.05f, 0f)));
        [Test] public void Bucket_Point2_IsOne()              => Assert.AreEqual(1, PostureBreakOps.Bucket(new Vec2(0.2f,  0f)));
        [Test] public void Bucket_Point4_IsTwo()              => Assert.AreEqual(2, PostureBreakOps.Bucket(new Vec2(0f,    0.4f)));
        [Test] public void Bucket_Point6_IsThree()            => Assert.AreEqual(3, PostureBreakOps.Bucket(new Vec2(0.6f,  0f)));
        [Test] public void Bucket_Point8_IsFour()             => Assert.AreEqual(4, PostureBreakOps.Bucket(new Vec2(0.8f,  0f)));
    }
}
