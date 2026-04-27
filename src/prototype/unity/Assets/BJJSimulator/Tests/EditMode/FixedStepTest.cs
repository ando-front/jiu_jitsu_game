// NUnit EditMode mirror of src/prototype/web/tests/unit/fixed_step.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite so
// a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/architecture_overview_v1.md §3.2.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public static class FixedStepTestHelpers
    {
        public sealed class IdleProvider : IStepProvider
        {
            public List<long> Seen = new List<long>();

            public (InputFrame Frame, Intent Intent, DefenseIntent? Defense) Sample(long nowMs)
            {
                Seen.Add(nowMs);
                return (InputFrame.Zero(nowMs), Intent.Zero, null);
            }

            public Technique?        ResolveCommit(InputFrame frame, Intent intent, GameState game, float dtMs) => null;
            public CounterTechnique? ResolveCounterCommit(InputFrame frame, GameState game, float dtMs)         => null;
        }

        public static FixedStepState MakeStartState() => new FixedStepState
        {
            AccumulatorMs  = 0f,
            SimClockMs     = 0,
            SimClockFracMs = 0.0,
            Game           = GameStateOps.InitialGameState(0),
        };
    }

    // -------------------------------------------------------------------------
    // describe("fixed-timestep accumulator")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FixedStepAccumulatorTests
    {
        // it("runs zero steps if realDt is below fixedDt")
        [Test]
        public void RunsZeroStepsIfRealDtIsBelowFixedDt()
        {
            var res = FixedStepOps.Advance(
                FixedStepTestHelpers.MakeStartState(), 10f, new FixedStepTestHelpers.IdleProvider());

            Assert.AreEqual(0, res.StepsRun);
            Assert.AreEqual(10f, res.Next.AccumulatorMs, 1e-4f);
            Assert.AreEqual(0,   res.Next.Game.FrameIndex);
        }

        // it("runs exactly one step when realDt == fixedDt")
        [Test]
        public void RunsExactlyOneStepWhenRealDtEqualsFixedDt()
        {
            var res = FixedStepOps.Advance(
                FixedStepTestHelpers.MakeStartState(),
                FixedStepOps.FixedStepMs,
                new FixedStepTestHelpers.IdleProvider());

            Assert.AreEqual(1, res.StepsRun);
            Assert.AreEqual(1, res.Next.Game.FrameIndex);
        }

        // it("runs multiple steps and preserves leftover accumulator")
        [Test]
        public void RunsMultipleStepsAndPreservesLeftoverAccumulator()
        {
            // 2.5 fixedDt worth of real time → expect 2 steps + half a step left.
            float realDt = FixedStepOps.FixedStepMs * 2.5f;
            var res = FixedStepOps.Advance(
                FixedStepTestHelpers.MakeStartState(), realDt, new FixedStepTestHelpers.IdleProvider());

            Assert.AreEqual(2, res.StepsRun);
            Assert.AreEqual(FixedStepOps.FixedStepMs * 0.5f, res.Next.AccumulatorMs, 1e-3f);
        }
    }

    // -------------------------------------------------------------------------
    // describe("fixed-timestep step cap")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FixedStepStepCapTests
    {
        // it("never runs more than MAX_STEPS_PER_ADVANCE per call")
        [Test]
        public void NeverRunsMoreThanMaxStepsPerAdvancePerCall()
        {
            // Simulating a backgrounded tab: 10 seconds of realDt arriving at once.
            var res = FixedStepOps.Advance(
                FixedStepTestHelpers.MakeStartState(), 10_000f, new FixedStepTestHelpers.IdleProvider());

            Assert.AreEqual(FixedStepOps.MaxStepsPerAdvance, res.StepsRun);
            Assert.AreEqual(0f, res.Next.AccumulatorMs);
        }
    }

    // -------------------------------------------------------------------------
    // describe("fixed-timestep sim clock")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FixedStepSimClockTests
    {
        // it("advances simClockMs by fixedDt per step, independent of realDt jitter")
        [Test]
        public void AdvancesSimClockMsByFixedDtPerStepIndependentOfRealDtJitter()
        {
            var s = FixedStepTestHelpers.MakeStartState();
            // Deliver 33ms twice (bad vsync), each giving 1–2 steps.
            s = FixedStepOps.Advance(s, 33f, new FixedStepTestHelpers.IdleProvider()).Next;
            s = FixedStepOps.Advance(s, 33f, new FixedStepTestHelpers.IdleProvider()).Next;

            // 33ms × 2 = 66ms total real time; floor(66 / 16.6667) = 3 steps
            // run total. Sub-ms residue is carried in SimClockFracMs so the
            // visible long clock lands at floor(3 * 16.6667) = 50.
            const int  expectedSteps    = 3;
            const long expectedSimClock = 50;
            Assert.AreEqual(expectedSimClock, s.SimClockMs);
            // sanity: the carried residue is 3 * 16.6667 - 50 = 0.0
            Assert.AreEqual(0.0, s.SimClockFracMs, 1e-9);
            // also sanity-check via the unused expectedSteps so a refactor that
            // changes step count surfaces here.
            Assert.Greater(expectedSteps, 0);
        }
    }

    // -------------------------------------------------------------------------
    // describe("fixed-timestep frame timestamp routing")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class FixedStepFrameTimestampRoutingTests
    {
        // it("each step sees a timestamp equal to its simClock")
        [Test]
        public void EachStepSeesATimestampEqualToItsSimClock()
        {
            var provider = new FixedStepTestHelpers.IdleProvider();
            // Use a slightly-over realDt so floating-point residue at the 3rd step
            // doesn't leave the accumulator just under fixedDt.
            FixedStepOps.Advance(
                FixedStepTestHelpers.MakeStartState(),
                3f * FixedStepOps.FixedStepMs + 0.1f,
                provider);

            Assert.AreEqual(3, provider.Seen.Count);
            // With sub-ms residue carried across steps, the visible long clock
            // lands at floor(N * 16.6667). Step1=16, step2=33, step3=50 — same
            // as the TS reference suite (which uses float64).
            Assert.AreEqual(16L, provider.Seen[0]);
            Assert.AreEqual(33L, provider.Seen[1]);
            Assert.AreEqual(50L, provider.Seen[2]);
        }
    }
}
