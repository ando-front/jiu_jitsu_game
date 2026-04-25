// Ported from src/prototype/web/tests/unit/layerA.test.ts.
// Tests for InputTransform (pure transforms for Layer A).
// Reference: docs/design/input_system_v1.md §A.3 / §A.4 / §A.2.2.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class InputTransformTest
    {
        // -----------------------------------------------------------------------
        // §A.3.1 — stick deadzone & response curve
        // -----------------------------------------------------------------------

        [Test]
        public void StickZeroesInsideInnerDeadzone()
        {
            var r = InputTransform.ApplyStickDeadzoneAndCurve(new Vec2(0.10f, 0.05f));
            Assert.AreEqual(Vec2.Zero.X, r.X, 1e-6f);
            Assert.AreEqual(Vec2.Zero.Y, r.Y, 1e-6f);
        }

        [Test]
        public void StickSaturatesAtOuterDeadzone()
        {
            var r = InputTransform.ApplyStickDeadzoneAndCurve(new Vec2(0.96f, 0f));
            Assert.AreEqual(1f, r.X, 1e-5f);
            Assert.AreEqual(0f, r.Y, 1e-5f);
        }

        [Test]
        public void StickAppliesCurveInMiddleBand()
        {
            // Input half-way through the active band → rescaled ≈ 0.5 → curved ≈ 0.5^1.5
            float mid = (InputTransform.StickInnerDeadzone + InputTransform.StickOuterDeadzone) / 2f;
            var r = InputTransform.ApplyStickDeadzoneAndCurve(new Vec2(mid, 0f));
            float expected = (float)System.Math.Pow(0.5, InputTransform.StickCurveExponent);
            Assert.AreEqual(expected, r.X, 1e-4f);
        }

        [Test]
        public void StickPreservesDirection()
        {
            var r = InputTransform.ApplyStickDeadzoneAndCurve(new Vec2(0.5f, 0.5f));
            Assert.AreEqual(r.X, r.Y, 1e-9f);
        }

        // -----------------------------------------------------------------------
        // §A.3.2 — trigger deadzone
        // -----------------------------------------------------------------------

        [Test]
        public void TriggerClampsLowToZero()
        {
            Assert.AreEqual(0f, InputTransform.ApplyTriggerDeadzone(0.04f), 1e-6f);
        }

        [Test]
        public void TriggerClampsHighToOne()
        {
            Assert.AreEqual(1f, InputTransform.ApplyTriggerDeadzone(0.97f), 1e-6f);
        }

        [Test]
        public void TriggerIsLinearInActiveBand()
        {
            // Midpoint of [0.05, 0.95] = 0.5 → expect 0.5
            Assert.AreEqual(0.5f, InputTransform.ApplyTriggerDeadzone(0.5f), 1e-5f);
        }

        // -----------------------------------------------------------------------
        // §A.2.2 — 8-way digital normalisation
        // -----------------------------------------------------------------------

        [Test]
        public void EightWayDiagonalIsUnitLength()
        {
            var r = InputTransform.EightWayFromDigital(up: true, down: false, left: true, right: false);
            Assert.AreEqual(1f, r.Magnitude, 1e-5f);
            float inv = 0.7071068f;
            Assert.AreEqual(-inv, r.X, 1e-5f);
            Assert.AreEqual( inv, r.Y, 1e-5f);
        }

        [Test]
        public void EightWayOpposingKeysCancelToZero()
        {
            var r = InputTransform.EightWayFromDigital(up: true, down: true, left: false, right: false);
            Assert.AreEqual(0f, r.X, 1e-6f);
            Assert.AreEqual(0f, r.Y, 1e-6f);
        }

        [Test]
        public void EightWayNoKeysYieldsZero()
        {
            var r = InputTransform.EightWayFromDigital(false, false, false, false);
            Assert.AreEqual(0f, r.X);
            Assert.AreEqual(0f, r.Y);
        }

        // -----------------------------------------------------------------------
        // Button edge detection
        // -----------------------------------------------------------------------

        [Test]
        public void EdgeFiresOnlyOnTransition()
        {
            var e = InputTransform.ComputeButtonEdges((ButtonBit)0b0000, (ButtonBit)0b0101);
            Assert.AreEqual((ButtonBit)0b0101, e);
        }

        [Test]
        public void HeldButtonsProduceNoEdge()
        {
            var e = InputTransform.ComputeButtonEdges((ButtonBit)0b0101, (ButtonBit)0b0101);
            Assert.AreEqual(ButtonBit.None, e);
        }

        [Test]
        public void ReleaseProducesNoEdge()
        {
            var e = InputTransform.ComputeButtonEdges((ButtonBit)0b1111, ButtonBit.None);
            Assert.AreEqual(ButtonBit.None, e);
        }

        [Test]
        public void PartiallyNewBitsAreIsolated()
        {
            // Bit 0 held, bit 2 newly pressed → only bit 2 edges
            var e = InputTransform.ComputeButtonEdges((ButtonBit)0b0001, (ButtonBit)0b0101);
            Assert.AreEqual((ButtonBit)0b0100, e);
        }

        // -----------------------------------------------------------------------
        // LayerA assembler — pure path
        // -----------------------------------------------------------------------

        [Test]
        public void LayerAUsesKeyboardWhenNoGamepad()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                // keyboard: LS right + L trigger + BTN_BASE
                LsRight  = true,
                KbLTrigger = true,
                KbButtons  = ButtonBit.BtnBase,
            };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 1000L);
            Assert.AreEqual(DeviceKind.Keyboard, frame.DeviceKind);
            Assert.AreEqual(1f, frame.Ls.X, 1e-5f);
            Assert.AreEqual(0f, frame.Ls.Y, 1e-5f);
            Assert.AreEqual(1f, frame.LTrigger, 1e-5f);
            Assert.AreNotEqual(ButtonBit.None, frame.Buttons & ButtonBit.BtnBase);
            Assert.AreNotEqual(ButtonBit.None, frame.ButtonEdges & ButtonBit.BtnBase);
        }

        [Test]
        public void LayerATimestampPassesThrough()
        {
            var hw = new RawHardwareSnapshot { GamepadConnected = false };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 42L);
            Assert.AreEqual(42L, frame.Timestamp);
        }

        [Test]
        public void LayerAEdgeBitsComputedAcrossFrames()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                KbButtons        = ButtonBit.BtnBreath,
            };

            // Frame 1: first time BTN_BREATH seen — should be an edge.
            var (f1, state1) = LayerAOps.Assemble(LayerAState.Initial, hw, 16L);
            Assert.AreNotEqual(ButtonBit.None, f1.ButtonEdges & ButtonBit.BtnBreath);

            // Frame 2: still held — no edge.
            var (f2, _) = LayerAOps.Assemble(state1, hw, 32L);
            Assert.AreEqual(ButtonBit.None, f2.ButtonEdges & ButtonBit.BtnBreath);
            Assert.AreNotEqual(ButtonBit.None, f2.Buttons & ButtonBit.BtnBreath);
        }
    }
}
