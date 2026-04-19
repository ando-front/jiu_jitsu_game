// Tests for the defender cut-attempt FSM.
// docs/design/state_machines_v1.md §4.2.

import { describe, expect, it } from "vitest";
import {
  CUT_TIMING,
  INITIAL_CUT_ATTEMPTS,
  pickCutTarget,
  tickCutAttempts,
  type CutTickInput,
} from "../../src/state/cut_attempt.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";
import type { GripZone } from "../../src/input/intent.js";

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

function inputs(over: Partial<CutTickInput> = {}): CutTickInput {
  return {
    nowMs: 0,
    leftCommit: null,
    rightCommit: null,
    attackerLeft: idle("L"),
    attackerRight: idle("R"),
    attackerTriggerL: 0,
    attackerTriggerR: 0,
    ...over,
  };
}

describe("pickCutTarget (§B.4.1)", () => {
  it("returns null when no attacker hand is GRIPPED", () => {
    expect(pickCutTarget({ x: 0, y: 0 }, idle("L"), idle("R"))).toBe(null);
  });

  it("returns the only GRIPPED hand regardless of RS", () => {
    const t = pickCutTarget({ x: 0.5, y: 0.5 }, gripped("L", "SLEEVE_R"), idle("R"));
    expect(t).toEqual({ side: "L", zone: "SLEEVE_R" });
  });

  it("prefers attacker L when RS points left", () => {
    const t = pickCutTarget(
      { x: -1, y: 0 },
      gripped("L", "SLEEVE_R"),
      gripped("R", "SLEEVE_L"),
    );
    expect(t?.side).toBe("L");
  });

  it("prefers attacker R when RS points right", () => {
    const t = pickCutTarget(
      { x: 1, y: 0 },
      gripped("L", "SLEEVE_R"),
      gripped("R", "SLEEVE_L"),
    );
    expect(t?.side).toBe("R");
  });
});

describe("tickCutAttempts lifecycle", () => {
  it("commits start IN_PROGRESS with a target", () => {
    const res = tickCutAttempts(INITIAL_CUT_ATTEMPTS, inputs({
      nowMs: 0,
      leftCommit: { rs: { x: 0, y: 0 } },
      attackerLeft: gripped("L", "SLEEVE_R"),
    }));
    expect(res.next.left.kind).toBe("IN_PROGRESS");
    expect(res.events.some((e) => e.kind === "CUT_STARTED")).toBe(true);
  });

  it("silent drop when no GRIPPED hand available", () => {
    const res = tickCutAttempts(INITIAL_CUT_ATTEMPTS, inputs({
      leftCommit: { rs: { x: 1, y: 0 } },
    }));
    expect(res.next.left.kind).toBe("IDLE");
    expect(res.events).toEqual([]);
  });

  it("resolution SUCCEEDS when attacker trigger < 0.5 at expiry", () => {
    let s = INITIAL_CUT_ATTEMPTS;
    s = tickCutAttempts(s, inputs({
      nowMs: 0,
      leftCommit: { rs: { x: 0, y: 0 } },
      attackerLeft: gripped("L", "SLEEVE_R"),
      attackerTriggerL: 0.8,
    })).next;
    const res = tickCutAttempts(s, inputs({
      nowMs: CUT_TIMING.attemptMs,
      attackerLeft: gripped("L", "SLEEVE_R"),
      attackerTriggerL: 0.3, // attacker let go during attempt
    }));
    expect(res.events.some((e) => e.kind === "CUT_SUCCEEDED" && "attackerSide" in e && e.attackerSide === "L")).toBe(true);
    expect(res.next.left.kind).toBe("IDLE");
  });

  it("resolution FAILS when attacker trigger ≥ 0.5 at expiry", () => {
    let s = INITIAL_CUT_ATTEMPTS;
    s = tickCutAttempts(s, inputs({
      nowMs: 0,
      rightCommit: { rs: { x: 0, y: 0 } },
      attackerRight: gripped("R", "COLLAR_L"),
      attackerTriggerR: 0.4,
    })).next;
    const res = tickCutAttempts(s, inputs({
      nowMs: CUT_TIMING.attemptMs,
      attackerRight: gripped("R", "COLLAR_L"),
      attackerTriggerR: 0.9, // attacker re-asserted grip
    }));
    expect(res.events.some((e) => e.kind === "CUT_FAILED" && "defender" in e && e.defender === "R")).toBe(true);
    expect(res.next.right.kind).toBe("IDLE");
  });

  it("mid-attempt second commit is ignored", () => {
    let s = INITIAL_CUT_ATTEMPTS;
    s = tickCutAttempts(s, inputs({
      nowMs: 0,
      leftCommit: { rs: { x: 0, y: 0 } },
      attackerLeft: gripped("L", "SLEEVE_R"),
    })).next;
    const res = tickCutAttempts(s, inputs({
      nowMs: 500,
      leftCommit: { rs: { x: 0, y: 0 } }, // second commit mid-attempt
      attackerLeft: gripped("L", "SLEEVE_R"),
    }));
    expect(res.events).toEqual([]);
    expect(res.next.left.kind).toBe("IN_PROGRESS");
  });

  it("both defender slots can run in parallel", () => {
    let s = INITIAL_CUT_ATTEMPTS;
    s = tickCutAttempts(s, inputs({
      nowMs: 0,
      leftCommit: { rs: { x: -1, y: 0 } },
      rightCommit: { rs: { x: 1, y: 0 } },
      attackerLeft: gripped("L", "SLEEVE_R"),
      attackerRight: gripped("R", "SLEEVE_L"),
    })).next;
    expect(s.left.kind).toBe("IN_PROGRESS");
    expect(s.right.kind).toBe("IN_PROGRESS");
  });
});
