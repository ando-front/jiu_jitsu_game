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

        // --- Grip coupling (computeScenePoses step 3) -------------------------

        [Test]
        public void HeldSleeve_DragsDefenderArm()
        {
            var poses = BJJPose.ComputeScenePoses(
                Bottom(r: Hand(HandState.Gripped, GripZone.SleeveL)), Top());
            var bf = BJJPose.ComputeBodyFrames(poses.Bottom, BJJPose.BottomPlacement);
            var tf = BJJPose.ComputeBodyFrames(poses.Top, BJJPose.TopPlacement);
            Assert.That(Vector3.Distance(tf.HandL, bf.HandR), Is.LessThan(0.06f)); // hands overlap
            Assert.That(poses.Top.ArmL.Tremor, Is.GreaterThanOrEqualTo(0.3f));     // fights the grip
        }

        // --- Base zone anchors -------------------------------------------------

        [Test]
        public void BaseZoneAnchor_MapsKneeAndChest()
        {
            var frames = BJJPose.ComputeBodyFrames(BJJPose.ComputeTopPose(Top()), BJJPose.TopPlacement);
            Assert.IsTrue(BJJPose.BaseZoneAnchor(BaseZone.KneeL, frames, out Vector3 knee));
            Assert.That(Vector3.Distance(knee, frames.KneeL), Is.LessThan(1e-5f));
            Assert.IsTrue(BJJPose.BaseZoneAnchor(BaseZone.Chest, frames, out Vector3 chest));
            Assert.That(chest.y, Is.GreaterThan(frames.PelvisPos.y)); // chest sits above hips
            Assert.IsFalse(BJJPose.BaseZoneAnchor(BaseZone.None, frames, out _));
        }

        // --- Finish tableaux (computeFinishPoses) ------------------------------

        [Test]
        public void TriangleFinish_LocksLegsHighAcrossNeck()
        {
            var f = BJJPose.ComputeFinishPoses(FinishKind.Triangle, 0f);
            BJJPose.LegDirections(f.Bottom.LegR, out Vector3 thigh, out Vector3 shin);
            Assert.That(thigh.z, Is.GreaterThan(0.6f));   // thigh steeply up
            Assert.That(shin.x, Is.LessThan(-0.5f));      // shin hard across
            Assert.That(f.Top.TorsoPitch, Is.GreaterThan(0.6f)); // defender folded
        }

        [Test]
        public void ScissorFinish_TopplesDefender()
        {
            var f = BJJPose.ComputeFinishPoses(FinishKind.ScissorSweep, 600f);
            Assert.That(Mathf.Abs(f.Top.PelvisRoll), Is.GreaterThan(1f));
            Assert.That(f.Top.PelvisY, Is.LessThan(0.35f));
        }

        [Test]
        public void FlowerFinish_MirrorsScissor()
        {
            var scissor = BJJPose.ComputeFinishPoses(FinishKind.ScissorSweep, 0f).Top;
            var flower  = BJJPose.ComputeFinishPoses(FinishKind.FlowerSweep, 0f).Top;
            Assert.That(Mathf.Sign(flower.PelvisRoll), Is.EqualTo(-Mathf.Sign(scissor.PelvisRoll)));
            Assert.That(Mathf.Sign(flower.PelvisX), Is.EqualTo(-Mathf.Sign(scissor.PelvisX)));
        }

        [Test]
        public void HipBumpFinish_SitsAttackerUp_TipsDefenderBack()
        {
            var f = BJJPose.ComputeFinishPoses(FinishKind.HipBump, 600f);
            Assert.That(f.Bottom.TorsoPitch, Is.GreaterThan(0.9f));
            Assert.That(f.Top.TorsoPitch, Is.LessThan(-0.3f));
        }

        [Test]
        public void PassFinish_SettlesDefenderToSide()
        {
            var f = BJJPose.ComputeFinishPoses(FinishKind.Pass, 600f);
            Assert.That(Mathf.Abs(f.Top.PelvisX), Is.GreaterThan(0.3f));
            BJJPose.LegDirections(f.Bottom.LegR, out Vector3 rThigh, out _);
            BJJPose.LegDirections(f.Bottom.LegL, out Vector3 lThigh, out _);
            Assert.That(Mathf.Sign(lThigh.x), Is.EqualTo(-Mathf.Sign(rThigh.x)));
        }

        [Test]
        public void Finish_RampsThroughExecutionPhase()
        {
            var mid     = BJJPose.ComputeFinishPoses(FinishKind.ScissorSweep, 60f).Top;
            var settled = BJJPose.ComputeFinishPoses(FinishKind.ScissorSweep, 1200f).Top;
            Assert.That(mid.PelvisRoll, Is.LessThan(settled.PelvisRoll));
            Assert.That(mid.PelvisY, Is.GreaterThan(settled.PelvisY)); // still falling
        }

        [Test]
        public void ScrambleFinish_ResetsBothPlayers()
        {
            var f = BJJPose.ComputeFinishPoses(FinishKind.Scramble, 600f);
            Assert.That(f.Bottom.TorsoPitch, Is.GreaterThan(0.5f)); // sat up
            Assert.That(f.Top.PelvisZ, Is.LessThan(-0.1f));         // backing off
        }

        [Test]
        public void SubmissionTableaux_KeepBreathing()
        {
            var a = BJJPose.ComputeFinishPoses(FinishKind.CrossCollar, 0f);
            var b = BJJPose.ComputeFinishPoses(FinishKind.CrossCollar, 700f);
            Assert.That(Mathf.Abs(a.Bottom.Breath - b.Bottom.Breath), Is.GreaterThan(1e-4f));
            Assert.That(Mathf.Abs(a.Bottom.ArmL.ElbowBend - b.Bottom.ArmL.ElbowBend), Is.GreaterThan(1e-4f));
        }

        // --- Realism helpers (Tier 7) -----------------------------------------

        [Test]
        public void GroundRestOffset_LiftsLowestContactToMat()
        {
            Assert.That(BJJPose.GroundRestOffsetY(0f, -0.1f, 0.2f, 0.05f), Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(BJJPose.GroundRestOffsetY(0f, 0.1f, 0.2f), Is.EqualTo(0f));        // nothing penetrates
            Assert.That(BJJPose.GroundRestOffsetY(0.5f, 0.2f), Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void ComInsideSupport_DetectsBalance()
        {
            var square = new[]
            {
                new Vector2(-1f, -1f), new Vector2(1f, -1f),
                new Vector2(1f, 1f),  new Vector2(-1f, 1f),
            };
            Assert.IsTrue(BJJPose.ComInsideSupport(new Vector2(0f, 0f), square));   // centred
            Assert.IsTrue(BJJPose.ComInsideSupport(new Vector2(0.9f, 0.9f), square));
            Assert.IsFalse(BJJPose.ComInsideSupport(new Vector2(2f, 2f), square));  // toppling out
            Assert.IsFalse(BJJPose.ComInsideSupport(new Vector2(0f, 0f),
                new[] { new Vector2(0f, 0f), new Vector2(1f, 0f) }));               // <3 = unstable
        }

        [Test]
        public void GazeTo_TurnsTowardTargetAndClamps()
        {
            // Target to the right (+x) in an identity torso frame → positive yaw.
            BJJPose.GazeTo(Vector3.zero, Matrix3.Identity, new Vector3(1f, 0f, 1f),
                0f, 0f, 0.6f, 1.0472f, -0.6f, 1.1f, out _, out float yaw);
            Assert.That(yaw, Is.GreaterThan(0f));

            // Hard right → clamped to the ±60° (1.0472 rad) limit.
            BJJPose.GazeTo(Vector3.zero, Matrix3.Identity, new Vector3(20f, 0f, 0.01f),
                0f, 0f, 1.0f, 1.0472f, -0.6f, 1.1f, out _, out float clamped);
            Assert.That(clamped, Is.EqualTo(1.0472f).Within(1e-4f));
        }

        // --- Realism helpers (Tier 8) -----------------------------------------

        [Test]
        public void IntegrateSpring_UnderdampedOvershoots_CriticalDoesNot()
        {
            // Step the target from 0 → 1 and watch the peak. zeta 0.4 (the
            // secondary-motion oscillator) must overshoot past 1; zeta 1.0
            // (pelvis tuning) must not.
            float xU = 0f, vU = 0f, peakU = 0f;
            float xC = 0f, vC = 0f, peakC = 0f;
            for (int i = 0; i < 240; i++) // 4 s @ 60 fps
            {
                BJJPose.IntegrateSpring(ref xU, ref vU, 1f, 8f, 0.4f, 1f / 60f);
                BJJPose.IntegrateSpring(ref xC, ref vC, 1f, 8f, 1.0f, 1f / 60f);
                if (xU > peakU) peakU = xU;
                if (xC > peakC) peakC = xC;
            }
            Assert.That(peakU, Is.GreaterThan(1.02f), "underdamped should overshoot");
            Assert.That(peakC, Is.LessThan(1.01f), "critical should not overshoot");
            // Both settle on the target.
            Assert.That(xU, Is.EqualTo(1f).Within(1e-2f));
            Assert.That(xC, Is.EqualTo(1f).Within(1e-2f));
        }

        [Test]
        public void SpineSCurve_PreservesSumAndShapesByRound()
        {
            // Sum is preserved for any round (chest lands where single-bone FK
            // put it).
            BJJPose.SpineSCurve(0.6f, 0.5f, out float lo, out float mid, out float up);
            Assert.That(lo + mid + up, Is.EqualTo(0.6f).Within(1e-5f));

            // round = 0 → even split.
            BJJPose.SpineSCurve(0.6f, 0f, out float a, out float b, out float c);
            Assert.That(a, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(a, Is.EqualTo(b).Within(1e-5f));
            Assert.That(b, Is.EqualTo(c).Within(1e-5f));

            // round > 0 rounds the upper back forward (upper > lower).
            Assert.That(up, Is.GreaterThan(lo));
            // round < 0 extends a tall passer spine (upper < lower).
            BJJPose.SpineSCurve(0.6f, -0.5f, out float lo2, out _, out float up2);
            Assert.That(up2, Is.LessThan(lo2));
        }

        [Test]
        public void FingerCurl_MonotonicAndProgressive()
        {
            // Open hand: no curl. Fist: all knuckles fold.
            Assert.That(BJJPose.FingerCurl(0f, 0), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(BJJPose.FingerCurl(1f, 0), Is.GreaterThan(1f));
            // Monotonic in grip.
            Assert.That(BJJPose.FingerCurl(0.7f, 1), Is.GreaterThan(BJJPose.FingerCurl(0.3f, 1)));
            // PIP folds further than the MCP and DIP at a full fist.
            Assert.That(BJJPose.FingerCurl(1f, 1), Is.GreaterThan(BJJPose.FingerCurl(1f, 0)));
            Assert.That(BJJPose.FingerCurl(1f, 1), Is.GreaterThan(BJJPose.FingerCurl(1f, 2)));
            // Out-of-range joint index is clamped, not thrown.
            Assert.That(BJJPose.FingerCurl(1f, 9), Is.EqualTo(BJJPose.FingerCurl(1f, 2)).Within(1e-6f));
        }

        [Test]
        public void AnticipationOffset_ReverseBumpThenZero()
        {
            // Zero at the boundaries, negative (reverse) inside, peak at midpoint.
            Assert.That(BJJPose.AnticipationOffset(0f, 120f, 0.05f), Is.EqualTo(0f));
            Assert.That(BJJPose.AnticipationOffset(120f, 120f, 0.05f), Is.EqualTo(0f));
            Assert.That(BJJPose.AnticipationOffset(200f, 120f, 0.05f), Is.EqualTo(0f)); // past window
            Assert.That(BJJPose.AnticipationOffset(60f, 120f, 0.05f), Is.EqualTo(-0.05f).Within(1e-5f));
            Assert.That(BJJPose.AnticipationOffset(30f, 120f, 0.05f), Is.LessThan(0f));
        }

        [Test]
        public void RippleFired_CrossesEachSegmentOnce()
        {
            // step 50 ms: segment 2 fires at 100 ms. Crossing 90→110 fires it…
            Assert.IsTrue(BJJPose.RippleFired(90f, 110f, 2, 50f));
            // …but a frame fully before or after does not.
            Assert.IsFalse(BJJPose.RippleFired(110f, 130f, 2, 50f));
            Assert.IsFalse(BJJPose.RippleFired(40f, 60f, 2, 50f));
            // Segment 0 (trigger 0) fires on the impact frame: elapsed crosses
            // 0 from a negative "pre-impact" reading.
            Assert.IsTrue(BJJPose.RippleFired(-2f, 0f, 0, 50f));
            // Inactive ripple keeps elapsed negative → no segment ever fires.
            Assert.IsFalse(BJJPose.RippleFired(-2f, -1f, 0, 50f));
            Assert.IsFalse(BJJPose.RippleFired(-2f, -1f, 1, 50f));
        }

        // --- Whole-body dynamics (Tier 9) -------------------------------------

        [Test]
        public void WeightShift_DampedSpring_ConvergesToZero()
        {
            // A COM offset released toward zero settles back to centre with no
            // residual velocity (ζ0.7 — slight overshoot, then quiet).
            Vector3 pos = new Vector3(0.05f, 0f, -0.04f);
            Vector3 vel = Vector3.zero;
            for (int i = 0; i < 600; i++) // 10 s @ 60 fps
                BJJPoseRig.StepWeightShift(ref pos, ref vel, Vector3.zero, 1f / 60f);
            Assert.That(pos.magnitude, Is.LessThan(1e-3f));
            Assert.That(vel.magnitude, Is.LessThan(1e-3f));
        }

        [Test]
        public void InertialYaw_FollowsTarget_WithLag()
        {
            // After one frame the yaw has begun turning but lags well behind the
            // commanded angle (heavy body, late turn); it converges given time.
            float yaw = 0f, vel = 0f;
            const float target = 0.5f;
            BJJPoseRig.StepInertialYaw(ref yaw, ref vel, target, 1f / 60f);
            Assert.That(yaw, Is.GreaterThan(0f));
            Assert.That(yaw, Is.LessThan(target));
            for (int i = 0; i < 600; i++)
                BJJPoseRig.StepInertialYaw(ref yaw, ref vel, target, 1f / 60f);
            Assert.That(yaw, Is.EqualTo(target).Within(1e-2f));
        }

        [Test]
        public void ShoulderBreath_AmplitudeReducedByTension()
        {
            // Peak inhale (sin = 1 at π/2): full ±8 mm relaxed, halved at a full
            // grip (× (1 − 0.5)). Tension never flips the sign.
            float relaxed  = BJJPoseRig.ShoulderBreathOffset(Mathf.PI * 0.5f, 0f);
            float strained = BJJPoseRig.ShoulderBreathOffset(Mathf.PI * 0.5f, 1f);
            Assert.That(relaxed, Is.EqualTo(0.008f).Within(1e-6f));
            Assert.That(strained, Is.EqualTo(0.004f).Within(1e-6f));
            Assert.That(strained, Is.LessThan(relaxed));
            Assert.That(strained, Is.GreaterThan(0f));
        }
    }
}
