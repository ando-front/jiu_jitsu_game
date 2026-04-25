// Ported 1:1 from src/prototype/web/src/sim/fixed_step.ts.
// PURE — fixed-timestep driver.
// Reference: docs/design/architecture_overview_v1.md §3.2.
//
// Semantics:
//   accumulator += realDt
//   while accumulator >= fixedDt:
//     sample provider → resolve Layer D → StepSimulation
//     accumulator -= fixedDt

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Provider interface (caller-supplied; allows test injection)
    // -------------------------------------------------------------------------

    public interface IStepProvider
    {
        /// <summary>
        /// Sample raw input for this step. nowMs = current sim clock.
        /// </summary>
        (InputFrame Frame, Intent Intent, DefenseIntent? Defense) Sample(long nowMs);

        /// <summary>
        /// Given the freshly-sampled frame + the CURRENT game state,
        /// return a technique commit for this step (or null).
        /// Invoked once per fixed step, before StepSimulation.
        /// </summary>
        Technique? ResolveCommit(InputFrame frame, Intent intent, GameState game, float dtMs);

        /// <summary>
        /// Symmetric hook for the defender's counter window.
        /// </summary>
        CounterTechnique? ResolveCounterCommit(InputFrame frame, GameState game, float dtMs);
    }

    // -------------------------------------------------------------------------
    // Persistent state
    // -------------------------------------------------------------------------

    public struct FixedStepState
    {
        public float     AccumulatorMs;
        public long      SimClockMs;
        public GameState Game;

        public static FixedStepState Initial(long startMs, GameState game) =>
            new FixedStepState
            {
                AccumulatorMs = 0f,
                SimClockMs    = startMs,
                Game          = game,
            };
    }

    // -------------------------------------------------------------------------
    // Advance result
    // -------------------------------------------------------------------------

    public struct AdvanceResult
    {
        public FixedStepState  Next;
        public SimEvent[]      Events;
        public int             StepsRun;
    }

    // -------------------------------------------------------------------------
    // Pure fixed-step driver
    // -------------------------------------------------------------------------

    public static class FixedStepOps
    {
        public const float FixedStepMs           = 1000f / 60f;
        public const int   MaxStepsPerAdvance     = 8;

        public static AdvanceResult Advance(
            FixedStepState prev,
            float          realDtMs,
            IStepProvider  provider,
            float          fixedDtMs = 0f,
            int            maxSteps  = 0)
        {
            if (fixedDtMs <= 0f) fixedDtMs = FixedStepMs;
            if (maxSteps  <= 0)  maxSteps  = MaxStepsPerAdvance;

            var   events   = new List<SimEvent>(8);
            float acc      = prev.AccumulatorMs + realDtMs;
            long  simClock = prev.SimClockMs;
            var   game     = prev.Game;
            int   steps    = 0;

            while (acc >= fixedDtMs && steps < maxSteps)
            {
                steps++;
                simClock += (long)fixedDtMs;

                var (frame, intent, defense) = provider.Sample(simClock);
                float timeScale  = game.Time.Scale;
                float gameDtMs   = fixedDtMs * timeScale;
                var   confirmed  = provider.ResolveCommit(frame, intent, game, fixedDtMs);
                var   confirmedC = provider.ResolveCounterCommit(frame, game, fixedDtMs);

                var opts = new StepOptions
                {
                    RealDtMs           = fixedDtMs,
                    GameDtMs           = gameDtMs,
                    ConfirmedTechnique = confirmed,
                    DefenseIntent      = defense,
                    ConfirmedCounter   = confirmedC,
                };

                var res  = GameStateOps.Step(game, frame, intent, opts);
                game     = res.NextState;
                events.AddRange(res.Events);

                acc -= fixedDtMs;
            }

            // Spiral-of-death guard: drop any excess if we hit the step cap.
            if (steps >= maxSteps && acc >= fixedDtMs)
                acc = 0f;

            return new AdvanceResult
            {
                Next = new FixedStepState
                {
                    AccumulatorMs = acc,
                    SimClockMs    = simClock,
                    Game          = game,
                },
                Events   = events.ToArray(),
                StepsRun = steps,
            };
        }
    }
}
