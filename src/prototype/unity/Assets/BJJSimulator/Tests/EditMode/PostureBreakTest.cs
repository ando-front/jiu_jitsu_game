// NUnit EditMode mirror of src/prototype/web/tests/unit/posture_break.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class PostureBreakTest
    {
        static PostureBreakInputs Inputs(
            float dtMs          = 16.67f,
            float hipLateral    = 0f,
            float hipPush       = 0f,
            Vec2[]  gripPulls   = null,
            Vec2  defRec        = default) =>
            new PostureBreakInputs
            {
                DtMs             = dtMs,
                AttackerHip      = new HipIntent { HipAngleTarget = 0f, HipPush = hipPush, HipLateral = hipLateral },
                GripPulls        = gripPulls ?? System.Array.Empty<Vec2>(),
                DefenderRecovery = defRec.X == 0f && defRec.Y == 0f ? Vec2.Zero : defRec,
            };

        // -------------------------------------------------------------------------
        // Decay (§3.3 bullet 1)
        // -------------------------------------------------------------------------

        [Test]
        public void ZeroInput_ZeroState_RemainsZero()
        {
            var out_ = PostureBreakOps.Update(Vec2.Zero, Inputs());
            Assert.AreEqual(0f, out_.X, 1e-6f);
            Assert.AreEqual(0f, out_.Y, 1e-6f);
        }

        [Test]
        public void Decay_800ms_TimeConstant_FallsToOneOverE()
        {
            var start = new Vec2(0.8f, 0f);
            var v     = start;
            float tau  = PostureBreakConfig.Default.DecayTauMs;
            float step = 16.67f;
            for (float t = 0f; t < tau; t += step)
                v = PostureBreakOps.Update(v, Inputs(dtMs: step));
            // After one τ magnitude should be ~0.8 × e^-1 ≈ 0.294.
            Assert.Greater(v.X, 0.8f * 0.35f);
            Assert.Less(v.X,    0.8f * 0.40f);
        }

        // -------------------------------------------------------------------------
        // Attacker hip contribution (§3.3 bullet 2)
        // -------------------------------------------------------------------------

        [Test]
        public void ForwardHipPush_AccumulatesSagittalBreak()
        {
            var v = Vec2.Zero;
            for (float t = 0f; t < 1000f; t += 16.67f)
                v = PostureBreakOps.Update(v, Inputs(hipPush: 1f));
            Assert.Greater(v.Y, 0.3f);
            Assert.LessOrEqual(v.Y, 1f);
            Assert.AreEqual(0f, v.X, 1e-4f);
        }

        [Test]
        public void LateralHip_DrivesSign_OfLateralAxis()
        {
            var v = Vec2.Zero;
            for (float t = 0f; t < 500f; t += 16.67f)
                v = PostureBreakOps.Update(v, Inputs(hipLateral: -1f));
            Assert.Less(v.X, -0.1f);
        }

        // -------------------------------------------------------------------------
        // Grip pulls (§3.3 bullet 3)
        // -------------------------------------------------------------------------

        [Test]
        public void SleeveR_GrippedPull_AddsBothForwardAndSideBreak()
        {
            var pull = PostureBreakOps.GripPullVector(GripZone.SleeveR, 1f);
            Assert.Greater(pull.X, 0f);
            Assert.Greater(pull.Y, 0f);

            var v = Vec2.Zero;
            for (float t = 0f; t < 500f; t += 16.67f)
                v = PostureBreakOps.Update(v, Inputs(gripPulls: new[] { pull }));
            Assert.Greater(v.X, 0f);
            Assert.Greater(v.Y, 0f);
        }

        [Test]
        public void ZeroStrengthGrip_ContributesNothing()
        {
            var pull = PostureBreakOps.GripPullVector(GripZone.SleeveR, 0f);
            Assert.AreEqual(0f, pull.X);
            Assert.AreEqual(0f, pull.Y);
        }

        // -------------------------------------------------------------------------
        // Defender recovery (§3.3 bullet 4)
        // -------------------------------------------------------------------------

        [Test]
        public void Recovery_OpposesExistingBreak()
        {
            var v = new Vec2(0f, 0.5f);
            for (float t = 0f; t < 500f; t += 16.67f)
                v = PostureBreakOps.Update(v, Inputs(defRec: new Vec2(0f, 1f)));
            Assert.Less(v.Y, 0.5f);
        }

        // -------------------------------------------------------------------------
        // Magnitude clamp
        // -------------------------------------------------------------------------

        [Test]
        public void SustainedLargeInput_ClampsToUnitDisc()
        {
            var v = Vec2.Zero;
            var pulls = new[]
            {
                PostureBreakOps.GripPullVector(GripZone.CollarL, 1f),
                PostureBreakOps.GripPullVector(GripZone.SleeveR, 1f),
            };
            for (float t = 0f; t < 5000f; t += 16.67f)
                v = PostureBreakOps.Update(v, Inputs(hipPush: 1f, hipLateral: 1f, gripPulls: pulls));
            Assert.LessOrEqual(PostureBreakOps.BreakMagnitude(v),
                PostureBreakConfig.Default.MaxMagnitude + 1e-5f);
        }

        // -------------------------------------------------------------------------
        // Paper-proto buckets (§3.4)
        // -------------------------------------------------------------------------

        [Test] public void Bucket_BelowPoint1_IsZero()     => Assert.AreEqual(0, PostureBreakOps.BreakBucket(new Vec2(0.05f, 0f)));
        [Test] public void Bucket_Point2_IsOne()           => Assert.AreEqual(1, PostureBreakOps.BreakBucket(new Vec2(0.2f, 0f)));
        [Test] public void Bucket_Point4_IsTwo()           => Assert.AreEqual(2, PostureBreakOps.BreakBucket(new Vec2(0f, 0.4f)));
        [Test] public void Bucket_Point6_IsThree()         => Assert.AreEqual(3, PostureBreakOps.BreakBucket(new Vec2(0.6f, 0f)));
        [Test] public void Bucket_Point8_IsFour()          => Assert.AreEqual(4, PostureBreakOps.BreakBucket(new Vec2(0.8f, 0f)));
    }
}
