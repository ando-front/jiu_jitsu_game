// Tests for the counter window FSM — docs/design/input_system_defense_v1.md §D.

import { describe, expect, it } from "vitest";
import {
  INITIAL_COUNTER_WINDOW,
  counterCandidatesFor,
  tickCounterWindow,
  type CounterTechnique,
} from "../../src/state/counter_window.js";
import { WINDOW_TIMING } from "../../src/state/judgment_window.js";

const EMPTY: ReadonlyArray<CounterTechnique> = [];

describe("counterCandidatesFor mapping (§D.2)", () => {
  it("SCISSOR_SWEEP maps to SCISSOR_COUNTER", () => {
    expect(counterCandidatesFor(["SCISSOR_SWEEP"])).toEqual(["SCISSOR_COUNTER"]);
  });
  it("TRIANGLE maps to TRIANGLE_EARLY_STACK", () => {
    expect(counterCandidatesFor(["TRIANGLE"])).toEqual(["TRIANGLE_EARLY_STACK"]);
  });
  it("techniques with no counter yield empty array", () => {
    expect(counterCandidatesFor(["FLOWER_SWEEP", "HIP_BUMP"])).toEqual([]);
  });
  it("dedupes when multiple techniques share a counter", () => {
    const out = counterCandidatesFor(["SCISSOR_SWEEP", "TRIANGLE"]);
    expect(out).toContain("SCISSOR_COUNTER");
    expect(out).toContain("TRIANGLE_EARLY_STACK");
  });
});

describe("counter window lifecycle", () => {
  it("stays CLOSED when the attacker has no counter-able techniques", () => {
    const { next, events } = tickCounterWindow(INITIAL_COUNTER_WINDOW, {
      nowMs: 0,
      openAttackerWindow: true,
      openingSeed: EMPTY,
      confirmedCounter: null,
      dismissRequested: false,
    });
    expect(next.state).toBe("CLOSED");
    expect(events).toEqual([]);
  });

  it("opens when given a non-empty openingSeed", () => {
    const { next, events } = tickCounterWindow(INITIAL_COUNTER_WINDOW, {
      nowMs: 0,
      openAttackerWindow: true,
      openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null,
      dismissRequested: false,
    });
    expect(next.state).toBe("OPENING");
    expect(events[0]?.kind).toBe("COUNTER_WINDOW_OPENING");
  });

  it("progresses OPENING → OPEN after 200ms", () => {
    let w = INITIAL_COUNTER_WINDOW;
    w = tickCounterWindow(w, {
      nowMs: 0, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    }).next;
    w = tickCounterWindow(w, {
      nowMs: 200, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    }).next;
    expect(w.state).toBe("OPEN");
  });

  it("confirm transitions OPEN → CLOSING and emits COUNTER_CONFIRMED", () => {
    let w = INITIAL_COUNTER_WINDOW;
    w = tickCounterWindow(w, {
      nowMs: 0, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    }).next;
    w = tickCounterWindow(w, {
      nowMs: 200, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    }).next;
    const res = tickCounterWindow(w, {
      nowMs: 300, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: "SCISSOR_COUNTER", dismissRequested: false,
    });
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "COUNTER_CONFIRMED")).toBe(true);
  });

  it("aborts when the attacker's window closes before we reach OPEN", () => {
    let w = INITIAL_COUNTER_WINDOW;
    w = tickCounterWindow(w, {
      nowMs: 0, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    }).next;
    const res = tickCounterWindow(w, {
      nowMs: 50, openAttackerWindow: false, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    });
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "COUNTER_WINDOW_CLOSING" && "reason" in e && e.reason === "ATTACKER_CLOSED")).toBe(true);
  });

  it("times out after openMaxMs", () => {
    let w = INITIAL_COUNTER_WINDOW;
    w = tickCounterWindow(w, {
      nowMs: 0, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    }).next;
    w = tickCounterWindow(w, {
      nowMs: 200, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    }).next;
    const res = tickCounterWindow(w, {
      nowMs: 200 + WINDOW_TIMING.openMaxMs,
      openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    });
    expect(res.next.state).toBe("CLOSING");
    expect(res.events.some((e) => e.kind === "COUNTER_WINDOW_CLOSING" && "reason" in e && e.reason === "TIMED_OUT")).toBe(true);
  });

  it("honours the cooldown after CLOSED", () => {
    // Drive all the way through to CLOSED then attempt to reopen.
    let w = INITIAL_COUNTER_WINDOW;
    w = tickCounterWindow(w, {
      nowMs: 0, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    }).next;
    w = tickCounterWindow(w, {
      nowMs: 200, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    }).next;
    w = tickCounterWindow(w, {
      nowMs: 300, openAttackerWindow: true, openingSeed: EMPTY,
      confirmedCounter: "SCISSOR_COUNTER", dismissRequested: false,
    }).next;
    // CLOSING → CLOSED
    const closeAt = 300 + WINDOW_TIMING.closingMs;
    w = tickCounterWindow(w, {
      nowMs: closeAt, openAttackerWindow: false, openingSeed: EMPTY,
      confirmedCounter: null, dismissRequested: false,
    }).next;
    expect(w.state).toBe("CLOSED");
    expect(w.cooldownUntilMs).toBe(closeAt + WINDOW_TIMING.cooldownMs);

    // Cooldown in effect — openingSeed present but window stays closed.
    const mid = tickCounterWindow(w, {
      nowMs: closeAt + 100, openAttackerWindow: true, openingSeed: ["SCISSOR_COUNTER"],
      confirmedCounter: null, dismissRequested: false,
    });
    expect(mid.next.state).toBe("CLOSED");
  });
});
