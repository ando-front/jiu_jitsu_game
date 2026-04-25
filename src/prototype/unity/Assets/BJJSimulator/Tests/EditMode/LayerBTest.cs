// Ported from src/prototype/web/tests/unit/layerB.test.ts.
// Tests for Layer B intent transforms.
// Reference: docs/design/input_system_v1.md §B.1, §B.2, §B.3.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class LayerBTest
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static InputFrame MakeFrame(
            Vec2     ls       = default,
            Vec2     rs       = default,
            float    lTrigger = 0f,
            float    rTrigger = 0f,
            ButtonBit buttons = ButtonBit.None,
            ButtonBit edges   = ButtonBit.None) =>
            new InputFrame
            {
                Timestamp   = 0L,
                Ls          = ls,
                Rs          = rs,
                LTrigger    = lTrigger,
                RTrigger    = rTrigger,
                Buttons     = buttons,
                ButtonEdges = edges,
                DeviceKind  = DeviceKind.Keyboard,
            };

        // -----------------------------------------------------------------------
        // §B.1 — hip intent
        // -----------------------------------------------------------------------

        [Test]
        public void CentredStickProducesZeroHipIntent()
        {
            var hip = LayerBOps.ComputeHipIntent(MakeFrame(), LayerBConfig.Default);
            Assert.AreEqual(0f, hip.HipAngleTarget, 1e-6f);
            Assert.AreEqual(0f, hip.HipPush,        1e-6f);
            Assert.AreEqual(0f, hip.HipLateral,     1e-6f);
        }

        [Test]
        public void StickUpYieldsZeroAngle()
        {
            var hip = LayerBOps.ComputeHipIntent(MakeFrame(ls: new Vec2(0f, 1f)), LayerBConfig.Default);
            Assert.AreEqual(0f, hip.HipAngleTarget, 1e-5f);
            Assert.AreEqual(1f, hip.HipPush,        1e-5f);
            Assert.AreEqual(0f, hip.HipLateral,     1e-5f);
        }

        [Test]
        public void PureRightStickYieldsScaledHalfPi()
        {
            var hip = LayerBOps.ComputeHipIntent(MakeFrame(ls: new Vec2(1f, 0f)), LayerBConfig.Default);
            float expected = (float)(System.Math.PI / 2 * LayerBConfig.Default.KAngleScale);
            Assert.AreEqual(expected, hip.HipAngleTarget, 1e-5f);
            Assert.AreEqual(1f, hip.HipLateral, 1e-5f);
        }

        [Test]
        public void HipPushAndLateralMirrorLS()
        {
            var hip = LayerBOps.ComputeHipIntent(MakeFrame(ls: new Vec2(-0.3f, 0.8f)), LayerBConfig.Default);
            Assert.AreEqual( 0.8f, hip.HipPush,    1e-5f);
            Assert.AreEqual(-0.3f, hip.HipLateral, 1e-5f);
        }

        // -----------------------------------------------------------------------
        // §B.2.1 — grip zone selection
        // -----------------------------------------------------------------------

        [Test]
        public void CentredStickNoTriggersYieldsNoTargets()
        {
            var (grip, nextZone) = LayerBOps.ComputeGripIntent(MakeFrame(), GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.None, grip.LHandTarget);
            Assert.AreEqual(GripZone.None, grip.RHandTarget);
            Assert.AreEqual(GripZone.None, nextZone);
        }

        [Test]
        public void RsUpLeftSelectsCollarL()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(-0.7f, 0.7f)), GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.CollarL, next);
        }

        [Test]
        public void RsPureDownSelectsBelt()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(0f, -1f)), GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.Belt, next);
        }

        [Test]
        public void RsPureUpSelectsPostureBreak()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(0f, 1f)), GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.PostureBreak, next);
        }

        [Test]
        public void BothTriggersShareSameZone()
        {
            var (grip, _) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(-1f, 0f), lTrigger: 0.8f, rTrigger: 0.5f),
                GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.WristL, grip.LHandTarget);
            Assert.AreEqual(GripZone.WristL, grip.RHandTarget);
            Assert.AreEqual(0.8f, grip.LGripStrength, 1e-5f);
            Assert.AreEqual(0.5f, grip.RGripStrength, 1e-5f);
        }

        [Test]
        public void OnlyLTriggerDownOnlyLeftHandTargeted()
        {
            var (grip, _) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(1f, 0f), lTrigger: 1f, rTrigger: 0f),
                GripZone.None, LayerBConfig.Default);
            Assert.AreEqual(GripZone.WristR, grip.LHandTarget);
            Assert.AreEqual(GripZone.None,   grip.RHandTarget);
        }

        // -----------------------------------------------------------------------
        // §B.2.1 — hysteresis
        // -----------------------------------------------------------------------

        [Test]
        public void BoundaryNudgeDoesNotFlipZone()
        {
            // Held WRIST_L; small nudge toward COLLAR_L — stays WRIST_L.
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(-0.92f, 0.38f)),
                GripZone.WristL, LayerBConfig.Default);
            Assert.AreEqual(GripZone.WristL, next);
        }

        [Test]
        public void FirmMovePastThresholdFlipsZone()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: new Vec2(-0.6f, 0.8f)),
                GripZone.WristL, LayerBConfig.Default);
            Assert.AreEqual(GripZone.CollarL, next);
        }

        [Test]
        public void ReleasingRsClearsZoneWhenNoTrigger()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: Vec2.Zero), GripZone.WristL, LayerBConfig.Default);
            Assert.AreEqual(GripZone.None, next);
        }

        [Test]
        public void ReleasingRsKeepsZoneWhileTriggerHeld()
        {
            var (_, next) = LayerBOps.ComputeGripIntent(
                MakeFrame(rs: Vec2.Zero, lTrigger: 1f),
                GripZone.CollarR, LayerBConfig.Default);
            Assert.AreEqual(GripZone.CollarR, next);
        }

        // -----------------------------------------------------------------------
        // §B.3 — discrete intents
        // -----------------------------------------------------------------------

        [Test]
        public void LBumperEdgeEmitsFootHookToggleL()
        {
            var d = LayerBOps.ComputeDiscreteIntents(MakeFrame(edges: ButtonBit.LBumper));
            Assert.AreEqual(1, d.Length);
            Assert.AreEqual(DiscreteIntentKind.FootHookToggle, d[0].Kind);
            Assert.AreEqual(FootSide.L, d[0].FootSide);
        }

        [Test]
        public void BtnBaseHeldEmitsBaseHold()
        {
            var d = LayerBOps.ComputeDiscreteIntents(MakeFrame(buttons: ButtonBit.BtnBase));
            Assert.AreEqual(1, d.Length);
            Assert.AreEqual(DiscreteIntentKind.BaseHold, d[0].Kind);
        }

        [Test]
        public void BtnReleaseEdgeEmitsGripReleaseAll()
        {
            var d = LayerBOps.ComputeDiscreteIntents(MakeFrame(edges: ButtonBit.BtnRelease));
            Assert.AreEqual(1, d.Length);
            Assert.AreEqual(DiscreteIntentKind.GripReleaseAll, d[0].Kind);
        }

        [Test]
        public void MultipleSimultaneousIntentsAllSurface()
        {
            var frame = MakeFrame(
                buttons: ButtonBit.BtnBase,
                edges:   ButtonBit.LBumper | ButtonBit.BtnBreath | ButtonBit.BtnRelease);
            var d = LayerBOps.ComputeDiscreteIntents(frame);
            Assert.AreEqual(4, d.Length);

            bool hasToggle  = false, hasBase = false, hasRelease = false, hasBreath = false;
            foreach (var e in d)
            {
                if (e.Kind == DiscreteIntentKind.FootHookToggle && e.FootSide == FootSide.L) hasToggle  = true;
                if (e.Kind == DiscreteIntentKind.BaseHold)                                   hasBase    = true;
                if (e.Kind == DiscreteIntentKind.GripReleaseAll)                             hasRelease = true;
                if (e.Kind == DiscreteIntentKind.BreathStart)                                hasBreath  = true;
            }
            Assert.IsTrue(hasToggle);
            Assert.IsTrue(hasBase);
            Assert.IsTrue(hasRelease);
            Assert.IsTrue(hasBreath);
        }

        [Test]
        public void NoButtonsYieldsEmptyList()
        {
            var d = LayerBOps.ComputeDiscreteIntents(MakeFrame());
            Assert.AreEqual(0, d.Length);
        }

        // -----------------------------------------------------------------------
        // Integration: threaded state hysteresis
        // -----------------------------------------------------------------------

        [Test]
        public void TransformThreadsStateAcrossFrames()
        {
            var state = LayerBState.Initial;

            // Frame 1: aim firmly at WRIST_L.
            var (_, s1) = LayerBOps.Transform(MakeFrame(rs: new Vec2(-1f, 0f)), state);
            Assert.AreEqual(GripZone.WristL, s1.LastZone);

            // Frame 2: drift to boundary — stays WRIST_L.
            var (_, s2) = LayerBOps.Transform(MakeFrame(rs: new Vec2(-0.92f, 0.38f)), s1);
            Assert.AreEqual(GripZone.WristL, s2.LastZone);

            // Frame 3: firmly inside COLLAR_L — flips.
            var (_, s3) = LayerBOps.Transform(MakeFrame(rs: new Vec2(-0.6f, 0.8f)), s2);
            Assert.AreEqual(GripZone.CollarL, s3.LastZone);
        }
    }
}
