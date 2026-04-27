// Ported from src/prototype/web/tests/unit/layerD.test.ts.
// Tests for Layer D commit resolver.
// Reference: docs/design/input_system_v1.md §D.1.1.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class LayerDTest
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static InputFrame Frame(
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

        static LayerDInputs Inputs(
            long       nowMs       = 0L,
            float      dtMs        = 16.67f,
            InputFrame frame       = default,
            Technique[] candidates = null,
            bool       windowIsOpen = true)
        {
            return new LayerDInputs
            {
                NowMs        = nowMs,
                DtMs         = dtMs,
                Frame        = frame,
                Hip          = HipIntent.Zero,
                Candidates   = candidates ?? System.Array.Empty<Technique>(),
                WindowIsOpen = windowIsOpen,
            };
        }

        // -----------------------------------------------------------------------
        // Inactive outside OPEN window
        // -----------------------------------------------------------------------

        [Test]
        public void NeverConfirmsWhileWindowClosed()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                windowIsOpen: false,
                candidates:   new[] { Technique.ScissorSweep },
                frame:        Frame(ls: new Vec2(1f, 0f), edges: ButtonBit.LBumper, buttons: ButtonBit.LBumper)));
            Assert.IsNull(r.ConfirmedTechnique);
        }

        // -----------------------------------------------------------------------
        // Per-technique commits (§D.1.1)
        // -----------------------------------------------------------------------

        [Test]
        public void ScissorSweepCommits()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.ScissorSweep },
                frame:      Frame(ls: new Vec2(1f, 0f), edges: ButtonBit.LBumper, buttons: ButtonBit.LBumper)));
            Assert.AreEqual(Technique.ScissorSweep, r.ConfirmedTechnique);
        }

        [Test]
        public void ScissorSweepDoesNotCommitWhenNotCandidate()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.FlowerSweep },
                frame:      Frame(ls: new Vec2(1f, 0f), edges: ButtonBit.LBumper, buttons: ButtonBit.LBumper)));
            Assert.IsNull(r.ConfirmedTechnique);
        }

        [Test]
        public void FlowerSweepCommits()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.FlowerSweep },
                frame:      Frame(ls: new Vec2(0f, 1f), edges: ButtonBit.RBumper, buttons: ButtonBit.RBumper)));
            Assert.AreEqual(Technique.FlowerSweep, r.ConfirmedTechnique);
        }

        [Test]
        public void TriangleCommitsAfterHold()
        {
            var s = LayerDState.Initial;
            float stepMs = 50f;
            int   steps  = (int)System.Math.Ceiling(LayerDTiming.Default.TriangleHoldMs / stepMs);
            Technique? confirmed = null;

            for (int i = 0; i < steps; i++)
            {
                var r = LayerDOps.Resolve(s, Inputs(
                    nowMs:      (long)(i * stepMs),
                    dtMs:       stepMs,
                    candidates: new[] { Technique.Triangle },
                    frame:      Frame(buttons: ButtonBit.BtnBase)));
                s = r.NextState;
                if (r.ConfirmedTechnique.HasValue) { confirmed = r.ConfirmedTechnique; break; }
            }
            Assert.AreEqual(Technique.Triangle, confirmed);
        }

        [Test]
        public void TriangleShortTapDoesNotCommit()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                dtMs:       100f,
                candidates: new[] { Technique.Triangle },
                frame:      Frame(buttons: ButtonBit.BtnBase)));
            Assert.IsNull(r.ConfirmedTechnique);
        }

        [Test]
        public void OmoplataCommits()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.Omoplata },
                frame:      Frame(rs: new Vec2(0f, 1f), edges: ButtonBit.LBumper, buttons: ButtonBit.LBumper)));
            Assert.AreEqual(Technique.Omoplata, r.ConfirmedTechnique);
        }

        [Test]
        public void HipBumpCommitsOnRapidPress()
        {
            var s = LayerDState.Initial;

            // Tick 1: R trigger released (records last-released timestamp = 0)
            var r1 = LayerDOps.Resolve(s, Inputs(
                nowMs:      0L,
                candidates: new[] { Technique.HipBump },
                frame:      Frame(rTrigger: 0f)));
            s = r1.NextState;
            Assert.IsNull(r1.ConfirmedTechnique);

            // Tick 2: press to max within the 200ms window
            var r2 = LayerDOps.Resolve(s, Inputs(
                nowMs:      100L,
                candidates: new[] { Technique.HipBump },
                frame:      Frame(rTrigger: 1f)));
            Assert.AreEqual(Technique.HipBump, r2.ConfirmedTechnique);
        }

        [Test]
        public void HipBumpTooSlowDoesNotCommit()
        {
            var s = LayerDState.Initial;

            var r1 = LayerDOps.Resolve(s, Inputs(
                nowMs:      0L,
                candidates: new[] { Technique.HipBump },
                frame:      Frame(rTrigger: 0f)));
            s = r1.NextState;

            // 300ms later — outside the 200ms window
            var r2 = LayerDOps.Resolve(s, Inputs(
                nowMs:      300L,
                candidates: new[] { Technique.HipBump },
                frame:      Frame(rTrigger: 1f)));
            Assert.IsNull(r2.ConfirmedTechnique);
        }

        [Test]
        public void CrossCollarCommitsOnBothTriggersMax()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.CrossCollar },
                frame:      Frame(lTrigger: 1f, rTrigger: 1f)));
            Assert.AreEqual(Technique.CrossCollar, r.ConfirmedTechnique);
        }

        [Test]
        public void CrossCollarOnlyOneTriggerDoesNotCommit()
        {
            var r = LayerDOps.Resolve(LayerDState.Initial, Inputs(
                candidates: new[] { Technique.CrossCollar },
                frame:      Frame(lTrigger: 1f, rTrigger: 0.5f)));
            Assert.IsNull(r.ConfirmedTechnique);
        }
    }
}
