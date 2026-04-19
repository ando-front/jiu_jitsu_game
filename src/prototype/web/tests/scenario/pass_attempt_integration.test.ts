// Integration tests: PASS_COMMIT flowing through stepSimulation.
// docs/design/input_system_defense_v1.md §B.7.

import { describe, expect, it } from "vitest";
import {
  initialActorState,
  initialGameState,
  stepSimulation,
  type GameState,
} from "../../src/state/game_state.js";
import type { InputFrame } from "../../src/input/types.js";
import type { Intent } from "../../src/input/intent.js";
import type { DefenseIntent } from "../../src/input/intent_defense.js";
import { PASS_TIMING } from "../../src/state/pass_attempt.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";

function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
}

const NEUTRAL_INTENT: Intent = Object.freeze({
  hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
  grip: { l_hand_target: null, l_grip_strength: 0, r_hand_target: null, r_grip_strength: 0 },
  discrete: [],
});

function frame(over: Partial<InputFrame> = {}): InputFrame {
  return Object.freeze({
    timestamp: 0,
    ls: { x: 0, y: 0 },
    rs: { x: 0, y: 0 },
    l_trigger: 0,
    r_trigger: 0,
    buttons: 0,
    button_edges: 0,
    device_kind: "Keyboard" as const,
    ...over,
  });
}

function baseDefense(over: Partial<DefenseIntent> = {}): DefenseIntent {
  return Object.freeze({
    hip: { weight_forward: 0, weight_lateral: 0, ...(over.hip ?? {}) },
    base: {
      l_hand_target: "BICEP_L",
      l_base_pressure: 0.7,
      r_hand_target: "KNEE_R",
      r_base_pressure: 0.7,
      ...(over.base ?? {}),
    },
    discrete: over.discrete ?? [],
  });
}

function makeEligibleSeed(): GameState {
  return Object.freeze({
    ...initialGameState(0),
    bottom: Object.freeze({
      ...initialActorState(0),
      leftFoot: foot("L", "UNLOCKED"),
      rightFoot: foot("R", "LOCKED"),
    }),
  });
}

describe("PASS_COMMIT starts a pass when eligible", () => {
  it("eligible commit → PASS_STARTED event, passAttempt.kind becomes IN_PROGRESS", () => {
    const seed = makeEligibleSeed();
    const defense = baseDefense({
      discrete: [{ kind: "PASS_COMMIT", rs: { x: 0, y: -1 } }],
    });
    const res = stepSimulation(
      seed,
      frame({ rs: { x: 0, y: -1 } }),
      NEUTRAL_INTENT,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense },
    );
    expect(res.events.some((e) => e.kind === "PASS_STARTED")).toBe(true);
    expect(res.nextState.passAttempt.kind).toBe("IN_PROGRESS");
  });
});

describe("PASS_COMMIT rejected silently when ineligible", () => {
  it("both feet LOCKED → commit ignored, passAttempt stays IDLE", () => {
    const seed: GameState = Object.freeze({
      ...initialGameState(0),
      bottom: Object.freeze({
        ...initialActorState(0),
        // both feet LOCKED
      }),
    });
    const defense = baseDefense({
      discrete: [{ kind: "PASS_COMMIT", rs: { x: 0, y: -1 } }],
    });
    const res = stepSimulation(
      seed,
      frame({ rs: { x: 0, y: -1 } }),
      NEUTRAL_INTENT,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense },
    );
    expect(res.events.some((e) => e.kind === "PASS_STARTED")).toBe(false);
    expect(res.nextState.passAttempt.kind).toBe("IDLE");
  });
});

describe("PASS_SUCCEEDED after 5s, session ends", () => {
  it("no attacker triangle during the window → PASS_SUCCEEDED + SESSION_ENDED(PASS_SUCCESS)", () => {
    let g = makeEligibleSeed();

    // Tick 1: commit.
    const commit = baseDefense({
      discrete: [{ kind: "PASS_COMMIT", rs: { x: 0, y: -1 } }],
    });
    g = stepSimulation(
      g, frame({ timestamp: 0, rs: { x: 0, y: -1 } }),
      NEUTRAL_INTENT,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: commit },
    ).nextState;
    expect(g.passAttempt.kind).toBe("IN_PROGRESS");

    // Tick 2: 5s later, no more commit requested.
    const hold = baseDefense(); // no discrete events
    const res = stepSimulation(
      g, frame({ timestamp: PASS_TIMING.windowMs + 1, rs: { x: 0, y: -1 } }),
      NEUTRAL_INTENT,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: hold },
    );
    expect(res.events.some((e) => e.kind === "PASS_SUCCEEDED")).toBe(true);
    expect(res.events.some((e) => e.kind === "SESSION_ENDED" && "reason" in e && e.reason === "PASS_SUCCESS")).toBe(true);
    expect(res.nextState.sessionEnded).toBe(true);
  });
});
