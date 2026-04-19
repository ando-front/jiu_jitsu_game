// Tests for FootFSM — docs/design/state_machines_v1.md §2.2.

import { describe, expect, it } from "vitest";
import {
  FOOT_TIMING,
  LOCKING_POSTURE_THRESHOLD,
  initialFoot,
  tickFoot,
  type FootFSM,
  type FootTickInput,
} from "../../src/state/foot_fsm.js";

function baseInput(over: Partial<FootTickInput> = {}): FootTickInput {
  return {
    nowMs: 0,
    bumperEdge: false,
    opponentPostureSagittal: 0,
    ...over,
  };
}

function run(
  foot: FootFSM,
  steps: ReadonlyArray<Partial<FootTickInput> & { nowMs: number }>,
): { final: FootFSM; events: string[] } {
  let f = foot;
  const events: string[] = [];
  for (const s of steps) {
    const { next, events: ev } = tickFoot(f, baseInput(s));
    for (const e of ev) events.push(e.kind);
    f = next;
  }
  return { final: f, events };
}

describe("FootFSM transitions (§2.2)", () => {
  it("starts LOCKED by default (closed guard)", () => {
    expect(initialFoot("L").state).toBe("LOCKED");
  });

  it("LOCKED → UNLOCKED on bumper edge", () => {
    const { final, events } = run(initialFoot("L"), [{ nowMs: 0, bumperEdge: true }]);
    expect(final.state).toBe("UNLOCKED");
    expect(events).toEqual(["UNLOCKED"]);
  });

  it("UNLOCKED → LOCKING on bumper edge, and succeeds if posture is forward-broken", () => {
    const { final, events } = run(initialFoot("L"), [
      { nowMs: 0, bumperEdge: true }, // LOCKED→UNLOCKED
      { nowMs: 50, bumperEdge: true }, // UNLOCKED→LOCKING
      {
        nowMs: 50 + FOOT_TIMING.lockingMs,
        opponentPostureSagittal: LOCKING_POSTURE_THRESHOLD + 0.1,
      },
    ]);
    expect(final.state).toBe("LOCKED");
    expect(events).toEqual(["UNLOCKED", "LOCKING_STARTED", "LOCK_SUCCEEDED"]);
  });

  it("LOCKING fails and drops to UNLOCKED if posture is upright", () => {
    const { final, events } = run(initialFoot("R"), [
      { nowMs: 0, bumperEdge: true },
      { nowMs: 50, bumperEdge: true },
      {
        nowMs: 50 + FOOT_TIMING.lockingMs,
        opponentPostureSagittal: 0, // upright
      },
    ]);
    expect(final.state).toBe("UNLOCKED");
    expect(events).toEqual(["UNLOCKED", "LOCKING_STARTED", "LOCK_FAILED"]);
  });

  it("LOCKING aborts to UNLOCKED if bumper is pressed again", () => {
    const { final } = run(initialFoot("L"), [
      { nowMs: 0, bumperEdge: true },
      { nowMs: 50, bumperEdge: true },
      { nowMs: 100, bumperEdge: true }, // abort mid-LOCKING
    ]);
    expect(final.state).toBe("UNLOCKED");
  });

  it("held bumper (no edge) is ignored in LOCKED", () => {
    // bumperEdge=false across multiple ticks — should not fire.
    const { final, events } = run(initialFoot("L"), [
      { nowMs: 0 },
      { nowMs: 16 },
      { nowMs: 32 },
    ]);
    expect(final.state).toBe("LOCKED");
    expect(events).toEqual([]);
  });
});
