// End-to-end scenario tests per docs/design/state_machines_v1.md §11.2.
// For each of the six M1 techniques, run stepSimulation through a plausible
// input sequence and assert the judgment window opens (positive) or does
// not open (negative, one condition missing).
//
// These tests deliberately fabricate actor state at the frame boundaries
// rather than wait out 275ms reaches — that's what the per-FSM tests
// already cover. Here we exercise the *composition* of FSM + continuous
// updates + window evaluation.

import { describe, expect, it } from "vitest";
import {
  initialActorState,
  initialGameState,
  stepSimulation,
  type ActorState,
  type GameState,
} from "../../src/state/game_state.js";
import type { InputFrame } from "../../src/input/types.js";
import type { GripZone, Intent } from "../../src/input/intent.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";
import { INITIAL_JUDGMENT_WINDOW } from "../../src/state/judgment_window.js";
import { INITIAL_ARM_EXTRACTED, type ArmExtractedState } from "../../src/state/arm_extracted.js";

// --- Builders --------------------------------------------------------------

function gripped(side: "L" | "R", zone: GripZone): HandFSM {
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
function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
}

function stateWith(over: {
  bottom?: Partial<ActorState>;
  top?: Partial<ActorState>;
  sustainedHipPushMs?: number;
  topArmExtracted?: Partial<ArmExtractedState>;
}): GameState {
  const base = initialGameState(0);
  const top = Object.freeze({ ...initialActorState(0), ...(over.top ?? {}) });
  // Keep the arm_extracted side flags in sync with top.armExtracted* so
  // stepSimulation's re-derivation doesn't wipe them. We feed the same
  // flags into topArmExtracted with a recent setAtMs so the 5s reset
  // doesn't fire in-test.
  const armExt: ArmExtractedState = Object.freeze({
    ...INITIAL_ARM_EXTRACTED,
    left: top.armExtractedLeft,
    right: top.armExtractedRight,
    leftSetAtMs: top.armExtractedLeft ? 0 : Number.NEGATIVE_INFINITY,
    rightSetAtMs: top.armExtractedRight ? 0 : Number.NEGATIVE_INFINITY,
    ...(over.topArmExtracted ?? {}),
  });
  return Object.freeze({
    ...base,
    bottom: Object.freeze({ ...initialActorState(0), ...(over.bottom ?? {}) }),
    top,
    topArmExtracted: armExt,
    sustained: { hipPushMs: over.sustainedHipPushMs ?? 0 },
    judgmentWindow: INITIAL_JUDGMENT_WINDOW,
  });
}

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

function intent(over: {
  hip?: Partial<Intent["hip"]>;
  grip?: Partial<Intent["grip"]>;
  discrete?: Intent["discrete"];
} = {}): Intent {
  return Object.freeze({
    hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0, ...(over.hip ?? {}) },
    grip: {
      l_hand_target: null,
      l_grip_strength: 0,
      r_hand_target: null,
      r_grip_strength: 0,
      ...(over.grip ?? {}),
    },
    discrete: over.discrete ?? [],
  });
}

function step(prev: GameState, f: InputFrame, i: Intent) {
  return stepSimulation(prev, f, i, { realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null });
}

// Helper: did the judgment window OPENING event fire this tick with the
// given technique in its candidates?
function windowOpensFor(
  events: readonly { kind: string; candidates?: readonly string[] }[],
  technique: string,
): boolean {
  return events.some((e) =>
    e.kind === "WINDOW_OPENING" && (e.candidates ?? []).includes(technique),
  );
}

// --- SCISSOR_SWEEP ---------------------------------------------------------

