// Scenario tests for the GameState aggregate tick.
// References docs/design/architecture_overview_v1.md §7 and state_machines_v1.md §6 / §10.

import { describe, expect, it } from "vitest";
import { initialGameState, stepSimulation, type GameState } from "../src/state/game_state.js";
import { HAND_TIMING } from "../src/state/hand_fsm.js";
import { FOOT_TIMING, LOCKING_POSTURE_THRESHOLD } from "../src/state/foot_fsm.js";
import { ButtonBit, type InputFrame } from "../src/input/types.js";
import type { Intent } from "../src/input/intent.js";

const REACH_MID = (HAND_TIMING.reachMinMs + HAND_TIMING.reachMaxMs) / 2;

function frame(overrides: Partial<InputFrame> = {}): InputFrame {
  return Object.freeze({
    timestamp: 0,
    ls: { x: 0, y: 0 },
    rs: { x: 0, y: 0 },
    l_trigger: 0,
    r_trigger: 0,
    buttons: 0,
    button_edges: 0,
    device_kind: "Keyboard" as const,
    ...overrides,
  }) as InputFrame;
}

function intent(overrides: {
  hip?: Partial<Intent["hip"]>;
  grip?: Partial<Intent["grip"]>;
  discrete?: Intent["discrete"];
} = {}): Intent {
  const hip = {
    hip_angle_target: 0,
    hip_push: 0,
    hip_lateral: 0,
    ...(overrides.hip ?? {}),
  };
  const grip = {
    l_hand_target: null as Intent["grip"]["l_hand_target"],
    l_grip_strength: 0,
    r_hand_target: null as Intent["grip"]["r_hand_target"],
    r_grip_strength: 0,
    ...(overrides.grip ?? {}),
  };
  return Object.freeze({
    hip,
    grip,
    discrete: overrides.discrete ?? [],
  });
}

describe("GameState init", () => {
  it("starts with CLOSED guard and IDLE hands, LOCKED feet", () => {
    const g = initialGameState();
    expect(g.guard).toBe("CLOSED");
    expect(g.bottom.leftHand.state).toBe("IDLE");
    expect(g.bottom.rightHand.state).toBe("IDLE");
    expect(g.bottom.leftFoot.state).toBe("LOCKED");
    expect(g.bottom.rightFoot.state).toBe("LOCKED");
    expect(g.frameIndex).toBe(0);
  });
});

describe("stepSimulation routes bottom input to FSMs", () => {
  it("bottom L_TRIGGER + grip target kicks off REACHING on left hand", () => {
    let g: GameState = initialGameState();
    const res = stepSimulation(
      g,
      frame({ timestamp: 0, l_trigger: 1 }),
      intent({ grip: { l_hand_target: "COLLAR_L", l_grip_strength: 1 } }),
    );
    g = res.nextState;
    expect(g.bottom.leftHand.state).toBe("REACHING");
    expect(g.bottom.leftHand.target).toBe("COLLAR_L");
    expect(res.events.some((e) => e.kind === "REACH_STARTED")).toBe(true);
  });

  it("hand reaches and grips after the reach timer expires", () => {
    let g: GameState = initialGameState();
    g = stepSimulation(g, frame({ timestamp: 0, l_trigger: 1 }), intent({ grip: { l_hand_target: "COLLAR_L", l_grip_strength: 1 } })).nextState;
    g = stepSimulation(g, frame({ timestamp: REACH_MID, l_trigger: 1 }), intent({ grip: { l_hand_target: "COLLAR_L", l_grip_strength: 1 } })).nextState;
    const lastRes = stepSimulation(
      g,
      frame({ timestamp: REACH_MID + 16, l_trigger: 1 }),
      intent({ grip: { l_hand_target: "COLLAR_L", l_grip_strength: 1 } }),
    );
    g = lastRes.nextState;
    expect(g.bottom.leftHand.state).toBe("GRIPPED");
    expect(lastRes.events.some((e) => e.kind === "GRIPPED")).toBe(true);
  });

  it("L_BUMPER edge toggles left foot to UNLOCKED via FOOT_HOOK_TOGGLE intent", () => {
    let g: GameState = initialGameState();
    const res = stepSimulation(
      g,
      frame({ timestamp: 0, button_edges: ButtonBit.L_BUMPER, buttons: ButtonBit.L_BUMPER }),
      intent({ discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "L" }] }),
    );
    g = res.nextState;
    expect(g.bottom.leftFoot.state).toBe("UNLOCKED");
    expect(res.events.some((e) => e.kind === "UNLOCKED")).toBe(true);
  });
});

