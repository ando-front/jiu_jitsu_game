// Scenario tests for HandFSM.
// References docs/design/state_machines_v1.md §2.1.

import { describe, expect, it } from "vitest";
import {
  HAND_TIMING,
  initialHand,
  tickHand,
  type HandFSM,
  type HandTickInput,
} from "../src/state/hand_fsm.js";

// Midpoint of §C.1.2's 200–350ms reach window; matches the FSM default.
const REACH_MID = (HAND_TIMING.reachMinMs + HAND_TIMING.reachMaxMs) / 2; // 275

function baseInput(over: Partial<HandTickInput> = {}): HandTickInput {
  return {
    nowMs: 0,
    triggerValue: 0,
    targetZone: null,
    forceReleaseAll: false,
    opponentDefendsThisZone: false,
    opponentCutSucceeded: false,
    targetOutOfReach: false,
    ...over,
  };
}

// Helper: drive a hand through a sequence of inputs, returning (final, allEvents).
function run(
  hand: HandFSM,
  steps: ReadonlyArray<Partial<HandTickInput> & { nowMs: number }>,
): { final: HandFSM; events: string[] } {
  let h = hand;
  const events: string[] = [];
  for (const step of steps) {
    const { next, events: ev } = tickHand(h, baseInput(step));
    for (const e of ev) events.push(eventTag(e));
    h = next;
  }
  return { final: h, events };
}

function eventTag(e: { kind: string }): string {
  return e.kind;
}

describe("HandFSM IDLE → REACHING", () => {
  it("trigger alone does nothing without a target zone", () => {
    const h = initialHand("L");
    const { next, events } = tickHand(h, baseInput({ nowMs: 0, triggerValue: 1, targetZone: null }));
    expect(next.state).toBe("IDLE");
    expect(events).toEqual([]);
  });

  it("trigger + target zone kicks off REACHING", () => {
    const h = initialHand("L");
    const { next, events } = tickHand(h, baseInput({ nowMs: 0, triggerValue: 1, targetZone: "SLEEVE_R" }));
    expect(next.state).toBe("REACHING");
    expect(next.target).toBe("SLEEVE_R");
    expect(events.map(eventTag)).toEqual(["REACH_STARTED"]);
  });
});

describe("HandFSM REACHING → CONTACT → GRIPPED", () => {
  it("reaches target after the reach duration and grips on the next frame", () => {
    const h = initialHand("L");
    const { final, events } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "COLLAR_L" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "COLLAR_L" }, // CONTACT
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "COLLAR_L" }, // GRIPPED
    ]);
    expect(final.state).toBe("GRIPPED");
    expect(events).toEqual(["REACH_STARTED", "CONTACT", "GRIPPED"]);
  });

  it("aborts to IDLE if the trigger is released mid-reach", () => {
    const h = initialHand("L");
    const { final, events } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "COLLAR_L" },
      { nowMs: 100, triggerValue: 0, targetZone: null },
    ]);
    expect(final.state).toBe("IDLE");
    expect(events).toEqual(["REACH_STARTED"]);
  });

  it("re-aims mid-reach: new target restarts the reach timer", () => {
    const h = initialHand("L");
    const { final, events } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "COLLAR_L" },
      { nowMs: 100, triggerValue: 1, targetZone: "SLEEVE_R" }, // re-aim
      { nowMs: 100 + REACH_MID, triggerValue: 1, targetZone: "SLEEVE_R" }, // CONTACT
      { nowMs: 100 + REACH_MID + 16, triggerValue: 1, targetZone: "SLEEVE_R" }, // GRIPPED
    ]);
    expect(final.state).toBe("GRIPPED");
    expect(final.target).toBe("SLEEVE_R");
    expect(events).toEqual(["REACH_STARTED", "REACH_STARTED", "CONTACT", "GRIPPED"]);
  });
});

