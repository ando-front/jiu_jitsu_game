// NUnit EditMode mirror of src/prototype/web/tests/scenario/pass_attempt_integration.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/input_system_defense_v1.md §B.7
//            (PASS_COMMIT routed through GameStateOps.Step).
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests.Scenario
{
    // -------------------------------------------------------------------------
    // Helpers (mirrors foot() / NEUTRAL_INTENT / frame() / baseDefense() /
    // makeEligibleSeed()). Local to the Scenario folder so we don't
    // cross-reference EditMode unit helpers.
    // -------------------------------------------------------------------------

    public static class PassAttemptIntegrationHelpers
    {
        public static FootFSM Foot(FootSide side, FootState state) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = 0 };

        public static Intent NeutralIntent() => new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = System.Array.Empty<DiscreteIntent>(),
        };

        public static InputFrame Frame(long timestamp = 0, Vec2 rs = default)
        {
            var f = InputFrame.Zero(timestamp);
            f.Rs = rs;
            return f;
        }

        // Mirrors baseDefense({...}) in TS — defaults to BICEP_L / KNEE_R at 0.7
        // pressure, which is the well-formed control-zone pair from §B.7.1.
        public static DefenseIntent BaseDefense(
            DefenseDiscreteIntent[] discrete = null) =>
            new DefenseIntent
            {
                Hip  = TopHipIntent.Zero,
                Base = new TopBaseIntent
                {
                    LHandTarget   = BaseZone.BicepL,
                    LBasePressure = 0.7f,
                    RHandTarget   = BaseZone.KneeR,
                    RBasePressure = 0.7f,
                },
                Discrete = discrete ?? System.Array.Empty<DefenseDiscreteIntent>(),
            };

        public static GameState MakeEligibleSeed()
        {
            var g = GameStateOps.InitialGameState(0);
            var bottom = g.Bottom;
            bottom.LeftFoot  = Foot(FootSide.L, FootState.Unlocked);
            bottom.RightFoot = Foot(FootSide.R, FootState.Locked);
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
    // describe("PASS_COMMIT starts a pass when eligible")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class PassCommitStartsAPassWhenEligibleTests
    {
        // it("eligible commit → PASS_STARTED event, passAttempt.kind becomes IN_PROGRESS")
        [Test]
        public void EligibleCommitEmitsPassStartedAndPassAttemptBecomesInProgress()
        {
            var seed = PassAttemptIntegrationHelpers.MakeEligibleSeed();
            var defense = PassAttemptIntegrationHelpers.BaseDefense(new[]
            {
                new DefenseDiscreteIntent
                {
                    Kind = DefenseDiscreteIntentKind.PassCommit,
                    Rs   = new Vec2(0f, -1f),
                },
            });

            var res = GameStateOps.Step(
                seed,
                PassAttemptIntegrationHelpers.Frame(timestamp: 0, rs: new Vec2(0f, -1f)),
                PassAttemptIntegrationHelpers.NeutralIntent(),
                PassAttemptIntegrationHelpers.Opts(defense));

            bool passStarted = false;
            foreach (var e in res.Events)
                if (e.Kind == SimEventKind.PassStarted) { passStarted = true; break; }

            Assert.IsTrue(passStarted, "expected PASS_STARTED event");
            Assert.AreEqual(PassAttemptKind.InProgress, res.NextState.PassAttempt.Kind);
        }
    }

    // -------------------------------------------------------------------------
    // describe("PASS_COMMIT rejected silently when ineligible")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class PassCommitRejectedSilentlyWhenIneligibleTests
    {
        // it("both feet LOCKED → commit ignored, passAttempt stays IDLE")
        [Test]
        public void BothFeetLockedCommitIgnoredAndPassAttemptStaysIdle()
        {
            // Default initialGameState has both feet LOCKED — exactly the
            // condition under test.
            var seed = GameStateOps.InitialGameState(0);

            var defense = PassAttemptIntegrationHelpers.BaseDefense(new[]
            {
                new DefenseDiscreteIntent
                {
                    Kind = DefenseDiscreteIntentKind.PassCommit,
                    Rs   = new Vec2(0f, -1f),
                },
            });

            var res = GameStateOps.Step(
                seed,
                PassAttemptIntegrationHelpers.Frame(timestamp: 0, rs: new Vec2(0f, -1f)),
                PassAttemptIntegrationHelpers.NeutralIntent(),
                PassAttemptIntegrationHelpers.Opts(defense));

            bool passStarted = false;
            foreach (var e in res.Events)
                if (e.Kind == SimEventKind.PassStarted) { passStarted = true; break; }

            Assert.IsFalse(passStarted, "did not expect PASS_STARTED event");
            Assert.AreEqual(PassAttemptKind.Idle, res.NextState.PassAttempt.Kind);
        }
    }

    // -------------------------------------------------------------------------
    // describe("PASS_SUCCEEDED after 5s, session ends")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class PassSucceededAfter5sSessionEndsTests
    {
        // it("no attacker triangle during the window → PASS_SUCCEEDED + SESSION_ENDED(PASS_SUCCESS)")
        [Test]
        public void NoAttackerTriangleDuringWindowSucceedsAndSessionEnds()
        {
            var g = PassAttemptIntegrationHelpers.MakeEligibleSeed();

            // Tick 1: commit.
            var commit = PassAttemptIntegrationHelpers.BaseDefense(new[]
            {
                new DefenseDiscreteIntent
                {
                    Kind = DefenseDiscreteIntentKind.PassCommit,
                    Rs   = new Vec2(0f, -1f),
                },
            });
            g = GameStateOps.Step(
                g,
                PassAttemptIntegrationHelpers.Frame(timestamp: 0, rs: new Vec2(0f, -1f)),
                PassAttemptIntegrationHelpers.NeutralIntent(),
                PassAttemptIntegrationHelpers.Opts(commit)).NextState;
            Assert.AreEqual(PassAttemptKind.InProgress, g.PassAttempt.Kind);

            // Tick 2: 5s later, no more commit requested.
            var hold = PassAttemptIntegrationHelpers.BaseDefense(); // no discrete events
            var res = GameStateOps.Step(
                g,
                PassAttemptIntegrationHelpers.Frame(
                    timestamp: PassTiming.Default.WindowMs + 1,
                    rs: new Vec2(0f, -1f)),
                PassAttemptIntegrationHelpers.NeutralIntent(),
                PassAttemptIntegrationHelpers.Opts(hold));

            bool passSucceeded = false;
            bool sessionEndedPassSuccess = false;
            foreach (var e in res.Events)
            {
                if (e.Kind == SimEventKind.PassSucceeded) passSucceeded = true;
                if (e.Kind == SimEventKind.SessionEnded &&
                    e.SessionEndReason == SessionEndReason.PassSuccess)
                    sessionEndedPassSuccess = true;
            }

            Assert.IsTrue(passSucceeded, "expected PASS_SUCCEEDED event");
            Assert.IsTrue(sessionEndedPassSuccess,
                "expected SESSION_ENDED(PASS_SUCCESS) event");
            Assert.IsTrue(res.NextState.SessionEnded);
        }
    }
}
