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
        // Sub-millisecond residue carried between Advance() calls so the long
        // SimClockMs doesn't drift relative to a true fixedDt of 1000/60 ≈
        // 16.6667 ms. Without this, `simClock += (long)16.6667` truncates to
        // 16 per step (3.7% slow), accumulating 40 ms of slip per wall second
        // and silently pulling round timer / stamina / judgment-window timing
        // off the Stage 1 reference. Stage 1 (TS) keeps the clock as
        // float64 ms so this field is the C#-side equivalent. See
        // docs/design/stage2_port_plan_v1.md §2.4 — the public clock stays
        // `long` (predictable comparison semantics, no FP equality bugs); the
        // residue is a private accumulator only.
        public double    SimClockFracMs;
        public GameState Game;

        public static FixedStepState Initial(long startMs, GameState game) =>
            new FixedStepState
            {
                AccumulatorMs  = 0f,
                SimClockMs     = startMs,
                SimClockFracMs = 0.0,
                Game           = game,
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

            var    events    = new List<SimEvent>(8);
            float  acc       = prev.AccumulatorMs + realDtMs;
            long   simClock  = prev.SimClockMs;
            double clockFrac = prev.SimClockFracMs;
            // Sub-ms increment computed in double to avoid float rounding
            // (1000f/60f as float = 16.66666603…, so 3× steps land on
            // 49.99999809 and floor to 49 instead of 50). Done as 1000.0/60.0
            // directly so the value matches the Stage 1 TS reference.
            double fixedDtPrecise = (fixedDtMs == FixedStepMs)
                ? 1000.0 / 60.0
                : (double)fixedDtMs;
            var    game      = prev.Game;
            int    steps     = 0;

            while (acc >= fixedDtMs && steps < maxSteps)
            {
                steps++;
                // Carry sub-ms residue so 16.6667 + 16.6667 + 16.6667 lands
                // on 50, not 48. Public clock stays `long`; only the carry
                // is a double.
                clockFrac += fixedDtPrecise;
                long whole = (long)clockFrac;
                simClock  += whole;
                clockFrac -= whole;

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
                    AccumulatorMs  = acc,
                    SimClockMs     = simClock,
                    SimClockFracMs = clockFrac,
                    Game           = game,
                },
                Events   = events.ToArray(),
                StepsRun = steps,
            };
        }
    }
}
