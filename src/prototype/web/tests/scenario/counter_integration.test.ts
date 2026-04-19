// Integration: attacker window firing → defender counter window opens →
// defender confirms → attacker window force-closes.
// Covers docs/design/input_system_defense_v1.md §D.1 – §D.2.

import { describe, expect, it } from "vitest";
import {
  initialActorState,
  initialGameState,
  stepSimulation,
  type GameState,
} from "../../src/state/game_state.js";
import type { InputFrame } from "../../src/input/types.js";
import type { Intent } from "../../src/input/intent.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";

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
function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
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
      l_hand_target: null, l_grip_strength: 0, r_hand_target: null, r_grip_strength: 0,
      ...(over.grip ?? {}),
    },
    discrete: over.discrete ?? [],
  });
}

describe("counter window opens when attacker fires SCISSOR_SWEEP", () => {
  it("attacker OPENING same-frame causes counter OPENING with SCISSOR_COUNTER candidate", () => {
    // Seed conditions for SCISSOR_SWEEP (§8.2).
    const seed: GameState = Object.freeze({
      ...initialGameState(0),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L"),
      }),
      top: Object.freeze({
        ...initialActorState(0),
        postureBreak: { x: 0.5, y: 0 },
      }),
    });
    const f = frame({ l_trigger: 0.8, ls: { x: 0.8, y: 0 } }); // +x sweep
    const i = intent({
      hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0.8 },
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 },
    });
    const res = stepSimulation(seed, f, i, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
    });
    expect(res.events.some((e) => e.kind === "WINDOW_OPENING")).toBe(true);
    expect(res.events.some((e) => e.kind === "COUNTER_WINDOW_OPENING")).toBe(true);
    expect(res.nextState.counterWindow.candidates).toContain("SCISSOR_COUNTER");
    // Lateral sign of attacker intent was +1.
    expect(res.nextState.attackerSweepLateralSign).toBe(1);
  });
});

describe("counter confirm force-closes the attacker window", () => {
  it("SCISSOR_COUNTER confirmed → attacker window flips to CLOSING", () => {
    // Build up the same seed; run enough frames to bring both windows to OPEN.
    let g: GameState = Object.freeze({
      ...initialGameState(0),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: gripped("L"),
      }),
      top: Object.freeze({
        ...initialActorState(0),
        postureBreak: { x: 0.5, y: 0 },
      }),
    });

    const baseFrame = (t: number) => frame({
      timestamp: t, l_trigger: 0.8, ls: { x: 0.8, y: 0 },
    });
    const baseIntent = intent({
      hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0.8 },
      grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 },
    });

    // Tick 1: windows OPENING.
    g = stepSimulation(g, baseFrame(0), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
    }).nextState;
    expect(g.judgmentWindow.state).toBe("OPENING");
    expect(g.counterWindow.state).toBe("OPENING");

    // Tick 2: both transition to OPEN (past 200ms).
    g = stepSimulation(g, baseFrame(250), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
    }).nextState;
    expect(g.judgmentWindow.state).toBe("OPEN");
    expect(g.counterWindow.state).toBe("OPEN");

    // Tick 3: defender commits SCISSOR_COUNTER — attacker window must close.
    const res = stepSimulation(g, baseFrame(300), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
      confirmedCounter: "SCISSOR_COUNTER",
    });
    expect(res.nextState.counterWindow.state).toBe("CLOSING");
    expect(res.nextState.judgmentWindow.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "COUNTER_CONFIRMED")).toBe(true);
  });

  it("TRIANGLE_EARLY_STACK confirmed clears top.arm_extracted on both sides", () => {
    // Seed a state where TRIANGLE would fire and arm_extracted was true.
    let g: GameState = Object.freeze({
      ...initialGameState(0),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: Object.freeze({
          ...gripped("L"), target: "COLLAR_R" as const,
        }),
      }),
      top: Object.freeze({
        ...initialActorState(0),
        armExtractedLeft: true,
      }),
      topArmExtracted: Object.freeze({
        left: true, right: false,
        leftSustainMs: 0, rightSustainMs: 0,
        leftSetAtMs: 0, rightSetAtMs: Number.NEGATIVE_INFINITY,
      }),
    });

    // Drive the window to OPEN. Because arm_extracted derives off hand+pull,
    // we also need a SLEEVE grip to keep it true across ticks.
    const baseFrame = (t: number) => frame({
      timestamp: t, l_trigger: 0.8, r_trigger: 0.9,
    });
    const baseIntent = intent({
      hip: { hip_angle_target: 0, hip_push: -0.6, hip_lateral: 0 },
      grip: {
        l_hand_target: "COLLAR_R", l_grip_strength: 0.8,
        r_hand_target: "SLEEVE_L", r_grip_strength: 0.9,
      },
    });
    // Seed a right hand gripped on SLEEVE_L too.
    g = Object.freeze({
      ...g,
      bottom: Object.freeze({
        ...g.bottom,
        rightHand: Object.freeze({
          ...gripped("R"), target: "SLEEVE_L" as const,
        }),
      }),
    });

    g = stepSimulation(g, baseFrame(0), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
    }).nextState;
    g = stepSimulation(g, baseFrame(250), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
    }).nextState;
    expect(g.counterWindow.state).toBe("OPEN");
    // arm_extracted remains true because we're still pulling.
    expect(g.top.armExtractedLeft).toBe(true);

    const res = stepSimulation(g, baseFrame(300), baseIntent, {
      realDtMs: 16.67, gameDtMs: 16.67, confirmedTechnique: null,
      confirmedCounter: "TRIANGLE_EARLY_STACK",
    });
    expect(res.nextState.top.armExtractedLeft).toBe(false);
    expect(res.nextState.top.armExtractedRight).toBe(false);
  });
});
