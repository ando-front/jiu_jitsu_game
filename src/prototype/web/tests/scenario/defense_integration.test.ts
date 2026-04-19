// Integration tests: DefenseIntent flowing into stepSimulation.
// References docs/design/input_system_defense_v1.md §B.5 and
// docs/design/state_machines_v1.md §3.3 / §4.1.

import { describe, expect, it } from "vitest";
import {
  initialActorState,
  initialGameState,
  stepSimulation,
  type GameState,
} from "../../src/state/game_state.js";
import type { InputFrame } from "../../src/input/types.js";
import type { Intent } from "../../src/input/intent.js";
import {
  ZERO_DEFENSE_INTENT,
  type DefenseIntent,
} from "../../src/input/intent_defense.js";
import { INITIAL_ARM_EXTRACTED } from "../../src/state/arm_extracted.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";

function gripped(side: "L" | "R", zone: "SLEEVE_R" | "SLEEVE_L" | "WRIST_R"): HandFSM {
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

describe("DefenseIntent — posture_break recovery (§3.3 bullet 4)", () => {
  it("weight_forward shoves break toward origin (negative y component)", () => {
    // Seed a pre-existing forward break on the TOP actor; the defender
    // pushes weight forward → sagittal should fall faster than decay alone.
    const seed: GameState = Object.freeze({
      ...initialGameState(0),
      top: Object.freeze({ ...initialActorState(0), postureBreak: { x: 0, y: 0.8 } }),
    });

    // Run 500ms with defender pushing forward.
    const defense: DefenseIntent = Object.freeze({
      hip: { weight_forward: 1, weight_lateral: 0 },
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [],
    });

    let g = seed;
    for (let t = 0; t < 500; t += 16.67) {
      g = stepSimulation(g, frame({ timestamp: t }), NEUTRAL_INTENT, {
        realDtMs: 16.67,
        gameDtMs: 16.67,
        confirmedTechnique: null,
        defenseIntent: defense,
      }).nextState;
    }

    // Compare against pure-decay baseline: same seed, ZERO defense.
    let baseline = seed;
    for (let t = 0; t < 500; t += 16.67) {
      baseline = stepSimulation(baseline, frame({ timestamp: t }), NEUTRAL_INTENT, {
        realDtMs: 16.67,
        gameDtMs: 16.67,
        confirmedTechnique: null,
        defenseIntent: ZERO_DEFENSE_INTENT,
      }).nextState;
    }

    expect(g.top.postureBreak.y).toBeLessThan(baseline.top.postureBreak.y);
  });
});

describe("DefenseIntent — arm_extracted clear via base hold (§4.1)", () => {
  it("RECOVERY_HOLD clears a previously-extracted arm flag", () => {
    // Seed a GameState with arm_extracted already true. Feed pulling
    // conditions so arm_extracted would normally stay; but also feed
    // RECOVERY_HOLD — §4.1 defender-base-hold clears the flag.
    const seed: GameState = Object.freeze({
      ...initialGameState(0),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
      }),
      top: Object.freeze({
        ...initialActorState(0),
        armExtractedLeft: true,
      }),
      topArmExtracted: Object.freeze({
        ...INITIAL_ARM_EXTRACTED,
        right: true,                 // opponent's right arm is extracted
        rightSetAtMs: 0,
      }),
    });

    const defense: DefenseIntent = Object.freeze({
      hip: ZERO_DEFENSE_INTENT.hip,
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [{ kind: "RECOVERY_HOLD" }],
    });

    const res = stepSimulation(seed, frame({
      timestamp: 16,
      l_trigger: 0.9, // keep pulling
    }), {
      ...NEUTRAL_INTENT,
      hip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 },
      grip: {
        l_hand_target: "SLEEVE_R",
        l_grip_strength: 0.9,
        r_hand_target: null,
        r_grip_strength: 0,
      },
    }, {
      realDtMs: 16,
      gameDtMs: 16,
      confirmedTechnique: null,
      defenseIntent: defense,
    });

    expect(res.nextState.topArmExtracted.right).toBe(false);
    expect(res.nextState.top.armExtractedRight).toBe(false);
  });
});
