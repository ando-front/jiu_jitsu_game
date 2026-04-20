// Tests for the opponent AI decision function.
// docs/design/opponent_ai_v1.md.

import { describe, expect, it } from "vitest";
import {
  initialActorState,
  initialGameState,
  type GameState,
} from "../../src/state/game_state.js";
import { opponentIntent } from "../../src/ai/opponent_ai.js";
import { INITIAL_JUDGMENT_WINDOW } from "../../src/state/judgment_window.js";
import { INITIAL_COUNTER_WINDOW } from "../../src/state/counter_window.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";

function gripped(side: "L" | "R", zone: "SLEEVE_R" | "COLLAR_L" | "WRIST_R" | "SLEEVE_L"): HandFSM {
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
function idle(side: "L" | "R"): HandFSM {
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
function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
}

function base(): GameState {
  return initialGameState(0);
}

// -- TOP (defender) AI ------------------------------------------------------

describe("Top AI — priority 1 counter window commit", () => {
  it("commits TRIANGLE_EARLY_STACK when candidate is available", () => {
    const g: GameState = Object.freeze({
      ...base(),
      counterWindow: Object.freeze({
        ...INITIAL_COUNTER_WINDOW,
        state: "OPEN" as const,
        candidates: Object.freeze(["TRIANGLE_EARLY_STACK" as const]),
      }),
    });
    const out = opponentIntent(g, "Top");
    expect(out.role).toBe("Top");
    if (out.role === "Top") {
      expect(out.defense.hip.weight_forward).toBe(1);
      expect(out.defense.discrete.some((d) => d.kind === "RECOVERY_HOLD")).toBe(true);
      expect(out.confirmedCounter).toBe("TRIANGLE_EARLY_STACK");
    }
  });

  it("commits SCISSOR_COUNTER in opposite direction to sweep", () => {
    const g: GameState = Object.freeze({
      ...base(),
      counterWindow: Object.freeze({
        ...INITIAL_COUNTER_WINDOW,
        state: "OPEN" as const,
        candidates: Object.freeze(["SCISSOR_COUNTER" as const]),
      }),
      attackerSweepLateralSign: 1,
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      expect(out.defense.hip.weight_lateral).toBe(-1);
      expect(out.confirmedCounter).toBe("SCISSOR_COUNTER");
    }
  });

  it("leaves confirmedCounter null when counter window is closed", () => {
    const out = opponentIntent(base(), "Top");
    if (out.role === "Top") expect(out.confirmedCounter).toBeNull();
  });
});

describe("Top AI — priority 3 posture recovery", () => {
  it("RECOVERY_HOLD when sagittal break ≥ 0.5", () => {
    const g: GameState = Object.freeze({
      ...base(),
      top: Object.freeze({ ...initialActorState(0), postureBreak: { x: 0, y: 0.7 } }),
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      expect(out.defense.hip.weight_forward).toBe(1);
      expect(out.defense.discrete.some((d) => d.kind === "RECOVERY_HOLD")).toBe(true);
    }
  });
});

describe("Top AI — priority 4 cut attempt", () => {
  it("fires CUT_ATTEMPT on attacker's GRIPPED hand", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
      }),
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      const cut = out.defense.discrete.find((d) => d.kind === "CUT_ATTEMPT");
      expect(cut).toBeDefined();
    }
  });

  it("does NOT fire a new CUT_ATTEMPT while both slots are busy", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
      }),
      cutAttempts: Object.freeze({
        left: Object.freeze({
          kind: "IN_PROGRESS" as const,
          startedMs: 0,
          targetAttackerSide: "L" as const,
          targetZone: "SLEEVE_R" as const,
        }),
        right: Object.freeze({
          kind: "IN_PROGRESS" as const,
          startedMs: 0,
          targetAttackerSide: "L" as const,
          targetZone: "SLEEVE_R" as const,
        }),
      }),
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      const cut = out.defense.discrete.find((d) => d.kind === "CUT_ATTEMPT");
      expect(cut).toBeUndefined();
    }
  });
});

describe("Top AI — priority 7 breath", () => {
  it("emits BREATH_START when stamina < 0.3 (and no higher-priority trigger)", () => {
    const g: GameState = Object.freeze({
      ...base(),
      top: Object.freeze({ ...initialActorState(0), stamina: 0.1 }),
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      expect(out.defense.discrete.some((d) => d.kind === "BREATH_START")).toBe(true);
    }
  });
});

describe("Top AI — idle fallback", () => {
  it("returns ZERO_DEFENSE_INTENT when nothing interesting is happening", () => {
    // No attacker grips (so no cut), no arm_extracted, no posture break,
    // and bottom two feet locked but 0 grips keeps priority 6 active
    // unless we bump top stamina below 0.5 so the pass-prep rule skips.
    // We want to test true idle: both feet UNLOCKED removes the
    // bothFeetLocked condition in priority 6.
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "UNLOCKED"),
      }),
    });
    const out = opponentIntent(g, "Top");
    if (out.role === "Top") {
      expect(out.defense.discrete).toEqual([]);
    }
  });
});

