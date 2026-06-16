// NUnit EditMode mirror of the pose-synthesis invariants from
// src/prototype/web/tests/unit/pose.test.ts (Tiers 1-6). Verifies the C#
// port (BJJSimulator.Platform.BJJPose) reproduces the Stage-1 motion logic.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;
using UnityEngine;
using BJJSimulator.Platform;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class BJJPoseTest
    {
        static LimbSnapshot Hand(HandState st = HandState.Idle, GripZone t = GripZone.None, float since = 1000f) =>
            new LimbSnapshot { State = st, Target = t, SinceMs = since };

        static BottomPoseInputs Bottom(
            float now = 0f, float stamina = 1f, GuardState guard = GuardState.Closed,
            LimbSnapshot? l = null, LimbSnapshot? r = null,
            FootState lf = FootState.Locked, FootState rf = FootState.Locked,
            float gripL = 0f, float gripR = 0f,
            bool windowOpen = false, bool hasTech = false, Technique tech = Technique.Triangle) =>
            new BottomPoseInputs
            {
                NowMs = now, Stamina = stamina, Guard = guard,
                LeftHand = l ?? Hand(), RightHand = r ?? Hand(),
                LeftFoot = lf, RightFoot = rf,
                HipAngle = 0f, HipPush = 0f, HipLateral = 0f,
                GripStrengthL = gripL, GripStrengthR = gripR,
                WindowOpen = windowOpen, HasWindowTechnique = hasTech, WindowTechnique = tech,
            };

        static TopPoseInputs Top(
            float now = 0f, float stamina = 1f,
            LimbSnapshot? l = null, LimbSnapshot? r = null,
            float pbX = 0f, float pbY = 0f, float wFwd = 0f, float wLat = 0f,
            bool exL = false, bool exR = false,
            bool hasPass = false, float passMs = 0f) =>
            new TopPoseInputs
            {
                NowMs = now, Stamina = stamina,
                LeftHand = l ?? Hand(), RightHand = r ?? Hand(),
                PostureBreakX = pbX, PostureBreakY = pbY,
                WeightForward = wFwd, WeightLateral = wLat,
                ArmExtractedL = exL, ArmExtractedR = exR,
                HasPass = hasPass, PassElapsedMs = passMs,
                HasCutL = false, CutElapsedLMs = 0f, HasCutR = false, CutElapsedRMs = 0f,
                CounterWindowOpen = false,
            };

        // --- Leg solver --------------------------------------------------------

        [Test]
        public void SolveLeg_RoundTrips()
        {
            var d1 = new Vector3(0.31f, -0.76f, 0.56f).normalized;
            var d2 = new Vector3(-0.84f, -0.53f, -0.16f).normalized;
            BJJPose.LegDirections(BJJPose.SolveLeg(d1, d2), out Vector3 thigh, out Vector3 shin);
            Assert.That(Vector3.Distance(thigh, d1), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(shin, d2), Is.LessThan(1e-4f));
        }

        [Test]
        public void LockedGuard_WrapsAcrossMidline()
        {
            var locked = BJJPose.ComputeBottomPose(Bottom());
            BJJPose.LegDirections(locked.LegR, out Vector3 thigh, out Vector3 shin);
            Assert.That(thigh.y, Is.LessThan(0f));    // toward the opponent
            Assert.That(thigh.z, Is.GreaterThan(0f)); // lifted off the mat
            Assert.That(shin.x, Is.LessThan(0f));     // shin sweeps across
            Assert.That(locked.LegR.Ankle, Is.GreaterThan(0.3f)); // plantarflexed hook
        }

        [Test]
        public void FramingFeet_Dorsiflex()
        {
            var framing = BJJPose.ComputeBottomPose(Bottom(lf: FootState.Unlocked, rf: FootState.Unlocked));
            Assert.That(framing.LegR.Ankle, Is.LessThan(0f));
        }

        // --- Arms / grip -------------------------------------------------------

        [Test]
        public void Reach_OpensHand_GripClenchesFist()
        {
            var reaching = BJJPose.ComputeBottomPose(Bottom(l: Hand(HandState.Reaching, GripZone.CollarL, 400f)));
            var gripped = BJJPose.ComputeBottomPose(Bottom(l: Hand(HandState.Gripped, GripZone.CollarL), gripL: 1f));
            Assert.That(reaching.ArmL.Grip, Is.LessThan(0.2f));
            Assert.That(gripped.ArmL.Grip, Is.GreaterThan(0.9f));
        }

        [Test]
        public void CollarReach_HigherThanBelt()
        {
            var collar = BJJPose.ComputeBottomPose(Bottom(r: Hand(HandState.Reaching, GripZone.CollarR, 400f)));
            var belt = BJJPose.ComputeBottomPose(Bottom(r: Hand(HandState.Reaching, GripZone.Belt, 400f)));
            Assert.That(collar.ArmR.ShoulderPitch, Is.LessThan(belt.ArmR.ShoulderPitch));
        }

        // --- Contact IK --------------------------------------------------------

        [Test]
        public void SleeveGrip_PlantsHandOnDefenderHand()
        {
            var poses = BJJPose.ComputeScenePoses(
                Bottom(r: Hand(HandState.Contact, GripZone.SleeveL)), Top());
            var bf = BJJPose.ComputeBodyFrames(poses.Bottom, BJJPose.BottomPlacement);
            var tf = BJJPose.ComputeBodyFrames(poses.Top, BJJPose.TopPlacement);
            Assert.That(Vector3.Distance(bf.HandR, tf.HandL), Is.LessThan(0.06f));
        }

        [Test]
        public void Gaze_TracksOpponentLaterally()
        {
            var left = BJJPose.ComputeScenePoses(Bottom(), Top(wLat: -0.9f));
            var right = BJJPose.ComputeScenePoses(Bottom(), Top(wLat: 0.9f));
            Assert.That(Mathf.Sign(left.Bottom.HeadYaw), Is.EqualTo(-Mathf.Sign(right.Bottom.HeadYaw)));
        }

        // --- Window entry / balance post --------------------------------------

        [Test]
        public void TriangleWindow_RaisesHips()
        {
            var plain = BJJPose.ComputeBottomPose(Bottom(windowOpen: true));
            var tri = BJJPose.ComputeBottomPose(Bottom(windowOpen: true, hasTech: true, tech: Technique.Triangle));
            Assert.That(tri.PelvisY, Is.GreaterThan(plain.PelvisY));
        }

        [Test]
        public void HardBreak_PostsHand()
        {
            Assert.IsFalse(BJJPose.BalancePost(Top(pbX: 0.2f, pbY: 0.2f), out _, out _));
            Assert.IsTrue(BJJPose.BalancePost(Top(pbX: 0.9f), out _, out _));
        }

        [Test]
        public void BalancePost_PlantsHandLow()
        {
            var poses = BJJPose.ComputeScenePoses(Bottom(), Top(pbX: 0.95f, pbY: 0.3f));
            var tf = BJJPose.ComputeBodyFrames(poses.Top, BJJPose.TopPlacement);
            Assert.That(tf.HandR.y, Is.LessThan(0.55f));
        }

        // --- Variation ---------------------------------------------------------

        [Test]
        public void IdleGuard_ShufflesOverTime()
        {
            var a = BJJPose.ComputeBottomPose(Bottom(now: 0f));
            var b = BJJPose.ComputeBottomPose(Bottom(now: 700f));
            Assert.That(Mathf.Abs(a.LegL.KneeBend - b.LegL.KneeBend), Is.GreaterThan(1e-3f));
            Assert.That(Mathf.Abs(a.PelvisYaw - b.PelvisYaw), Is.GreaterThan(1e-3f));
        }

        [Test]
        public void SearchingPasser_WeavesOverTime()
        {
            var a = BJJPose.ComputeTopPose(Top(now: 0f));
            var b = BJJPose.ComputeTopPose(Top(now: 900f));
            Assert.That(Mathf.Abs(a.PelvisX - b.PelvisX), Is.GreaterThan(1e-3f));
        }

        [Test]
        public void DeterministicAtAFixedInstant()
        {
            var a = BJJPose.ComputeBottomPose(Bottom(now: 1234f));
            var b = BJJPose.ComputeBottomPose(Bottom(now: 1234f));
            Assert.That(a.PelvisYaw, Is.EqualTo(b.PelvisYaw));
            Assert.That(a.LegL.KneeBend, Is.EqualTo(b.LegL.KneeBend));
        }
    }
}
