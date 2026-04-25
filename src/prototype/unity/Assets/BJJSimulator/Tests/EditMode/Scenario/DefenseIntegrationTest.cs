// NUnit EditMode mirror of src/prototype/web/tests/scenario/defense_integration.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so a
// regression on either side produces a named, greppable failure.
//
// Integration tests: DefenseIntent flowing into stepSimulation.
// References docs/design/input_system_defense_v1.md §B.5 and
// docs/design/state_machines_v1.md §3.3 / §4.1.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers — mirror the gripped() / frame() factories and NEUTRAL_INTENT in TS.
    // -------------------------------------------------------------------------

    public static class DefenseIntegrationHelpers
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

        public static readonly Intent NeutralIntent = new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = System.Array.Empty<DiscreteIntent>(),
        };

        public static InputFrame Frame(
            long timestamp = 0,
            float lTrigger = 0f,
            float rTrigger = 0f) =>
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

        public static StepOptions Opts(
            float dtMs = 16.67f,
            Technique? confirmedTechnique = null,
            DefenseIntent? defense = null) =>
            new StepOptions
            {
                RealDtMs           = dtMs,
                GameDtMs           = dtMs,
                ConfirmedTechnique = confirmedTechnique,
                ConfirmedCounter   = null,
                DefenseIntent      = defense,
            };
    }

    // -------------------------------------------------------------------------
    // describe("DefenseIntent — posture_break recovery (§3.3 bullet 4)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class DefenseIntentPostureBreakRecoveryTests
    {
        // it("weight_forward shoves break toward origin (negative y component)")
        [Test]
        public void WeightForwardShovesBreakTowardOriginNegativeYComponent()
        {
            // Seed a pre-existing forward break on the TOP actor; the defender
            // pushes weight forward → sagittal should fall faster than decay alone.
            var seed = GameStateOps.InitialGameState(0);
            var top = seed.Top;
            top.PostureBreak = new Vec2(0f, 0.8f);
            seed.Top = top;

            // Run 500ms with defender pushing forward.
            var defense = new DefenseIntent
            {
                Hip      = new TopHipIntent { WeightForward = 1f, WeightLateral = 0f },
                Base     = DefenseIntent.Zero.Base,
                Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
            };

            var g = seed;
            for (float t = 0; t < 500f; t += 16.67f)
            {
                g = GameStateOps.Step(
                    g,
                    DefenseIntegrationHelpers.Frame(timestamp: (long)t),
                    DefenseIntegrationHelpers.NeutralIntent,
                    DefenseIntegrationHelpers.Opts(
                        dtMs:    16.67f,
                        defense: defense)).NextState;
            }

            // Compare against pure-decay baseline: same seed, ZERO defense.
            var baseline = seed;
            for (float t = 0; t < 500f; t += 16.67f)
            {
                baseline = GameStateOps.Step(
                    baseline,
                    DefenseIntegrationHelpers.Frame(timestamp: (long)t),
                    DefenseIntegrationHelpers.NeutralIntent,
                    DefenseIntegrationHelpers.Opts(
                        dtMs:    16.67f,
                        defense: DefenseIntent.Zero)).NextState;
            }

            Assert.Less(g.Top.PostureBreak.Y, baseline.Top.PostureBreak.Y);
        }
    }

    // -------------------------------------------------------------------------
    // describe("DefenseIntent — arm_extracted clear via base hold (§4.1)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class DefenseIntentArmExtractedClearTests
    {
        // it("RECOVERY_HOLD clears a previously-extracted arm flag")
        [Test]
        public void RecoveryHoldClearsAPreviouslyExtractedArmFlag()
        {
            // Seed a GameState with arm_extracted already true. Feed pulling
            // conditions so arm_extracted would normally stay; but also feed
            // RECOVERY_HOLD — §4.1 defender-base-hold clears the flag.
            var seed = GameStateOps.InitialGameState(0);
            var bottom = seed.Bottom;
            bottom.LeftHand = DefenseIntegrationHelpers.Gripped(HandSide.L, GripZone.SleeveR);
            seed.Bottom = bottom;
            var top = seed.Top;
            top.ArmExtractedLeft = true;
            seed.Top = top;
            seed.TopArmExtracted = new ArmExtractedState
            {
                Left           = ArmExtractedState.Initial.Left,
                Right          = true,                       // opponent's right arm is extracted
                LeftSustainMs  = ArmExtractedState.Initial.LeftSustainMs,
                RightSustainMs = ArmExtractedState.Initial.RightSustainMs,
                LeftSetAtMs    = ArmExtractedState.Initial.LeftSetAtMs,
                RightSetAtMs   = 0,
            };

            var defense = new DefenseIntent
            {
                Hip      = DefenseIntent.Zero.Hip,
                Base     = DefenseIntent.Zero.Base,
                Discrete = new[] { new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.RecoveryHold } },
            };

            var pullingIntent = new Intent
            {
                Hip = new HipIntent { HipAngleTarget = 0f, HipPush = -0.6f, HipLateral = 0f },
                Grip = new GripIntent
                {
                    LHandTarget   = GripZone.SleeveR,
                    LGripStrength = 0.9f,
                    RHandTarget   = GripZone.None,
                    RGripStrength = 0f,
                },
                Discrete = System.Array.Empty<DiscreteIntent>(),
            };

            var (next, _) = GameStateOps.Step(
                seed,
                DefenseIntegrationHelpers.Frame(timestamp: 16, lTrigger: 0.9f), // keep pulling
                pullingIntent,
                DefenseIntegrationHelpers.Opts(
                    dtMs:    16f,
                    defense: defense));

            Assert.IsFalse(next.TopArmExtracted.Right);
            Assert.IsFalse(next.Top.ArmExtractedRight);
        }
    }
}