describe("SCISSOR_SWEEP scenario (§8.2)", () => {
  it("positive: both feet LOCKED + SLEEVE gripped 0.8 + break 0.5 → window opens", () => {
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L", "SLEEVE_R"),
      },
      top: { postureBreak: { x: 0.5, y: 0 } },
    });
    const f = frame({ l_trigger: 0.8 });
    const i = intent({ grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "SCISSOR_SWEEP")).toBe(true);
  });

  it("negative: posture break too small → no window", () => {
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L", "SLEEVE_R"),
      },
      top: { postureBreak: { x: 0.2, y: 0 } }, // magnitude 0.2 < 0.4
    });
    const f = frame({ l_trigger: 0.8 });
    const i = intent({ grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "SCISSOR_SWEEP")).toBe(false);
  });
});

// --- FLOWER_SWEEP ----------------------------------------------------------

describe("FLOWER_SWEEP scenario (§8.2)", () => {
  it("positive: both feet LOCKED + WRIST gripped + sagittal 0.6", () => {
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        rightHand: gripped("R", "WRIST_L"),
      },
      top: { postureBreak: { x: 0, y: 0.6 } },
    });
    const f = frame({ r_trigger: 0.7 });
    const i = intent({ grip: { r_hand_target: "WRIST_L", r_grip_strength: 0.7 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "FLOWER_SWEEP")).toBe(true);
  });

  it("negative: sagittal 0.3 is below the 0.5 threshold", () => {
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        rightHand: gripped("R", "WRIST_L"),
      },
      top: { postureBreak: { x: 0, y: 0.3 } },
    });
    const f = frame({ r_trigger: 0.7 });
    const i = intent({ grip: { r_hand_target: "WRIST_L", r_grip_strength: 0.7 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "FLOWER_SWEEP")).toBe(false);
  });
});

// --- TRIANGLE --------------------------------------------------------------

describe("TRIANGLE scenario (§8.2)", () => {
  it("positive: one foot UNLOCKED + arm_extracted + collar gripped", () => {
    // TRIANGLE requires arm_extracted to BE true at the end of this tick.
    // Our arm_extracted module re-derives on every step, clearing when
    // the sustain conditions are absent. So the positive scenario feeds
    // BOTH hands: left on COLLAR_R (the trigger condition for TRIANGLE)
    // AND right on SLEEVE_L + strong hip pull, which keeps the left arm
    // extracted flag alive via the right hand's pull. In a real session
    // the arm was presumably extracted in an earlier tick using a sleeve
    // grip, and the COLLAR pull happens right after.
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L", "COLLAR_R"),
        rightHand: gripped("R", "SLEEVE_L"), // feeds arm_extracted
      },
      top: { armExtractedLeft: true },
    });
    const f = frame({ l_trigger: 0.7, r_trigger: 0.9 });
    const i = intent({
      hip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 }, // sustained pull
      grip: {
        l_hand_target: "COLLAR_R",
        l_grip_strength: 0.7,
        r_hand_target: "SLEEVE_L",
        r_grip_strength: 0.9,
      },
    });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "TRIANGLE")).toBe(true);
  });

  it("negative: no arm_extracted → triangle locked out", () => {
    const g = stateWith({
      bottom: {
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L", "COLLAR_R"),
      },
      top: { armExtractedLeft: false },
    });
    const f = frame({ l_trigger: 0.7 });
    const i = intent({ grip: { l_hand_target: "COLLAR_R", l_grip_strength: 0.7 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "TRIANGLE")).toBe(false);
  });
});

// --- OMOPLATA --------------------------------------------------------------

describe("OMOPLATA scenario (§8.2)", () => {
  it("positive: sleeve side-sign matches lateral break + sagittal 0.7 + yaw ≥ π/3", () => {
    const g = stateWith({
      bottom: {
        leftHand: gripped("L", "SLEEVE_R"),
      },
      // L sleeve → sign(lateral) must be -1.
      top: { postureBreak: { x: -0.3, y: 0.7 } },
    });
    const f = frame({ l_trigger: 0.8 });
    const i = intent({
      hip: { hip_angle_target: Math.PI / 3 + 0.1, hip_push: 0, hip_lateral: 0 },
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 },
    });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "OMOPLATA")).toBe(true);
  });

  it("negative: hip yaw below π/3 → no window", () => {
    const g = stateWith({
      bottom: { leftHand: gripped("L", "SLEEVE_R") },
      top: { postureBreak: { x: -0.3, y: 0.7 } },
    });
    const f = frame({ l_trigger: 0.8 });
    const i = intent({
      hip: { hip_angle_target: Math.PI / 6, hip_push: 0, hip_lateral: 0 }, // too small
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 },
    });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "OMOPLATA")).toBe(false);
  });
});