// -- BOTTOM (attacker) AI ---------------------------------------------------

describe("Bottom AI — priority 1 commit", () => {
  it("commits first candidate when judgment window is OPEN", () => {
    const g: GameState = Object.freeze({
      ...base(),
      judgmentWindow: Object.freeze({
        ...INITIAL_JUDGMENT_WINDOW,
        state: "OPEN" as const,
        candidates: Object.freeze(["SCISSOR_SWEEP" as const]),
      }),
    });
    const out = opponentIntent(g, "Bottom");
    if (out.role === "Bottom") {
      // SCISSOR_SWEEP commit pattern: LS horizontal + L_BUMPER toggle.
      expect(Math.abs(out.intent.hip.hip_lateral)).toBe(1);
      expect(out.intent.discrete.some((d) => d.kind === "FOOT_HOOK_TOGGLE" && d.side === "L")).toBe(true);
      expect(out.confirmedTechnique).toBe("SCISSOR_SWEEP");
    }
  });

  it("leaves confirmedTechnique null when judgment window is closed", () => {
    const out = opponentIntent(base(), "Bottom");
    if (out.role === "Bottom") expect(out.confirmedTechnique).toBeNull();
  });
});

describe("Bottom AI — priority 2 first grip", () => {
  it("reaches for SLEEVE_R with left hand when no grips yet", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({ ...initialActorState(0), stamina: 0.8 }),
    });
    const out = opponentIntent(g, "Bottom");
    if (out.role === "Bottom") {
      expect(out.intent.grip.l_hand_target).toBe("SLEEVE_R");
      expect(out.intent.grip.l_grip_strength).toBeGreaterThan(0.5);
    }
  });
});

describe("Bottom AI — priority 3 second grip", () => {
  it("with one hand GRIPPED, reaches COLLAR with the free hand", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
      }),
    });
    const out = opponentIntent(g, "Bottom");
    if (out.role === "Bottom") {
      // Left is occupied → right should be aimed at COLLAR_R.
      expect(out.intent.grip.r_hand_target).toBe("COLLAR_R");
      expect(out.intent.grip.l_hand_target).toBe("SLEEVE_R");
    }
  });
});

describe("Bottom AI — priority 4 hip push for break", () => {
  it("with two grips + break < 0.4, pushes hip forward", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
        rightHand: gripped("R", "COLLAR_L"),
      }),
      top: Object.freeze({ ...initialActorState(0), postureBreak: { x: 0.1, y: 0.1 } }),
    });
    const out = opponentIntent(g, "Bottom");
    if (out.role === "Bottom") {
      expect(out.intent.hip.hip_push).toBeGreaterThan(0.5);
    }
  });
});

describe("Bottom AI — priority 5 breath", () => {
  it("emits BREATH_START when stamina low", () => {
    const g: GameState = Object.freeze({
      ...base(),
      bottom: Object.freeze({
        ...initialActorState(0),
        leftHand: gripped("L", "SLEEVE_R"),
        rightHand: gripped("R", "COLLAR_L"),
        stamina: 0.1,
      }),
      top: Object.freeze({ ...initialActorState(0), postureBreak: { x: 0.5, y: 0.5 } }),
    });
    const out = opponentIntent(g, "Bottom");
    // Only priority 4 would trigger, but break is already ≥ 0.4, so we
    // proceed to priority 5. Expect breath.
    if (out.role === "Bottom") {
      expect(out.intent.discrete.some((d) => d.kind === "BREATH_START")).toBe(true);
    }
  });
});

describe("Bottom AI — determinism", () => {
  it("same input → same output", () => {
    const g = base();
    const out1 = opponentIntent(g, "Bottom");
    const out2 = opponentIntent(g, "Bottom");
    expect(out1).toEqual(out2);
  });
});

// Sanity
describe("opponentIntent role wiring", () => {
  it("role='Top' produces defense-shaped output", () => {
    const out = opponentIntent(base(), "Top");
    expect(out.role).toBe("Top");
  });
  it("role='Bottom' produces intent-shaped output", () => {
    const out = opponentIntent(base(), "Bottom");
    expect(out.role).toBe("Bottom");
  });
  // unused import guard
  it("idle top helper references", () => {
    expect(idle("L").state).toBe("IDLE");
    expect(foot("L", "LOCKED").state).toBe("LOCKED");
  });
});
