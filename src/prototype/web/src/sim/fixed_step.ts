// PURE — fixed-timestep driver per docs/design/architecture_overview_v1.md §3.2.
//
// Semantics:
//   accumulator += realDt
//   while accumulator >= fixedDt:
//     stepSimulation with realDtMs = fixedDt
//                       gameDtMs = fixedDt * timeScale (from previous tick)
//     accumulator -= fixedDt
//
// The "timeScale from previous tick" rule keeps the judgment-window
// slow-motion consistent: the frame in which the window OPENS still ticks
// at scale 1 (game time moves normally up to the exact entry moment), and
// subsequent frames use scale 0.3. This mirrors the §9 design rule that
// A層 uses real_dt and B以降 uses game_dt.
//
// The driver does not touch the DOM or rAF. main.ts feeds it realDt from
// whichever source it prefers (rAF, setInterval, a test clock).

import type { InputFrame } from "../input/types.js";
import type { Intent } from "../input/intent.js";
import {
  stepSimulation,
  type GameState,
  type SimEvent,
  type StepOptions,
} from "../state/game_state.js";
import type { Technique } from "../state/judgment_window.js";

export const FIXED_STEP_MS = 1000 / 60; // 16.666...ms, matches §A.1 polling

// Hard upper bound on how many fixed steps we run per advance() call.
// Without a cap, a backgrounded tab that accumulates multiple seconds of
// realDt would try to catch up in a single frame and hang. 8 is generous
// for a typical hiccup (133ms budget) and small enough to never block.
export const MAX_STEPS_PER_ADVANCE = 8;

export type StepProvider = Readonly<{
  // Supplies a fresh InputFrame + Intent for each fixed tick. `stepNowMs`
  // is the simulated-time timestamp the sim should attribute to this
  // frame (already advanced by fixedDt per step).
  sample: (stepNowMs: number) => { frame: InputFrame; intent: Intent };
  // Optional — if the outside world resolves a technique commitment, it
  // feeds it here. Returning null each call means "no commit this step".
  confirmedTechnique?: (stepNowMs: number) => Technique | null;
}>;

export type FixedStepState = Readonly<{
  accumulatorMs: number;
  simClockMs: number;      // monotonic simulated-time clock used for InputFrame timestamps
  game: GameState;
}>;

export function createFixedStepState(startMs: number = 0): FixedStepState {
  return Object.freeze({
    accumulatorMs: 0,
    simClockMs: startMs,
    // initialGameState is deliberately NOT imported here — callers pass
    // their own starting GameState so tests can seed mid-session state.
    // We construct a default one when needed via `advance`'s type.
    game: undefined as unknown as GameState,
  });
}

// Concrete entry point used by main.ts: you provide the initial GameState
// and a provider; each call advances the sim by as many fixed steps as
// realDtMs can cover.
export function advance(
  prev: FixedStepState,
  realDtMs: number,
  provider: StepProvider,
  fixedDtMs: number = FIXED_STEP_MS,
  maxSteps: number = MAX_STEPS_PER_ADVANCE,
): { next: FixedStepState; events: readonly SimEvent[]; stepsRun: number } {
  const events: SimEvent[] = [];
  let acc = prev.accumulatorMs + realDtMs;
  let simClock = prev.simClockMs;
  let game = prev.game;
  let stepsRun = 0;

  while (acc >= fixedDtMs && stepsRun < maxSteps) {
    stepsRun += 1;
    simClock += fixedDtMs;

    const { frame, intent } = provider.sample(simClock);
    // The time scale that was active AT THE END of the previous step gates
    // this step's gameDtMs. This is the piece that makes judgment-window
    // slow-motion show up on the tick AFTER OPEN, not during OPENING.
    const timeScale = game.time.scale;
    const gameDtMs = fixedDtMs * timeScale;
    const confirmed = provider.confirmedTechnique?.(simClock) ?? null;

    const opts: StepOptions = { realDtMs: fixedDtMs, gameDtMs, confirmedTechnique: confirmed };
    const res = stepSimulation(game, frame, intent, opts);
    game = res.nextState;
    for (const ev of res.events) events.push(ev);

    acc -= fixedDtMs;
  }

  // Drop any remaining accumulator if we hit the step cap — better to
  // lose a few ms of sim progress than freeze the UI chasing wall clock.
  if (stepsRun >= maxSteps && acc >= fixedDtMs) {
    acc = 0;
  }

  const next: FixedStepState = Object.freeze({
    accumulatorMs: acc,
    simClockMs: simClock,
    game,
  });
  return { next, events: Object.freeze(events), stepsRun };
}
