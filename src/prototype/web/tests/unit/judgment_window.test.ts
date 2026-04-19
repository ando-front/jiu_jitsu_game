// Tests for JudgmentWindowFSM and the six technique predicates.
// References docs/design/state_machines_v1.md §8.

import { describe, expect, it } from "vitest";
import {
  INITIAL_JUDGMENT_WINDOW,
  TIME_SCALE,
  WINDOW_TIMING,
  crossCollarConditions,
  evaluateAllTechniques,
  flowerSweepConditions,
  hipBumpConditions,
  omoplataConditions,
  scissorSweepConditions,
  tickJudgmentWindow,
  triangleConditions,
  type JudgmentContext,
  type Technique,
} from "../../src/state/judgment_window.js";
import { initialActorState, type ActorState } from "../../src/state/game_state.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";
import type { GripZone } from "../../src/input/intent.js";

// Helpers to synthesise actor states for predicate testing without routing
// through the full FSM machinery.
function grippedHand(side: "L" | "R", zone: GripZone, atMs = 0): HandFSM {
  return Object.freeze({
    side,
    state: "GRIPPED" as const,
    target: zone,
    stateEnteredMs: atMs,
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
function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
}

function actor(over: Partial<ActorState> = {}): ActorState {
  return Object.freeze({
    ...initialActorState(0),
    ...over,
  });
}

function ctx(over: Partial<JudgmentContext> = {}): JudgmentContext {
  return {
    bottom: actor(),
    top: actor(),
    bottomHipYaw: 0,
    bottomHipPush: 0,
    sustainedHipPushMs: 0,
    ...over,
  };
}

// --- Per-technique predicates ---------------------------------------------

describe("SCISSOR_SWEEP predicate (§8.2)", () => {
  it("fires with both feet LOCKED + SLEEVE gripped 0.6 + break ≥ 0.4", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "SLEEVE_R"),
        rightHand: idleHand("R"),
      }),
      top: actor({ postureBreak: { x: 0.5, y: 0 } }),
    });
    expect(scissorSweepConditions(c, 0.8, 0)).toBe(true);
  });

  it("fails if grip strength below 0.6", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "SLEEVE_R"),
      }),
      top: actor({ postureBreak: { x: 0.5, y: 0 } }),
    });
    expect(scissorSweepConditions(c, 0.4, 0)).toBe(false);
  });

  it("fails if a foot is UNLOCKED", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "SLEEVE_R"),
      }),
      top: actor({ postureBreak: { x: 0.5, y: 0 } }),
    });
    expect(scissorSweepConditions(c, 0.8, 0)).toBe(false);
  });

  it("fails if posture break magnitude < 0.4", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "SLEEVE_R"),
      }),
      top: actor({ postureBreak: { x: 0.2, y: 0 } }),
    });
    expect(scissorSweepConditions(c, 0.8, 0)).toBe(false);
  });
});

describe("FLOWER_SWEEP predicate (§8.2)", () => {
  it("needs both feet LOCKED + WRIST gripped + sagittal ≥ 0.5", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        rightHand: grippedHand("R", "WRIST_L"),
      }),
      top: actor({ postureBreak: { x: 0, y: 0.6 } }),
    });
    expect(flowerSweepConditions(c, 0, 0.5)).toBe(true);
  });
});

describe("TRIANGLE predicate (§8.2)", () => {
  it("needs one foot UNLOCKED + arm_extracted + collar gripped", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "UNLOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "COLLAR_R"),
      }),
      top: actor({ armExtractedLeft: true }),
    });
    expect(triangleConditions(c)).toBe(true);
  });

  it("rejects when both feet are LOCKED", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "COLLAR_R"),
      }),
      top: actor({ armExtractedLeft: true }),
    });
    expect(triangleConditions(c)).toBe(false);
  });
});