describe("HandFSM CONTACT resolution (§2.1.3)", () => {
  it("opponent defending the same zone → PARRIED → RETRACT → IDLE", () => {
    const h = initialHand("L");
    const { final, events } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "SLEEVE_R" }, // CONTACT
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: true }, // PARRIED
      { nowMs: REACH_MID + 32, triggerValue: 0, targetZone: null }, // → RETRACT
      { nowMs: REACH_MID + 32 + HAND_TIMING.retractMs, triggerValue: 0, targetZone: null }, // IDLE
    ]);
    expect(final.state).toBe("IDLE");
    // The last PARRIED event should be emitted; RETRACT/IDLE are silent transitions.
    expect(events).toContain("PARRIED");
    // lastParriedZone should be remembered through IDLE.
    expect(final.lastParriedZone).toBe("SLEEVE_R");
  });

  it("short memory: CONTACT within 400ms of a prior parry re-parries the same zone", () => {
    // Direct unit test of the §2.1.3 short-memory clause. We fabricate a
    // REACHING hand with a recent parry baked in, then hit CONTACT before
    // the 400ms window elapses — the hand should PARRY even though the
    // opponent no longer defends the zone.
    const baseline = initialHand("L");
    const primed: HandFSM = Object.freeze({
      ...baseline,
      state: "REACHING" as const,
      target: "SLEEVE_R",
      stateEnteredMs: 100,
      reachDurationMs: 100,
      lastParriedZone: "SLEEVE_R",
      lastParriedAtMs: 50, // parried 150ms before the CONTACT frame below
    });

    // Advance to CONTACT (1 tick after the reach completes).
    const contact = tickHand(primed, baseInput({ nowMs: 200, triggerValue: 1, targetZone: "SLEEVE_R" }));
    expect(contact.next.state).toBe("CONTACT");

    // CONTACT frame: 150ms since the last parry → still inside the 400ms window.
    const resolve = tickHand(contact.next, baseInput({
      nowMs: 200, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: false,
    }));
    expect(resolve.next.state).toBe("PARRIED");
    expect(resolve.events.some((e) => e.kind === "PARRIED")).toBe(true);
  });

  it("short memory: CONTACT AFTER 400ms is NOT re-parried by memory alone", () => {
    const baseline = initialHand("L");
    const primed: HandFSM = Object.freeze({
      ...baseline,
      state: "CONTACT" as const,
      target: "SLEEVE_R",
      stateEnteredMs: 500, // far past the 400ms window
      reachDurationMs: 0,
      lastParriedZone: "SLEEVE_R",
      lastParriedAtMs: 50, // 450ms earlier → outside window
    });

    const resolve = tickHand(primed, baseInput({
      nowMs: 500, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: false,
    }));
    expect(resolve.next.state).toBe("GRIPPED");
  });

  it("short memory does NOT apply to a different zone", () => {
    const h = initialHand("L");
    const s1 = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: true },
      { nowMs: REACH_MID + 32, triggerValue: 0, targetZone: null },
      { nowMs: REACH_MID + 32 + HAND_TIMING.retractMs, triggerValue: 0, targetZone: null },
    ]);

    // Immediately retarget a different zone.
    const t = REACH_MID + 32 + HAND_TIMING.retractMs;
    const s2 = run(s1.final, [
      { nowMs: t, triggerValue: 1, targetZone: "COLLAR_L" },
      { nowMs: t + REACH_MID, triggerValue: 1, targetZone: "COLLAR_L" },
      { nowMs: t + REACH_MID + 16, triggerValue: 1, targetZone: "COLLAR_L" }, // should GRIP
    ]);
    expect(s2.final.state).toBe("GRIPPED");
    expect(s2.events).toContain("GRIPPED");
  });
});

describe("HandFSM GRIPPED break conditions (§2.1.4)", () => {
  function upToGripped(): HandFSM {
    const h = initialHand("R");
    const { final } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "BELT" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "BELT" },
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "BELT" },
    ]);
    expect(final.state).toBe("GRIPPED");
    return final;
  }

  it("releasing trigger emits GRIP_BROKEN(TRIGGER_RELEASED) and enters RETRACT", () => {
    const gripped = upToGripped();
    const { next, events } = tickHand(gripped, baseInput({ nowMs: 1000, triggerValue: 0, targetZone: null }));
    expect(next.state).toBe("RETRACT");
    const broken = events.find((e) => e.kind === "GRIP_BROKEN");
    expect(broken).toBeDefined();
    expect(broken && "reason" in broken ? broken.reason : null).toBe("TRIGGER_RELEASED");
  });

  it("opponent cut succeeds → GRIP_BROKEN(OPPONENT_CUT)", () => {
    const gripped = upToGripped();
    const { events } = tickHand(gripped, baseInput({
      nowMs: 1000, triggerValue: 1, targetZone: "BELT", opponentCutSucceeded: true,
    }));
    expect(events.some((e) => e.kind === "GRIP_BROKEN" && "reason" in e && e.reason === "OPPONENT_CUT")).toBe(true);
  });

  it("BTN_RELEASE forces any engaged state to RETRACT", () => {
    const gripped = upToGripped();
    const { next, events } = tickHand(gripped, baseInput({
      nowMs: 1000, triggerValue: 1, targetZone: "BELT", forceReleaseAll: true,
    }));
    expect(next.state).toBe("RETRACT");
    expect(events.some((e) => e.kind === "GRIP_BROKEN" && "reason" in e && e.reason === "FORCE_RELEASE")).toBe(true);
  });
});

describe("HandFSM RETRACT blocks new REACHING (§2.1.2)", () => {
  it("cannot enter REACHING while RETRACT timer is running", () => {
    const h = initialHand("L");
    // Go IDLE → REACHING → CONTACT → PARRIED → RETRACT.
    const { final: afterRetract } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: true },
      { nowMs: REACH_MID + 32, triggerValue: 1, targetZone: "SLEEVE_R" }, // PARRIED→RETRACT
    ]);
    expect(afterRetract.state).toBe("RETRACT");

    // Try to start a new reach while still in RETRACT.
    const midRetract = afterRetract.stateEnteredMs + HAND_TIMING.retractMs / 2;
    const { next } = tickHand(afterRetract, baseInput({ nowMs: midRetract, triggerValue: 1, targetZone: "COLLAR_L" }));
    expect(next.state).toBe("RETRACT");
  });

  it("after RETRACT timer expires, hand returns to IDLE and can reach again", () => {
    const h = initialHand("L");
    const { final: afterRetract } = run(h, [
      { nowMs: 0, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID, triggerValue: 1, targetZone: "SLEEVE_R" },
      { nowMs: REACH_MID + 16, triggerValue: 1, targetZone: "SLEEVE_R", opponentDefendsThisZone: true },
      { nowMs: REACH_MID + 32, triggerValue: 1, targetZone: "SLEEVE_R" },
    ]);
    const idleAt = afterRetract.stateEnteredMs + HAND_TIMING.retractMs;
    const { next: nowIdle } = tickHand(afterRetract, baseInput({ nowMs: idleAt, triggerValue: 0, targetZone: null }));
    expect(nowIdle.state).toBe("IDLE");

    const { next: reaching } = tickHand(nowIdle, baseInput({ nowMs: idleAt + 1, triggerValue: 1, targetZone: "WRIST_L" }));
    expect(reaching.state).toBe("REACHING");
  });
});
