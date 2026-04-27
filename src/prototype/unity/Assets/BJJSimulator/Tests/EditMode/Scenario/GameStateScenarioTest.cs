// NUnit EditMode mirror of src/prototype/web/tests/scenario/game_state.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/architecture_overview_v1.md §7,
//            docs/design/state_machines_v1.md §6 / §10.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors the frame() / intent() factories in TS).
    // -------------------------------------------------------------------------

    public static class GameStateScenarioHelpers
    {
        // Midpoint of §C.1.2's 200–350ms reach window.
        public const int ReachMid = (200 + 350) / 2; // 275

        public static InputFrame Frame(
            long      timestamp   = 0,
            float     lTrigger    = 0f,
            float     rTrigger    = 0f,
            ButtonBit buttons     = ButtonBit.None,
            ButtonBit buttonEdges = ButtonBit.None) =>
            new InputFrame
            {
                Timestamp   = timestamp,
                Ls          = Vec2.Zero,
                Rs          = Vec2.Zero,
                LTrigger    = lTrigger,
                RTrigger    = rTrigger,
                Buttons     = buttons,
                ButtonEdges = buttonEdges,
                DeviceKind  = DeviceKind.Keyboard,
            };

        public static Intent IntentOf(
            HipIntent?         hip      = null,
            GripIntent?        grip     = null,
            DiscreteIntent[]   discrete = null) =>
            new Intent
            {
                Hip      = hip  ?? HipIntent.Zero,
                Grip     = grip ?? GripIntent.Zero,
                Discrete = discrete ?? System.Array.Empty<DiscreteIntent>(),
            };

        // Mirrors `step(prev, frame, intent, { realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null })`.
        public static StepOptions DefaultOpts() => new StepOptions
        {
            RealDtMs           = 16.67f,
            GameDtMs           = 16.67f,
            ConfirmedTechnique = null,
            DefenseIntent      = null,
            ConfirmedCounter   = null,
        };

        // Build the bottom-side grip intent that the TS test passes inline.
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

        public static DiscreteIntent FootHook(FootSide side) =>
            new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = side };

        public static bool ContainsKind(SimEvent[] events, SimEventKind kind)
        {
            foreach (var e in events) if (e.Kind == kind) return true;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // describe("GameState init")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class GameStateInitTests
    {
        // it("starts with CLOSED guard and IDLE hands, LOCKED feet")
        [Test]
        public void StartsWithClosedGuardAndIdleHandsLockedFeet()
        {
            var g = GameStateOps.InitialGameState();
            Assert.AreEqual(GuardState.Closed, g.Guard);
            Assert.AreEqual(HandState.Idle,    g.Bottom.LeftHand.State);
            Assert.AreEqual(HandState.Idle,    g.Bottom.RightHand.State);
            Assert.AreEqual(FootState.Locked,  g.Bottom.LeftFoot.State);
            Assert.AreEqual(FootState.Locked,  g.Bottom.RightFoot.State);
            Assert.AreEqual(0,                 g.FrameIndex);
        }
    }

    // -------------------------------------------------------------------------
    // describe("stepSimulation routes bottom input to FSMs")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class StepSimulationRoutesBottomInputTests
    {
        // it("bottom L_TRIGGER + grip target kicks off REACHING on left hand")
        [Test]
        public void BottomLTriggerPlusGripTargetKicksOffReachingOnLeftHand()
        {
            var g = GameStateOps.InitialGameState();
            var (next, events) = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(timestamp: 0, lTrigger: 1f),
                GameStateScenarioHelpers.IntentOf(grip: GameStateScenarioHelpers.BottomGrip(
                    lTarget: GripZone.CollarL, lStrength: 1f)),
                GameStateScenarioHelpers.DefaultOpts());

            Assert.AreEqual(HandState.Reaching, next.Bottom.LeftHand.State);
            Assert.AreEqual(GripZone.CollarL,   next.Bottom.LeftHand.Target);
            Assert.IsTrue(GameStateScenarioHelpers.ContainsKind(events, SimEventKind.HandReachStarted));
        }

        // it("hand reaches and grips after the reach timer expires")
        [Test]
        public void HandReachesAndGripsAfterTheReachTimerExpires()
        {
            var g = GameStateOps.InitialGameState();
            var grip = GameStateScenarioHelpers.BottomGrip(lTarget: GripZone.CollarL, lStrength: 1f);
            var intent = GameStateScenarioHelpers.IntentOf(grip: grip);
            var opts = GameStateScenarioHelpers.DefaultOpts();

            g = GameStateOps.Step(g,
                GameStateScenarioHelpers.Frame(timestamp: 0, lTrigger: 1f), intent, opts).NextState;
            g = GameStateOps.Step(g,
                GameStateScenarioHelpers.Frame(timestamp: GameStateScenarioHelpers.ReachMid, lTrigger: 1f),
                intent, opts).NextState;
            var (last, lastEvents) = GameStateOps.Step(g,
                GameStateScenarioHelpers.Frame(timestamp: GameStateScenarioHelpers.ReachMid + 16, lTrigger: 1f),
                intent, opts);

            Assert.AreEqual(HandState.Gripped, last.Bottom.LeftHand.State);
            Assert.IsTrue(GameStateScenarioHelpers.ContainsKind(lastEvents, SimEventKind.HandGripped));
        }

        // it("L_BUMPER edge toggles left foot to UNLOCKED via FOOT_HOOK_TOGGLE intent")
        [Test]
        public void LBumperEdgeTogglesLeftFootToUnlockedViaFootHookToggleIntent()
        {
            var g = GameStateOps.InitialGameState();
            var (next, events) = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(
                    timestamp:   0,
                    buttons:     ButtonBit.LBumper,
                    buttonEdges: ButtonBit.LBumper),
                GameStateScenarioHelpers.IntentOf(discrete: new[]
                {
                    GameStateScenarioHelpers.FootHook(FootSide.L),
                }),
                GameStateScenarioHelpers.DefaultOpts());

            Assert.AreEqual(FootState.Unlocked, next.Bottom.LeftFoot.State);
            Assert.IsTrue(GameStateScenarioHelpers.ContainsKind(events, SimEventKind.FootUnlocked));
        }
    }

    // -------------------------------------------------------------------------
    // describe("Guard FSM (§6)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class GuardFSMTests
    {
        // it("both feet UNLOCKED in the same tick → GUARD_OPENED")
        [Test]
        public void BothFeetUnlockedInTheSameTickGuardOpened()
        {
            var g = GameStateOps.InitialGameState();
            var (next, events) = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(
                    timestamp:   0,
                    buttonEdges: ButtonBit.LBumper | ButtonBit.RBumper),
                GameStateScenarioHelpers.IntentOf(discrete: new[]
                {
                    GameStateScenarioHelpers.FootHook(FootSide.L),
                    GameStateScenarioHelpers.FootHook(FootSide.R),
                }),
                GameStateScenarioHelpers.DefaultOpts());

            Assert.AreEqual(GuardState.Open, next.Guard);
            Assert.IsTrue(GameStateScenarioHelpers.ContainsKind(events, SimEventKind.GuardOpened));
        }

        // it("guard opens across two ticks if each foot unlocks separately")
        [Test]
        public void GuardOpensAcrossTwoTicksIfEachFootUnlocksSeparately()
        {
            var g = GameStateOps.InitialGameState();

            // Tick 1: unlock left foot only.
            g = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(
                    timestamp:   0,
                    buttonEdges: ButtonBit.LBumper),
                GameStateScenarioHelpers.IntentOf(discrete: new[]
                {
                    GameStateScenarioHelpers.FootHook(FootSide.L),
                }),
                GameStateScenarioHelpers.DefaultOpts()).NextState;
            Assert.AreEqual(GuardState.Closed,   g.Guard);
            Assert.AreEqual(FootState.Unlocked,  g.Bottom.LeftFoot.State);

            // Tick 2: unlock right foot → guard opens.
            var (after, events2) = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(
                    timestamp:   16,
                    buttonEdges: ButtonBit.RBumper),
                GameStateScenarioHelpers.IntentOf(discrete: new[]
                {
                    GameStateScenarioHelpers.FootHook(FootSide.R),
                }),
                GameStateScenarioHelpers.DefaultOpts());

            Assert.AreEqual(GuardState.Open, after.Guard);
            Assert.IsTrue(GameStateScenarioHelpers.ContainsKind(events2, SimEventKind.GuardOpened));
        }

        // it("GUARD_OPENED fires only once, not again on subsequent ticks")
        [Test]
        public void GuardOpenedFiresOnlyOnceNotAgainOnSubsequentTicks()
        {
            var g = GameStateOps.InitialGameState();
            g = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(
                    timestamp:   0,
                    buttonEdges: ButtonBit.LBumper | ButtonBit.RBumper),
                GameStateScenarioHelpers.IntentOf(discrete: new[]
                {
                    GameStateScenarioHelpers.FootHook(FootSide.L),
                    GameStateScenarioHelpers.FootHook(FootSide.R),
                }),
                GameStateScenarioHelpers.DefaultOpts()).NextState;

            var (_, events2) = GameStateOps.Step(
                g,
                GameStateScenarioHelpers.Frame(timestamp: 16),
                GameStateScenarioHelpers.IntentOf(),
                GameStateScenarioHelpers.DefaultOpts());

            Assert.IsFalse(GameStateScenarioHelpers.ContainsKind(events2, SimEventKind.GuardOpened));
        }
    }

    // -------------------------------------------------------------------------
    // describe("frameIndex and nowMs propagate")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FrameIndexAndNowMsPropagateTests
    {
        // it("frameIndex increments by one per step; nowMs mirrors the input timestamp")
        [Test]
        public void FrameIndexIncrementsByOnePerStepNowMsMirrorsTheInputTimestamp()
        {
            var g = GameStateOps.InitialGameState();
            g = GameStateOps.Step(g,
                GameStateScenarioHelpers.Frame(timestamp: 100),
                GameStateScenarioHelpers.IntentOf(),
                GameStateScenarioHelpers.DefaultOpts()).NextState;
            Assert.AreEqual(1,    g.FrameIndex);
            Assert.AreEqual(100L, g.NowMs);

            g = GameStateOps.Step(g,
                GameStateScenarioHelpers.Frame(timestamp: 116),
                GameStateScenarioHelpers.IntentOf(),
                GameStateScenarioHelpers.DefaultOpts()).NextState;
            Assert.AreEqual(2,    g.FrameIndex);
            Assert.AreEqual(116L, g.NowMs);
        }
    }

    // -------------------------------------------------------------------------
    // describe("unused variable sanity")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class UnusedVariableSanityTests
    {
        // it("foot and hand timings are exported")
        [Test]
        public void FootAndHandTimingsAreExported()
        {
            Assert.AreEqual(300,    FootTiming.Default.LockingMs);
            Assert.AreEqual(0.3f,   FootFSMOps.LockingPostureThreshold);
            Assert.AreEqual(150,    HandTiming.Default.RetractMs);
        }
    }
}
