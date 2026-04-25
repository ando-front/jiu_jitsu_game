// Ported from src/prototype/web/tests/unit/layerD_defense.test.ts.
// Tests for Layer D (defender) counter-window commit resolver.
// Reference: docs/design/input_system_defense_v1.md §D.2.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class LayerDDefenseTest
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static InputFrame Frame(
            Vec2     ls       = default,
            Vec2     rs       = default,
            ButtonBit buttons  = ButtonBit.None,
            ButtonBit edges    = ButtonBit.None) =>
            new InputFrame
            {
                Timestamp   = 0L,
                Ls          = ls,
                Rs          = rs,
                LTrigger    = 0f,
                RTrigger    = 0f,
                Buttons     = buttons,
                ButtonEdges = edges,
                DeviceKind  = DeviceKind.Keyboard,
            };

        static LayerDDefenseInputs Inputs(
            long              nowMs                    = 0L,
            float             dtMs                     = 16.67f,
            InputFrame        frame                    = default,
            CounterTechnique[] candidates              = null,
            bool              windowIsOpen             = true,
            int               attackerSweepLateralSign = 0) =>
            new LayerDDefenseInputs
            {
                NowMs                   = nowMs,
                DtMs                    = dtMs,
                Frame                   = frame,
                Candidates              = candidates ?? System.Array.Empty<CounterTechnique>(),
                WindowIsOpen            = windowIsOpen,
                AttackerSweepLateralSign = attackerSweepLateralSign,
            };

        // -----------------------------------------------------------------------
        // Inactive outside OPEN window
        // -----------------------------------------------------------------------

        [Test]
        public void NeverConfirmsWhileWindowClosed()
        {
            var r = LayerDDefenseOps.Resolve(LayerDDefenseState.Initial, Inputs(
                windowIsOpen:             false,
                candidates:               new[] { CounterTechnique.ScissorCounter },
                attackerSweepLateralSign: 1,
                frame:                    Frame(ls: new Vec2(-1f, 0f))));
            Assert.IsNull(r.ConfirmedCounter);
        }

        // -----------------------------------------------------------------------
        // SCISSOR_COUNTER
        // -----------------------------------------------------------------------

        [Test]
        public void ScissorCounterFiresOppositeToSweep()
        {
            // Sweep sign = +1 → defender pushes LS to -x ≥ 0.8
            var r = LayerDDefenseOps.Resolve(LayerDDefenseState.Initial, Inputs(
                candidates:               new[] { CounterTechnique.ScissorCounter },
                attackerSweepLateralSign: 1,
                frame:                    Frame(ls: new Vec2(-1f, 0f))));
            Assert.AreEqual(CounterTechnique.ScissorCounter, r.ConfirmedCounter);
        }

        [Test]
        public void ScissorCounterNoFireSameDirectionAsSweep()
        {
            var r = LayerDDefenseOps.Resolve(LayerDDefenseState.Initial, Inputs(
                candidates:               new[] { CounterTechnique.ScissorCounter },
                attackerSweepLateralSign: 1,
                frame:                    Frame(ls: new Vec2(1f, 0f))));
            Assert.IsNull(r.ConfirmedCounter);
        }

        [Test]
        public void ScissorCounterNoFireOnWeakMagnitude()
        {
            var r = LayerDDefenseOps.Resolve(LayerDDefenseState.Initial, Inputs(
                candidates:               new[] { CounterTechnique.ScissorCounter },
                attackerSweepLateralSign: 1,
                frame:                    Frame(ls: new Vec2(-0.5f, 0f))));
            Assert.IsNull(r.ConfirmedCounter);
        }

        // -----------------------------------------------------------------------
        // TRIANGLE_EARLY_STACK
        // -----------------------------------------------------------------------

        [Test]
        public void StackCommitsAfterHoldAndLsUp()
        {
            var s    = LayerDDefenseState.Initial;
            float step = 50f;
            int frames = (int)System.Math.Ceiling(LayerDDefenseTiming.Default.StackHoldMs / step);
            CounterTechnique? confirmed = null;

            for (int i = 0; i < frames; i++)
            {
                var r = LayerDDefenseOps.Resolve(s, Inputs(
                    nowMs:      (long)(i * step),
                    dtMs:       step,
                    candidates: new[] { CounterTechnique.TriangleEarlyStack },
                    frame:      Frame(buttons: ButtonBit.BtnBase, ls: new Vec2(0f, 1f))));
                s = r.NextState;
                if (r.ConfirmedCounter.HasValue) { confirmed = r.ConfirmedCounter; break; }
            }
            Assert.AreEqual(CounterTechnique.TriangleEarlyStack, confirmed);
        }

        [Test]
        public void StackNoFireIfLsNotUp()
        {
            // Hold long enough but LS sideways → no commit
            var   s      = LayerDDefenseState.Initial;
            float step   = 50f;
            int   frames = (int)System.Math.Ceiling(LayerDDefenseTiming.Default.StackHoldMs / step) + 2;
            CounterTechnique? confirmed = null;

            for (int i = 0; i < frames; i++)
            {
                var r = LayerDDefenseOps.Resolve(s, Inputs(
                    nowMs:      (long)(i * step),
                    dtMs:       step,
                    candidates: new[] { CounterTechnique.TriangleEarlyStack },
                    frame:      Frame(buttons: ButtonBit.BtnBase, ls: new Vec2(1f, 0f)))); // sideways
                s = r.NextState;
                if (r.ConfirmedCounter.HasValue) confirmed = r.ConfirmedCounter;
            }
            Assert.IsNull(confirmed);
        }
    }
}
