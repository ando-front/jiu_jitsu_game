// Tests for arm_extracted flag — docs/design/state_machines_v1.md §4.1.

import { describe, expect, it } from "vitest";
import {
  DEFAULT_ARM_EXTRACTED_CONFIG,
  INITIAL_ARM_EXTRACTED,
  updateArmExtracted,
  type ArmExtractedInputs,
  type ArmExtractedState,
} from "../src/state/arm_extracted.js";
import type { HandFSM } from "../src/state/hand_fsm.js";
import type { GripZone } from "../src/input/intent.js";

function grippedAt(zone: GripZone, side: "L" | "R"): HandFSM {
  return Object.freeze({
    side,
    state: "GRIPPED" as const,
    target: zone,
    stateEnteredMs: 0,
    reachDurationMs: 0,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

function idleHand(side: "L" | "R"): HandFSM {
  return Object.freeze({
    side,
    state: "IDLE" as const,
    target: null,
    stateEnteredMs: 0,
    reachDurationMs: 0,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

function inputs(over: Partial<ArmExtractedInputs>): ArmExtractedInputs {
  return {
    nowMs: 0,
    dtMs: 16.67,
    bottomLeftHand: idleHand("L"),
    bottomRightHand: idleHand("R"),
    triggerL: 0,
    triggerR: 0,
    attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    defenderBaseHold: false,
    ...over,
  };
}

describe("arm_extracted transitions (§4.1)", () => {
  it("grip on SLEEVE_R with strong pull for 1.5s → right arm extracted", () => {
    let s: ArmExtractedState = INITIAL_ARM_EXTRACTED;
    const frames = Math.ceil(DEFAULT_ARM_EXTRACTED_CONFIG.requiredSustainMs / 16.67);
    for (let i = 0; i < frames; i += 1) {
      s = updateArmExtracted(s, inputs({
        nowMs: i * 16.67,
        bottomLeftHand: grippedAt("SLEEVE_R", "L"),
        triggerL: 0.8,
        attackerHip: { hip_angle_target: 0, hip_push: -0.5, hip_lateral: 0 },
      }));
    }
    expect(s.right).toBe(true);
    expect(s.left).toBe(false);
  });

  it("weak trigger fails to extract even with sustained time", () => {
    let s: ArmExtractedState = INITIAL_ARM_EXTRACTED;
    for (let i = 0; i < 120; i += 1) {
      s = updateArmExtracted(s, inputs({
        nowMs: i * 16.67,
        bottomLeftHand: grippedAt("SLEEVE_R", "L"),
        triggerL: 0.3,
        attackerHip: { hip_angle_target: 0, hip_push: -0.5, hip_lateral: 0 },
      }));
    }
    expect(s.right).toBe(false);
  });

  it("no hip pull (hip_push ≥ 0) fails to sustain", () => {
    let s: ArmExtractedState = INITIAL_ARM_EXTRACTED;
    for (let i = 0; i < 120; i += 1) {
      s = updateArmExtracted(s, inputs({
        nowMs: i * 16.67,
        bottomLeftHand: grippedAt("SLEEVE_R", "L"),
        triggerL: 0.8,
        attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
      }));
    }
    expect(s.right).toBe(false);
  });

  it("releasing the grip clears a previously-set flag", () => {
    // Bring right arm to extracted first.
    let s: ArmExtractedState = INITIAL_ARM_EXTRACTED;
    const buildFrames = Math.ceil(DEFAULT_ARM_EXTRACTED_CONFIG.requiredSustainMs / 16.67);
    for (let i = 0; i < buildFrames; i += 1) {
      s = updateArmExtracted(s, inputs({
        nowMs: i * 16.67,
        bottomLeftHand: grippedAt("SLEEVE_R", "L"),
        triggerL: 0.9,
        attackerHip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 },
      }));
    }
    expect(s.right).toBe(true);

    // Now release (hand IDLE).
    s = updateArmExtracted(s, inputs({
      nowMs: buildFrames * 16.67 + 16.67,
      bottomLeftHand: idleHand("L"),
    }));
    expect(s.right).toBe(false);
  });

  it("5s auto-reset clears the flag (briefly) even if the pull continues", () => {
    // Observe the exact tick after 5s: the flag must flip from true → false.
    // The attacker can of course re-set it later, but the auto-reset itself
    // is the contract we're testing.
    let s: ArmExtractedState = INITIAL_ARM_EXTRACTED;
    const step = 50;

    // Build up to true.
    const buildUpFrames = Math.ceil(DEFAULT_ARM_EXTRACTED_CONFIG.requiredSustainMs / step);
    for (let i = 0; i < buildUpFrames; i += 1) {
      s = updateArmExtracted(s, inputs({
        nowMs: i * step,
        dtMs: step,
        bottomLeftHand: grippedAt("SLEEVE_R", "L"),
        triggerL: 0.9,
        attackerHip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 },
      }));
    }
    expect(s.right).toBe(true);
    const setAt = s.rightSetAtMs;

    // Advance to just past the auto-reset boundary.
    const resetTick = setAt + DEFAULT_ARM_EXTRACTED_CONFIG.autoResetAfterMs;
    s = updateArmExtracted(s, inputs({
      nowMs: resetTick,
      dtMs: step,
      bottomLeftHand: grippedAt("SLEEVE_R", "L"),
      triggerL: 0.9,
      attackerHip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 },
    }));
    expect(s.right).toBe(false);
  });
});
