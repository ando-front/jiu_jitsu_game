// Integration: defender cut attempt running through stepSimulation and
// forcing the attacker's HandFSM to RETRACT on success.
// docs/design/state_machines_v1.md §4.2 + §2.1.4.

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
import { CUT_TIMING } from "../../src/state/cut_attempt.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";

function gripped(side: "L" | "R"): HandFSM {
  return Object.freeze({
    side,
    state: "GRIPPED" as const,
    target: "SLEEVE_R" as const,
    stateEnteredMs: 0,
    reachDurationMs: 0,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
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

function seedWithGrip(): GameState {
  return Object.freeze({
    ...initialGameState(0),
    bottom: Object.freeze({
      ...initialActorState(0),
      leftHand: gripped("L"),
    }),
  });
}

describe("cut attempt flow through stepSimulation", () => {
  it("commit fires CUT_STARTED event and cutAttempts slot transitions", () => {
    const seed = seedWithGrip();
    const defense: DefenseIntent = Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: { l_hand_target: null, l_base_pressure: 0, r_hand_target: null, r_base_pressure: 0 },
      discrete: [{ kind: "CUT_ATTEMPT", side: "L", rs: { x: -1, y: 0 } }],
    });
    const attackerIntent: Intent = Object.freeze({
      ...NEUTRAL_INTENT,
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.3, r_hand_target: null, r_grip_strength: 0 },
    });
    const res = stepSimulation(
      seed,
      frame({ l_trigger: 0.3 }),
      attackerIntent,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense },
    );
    expect(res.events.some((e) => e.kind === "CUT_STARTED")).toBe(true);
    expect(res.nextState.cutAttempts.left.kind).toBe("IN_PROGRESS");
  });

  it("weak attacker grip → cut SUCCEEDS and attacker hand enters RETRACT", () => {
    let g = seedWithGrip();
    const attackerIntent: Intent = Object.freeze({
      ...NEUTRAL_INTENT,
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.3, r_hand_target: null, r_grip_strength: 0 },
    });

    // Tick 1: defender commits.
    const defense1: DefenseIntent = Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: { l_hand_target: null, l_base_pressure: 0, r_hand_target: null, r_base_pressure: 0 },
      discrete: [{ kind: "CUT_ATTEMPT", side: "L", rs: { x: -1, y: 0 } }],
    });
    g = stepSimulation(
      g, frame({ timestamp: 0, l_trigger: 0.3 }),
      attackerIntent,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense1 },
    ).nextState;

    // Tick 2: 1500ms later, attacker grip is still weak.
    const defense2: DefenseIntent = Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: { l_hand_target: null, l_base_pressure: 0, r_hand_target: null, r_base_pressure: 0 },
      discrete: [],
    });
    const res = stepSimulation(
      g, frame({ timestamp: CUT_TIMING.attemptMs, l_trigger: 0.3 }),
      attackerIntent,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense2 },
    );
    expect(res.events.some((e) => e.kind === "CUT_SUCCEEDED")).toBe(true);
    // Attacker L hand must have routed through GRIP_BROKEN(OPPONENT_CUT) → RETRACT.
    expect(res.nextState.bottom.leftHand.state).toBe("RETRACT");
    const broken = res.events.find((e) => e.kind === "GRIP_BROKEN");
    expect(broken && "reason" in broken ? broken.reason : null).toBe("OPPONENT_CUT");
  });

  it("strong attacker grip → cut FAILS and attacker hand stays GRIPPED", () => {
    let g = seedWithGrip();
    const attackerIntent: Intent = Object.freeze({
      ...NEUTRAL_INTENT,
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.9, r_hand_target: null, r_grip_strength: 0 },
    });
    const defense1: DefenseIntent = Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: { l_hand_target: null, l_base_pressure: 0, r_hand_target: null, r_base_pressure: 0 },
      discrete: [{ kind: "CUT_ATTEMPT", side: "L", rs: { x: -1, y: 0 } }],
    });
    g = stepSimulation(
      g, frame({ timestamp: 0, l_trigger: 0.9 }),
      attackerIntent,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense1 },
    ).nextState;

    const defense2: DefenseIntent = Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: { l_hand_target: null, l_base_pressure: 0, r_hand_target: null, r_base_pressure: 0 },
      discrete: [],
    });
    const res = stepSimulation(
      g, frame({ timestamp: CUT_TIMING.attemptMs, l_trigger: 0.9 }),
      attackerIntent,
      { realDtMs: 16, gameDtMs: 16, confirmedTechnique: null, defenseIntent: defense2 },
    );
    expect(res.events.some((e) => e.kind === "CUT_FAILED")).toBe(true);
    expect(res.nextState.bottom.leftHand.state).toBe("GRIPPED");
  });
});
