// Tests for the fixed-timestep driver.
// References docs/design/architecture_overview_v1.md §3.2.

import { describe, expect, it } from "vitest";
import {
  FIXED_STEP_MS,
  MAX_STEPS_PER_ADVANCE,
  advance,
  type FixedStepState,
  type StepProvider,
} from "../../src/sim/fixed_step.js";
import { initialGameState } from "../../src/state/game_state.js";
import type { InputFrame } from "../../src/input/types.js";
import type { Intent } from "../../src/input/intent.js";

function emptyFrame(timestamp: number): InputFrame {
  return Object.freeze({
    timestamp,
    ls: { x: 0, y: 0 },
    rs: { x: 0, y: 0 },
    l_trigger: 0,
    r_trigger: 0,
    buttons: 0,
    button_edges: 0,
    device_kind: "Keyboard" as const,
  });
}

function emptyIntent(): Intent {
  return Object.freeze({
    hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    grip: { l_hand_target: null, l_grip_strength: 0, r_hand_target: null, r_grip_strength: 0 },
    discrete: [],
  });
}

function idleProvider(): StepProvider {
  return {
    sample: (t: number) => ({ frame: emptyFrame(t), intent: emptyIntent() }),
  };
}

function makeStartState(): FixedStepState {
  return Object.freeze({
    accumulatorMs: 0,
    simClockMs: 0,
    game: initialGameState(0),
  });
}

describe("fixed-timestep accumulator", () => {
  it("runs zero steps if realDt is below fixedDt", () => {
    const res = advance(makeStartState(), 10, idleProvider());
    expect(res.stepsRun).toBe(0);
    expect(res.next.accumulatorMs).toBeCloseTo(10, 5);
    expect(res.next.game.frameIndex).toBe(0);
  });

  it("runs exactly one step when realDt == fixedDt", () => {
    const res = advance(makeStartState(), FIXED_STEP_MS, idleProvider());
    expect(res.stepsRun).toBe(1);
    expect(res.next.game.frameIndex).toBe(1);
  });

  it("runs multiple steps and preserves leftover accumulator", () => {
    // 2.5 fixedDt worth of real time → expect 2 steps + half a step left.
    const realDt = FIXED_STEP_MS * 2.5;
    const res = advance(makeStartState(), realDt, idleProvider());
    expect(res.stepsRun).toBe(2);
    expect(res.next.accumulatorMs).toBeCloseTo(FIXED_STEP_MS * 0.5, 5);
  });
});

describe("fixed-timestep step cap", () => {
  it("never runs more than MAX_STEPS_PER_ADVANCE per call", () => {
    // Simulating a backgrounded tab: 10 seconds of realDt arriving at once.
    const res = advance(makeStartState(), 10_000, idleProvider());
    expect(res.stepsRun).toBe(MAX_STEPS_PER_ADVANCE);
    expect(res.next.accumulatorMs).toBe(0);
  });
});

describe("fixed-timestep sim clock", () => {
  it("advances simClockMs by fixedDt per step, independent of realDt jitter", () => {
    let s = makeStartState();
    // Deliver 33ms twice (bad vsync), each giving 1–2 steps.
    s = advance(s, 33, idleProvider()).next;
    s = advance(s, 33, idleProvider()).next;
    const expectedSteps = Math.floor(66 / FIXED_STEP_MS);
    expect(s.simClockMs).toBeCloseTo(expectedSteps * FIXED_STEP_MS, 5);
  });
});

describe("fixed-timestep frame timestamp routing", () => {
  it("each step sees a timestamp equal to its simClock", () => {
    const seen: number[] = [];
    const provider: StepProvider = {
      sample: (t: number) => {
        seen.push(t);
        return { frame: emptyFrame(t), intent: emptyIntent() };
      },
    };
    // Use a slightly-over realDt so floating-point residue at the 3rd step
    // doesn't leave the accumulator just under fixedDt.
    advance(makeStartState(), 3 * FIXED_STEP_MS + 0.1, provider);
    expect(seen).toHaveLength(3);
    expect(seen[0]).toBeCloseTo(FIXED_STEP_MS, 5);
    expect(seen[1]).toBeCloseTo(2 * FIXED_STEP_MS, 5);
    expect(seen[2]).toBeCloseTo(3 * FIXED_STEP_MS, 5);
  });
});
