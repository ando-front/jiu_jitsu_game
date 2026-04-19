// PURE — fixed-timestep driver per docs/design/architecture_overview_v1.md §3.2.
//
// Semantics:
//   accumulator += realDt
//   while accumulator >= fixedDt:
//     sample provider → resolve Layer D → stepSimulation
//     accumulator -= fixedDt
//
// Layer D (judgment-window commit resolution) sits in this loop because
// it needs per-fixed-step timing (see LAYER_D_TIMING) and visibility of
// the current window state. Caller supplies a Layer-D-state-threading
// function so tests can skip it entirely.

import type { InputFrame } from "../input/types.js";
import type { Intent } from "../input/intent.js";
import {
  stepSimulation,
  type GameState,
  type SimEvent,
  type StepOptions,
} from "../state/game_state.js";
import type { Technique } from "../state/judgment_window.js";

export const FIXED_STEP_MS = 1000 / 60;
export const MAX_STEPS_PER_ADVANCE = 8;

export type StepProvider = Readonly<{
  sample: (stepNowMs: number) => { frame: InputFrame; intent: Intent };
  // Given the freshly-sampled frame/intent + the CURRENT game state,
  // return a technique commit to apply this step (or null). Invoked once
  // per fixed step, after sample() but before stepSimulation(). The
  // provider owns any persistent Layer-D state it needs.
  resolveCommit?: (
    frame: InputFrame,
    intent: Intent,
    game: GameState,
    dtMs: number,
  ) => Technique | null;
}>;

export type FixedStepState = Readonly<{
  accumulatorMs: number;
  simClockMs: number;
  game: GameState;
}>;

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
    const timeScale = game.time.scale;
    const gameDtMs = fixedDtMs * timeScale;
    const confirmed = provider.resolveCommit?.(frame, intent, game, fixedDtMs) ?? null;

    const opts: StepOptions = {
      realDtMs: fixedDtMs,
      gameDtMs,
      confirmedTechnique: confirmed,
    };
    const res = stepSimulation(game, frame, intent, opts);
    game = res.nextState;
    for (const ev of res.events) events.push(ev);

    acc -= fixedDtMs;
  }

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