describe("OMOPLATA predicate (§8.2)", () => {
  it("requires sleeve-side sign match + sagittal ≥ 0.6 + yaw ≥ π/3", () => {
    const c = ctx({
      bottom: actor({
        leftHand: grippedHand("L", "SLEEVE_R"),
      }),
      top: actor({ postureBreak: { x: -0.3, y: 0.7 } }), // lateral neg ↔ L sleeve sign
      bottomHipYaw: Math.PI / 3 + 0.1,
    });
    expect(omoplataConditions(c)).toBe(true);
  });

  it("fails if hip yaw below π/3", () => {
    const c = ctx({
      bottom: actor({ leftHand: grippedHand("L", "SLEEVE_R") }),
      top: actor({ postureBreak: { x: -0.3, y: 0.7 } }),
      bottomHipYaw: Math.PI / 4,
    });
    expect(omoplataConditions(c)).toBe(false);
  });

  it("fails if lateral break sign doesn't match the sleeve-hand side", () => {
    const c = ctx({
      bottom: actor({ leftHand: grippedHand("L", "SLEEVE_R") }),
      top: actor({ postureBreak: { x: 0.3, y: 0.7 } }), // wrong sign
      bottomHipYaw: Math.PI / 3 + 0.1,
    });
    expect(omoplataConditions(c)).toBe(false);
  });
});

describe("HIP_BUMP predicate (§8.2)", () => {
  it("needs sagittal ≥ 0.7 AND sustained 300ms push", () => {
    const c = ctx({
      top: actor({ postureBreak: { x: 0, y: 0.8 } }),
      sustainedHipPushMs: 350,
    });
    expect(hipBumpConditions(c)).toBe(true);
  });

  it("rejects short push durations", () => {
    const c = ctx({
      top: actor({ postureBreak: { x: 0, y: 0.8 } }),
      sustainedHipPushMs: 100,
    });
    expect(hipBumpConditions(c)).toBe(false);
  });
});

describe("CROSS_COLLAR predicate (§8.2)", () => {
  it("needs both COLLAR GRIPPED ≥ 0.7 and break ≥ 0.5", () => {
    const c = ctx({
      bottom: actor({
        leftHand: grippedHand("L", "COLLAR_L"),
        rightHand: grippedHand("R", "COLLAR_R"),
      }),
      top: actor({ postureBreak: { x: 0.3, y: 0.5 } }),
    });
    expect(crossCollarConditions(c, 0.8, 0.8)).toBe(true);
  });

  it("fails if one hand is not on a COLLAR zone", () => {
    const c = ctx({
      bottom: actor({
        leftHand: grippedHand("L", "COLLAR_L"),
        rightHand: grippedHand("R", "SLEEVE_L"),
      }),
      top: actor({ postureBreak: { x: 0.3, y: 0.5 } }),
    });
    expect(crossCollarConditions(c, 0.8, 0.8)).toBe(false);
  });
});

describe("evaluateAllTechniques", () => {
  it("reports every currently-satisfied technique", () => {
    const c = ctx({
      bottom: actor({
        leftFoot: foot("L", "LOCKED"),
        rightFoot: foot("R", "LOCKED"),
        leftHand: grippedHand("L", "COLLAR_L"),
        rightHand: grippedHand("R", "COLLAR_R"),
      }),
      top: actor({ postureBreak: { x: 0.3, y: 0.5 } }),
    });
    const list = evaluateAllTechniques(c, 0.9, 0.9);
    expect(list).toContain("CROSS_COLLAR" satisfies Technique);
  });
});

// --- FSM lifecycle --------------------------------------------------------