// --- HIP_BUMP --------------------------------------------------------------

describe("HIP_BUMP scenario (§8.2)", () => {
  it("positive: sagittal 0.8 + sustained push already ≥ 300ms", () => {
    const g = stateWith({
      top: { postureBreak: { x: 0, y: 0.8 } },
      sustainedHipPushMs: 290, // +16.67 this tick will push it over 300
    });
    const f = frame();
    const i = intent({ hip: { hip_angle_target: 0, hip_push: 0.6, hip_lateral: 0 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "HIP_BUMP")).toBe(true);
  });

  it("negative: push not sustained long enough", () => {
    const g = stateWith({
      top: { postureBreak: { x: 0, y: 0.8 } },
      sustainedHipPushMs: 50,
    });
    const f = frame();
    const i = intent({ hip: { hip_angle_target: 0, hip_push: 0.6, hip_lateral: 0 } });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "HIP_BUMP")).toBe(false);
  });
});

// --- CROSS_COLLAR ----------------------------------------------------------

describe("CROSS_COLLAR scenario (§8.2)", () => {
  it("positive: both COLLAR gripped ≥ 0.7 + break ≥ 0.5", () => {
    const g = stateWith({
      bottom: {
        leftHand: gripped("L", "COLLAR_L"),
        rightHand: gripped("R", "COLLAR_R"),
      },
      top: { postureBreak: { x: 0.3, y: 0.5 } },
    });
    const f = frame({ l_trigger: 0.8, r_trigger: 0.8 });
    const i = intent({
      grip: {
        l_hand_target: "COLLAR_L",
        l_grip_strength: 0.8,
        r_hand_target: "COLLAR_R",
        r_grip_strength: 0.8,
      },
    });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "CROSS_COLLAR")).toBe(true);
  });

  it("negative: one hand is not on COLLAR → no window", () => {
    const g = stateWith({
      bottom: {
        leftHand: gripped("L", "COLLAR_L"),
        rightHand: gripped("R", "SLEEVE_L"), // wrong zone
      },
      top: { postureBreak: { x: 0.3, y: 0.5 } },
    });
    const f = frame({ l_trigger: 0.8, r_trigger: 0.8 });
    const i = intent({
      grip: {
        l_hand_target: "COLLAR_L",
        l_grip_strength: 0.8,
        r_hand_target: "SLEEVE_L",
        r_grip_strength: 0.8,
      },
    });
    const res = step(g, f, i);
    expect(windowOpensFor(res.events, "CROSS_COLLAR")).toBe(false);
  });
});

// --- Stamina-clamp cross-check ---------------------------------------------

describe("stamina clamp blocks high-strength conditions (§5.3)", () => {
  it("bottom stamina < 0.2 prevents CROSS_COLLAR (strength capped at 0.6)", () => {
    const g = stateWith({
      bottom: {
        leftHand: gripped("L", "COLLAR_L"),
        rightHand: gripped("R", "COLLAR_R"),
        stamina: 0.1, // below lowGripCapThreshold
      },
      top: { postureBreak: { x: 0.3, y: 0.5 } },
    });
    const f = frame({ l_trigger: 1, r_trigger: 1 });
    const i = intent({
      grip: {
        l_hand_target: "COLLAR_L",
        l_grip_strength: 1,
        r_hand_target: "COLLAR_R",
        r_grip_strength: 1,
      },
    });
    const res = step(g, f, i);
    // Effective trigger is clamped to 0.6 → below the 0.7 threshold → no fire.
    expect(windowOpensFor(res.events, "CROSS_COLLAR")).toBe(false);
  });
});
