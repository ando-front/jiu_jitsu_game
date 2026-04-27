// NUnit EditMode mirror of src/prototype/web/tests/scenario/technique_scenarios.test.ts.
// End-to-end scenario tests per docs/design/state_machines_v1.md §11.2.
// For each of the six M1 techniques, run GameStateOps.Step through a plausible
// input sequence and assert the judgment window opens (positive) or does not
// open (negative, one condition missing).
//
// These tests deliberately fabricate actor state at the frame boundaries
// rather than wait out 275ms reaches — that's what the per-FSM tests
// already cover. Here we exercise the *composition* of FSM + continuous
// updates + window evaluation.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors gripped() / foot() / stateWith() / step() / windowOpensFor() in TS).
    // -------------------------------------------------------------------------

    public static class TechniqueScenariosHelpers
    {
        public static HandFSM Gripped(HandSide side, GripZone zone) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = zone,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        public static FootFSM Foot(FootSide side, FootState state) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = 0 };

        // Factory mirroring the TS `stateWith({ bottom?, top?, sustainedHipPushMs?, topArmExtracted? })`.
        // The TS version layers explicit overrides on top of `initialActorState(0)` and seeds
        // `topArmExtracted` so stepSimulation's re-derivation doesn't wipe armExtracted flags.
        public static GameState StateWith(
            ActorState? bottom            = null,
            ActorState? top               = null,
            float       sustainedHipPushMs = 0f,
            ArmExtractedState? topArmOverride = null)
        {
            var g = GameStateOps.InitialGameState(0);
            var nextBottom = bottom ?? GameStateOps.InitialActorState(0);
            var nextTop    = top    ?? GameStateOps.InitialActorState(0);

            // Mirror the TS arm_extracted seeding so stepSimulation's re-derivation
            // doesn't immediately wipe the flag set on the actor: feed leftSetAtMs=0
            // so the 5s auto-reset doesn't fire in-test.
            var armExt = ArmExtractedState.Initial;
            armExt.Left         = nextTop.ArmExtractedLeft;
            armExt.Right        = nextTop.ArmExtractedRight;
            armExt.LeftSetAtMs  = nextTop.ArmExtractedLeft  ? 0 : BJJConst.SentinelTimeMs;
            armExt.RightSetAtMs = nextTop.ArmExtractedRight ? 0 : BJJConst.SentinelTimeMs;
            if (topArmOverride.HasValue) armExt = topArmOverride.Value;

            return new GameState
            {
                Bottom                   = nextBottom,
                Top                      = nextTop,
                Guard                    = g.Guard,
                JudgmentWindow           = JudgmentWindow.Initial,
                CounterWindow            = g.CounterWindow,
                PassAttempt              = g.PassAttempt,
                CutAttempts              = g.CutAttempts,
                SessionEnded             = g.SessionEnded,
                AttackerSweepLateralSign = g.AttackerSweepLateralSign,
                Time                     = g.Time,
                Sustained                = new SustainedCounters { HipPushMs = sustainedHipPushMs },
                TopArmExtracted          = armExt,
                Control                  = g.Control,
                FrameIndex               = g.FrameIndex,
                NowMs                    = g.NowMs,
            };
        }

        // Mutate-on-copy helpers: build an ActorState by starting from initialActorState
        // and applying field overrides. C# value-type semantics make this trivial.
        public static ActorState BottomActor(
            HandFSM? leftHand  = null,
            HandFSM? rightHand = null,
            FootFSM? leftFoot  = null,
            FootFSM? rightFoot = null,
            float?   stamina   = null)
        {
            var a = GameStateOps.InitialActorState(0);
            if (leftHand.HasValue)  a.LeftHand  = leftHand.Value;
            if (rightHand.HasValue) a.RightHand = rightHand.Value;
            if (leftFoot.HasValue)  a.LeftFoot  = leftFoot.Value;
            if (rightFoot.HasValue) a.RightFoot = rightFoot.Value;
            if (stamina.HasValue)   a.Stamina   = stamina.Value;
            return a;
        }

        public static ActorState TopActor(
            Vec2? postureBreak       = null,
            bool? armExtractedLeft   = null,
            bool? armExtractedRight  = null)
        {
            var a = GameStateOps.InitialActorState(0);
            if (postureBreak.HasValue)      a.PostureBreak      = postureBreak.Value;
            if (armExtractedLeft.HasValue)  a.ArmExtractedLeft  = armExtractedLeft.Value;
            if (armExtractedRight.HasValue) a.ArmExtractedRight = armExtractedRight.Value;
            return a;
        }

        public static InputFrame Frame(
            long  timestamp = 0,
            float lTrigger  = 0f,
            float rTrigger  = 0f) =>
            new InputFrame
            {
                Timestamp   = timestamp,
                Ls          = Vec2.Zero,
                Rs          = Vec2.Zero,
                LTrigger    = lTrigger,
                RTrigger    = rTrigger,
                Buttons     = ButtonBit.None,
                ButtonEdges = ButtonBit.None,
                DeviceKind  = DeviceKind.Keyboard,
            };

        public static Intent IntentOf(
            HipIntent?  hip  = null,
            GripIntent? grip = null) =>
            new Intent
            {
                Hip      = hip  ?? HipIntent.Zero,
                Grip     = grip ?? GripIntent.Zero,
                Discrete = System.Array.Empty<DiscreteIntent>(),
            };

        public static GripIntent BottomGrip(
            GripZone lTarget   = GripZone.None,
            float    lStrength = 0f,
            GripZone rTarget   = GripZone.None,
            float    rStrength = 0f) =>
            new GripIntent
            {
                LHandTarget   = lTarget,
                LGripStrength = lStrength,
                RHandTarget   = rTarget,
                RGripStrength = rStrength,
            };

        // Mirrors `step(prev, frame, intent, { realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null })`.
        public static (GameState NextState, SimEvent[] Events) Step(
            GameState prev, InputFrame f, Intent i) =>
            GameStateOps.Step(prev, f, i, new StepOptions
            {
                RealDtMs           = 16.67f,
                GameDtMs           = 16.67f,
                ConfirmedTechnique = null,
                DefenseIntent      = null,
                ConfirmedCounter   = null,
            });

        // Helper: did a WINDOW_OPENING event fire this tick with the given technique
        // in its candidates?
        public static bool WindowOpensFor(SimEvent[] events, Technique technique)
        {
            foreach (var e in events)
            {
                if (e.Kind != SimEventKind.WindowOpening) continue;
                if (e.WindowCandidates == null) continue;
                foreach (var t in e.WindowCandidates)
                    if (t == technique) return true;
            }
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // describe("SCISSOR_SWEEP scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class ScissorSweepScenarioTests
    {
        // it("positive: both feet LOCKED + SLEEVE gripped 0.8 + break 0.5 → window opens")
        [Test]
        public void PositiveBothFeetLockedSleeveGrippedAndBreakOpensWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Locked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0.5f, 0f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.SleeveR, lStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.ScissorSweep));
        }

        // it("negative: posture break too small → no window")
        [Test]
        public void NegativePostureBreakTooSmallNoWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Locked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                // magnitude 0.2 < 0.4
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0.2f, 0f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.SleeveR, lStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.ScissorSweep));
        }
    }

    // -------------------------------------------------------------------------
    // describe("FLOWER_SWEEP scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FlowerSweepScenarioTests
    {
        // it("positive: both feet LOCKED + WRIST gripped + sagittal 0.6")
        [Test]
        public void PositiveBothFeetLockedWristGrippedAndSagittalOpensWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Locked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.WristL)),
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0f, 0.6f)));

            var f = TechniqueScenariosHelpers.Frame(rTrigger: 0.7f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                rTarget: GripZone.WristL, rStrength: 0.7f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.FlowerSweep));
        }

        // it("negative: sagittal 0.3 is below the 0.5 threshold")
        [Test]
        public void NegativeSagittalBelowThresholdNoWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Locked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.WristL)),
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0f, 0.3f)));

            var f = TechniqueScenariosHelpers.Frame(rTrigger: 0.7f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                rTarget: GripZone.WristL, rStrength: 0.7f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.FlowerSweep));
        }
    }

    // -------------------------------------------------------------------------
    // describe("TRIANGLE scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TriangleScenarioTests
    {
        // it("positive: one foot UNLOCKED + arm_extracted + collar gripped")
        // TRIANGLE requires arm_extracted to BE true at the end of this tick.
        // ArmExtracted re-derives every step, so the positive scenario feeds
        // BOTH hands: left on COLLAR_R (the trigger condition for TRIANGLE)
        // AND right on SLEEVE_L + strong hip pull, which keeps the left arm
        // extracted flag alive via the right hand's pull.
        [Test]
        public void PositiveOneFootUnlockedArmExtractedCollarGripped()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Unlocked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.CollarR),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.SleeveL)), // feeds arm_extracted
                top: TechniqueScenariosHelpers.TopActor(armExtractedLeft: true));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.7f, rTrigger: 0.9f);
            var i = TechniqueScenariosHelpers.IntentOf(
                hip: new HipIntent { HipAngleTarget = 0f, HipPush = -0.6f, HipLateral = 0f }, // sustained pull
                grip: TechniqueScenariosHelpers.BottomGrip(
                    lTarget: GripZone.CollarR, lStrength: 0.7f,
                    rTarget: GripZone.SleeveL, rStrength: 0.9f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.Triangle));
        }

        // it("negative: no arm_extracted → triangle locked out")
        [Test]
        public void NegativeNoArmExtractedTriangleLockedOut()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftFoot:  TechniqueScenariosHelpers.Foot(FootSide.L, FootState.Unlocked),
                    rightFoot: TechniqueScenariosHelpers.Foot(FootSide.R, FootState.Locked),
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.CollarR)),
                top: TechniqueScenariosHelpers.TopActor(armExtractedLeft: false));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.7f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.CollarR, lStrength: 0.7f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.Triangle));
        }
    }

    // -------------------------------------------------------------------------
    // describe("OMOPLATA scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class OmoplataScenarioTests
    {
        // it("positive: sleeve side-sign matches lateral break + sagittal 0.7 + yaw ≥ π/3")
        [Test]
        public void PositiveSleeveSideSignMatchesLateralBreakAndYawAboveThreshold()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftHand: TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                // L sleeve → sign(lateral) must be -1.
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(-0.3f, 0.7f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(
                hip: new HipIntent
                {
                    HipAngleTarget = (float)(System.Math.PI / 3.0) + 0.1f,
                    HipPush        = 0f,
                    HipLateral     = 0f,
                },
                grip: TechniqueScenariosHelpers.BottomGrip(
                    lTarget: GripZone.SleeveR, lStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.Omoplata));
        }

        // it("negative: hip yaw below π/3 → no window")
        [Test]
        public void NegativeHipYawBelowThresholdNoWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftHand: TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.SleeveR)),
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(-0.3f, 0.7f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(
                hip: new HipIntent
                {
                    HipAngleTarget = (float)(System.Math.PI / 6.0), // too small
                    HipPush        = 0f,
                    HipLateral     = 0f,
                },
                grip: TechniqueScenariosHelpers.BottomGrip(
                    lTarget: GripZone.SleeveR, lStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.Omoplata));
        }
    }

    // -------------------------------------------------------------------------
    // describe("HIP_BUMP scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HipBumpScenarioTests
    {
        // it("positive: sagittal 0.8 + sustained push already ≥ 300ms")
        [Test]
        public void PositiveSagittalAndSustainedPushAboveThreshold()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0f, 0.8f)),
                sustainedHipPushMs: 290f); // +16.67 this tick will push it over 300

            var f = TechniqueScenariosHelpers.Frame();
            var i = TechniqueScenariosHelpers.IntentOf(
                hip: new HipIntent { HipAngleTarget = 0f, HipPush = 0.6f, HipLateral = 0f });

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.HipBump));
        }

        // it("negative: push not sustained long enough")
        [Test]
        public void NegativePushNotSustainedLongEnough()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0f, 0.8f)),
                sustainedHipPushMs: 50f);

            var f = TechniqueScenariosHelpers.Frame();
            var i = TechniqueScenariosHelpers.IntentOf(
                hip: new HipIntent { HipAngleTarget = 0f, HipPush = 0.6f, HipLateral = 0f });

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.HipBump));
        }
    }

    // -------------------------------------------------------------------------
    // describe("CROSS_COLLAR scenario (§8.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class CrossCollarScenarioTests
    {
        // it("positive: both COLLAR gripped ≥ 0.7 + break ≥ 0.5")
        [Test]
        public void PositiveBothCollarGrippedAndBreakAboveThreshold()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.CollarL),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.CollarR)),
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0.3f, 0.5f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f, rTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.CollarL, lStrength: 0.8f,
                rTarget: GripZone.CollarR, rStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsTrue(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.CrossCollar));
        }

        // it("negative: one hand is not on COLLAR → no window")
        [Test]
        public void NegativeOneHandNotOnCollarNoWindow()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.CollarL),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.SleeveL)), // wrong zone
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0.3f, 0.5f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 0.8f, rTrigger: 0.8f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.CollarL, lStrength: 0.8f,
                rTarget: GripZone.SleeveL, rStrength: 0.8f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.CrossCollar));
        }
    }

    // -------------------------------------------------------------------------
    // describe("stamina clamp blocks high-strength conditions (§5.3)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class StaminaClampBlocksHighStrengthConditionsTests
    {
        // it("bottom stamina < 0.2 prevents CROSS_COLLAR (strength capped at 0.6)")
        [Test]
        public void BottomStaminaBelowFloorPreventsCrossCollar()
        {
            var g = TechniqueScenariosHelpers.StateWith(
                bottom: TechniqueScenariosHelpers.BottomActor(
                    leftHand:  TechniqueScenariosHelpers.Gripped(HandSide.L, GripZone.CollarL),
                    rightHand: TechniqueScenariosHelpers.Gripped(HandSide.R, GripZone.CollarR),
                    stamina:   0.1f), // below lowGripCapThreshold
                top: TechniqueScenariosHelpers.TopActor(postureBreak: new Vec2(0.3f, 0.5f)));

            var f = TechniqueScenariosHelpers.Frame(lTrigger: 1f, rTrigger: 1f);
            var i = TechniqueScenariosHelpers.IntentOf(grip: TechniqueScenariosHelpers.BottomGrip(
                lTarget: GripZone.CollarL, lStrength: 1f,
                rTarget: GripZone.CollarR, rStrength: 1f));

            var (_, events) = TechniqueScenariosHelpers.Step(g, f, i);
            // Effective trigger is clamped to 0.6 → below the 0.7 threshold → no fire.
            Assert.IsFalse(TechniqueScenariosHelpers.WindowOpensFor(events, Technique.CrossCollar));
        }
    }
}
