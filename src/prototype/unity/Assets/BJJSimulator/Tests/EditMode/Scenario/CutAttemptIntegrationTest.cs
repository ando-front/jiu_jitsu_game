// NUnit EditMode mirror of src/prototype/web/tests/scenario/cut_attempt_integration.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/state_machines_v1.md §4.2 + §2.1.4
//            (defender cut-attempt FSM driving attacker HandFSM into RETRACT).
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors gripped() / frame() / NEUTRAL_INTENT / seedWithGrip()).
    // Local to the Scenario folder so we don't cross-reference EditMode unit
    // helpers.
    // -------------------------------------------------------------------------

    public static class CutAttemptIntegrationHelpers
    {
        public static HandFSM Gripped(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = GripZone.SleeveR,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        public static Intent NeutralIntent() => new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = System.Array.Empty<DiscreteIntent>(),
        };

        public static InputFrame Frame(long timestamp = 0, float lTrigger = 0f)
        {
            var f = InputFrame.Zero(timestamp);
            f.LTrigger = lTrigger;
            return f;
        }

        public static GameState SeedWithGrip()
        {
            var g = GameStateOps.InitialGameState(0);
            var bottom = g.Bottom;
            bottom.LeftHand = Gripped(HandSide.L);
            g.Bottom = bottom;
            return g;
        }

        public static StepOptions Opts(DefenseIntent defense) => new StepOptions
        {
            RealDtMs           = 16f,
            GameDtMs           = 16f,
            ConfirmedTechnique = null,
            DefenseIntent      = defense,
            ConfirmedCounter   = null,
        };
    }

    // -------------------------------------------------------------------------
    // describe("cut attempt flow through stepSimulation")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class CutAttemptIntegrationTests
    {
        // it("commit fires CUT_STARTED event and cutAttempts slot transitions")
        [Test]
        public void CommitFiresCutStartedEventAndCutAttemptsSlotTransitions()
        {
            var seed = CutAttemptIntegrationHelpers.SeedWithGrip();

            var defense = new DefenseIntent
            {
                Hip  = TopHipIntent.Zero,
                Base = TopBaseIntent.Zero,
                Discrete = new[]
                {
                    new DefenseDiscreteIntent
                    {
                        Kind    = DefenseDiscreteIntentKind.CutAttempt,
                        CutSide = HandSide.L,
                        Rs      = new Vec2(-1f, 0f),
                    },
                },
            };

            var attackerIntent = CutAttemptIntegrationHelpers.NeutralIntent();
            attackerIntent.Grip = new GripIntent
            {
                LHandTarget   = GripZone.SleeveR,
                LGripStrength = 0.3f,
                RHandTarget   = GripZone.None,
                RGripStrength = 0f,
            };

            var res = GameStateOps.Step(
                seed,
                CutAttemptIntegrationHelpers.Frame(timestamp: 0, lTrigger: 0.3f),
                attackerIntent,
                CutAttemptIntegrationHelpers.Opts(defense));

            bool cutStarted = false;
            foreach (var e in res.Events)
                if (e.Kind == SimEventKind.CutStarted) { cutStarted = true; break; }

            Assert.IsTrue(cutStarted, "expected CUT_STARTED event");
            Assert.AreEqual(CutSlotKind.InProgress, res.NextState.CutAttempts.Left.Kind);
        }

        // it("weak attacker grip → cut SUCCEEDS and attacker hand enters RETRACT")
        [Test]
        public void WeakAttackerGripCutSucceedsAndAttackerHandEntersRetract()
        {
            var g = CutAttemptIntegrationHelpers.SeedWithGrip();

            var attackerIntent = CutAttemptIntegrationHelpers.NeutralIntent();
            attackerIntent.Grip = new GripIntent
            {
                LHandTarget   = GripZone.SleeveR,
                LGripStrength = 0.3f,
                RHandTarget   = GripZone.None,
                RGripStrength = 0f,
            };

            // Tick 1: defender commits.
            var defense1 = new DefenseIntent
            {
                Hip  = TopHipIntent.Zero,
                Base = TopBaseIntent.Zero,
                Discrete = new[]
                {
                    new DefenseDiscreteIntent
                    {
                        Kind    = DefenseDiscreteIntentKind.CutAttempt,
                        CutSide = HandSide.L,
                        Rs      = new Vec2(-1f, 0f),
                    },
                },
            };
            g = GameStateOps.Step(
                g,
                CutAttemptIntegrationHelpers.Frame(timestamp: 0, lTrigger: 0.3f),
                attackerIntent,
                CutAttemptIntegrationHelpers.Opts(defense1)).NextState;

            // Tick 2: 1500ms later, attacker grip is still weak.
            var defense2 = new DefenseIntent
            {
                Hip      = TopHipIntent.Zero,
                Base     = TopBaseIntent.Zero,
                Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
            };
            var res = GameStateOps.Step(
                g,
                CutAttemptIntegrationHelpers.Frame(timestamp: CutTiming.Default.AttemptMs, lTrigger: 0.3f),
                attackerIntent,
                CutAttemptIntegrationHelpers.Opts(defense2));

            bool cutSucceeded = false;
            SimEvent? brokenEvent = null;
            foreach (var e in res.Events)
            {
                if (e.Kind == SimEventKind.CutSucceeded) cutSucceeded = true;
                if (e.Kind == SimEventKind.HandGripBroken && brokenEvent == null) brokenEvent = e;
            }

            Assert.IsTrue(cutSucceeded, "expected CUT_SUCCEEDED event");
            // Attacker L hand must have routed through GRIP_BROKEN(OPPONENT_CUT) → RETRACT.
            Assert.AreEqual(HandState.Retract, res.NextState.Bottom.LeftHand.State);
            Assert.IsTrue(brokenEvent.HasValue, "expected GRIP_BROKEN event");
            Assert.AreEqual(GripBrokenReason.OpponentCut, brokenEvent.Value.GripBrokenReason);
        }

        // it("strong attacker grip → cut FAILS and attacker hand stays GRIPPED")
        [Test]
        public void StrongAttackerGripCutFailsAndAttackerHandStaysGripped()
        {
            var g = CutAttemptIntegrationHelpers.SeedWithGrip();

            var attackerIntent = CutAttemptIntegrationHelpers.NeutralIntent();
            attackerIntent.Grip = new GripIntent
            {
                LHandTarget   = GripZone.SleeveR,
                LGripStrength = 0.9f,
                RHandTarget   = GripZone.None,
                RGripStrength = 0f,
            };

            var defense1 = new DefenseIntent
            {
                Hip  = TopHipIntent.Zero,
                Base = TopBaseIntent.Zero,
                Discrete = new[]
                {
                    new DefenseDiscreteIntent
                    {
                        Kind    = DefenseDiscreteIntentKind.CutAttempt,
                        CutSide = HandSide.L,
                        Rs      = new Vec2(-1f, 0f),
                    },
                },
            };
            g = GameStateOps.Step(
                g,
                CutAttemptIntegrationHelpers.Frame(timestamp: 0, lTrigger: 0.9f),
                attackerIntent,
                CutAttemptIntegrationHelpers.Opts(defense1)).NextState;

            var defense2 = new DefenseIntent
            {
                Hip      = TopHipIntent.Zero,
                Base     = TopBaseIntent.Zero,
                Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
            };
            var res = GameStateOps.Step(
                g,
                CutAttemptIntegrationHelpers.Frame(timestamp: CutTiming.Default.AttemptMs, lTrigger: 0.9f),
                attackerIntent,
                CutAttemptIntegrationHelpers.Opts(defense2));

            bool cutFailed = false;
            foreach (var e in res.Events)
                if (e.Kind == SimEventKind.CutFailed) { cutFailed = true; break; }

            Assert.IsTrue(cutFailed, "expected CUT_FAILED event");
            Assert.AreEqual(HandState.Gripped, res.NextState.Bottom.LeftHand.State);
        }
    }
}
