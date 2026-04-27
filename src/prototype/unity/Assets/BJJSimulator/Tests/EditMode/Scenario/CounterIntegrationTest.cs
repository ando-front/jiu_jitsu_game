// NUnit EditMode mirror of src/prototype/web/tests/scenario/counter_integration.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so a
// regression on either side produces a named, greppable failure.
//
// Integration: attacker window firing → defender counter window opens →
// defender confirms → attacker window force-closes.
// Covers docs/design/input_system_defense_v1.md §D.1 – §D.2.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers — mirror the gripped() / foot() / frame() / intent() factories in TS.
    // -------------------------------------------------------------------------

    public static class CounterIntegrationHelpers
    {
        public static HandFSM Gripped(HandSide side, GripZone target = GripZone.SleeveR) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = target,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        public static FootFSM Foot(FootSide side, FootState state) => new FootFSM
        {
            Side           = side,
            State          = state,
            StateEnteredMs = 0,
        };

        // Equivalent to the `frame(over)` factory — start zeroed, then override.
        public static InputFrame Frame(
            long timestamp = 0,
            float lTrigger = 0f,
            float rTrigger = 0f,
            Vec2? ls = null,
            Vec2? rs = null) =>
            new InputFrame
            {
                Timestamp   = timestamp,
                Ls          = ls ?? Vec2.Zero,
                Rs          = rs ?? Vec2.Zero,
                LTrigger    = lTrigger,
                RTrigger    = rTrigger,
                Buttons     = ButtonBit.None,
                ButtonEdges = ButtonBit.None,
                DeviceKind  = DeviceKind.Keyboard,
            };

        public static Intent MakeIntent(
            HipIntent? hip = null,
            GripIntent? grip = null) =>
            new Intent
            {
                Hip      = hip  ?? HipIntent.Zero,
                Grip     = grip ?? GripIntent.Zero,
                Discrete = System.Array.Empty<DiscreteIntent>(),
            };

        public static StepOptions Opts(
            float dtMs = 16.67f,
            Technique? confirmedTechnique = null,
            CounterTechnique? confirmedCounter = null,
            DefenseIntent? defense = null) =>
            new StepOptions
            {
                RealDtMs           = dtMs,
                GameDtMs           = dtMs,
                ConfirmedTechnique = confirmedTechnique,
                ConfirmedCounter   = confirmedCounter,
                DefenseIntent      = defense,
            };

        public static bool HasEvent(SimEvent[] events, SimEventKind kind)
        {
            foreach (var e in events) if (e.Kind == kind) return true;
            return false;
        }

        public static bool ContainsCounter(CounterTechnique[] arr, CounterTechnique value)
        {
            if (arr == null) return false;
            foreach (var v in arr) if (v == value) return true;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // describe("counter window opens when attacker fires SCISSOR_SWEEP")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class CounterWindowOpensOnScissorSweepTests
    {
        // it("attacker OPENING same-frame causes counter OPENING with SCISSOR_COUNTER candidate")
        [Test]
        public void AttackerOpeningSameFrameCausesCounterOpeningWithScissorCounterCandidate()
        {
            // Seed conditions for SCISSOR_SWEEP (§8.2).
            var seed = GameStateOps.InitialGameState(0);
            var bottom = seed.Bottom;
            bottom.LeftFoot  = CounterIntegrationHelpers.Foot(FootSide.L, FootState.Locked);
            bottom.RightFoot = CounterIntegrationHelpers.Foot(FootSide.R, FootState.Locked);
            bottom.LeftHand  = CounterIntegrationHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            seed.Bottom = bottom;
            var top = seed.Top;
            top.PostureBreak = new Vec2(0.5f, 0f);
            seed.Top = top;

            var f = CounterIntegrationHelpers.Frame(
                lTrigger: 0.8f,
                ls:       new Vec2(0.8f, 0f)); // +x sweep
            var i = CounterIntegrationHelpers.MakeIntent(
                hip:  new HipIntent { HipAngleTarget = 0f, HipPush = 0f, HipLateral = 0.8f },
                grip: new GripIntent { LHandTarget = GripZone.SleeveR, LGripStrength = 0.8f, RHandTarget = GripZone.None, RGripStrength = 0f });

            var (next, events) = GameStateOps.Step(
                seed, f, i,
                CounterIntegrationHelpers.Opts(confirmedTechnique: null));

            Assert.IsTrue(CounterIntegrationHelpers.HasEvent(events, SimEventKind.WindowOpening),
                "expected WINDOW_OPENING event");
            Assert.IsTrue(CounterIntegrationHelpers.HasEvent(events, SimEventKind.CounterWindowOpening),
                "expected COUNTER_WINDOW_OPENING event");
            Assert.IsTrue(
                CounterIntegrationHelpers.ContainsCounter(next.CounterWindow.Candidates, CounterTechnique.ScissorCounter),
                "expected SCISSOR_COUNTER among counter candidates");
            // Lateral sign of attacker intent was +1.
            Assert.AreEqual(1, next.AttackerSweepLateralSign);
        }
    }

    // -------------------------------------------------------------------------
    // describe("counter confirm force-closes the attacker window")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class CounterConfirmForceClosesAttackerWindowTests
    {
        // it("SCISSOR_COUNTER confirmed → attacker window flips to CLOSING")
        [Test]
        public void ScissorCounterConfirmedFlipsAttackerWindowToClosing()
        {
            // Build up the same seed; run enough frames to bring both windows to OPEN.
            var g = GameStateOps.InitialGameState(0);
            var bottom = g.Bottom;
            bottom.LeftFoot  = CounterIntegrationHelpers.Foot(FootSide.L, FootState.Locked);
            bottom.RightFoot = CounterIntegrationHelpers.Foot(FootSide.R, FootState.Locked);
            bottom.LeftHand  = CounterIntegrationHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            g.Bottom = bottom;
            var top = g.Top;
            top.PostureBreak = new Vec2(0.5f, 0f);
            g.Top = top;

            InputFrame BaseFrame(long t) => CounterIntegrationHelpers.Frame(
                timestamp: t,
                lTrigger:  0.8f,
                ls:        new Vec2(0.8f, 0f));

            var baseIntent = CounterIntegrationHelpers.MakeIntent(
                hip:  new HipIntent { HipAngleTarget = 0f, HipPush = 0f, HipLateral = 0.8f },
                grip: new GripIntent { LHandTarget = GripZone.SleeveR, LGripStrength = 0.8f, RHandTarget = GripZone.None, RGripStrength = 0f });

            // Tick 1: windows OPENING.
            g = GameStateOps.Step(g, BaseFrame(0), baseIntent,
                CounterIntegrationHelpers.Opts(confirmedTechnique: null)).NextState;
            Assert.AreEqual(JudgmentWindowState.Opening, g.JudgmentWindow.State);
            Assert.AreEqual(CounterWindowState.Opening,  g.CounterWindow.State);

            // Tick 2: both transition to OPEN (past 200ms).
            g = GameStateOps.Step(g, BaseFrame(250), baseIntent,
                CounterIntegrationHelpers.Opts(confirmedTechnique: null)).NextState;
            Assert.AreEqual(JudgmentWindowState.Open, g.JudgmentWindow.State);
            Assert.AreEqual(CounterWindowState.Open,  g.CounterWindow.State);

            // Tick 3: defender commits SCISSOR_COUNTER — attacker window must close.
            var (next, events) = GameStateOps.Step(g, BaseFrame(300), baseIntent,
                CounterIntegrationHelpers.Opts(
                    confirmedTechnique: null,
                    confirmedCounter:   CounterTechnique.ScissorCounter));

            Assert.AreEqual(CounterWindowState.Closing,  next.CounterWindow.State);
            Assert.AreEqual(JudgmentWindowState.Closing, next.JudgmentWindow.State);
            Assert.IsTrue(CounterIntegrationHelpers.HasEvent(events, SimEventKind.CounterConfirmed),
                "expected COUNTER_CONFIRMED event");
        }

        // it("TRIANGLE_EARLY_STACK confirmed clears top.arm_extracted on both sides")
        [Test]
        public void TriangleEarlyStackConfirmedClearsTopArmExtractedOnBothSides()
        {
            // Seed a state where TRIANGLE would fire and arm_extracted was true.
            var g = GameStateOps.InitialGameState(0);
            var bottom = g.Bottom;
            bottom.LeftFoot  = CounterIntegrationHelpers.Foot(FootSide.L, FootState.Unlocked);
            bottom.RightFoot = CounterIntegrationHelpers.Foot(FootSide.R, FootState.Locked);
            bottom.LeftHand  = CounterIntegrationHelpers.Gripped(HandSide.L, GripZone.CollarR);
            // Seed a right hand gripped on SLEEVE_L too.
            bottom.RightHand = CounterIntegrationHelpers.Gripped(HandSide.R, GripZone.SleeveL);
            g.Bottom = bottom;

            var top = g.Top;
            top.ArmExtractedLeft = true;
            g.Top = top;

            g.TopArmExtracted = new ArmExtractedState
            {
                Left           = true,
                Right          = false,
                LeftSustainMs  = 0f,
                RightSustainMs = 0f,
                LeftSetAtMs    = 0,
                RightSetAtMs   = BJJConst.SentinelTimeMs,
            };

            // Drive the window to OPEN. Because arm_extracted derives off hand+pull,
            // we also need a SLEEVE grip to keep it true across ticks.
            InputFrame BaseFrame(long t) => CounterIntegrationHelpers.Frame(
                timestamp: t,
                lTrigger:  0.8f,
                rTrigger:  0.9f);

            var baseIntent = CounterIntegrationHelpers.MakeIntent(
                hip:  new HipIntent { HipAngleTarget = 0f, HipPush = -0.6f, HipLateral = 0f },
                grip: new GripIntent
                {
                    LHandTarget = GripZone.CollarR, LGripStrength = 0.8f,
                    RHandTarget = GripZone.SleeveL, RGripStrength = 0.9f,
                });

            g = GameStateOps.Step(g, BaseFrame(0), baseIntent,
                CounterIntegrationHelpers.Opts(confirmedTechnique: null)).NextState;
            g = GameStateOps.Step(g, BaseFrame(250), baseIntent,
                CounterIntegrationHelpers.Opts(confirmedTechnique: null)).NextState;

            Assert.AreEqual(CounterWindowState.Open, g.CounterWindow.State);
            // arm_extracted remains true because we're still pulling.
            Assert.IsTrue(g.Top.ArmExtractedLeft);

            var (next, _) = GameStateOps.Step(g, BaseFrame(300), baseIntent,
                CounterIntegrationHelpers.Opts(
                    confirmedTechnique: null,
                    confirmedCounter:   CounterTechnique.TriangleEarlyStack));

            Assert.IsFalse(next.Top.ArmExtractedLeft);
            Assert.IsFalse(next.Top.ArmExtractedRight);
        }
    }
}
