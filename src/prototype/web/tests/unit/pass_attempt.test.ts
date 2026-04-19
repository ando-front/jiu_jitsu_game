// Unit tests for the pass-attempt FSM — docs/design/input_system_defense_v1.md §B.7.

import { describe, expect, it } from "vitest";
import {
  INITIAL_PASS_ATTEMPT,
  PASS_TIMING,
  isPassEligible,
  tickPassAttempt,
  type PassAttemptState,
} from "../../src/state/pass_attempt.js";
import { initialActorState, type ActorState } from "../../src/state/game_state.js";
import type { FootFSM } from "../../src/state/foot_fsm.js";

function foot(side: "L" | "R", state: FootFSM["state"]): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: 0 });
}

function actor(over: Partial<ActorState> = {}): ActorState {
  return Object.freeze({ ...initialActorState(0), ...over });
}

describe("isPassEligible (§B.7.1)", () => {
  it("accepts a well-formed commit", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "UNLOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.5,
      leftBasePressure: 0.7,
      rightBasePressure: 0.8,
      leftBaseZone: "BICEP_L",
      rightBaseZone: "KNEE_R",
      rsY: -0.8,
      guard: "CLOSED",
    });
    expect(ok).toBe(true);
  });

  it("rejects when both feet are LOCKED", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "LOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.5,
      leftBasePressure: 0.7, rightBasePressure: 0.8,
      leftBaseZone: "BICEP_L", rightBaseZone: "KNEE_R",
      rsY: -0.8, guard: "CLOSED",
    });
    expect(ok).toBe(false);
  });

  it("rejects when stamina < 0.2", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "UNLOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.1,
      leftBasePressure: 0.7, rightBasePressure: 0.8,
      leftBaseZone: "BICEP_L", rightBaseZone: "KNEE_R",
      rsY: -0.8, guard: "CLOSED",
    });
    expect(ok).toBe(false);
  });

  it("rejects when a hand is on CHEST (not a control zone)", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "UNLOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.5,
      leftBasePressure: 0.7, rightBasePressure: 0.8,
      leftBaseZone: "CHEST", rightBaseZone: "KNEE_R",
      rsY: -0.8, guard: "CLOSED",
    });
    expect(ok).toBe(false);
  });

  it("rejects when pressure is below 0.5", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "UNLOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.5,
      leftBasePressure: 0.3, rightBasePressure: 0.8,
      leftBaseZone: "BICEP_L", rightBaseZone: "KNEE_R",
      rsY: -0.8, guard: "CLOSED",
    });
    expect(ok).toBe(false);
  });

  it("rejects when RS isn't pointing downward", () => {
    const ok = isPassEligible({
      bottom: actor({ leftFoot: foot("L", "UNLOCKED"), rightFoot: foot("R", "LOCKED") }),
      top: actor(),
      defenderStamina: 0.5,
      leftBasePressure: 0.7, rightBasePressure: 0.8,
      leftBaseZone: "BICEP_L", rightBaseZone: "KNEE_R",
      rsY: 0.5, guard: "CLOSED",
    });
    expect(ok).toBe(false);
  });
});

describe("tickPassAttempt lifecycle (§B.7.2)", () => {
  it("commit + eligible → IN_PROGRESS and PASS_STARTED event", () => {
    const res = tickPassAttempt(INITIAL_PASS_ATTEMPT, {
      nowMs: 0,
      commitRequested: true,
      eligibleNow: true,
      attackerTriangleConfirmedThisTick: false,
    });
    expect(res.next.kind).toBe("IN_PROGRESS");
    expect(res.events).toEqual([{ kind: "PASS_STARTED" }]);
  });

  it("ineligible commit is silently ignored", () => {
    const res = tickPassAttempt(INITIAL_PASS_ATTEMPT, {
      nowMs: 0,
      commitRequested: true,
      eligibleNow: false,
      attackerTriangleConfirmedThisTick: false,
    });
    expect(res.next.kind).toBe("IDLE");
    expect(res.events).toEqual([]);
  });

  it("triangle confirm during progress → PASS_FAILED and back to IDLE", () => {
    const prev: PassAttemptState = Object.freeze({ kind: "IN_PROGRESS" as const, startedMs: 0 });
    const res = tickPassAttempt(prev, {
      nowMs: 1000,
      commitRequested: false,
      eligibleNow: true,
      attackerTriangleConfirmedThisTick: true,
    });
    expect(res.next.kind).toBe("IDLE");
    expect(res.events.some((e) => e.kind === "PASS_FAILED")).toBe(true);
  });

  it("window elapses without triangle → PASS_SUCCEEDED and back to IDLE", () => {
    const prev: PassAttemptState = Object.freeze({ kind: "IN_PROGRESS" as const, startedMs: 0 });
    const res = tickPassAttempt(prev, {
      nowMs: PASS_TIMING.windowMs + 1,
      commitRequested: false,
      eligibleNow: true,
      attackerTriangleConfirmedThisTick: false,
    });
    expect(res.next.kind).toBe("IDLE");
    expect(res.events.some((e) => e.kind === "PASS_SUCCEEDED")).toBe(true);
  });

  it("in-progress with time remaining does nothing", () => {
    const prev: PassAttemptState = Object.freeze({ kind: "IN_PROGRESS" as const, startedMs: 0 });
    const res = tickPassAttempt(prev, {
      nowMs: 1000,
      commitRequested: false,
      eligibleNow: true,
      attackerTriangleConfirmedThisTick: false,
    });
    expect(res.next.kind).toBe("IN_PROGRESS");
    expect(res.events).toEqual([]);
  });
});