describe("Guard FSM (§6)", () => {
  it("both feet UNLOCKED in the same tick → GUARD_OPENED", () => {
    let g: GameState = initialGameState();
    const res = stepSimulation(
      g,
      frame({ timestamp: 0, button_edges: ButtonBit.L_BUMPER | ButtonBit.R_BUMPER }),
      intent({
        discrete: [
          { kind: "FOOT_HOOK_TOGGLE", side: "L" },
          { kind: "FOOT_HOOK_TOGGLE", side: "R" },
        ],
      }),
    );
    g = res.nextState;
    expect(g.guard).toBe("OPEN");
    expect(res.events.some((e) => e.kind === "GUARD_OPENED")).toBe(true);
  });

  it("guard opens across two ticks if each foot unlocks separately", () => {
    let g: GameState = initialGameState();

    // Tick 1: unlock left foot only.
    g = stepSimulation(
      g,
      frame({ timestamp: 0, button_edges: ButtonBit.L_BUMPER }),
      intent({ discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "L" }] }),
    ).nextState;
    expect(g.guard).toBe("CLOSED");
    expect(g.bottom.leftFoot.state).toBe("UNLOCKED");

    // Tick 2: unlock right foot → guard opens.
    const res2 = stepSimulation(
      g,
      frame({ timestamp: 16, button_edges: ButtonBit.R_BUMPER }),
      intent({ discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "R" }] }),
    );
    g = res2.nextState;
    expect(g.guard).toBe("OPEN");
    expect(res2.events.some((e) => e.kind === "GUARD_OPENED")).toBe(true);
  });

  it("GUARD_OPENED fires only once, not again on subsequent ticks", () => {
    let g: GameState = initialGameState();
    g = stepSimulation(
      g,
      frame({ timestamp: 0, button_edges: ButtonBit.L_BUMPER | ButtonBit.R_BUMPER }),
      intent({ discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "L" }, { kind: "FOOT_HOOK_TOGGLE", side: "R" }] }),
    ).nextState;
    const res2 = stepSimulation(g, frame({ timestamp: 16 }), intent());
    expect(res2.events.some((e) => e.kind === "GUARD_OPENED")).toBe(false);
  });
});

describe("frameIndex and nowMs propagate", () => {
  it("frameIndex increments by one per step; nowMs mirrors the input timestamp", () => {
    let g: GameState = initialGameState();
    g = stepSimulation(g, frame({ timestamp: 100 }), intent()).nextState;
    expect(g.frameIndex).toBe(1);
    expect(g.nowMs).toBe(100);
    g = stepSimulation(g, frame({ timestamp: 116 }), intent()).nextState;
    expect(g.frameIndex).toBe(2);
    expect(g.nowMs).toBe(116);
  });
});

describe("unused variable sanity", () => {
  // Keeps the import live so tsc's noUnusedLocals doesn't yell about it;
  // these are design-relevant constants that future tests will exercise.
  it("foot and hand timings are exported", () => {
    expect(FOOT_TIMING.lockingMs).toBe(300);
    expect(LOCKING_POSTURE_THRESHOLD).toBe(0.3);
    expect(HAND_TIMING.retractMs).toBe(150);
  });
});
