// Tests for stamina consumption / recovery — docs/design/state_machines_v1.md §5.

import { describe, expect, it } from "vitest";
import {
  DEFAULT_STAMINA_CONFIG,
  applyConfirmCost,
  canStartReach,
  gripStrengthCeiling,
  updateStamina,
  type StaminaInputs,
} from "../../src/state/stamina.js";
import { initialActorState, type ActorState } from "../../src/state/game_state.js";
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

function reaching(side: "L" | "R"): HandFSM {
  return Object.freeze({
    side,
    state: "REACHING" as const,
    target: "SLEEVE_R" as const,
    stateEnteredMs: 0,
    reachDurationMs: 275,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

function actor(over: Partial<ActorState> = {}): ActorState {
  return Object.freeze({ ...initialActorState(0), ...over });
}

function inputs(over: Partial<StaminaInputs> = {}): StaminaInputs {
  return {
    dtMs: 1000,
    actor: actor(),
    attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    triggerL: 0,
    triggerR: 0,
    breathPressed: false,
    ...over,
  };
}

describe("stamina drain", () => {
  it("active hand (REACHING) drains at 0.02/s", () => {
    const s = updateStamina(1, inputs({
      actor: actor({ leftHand: reaching("L") }),
    }));
    expect(s).toBeCloseTo(1 - DEFAULT_STAMINA_CONFIG.handActiveDrainPerSec, 4);
  });

  it("GRIPPED with strength ≥ 0.5 drains", () => {
    const s = updateStamina(1, inputs({
      actor: actor({ leftHand: gripped("L") }),
      triggerL: 0.8,
    }));
    expect(s).toBeLessThan(1);
  });

  it("GRIPPED with low strength does not drain from the hand-active clause", () => {
    const s = updateStamina(1, inputs({
      actor: actor({ leftHand: gripped("L") }),
      triggerL: 0.3,
    }));
    expect(s).toBeCloseTo(1, 5);
  });

  it("posture_break adds a proportional drain on top of hand-active drain", () => {
    // Hand active + posture break to isolate the posture term.
    // Expected per-second drain: 0.02 (hand) + 0.05 * 0.5 (posture) = 0.045.
    const s = updateStamina(1, inputs({
      actor: actor({ leftHand: reaching("L"), postureBreak: { x: 0.5, y: 0 } }),
    }));
    expect(s).toBeCloseTo(1 - (DEFAULT_STAMINA_CONFIG.handActiveDrainPerSec + 0.025), 4);
  });
});

describe("stamina recovery", () => {
  it("BTN_BREATH + no grips + static hip → +0.1/s", () => {
    const s = updateStamina(0.5, inputs({
      breathPressed: true,
      attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    }));
    expect(s).toBeCloseTo(0.5 + DEFAULT_STAMINA_CONFIG.breathRecoverPerSec, 4);
  });

  it("BTN_BREATH fails to recover if hip is moving", () => {
    const s = updateStamina(0.5, inputs({
      breathPressed: true,
      attackerHip: { hip_angle_target: 0, hip_push: 0.6, hip_lateral: 0 },
    }));
    // Hip is not static → idle recovery also won't fire (hip motion
    // implies motion). So stamina stays put.
    expect(s).toBeCloseTo(0.5, 4);
  });

  it("all limbs IDLE + no grips → +0.03/s", () => {
    const s = updateStamina(0.5, inputs());
    expect(s).toBeCloseTo(0.5 + DEFAULT_STAMINA_CONFIG.idleRecoverPerSec, 4);
  });
});

describe("stamina clamps and thresholds", () => {
  it("never exceeds 1", () => {
    const s = updateStamina(0.99, inputs({ breathPressed: true }));
    expect(s).toBeLessThanOrEqual(1);
  });

  it("never goes below 0", () => {
    const s = updateStamina(0.01, inputs({
      actor: actor({
        leftHand: gripped("L"),
        rightHand: gripped("R"),
        postureBreak: { x: 1, y: 0 },
      }),
      triggerL: 1,
      triggerR: 1,
    }));
    expect(s).toBeGreaterThanOrEqual(0);
  });

  it("gripStrengthCeiling caps at 0.6 when stamina < 0.2", () => {
    expect(gripStrengthCeiling(0.1)).toBe(0.6);
    expect(gripStrengthCeiling(0.5)).toBe(1);
  });

  it("canStartReach rejects when stamina < 0.05", () => {
    expect(canStartReach(0.04)).toBe(false);
    expect(canStartReach(0.05)).toBe(true);
  });

  it("applyConfirmCost deducts a flat 0.1", () => {
    expect(applyConfirmCost(0.5)).toBeCloseTo(0.4, 5);
  });
});