describe("JudgmentWindowFSM lifecycle (§8.1)", () => {
  it("CLOSED → OPENING on any satisfied technique (outside cooldown)", () => {
    const { next, events } = tickJudgmentWindow(
      INITIAL_JUDGMENT_WINDOW,
      ["SCISSOR_SWEEP"],
      { nowMs: 0, confirmedTechnique: null, dismissRequested: false },
    );
    expect(next.state).toBe("OPENING");
    expect(events[0]?.kind).toBe("WINDOW_OPENING");
  });

  it("OPENING → OPEN after 200ms and holds time_scale at 0.3", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    const cand: Technique[] = ["SCISSOR_SWEEP"];
    const tick = (n: number) =>
      tickJudgmentWindow(w, cand, { nowMs: n, confirmedTechnique: null, dismissRequested: false });

    w = tick(0).next;               // CLOSED → OPENING
    const t1 = tick(100);
    expect(t1.next.state).toBe("OPENING");
    expect(t1.timeScale).toBeGreaterThan(TIME_SCALE.open);
    expect(t1.timeScale).toBeLessThan(TIME_SCALE.normal);

    w = tick(200).next;
    expect(w.state).toBe("OPEN");
  });

  it("OPEN → CLOSING on confirmed technique", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    const cand: Technique[] = ["SCISSOR_SWEEP"];
    w = tickJudgmentWindow(w, cand, { nowMs: 0, confirmedTechnique: null, dismissRequested: false }).next;
    w = tickJudgmentWindow(w, cand, { nowMs: 200, confirmedTechnique: null, dismissRequested: false }).next;
    expect(w.state).toBe("OPEN");

    const res = tickJudgmentWindow(
      w,
      cand,
      { nowMs: 300, confirmedTechnique: "SCISSOR_SWEEP", dismissRequested: false },
    );
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "TECHNIQUE_CONFIRMED")).toBe(true);
  });

  it("OPEN → CLOSING (DISRUPTED) when all candidates lose their conditions", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    const cand: Technique[] = ["SCISSOR_SWEEP"];
    w = tickJudgmentWindow(w, cand, { nowMs: 0, confirmedTechnique: null, dismissRequested: false }).next;
    w = tickJudgmentWindow(w, cand, { nowMs: 200, confirmedTechnique: null, dismissRequested: false }).next;

    const res = tickJudgmentWindow(
      w,
      [], // all conditions collapsed
      { nowMs: 300, confirmedTechnique: null, dismissRequested: false },
    );
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "WINDOW_CLOSING" && "reason" in e && e.reason === "DISRUPTED")).toBe(true);
  });

  it("OPEN → CLOSING (TIMED_OUT) after openMaxMs", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    const cand: Technique[] = ["SCISSOR_SWEEP"];
    w = tickJudgmentWindow(w, cand, { nowMs: 0, confirmedTechnique: null, dismissRequested: false }).next;
    w = tickJudgmentWindow(w, cand, { nowMs: 200, confirmedTechnique: null, dismissRequested: false }).next;

    const t = 200 + WINDOW_TIMING.openMaxMs;
    const res = tickJudgmentWindow(
      w,
      cand,
      { nowMs: t, confirmedTechnique: null, dismissRequested: false },
    );
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "WINDOW_CLOSING" && "reason" in e && e.reason === "TIMED_OUT")).toBe(true);
  });

  it("CLOSING → CLOSED and honours cooldown for 400ms", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    const cand: Technique[] = ["SCISSOR_SWEEP"];
    // OPEN → CLOSING on confirm.
    w = tickJudgmentWindow(w, cand, { nowMs: 0, confirmedTechnique: null, dismissRequested: false }).next;
    w = tickJudgmentWindow(w, cand, { nowMs: 200, confirmedTechnique: null, dismissRequested: false }).next;
    w = tickJudgmentWindow(
      w,
      cand,
      { nowMs: 300, confirmedTechnique: "SCISSOR_SWEEP", dismissRequested: false },
    ).next;
    expect(w.state).toBe("CLOSING");

    const closedAt = 300 + WINDOW_TIMING.closingMs;
    w = tickJudgmentWindow(w, cand, { nowMs: closedAt, confirmedTechnique: null, dismissRequested: false }).next;
    expect(w.state).toBe("CLOSED");
    expect(w.cooldownUntilMs).toBe(closedAt + WINDOW_TIMING.cooldownMs);

    // Cooldown: technique satisfied but OPENING should not fire.
    const midCooldown = tickJudgmentWindow(
      w,
      cand,
      { nowMs: closedAt + 100, confirmedTechnique: null, dismissRequested: false },
    );
    expect(midCooldown.next.state).toBe("CLOSED");

    // After cooldown, a new OPENING is permitted.
    const afterCooldown = tickJudgmentWindow(
      w,
      cand,
      { nowMs: closedAt + WINDOW_TIMING.cooldownMs + 1, confirmedTechnique: null, dismissRequested: false },
    );
    expect(afterCooldown.next.state).toBe("OPENING");
  });

  it("candidate set is frozen at OPENING entry (§8.3)", () => {
    let w = INITIAL_JUDGMENT_WINDOW;
    w = tickJudgmentWindow(w, ["SCISSOR_SWEEP"], { nowMs: 0, confirmedTechnique: null, dismissRequested: false }).next;
    expect(w.candidates).toEqual(["SCISSOR_SWEEP"]);

    // A newly-satisfied technique later should NOT be added.
    w = tickJudgmentWindow(
      w,
      ["SCISSOR_SWEEP", "HIP_BUMP"],
      { nowMs: 100, confirmedTechnique: null, dismissRequested: false },
    ).next;
    expect(w.candidates).toEqual(["SCISSOR_SWEEP"]);
  });
});
