// PURE — HandFSM per docs/design/state_machines_v1.md §2.1.
// No DOM / no Three.js. Pure transition functions.
//
// Time handling: the FSM stores absolute timestamps at which transitions
// began (e.g. reachingStartMs). Each tick receives `nowMs` from the caller
// so the same state logic is testable without any real clock.

import type { GripZone } from "../input/intent.js";

export type HandSide = "L" | "R";

export type HandState =
  | "IDLE"
  | "REACHING"
  | "CONTACT"
  | "GRIPPED"
  | "PARRIED"
  | "RETRACT";

// §2.1 — timing parameters. These are the fixed defaults; they can be
// overridden per test to avoid time-waiting tests.
export const HAND_TIMING = Object.freeze({
  reachMinMs: 200,   // §C.1.2 REACHING 200–350ms
  reachMaxMs: 350,
  retractMs: 150,    // §C.1.2 RETRACT 150ms
  shortMemoryMs: 400, // §C.2 parry short-term memory
});

export type HandFSM = Readonly<{
  side: HandSide;
  state: HandState;
  target: GripZone | null; // where the hand is currently reaching / gripping
  stateEnteredMs: number;  // timestamp of the most recent state entry
  reachDurationMs: number; // chosen at REACHING entry (distance-dependent proxy)
  // Short-term memory: which (hand, zone) were parried recently.
  // The pair is implicit (the HandFSM is per-side already), so we only
  // need the zone and the timestamp.
  lastParriedZone: GripZone | null;
  lastParriedAtMs: number;
}>;

export function initialHand(side: HandSide, nowMs: number = 0): HandFSM {
  return Object.freeze({
    side,
    state: "IDLE" as const,
    target: null,
    stateEnteredMs: nowMs,
    reachDurationMs: 0,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

// Inputs needed to drive the FSM each tick. Keeping these explicit makes
// the pure dependency surface obvious — anything Layer C/D needs to know
// about grip state must flow through this struct.
export type HandTickInput = Readonly<{
  nowMs: number;
  triggerValue: number;           // [0,1] — L or R trigger depending on hand
  targetZone: GripZone | null;    // currently intended zone (from Layer B)
  forceReleaseAll: boolean;       // BTN_RELEASE edge
  // Contact resolution inputs — supplied by caller because HandFSM alone
  // cannot know the opponent's defensive state.
  opponentDefendsThisZone: boolean;
  // Opponent cut success against this GRIPPED hand (see §4.2 / §2.1.4).
  opponentCutSucceeded: boolean;
  // §2.1.4 last row: opponent posture moved the target out of reach.
  targetOutOfReach: boolean;
}>;

export type HandTickEvent =
  | { kind: "REACH_STARTED"; side: HandSide; zone: GripZone }
  | { kind: "CONTACT"; side: HandSide; zone: GripZone }
  | { kind: "GRIPPED"; side: HandSide; zone: GripZone }
  | { kind: "PARRIED"; side: HandSide; zone: GripZone }
  | { kind: "GRIP_BROKEN"; side: HandSide; zone: GripZone; reason: GripBrokenReason };

export type GripBrokenReason =
  | "TRIGGER_RELEASED"
  | "FORCE_RELEASE"
  | "OPPONENT_CUT"
  | "OUT_OF_REACH";

export function tickHand(
  prev: HandFSM,
  input: HandTickInput,
  timing = HAND_TIMING,
): { next: HandFSM; events: readonly HandTickEvent[] } {
  const events: HandTickEvent[] = [];
  let next = prev;

  // Global escape valve: BTN_RELEASE forces any hand currently engaged
  // back through RETRACT (§B.3 "事故の出口").
  if (input.forceReleaseAll && (prev.state === "GRIPPED" || prev.state === "CONTACT" || prev.state === "REACHING")) {
    if (prev.state === "GRIPPED" && prev.target !== null) {
      events.push({ kind: "GRIP_BROKEN", side: prev.side, zone: prev.target, reason: "FORCE_RELEASE" });
    }
    return { next: enterRetract(prev, input.nowMs), events };
  }

  switch (prev.state) {
    case "IDLE": {
      // §2.1.2 — trigger press + a target zone kicks off REACHING.
      // No zone = no action; a player pressing the trigger without aiming
      // does nothing, consistent with the §B.2.2 gating.
      if (input.triggerValue > 0 && input.targetZone !== null) {
        const zone = input.targetZone;
        const reachDurationMs = chooseReachDuration(timing);
        next = Object.freeze({
          ...prev,
          state: "REACHING" as const,
          target: zone,
          stateEnteredMs: input.nowMs,
          reachDurationMs,
        });
        events.push({ kind: "REACH_STARTED", side: prev.side, zone });
      }
      break;
    }

    case "REACHING": {
      // Abort back to IDLE if the player releases the trigger mid-reach.
      // Design note: we don't route REACHING→RETRACT here because nothing
      // was ever contacted. A mid-reach abort just cancels quietly.
      if (input.triggerValue === 0) {
        next = Object.freeze({
          ...prev,
          state: "IDLE" as const,
          target: null,
          stateEnteredMs: input.nowMs,
        });
        break;
      }
      // Zone re-aim: if the player swings RS to a new zone mid-reach,
      // restart the reach toward the new target. The short memory still
      // applies — if that new zone was recently parried, the follow-up
      // CONTACT resolution will re-parry it.
      if (input.targetZone !== null && input.targetZone !== prev.target) {
        const zone = input.targetZone;
        next = Object.freeze({
          ...prev,
          target: zone,
          stateEnteredMs: input.nowMs,
          reachDurationMs: chooseReachDuration(timing),
        });
        events.push({ kind: "REACH_STARTED", side: prev.side, zone });
        break;
      }
      // Reach timer expired → CONTACT (1 frame).
      if (input.nowMs - prev.stateEnteredMs >= prev.reachDurationMs) {
        next = Object.freeze({
          ...prev,
          state: "CONTACT" as const,
          stateEnteredMs: input.nowMs,
        });
        if (prev.target !== null) {
          events.push({ kind: "CONTACT", side: prev.side, zone: prev.target });
        }
      }
      break;
    }

    case "CONTACT": {
      // §2.1.3 — resolution happens on the frame AFTER CONTACT entry.
      // Priority order (strict): opponent-defends > short-memory > grip.
      if (prev.target === null) {
        next = enterIdle(prev, input.nowMs);
        break;
      }
      const recentlyParried =
        prev.lastParriedZone === prev.target &&
        input.nowMs - prev.lastParriedAtMs < timing.shortMemoryMs;

      if (input.opponentDefendsThisZone || recentlyParried) {
        next = Object.freeze({
          ...prev,
          state: "PARRIED" as const,
          stateEnteredMs: input.nowMs,
          lastParriedZone: prev.target,
          lastParriedAtMs: input.nowMs,
        });
        events.push({ kind: "PARRIED", side: prev.side, zone: prev.target });
      } else {
        next = Object.freeze({
          ...prev,
          state: "GRIPPED" as const,
          stateEnteredMs: input.nowMs,
        });
        events.push({ kind: "GRIPPED", side: prev.side, zone: prev.target });
      }
      break;
    }

    case "PARRIED": {
      // §2.1.2 — PARRIED is instantaneous; next tick goes to RETRACT.
      next = enterRetract(prev, input.nowMs);
      break;
    }

    case "GRIPPED": {
      // §2.1.4 — any of the break conditions routes back through RETRACT.
      const zone = prev.target;
      if (input.opponentCutSucceeded && zone !== null) {
        events.push({ kind: "GRIP_BROKEN", side: prev.side, zone, reason: "OPPONENT_CUT" });
        next = enterRetract(prev, input.nowMs);
        break;
      }
      if (input.targetOutOfReach && zone !== null) {
        events.push({ kind: "GRIP_BROKEN", side: prev.side, zone, reason: "OUT_OF_REACH" });
        next = enterRetract(prev, input.nowMs);
        break;
      }
      if (input.triggerValue === 0 && zone !== null) {
        events.push({ kind: "GRIP_BROKEN", side: prev.side, zone, reason: "TRIGGER_RELEASED" });
        next = enterRetract(prev, input.nowMs);
        break;
      }
      // GRIPPED persists — strength updates are read from the trigger
      // directly by downstream layers (Layer C). We don't store it here
      // because it's a pure function of the current frame's trigger.
      break;
    }

    case "RETRACT": {
      // §2.1.2 — retract timer; no new REACH allowed during this window.
      if (input.nowMs - prev.stateEnteredMs >= timing.retractMs) {
        next = enterIdle(prev, input.nowMs);
      }
      break;
    }
  }

  return { next, events: Object.freeze(events) };
}

function enterIdle(prev: HandFSM, nowMs: number): HandFSM {
  return Object.freeze({
    ...prev,
    state: "IDLE" as const,
    target: null,
    stateEnteredMs: nowMs,
    reachDurationMs: 0,
  });
}

function enterRetract(prev: HandFSM, nowMs: number): HandFSM {
  return Object.freeze({
    ...prev,
    state: "RETRACT" as const,
    target: null,
    stateEnteredMs: nowMs,
    reachDurationMs: 0,
  });
}

function chooseReachDuration(timing: typeof HAND_TIMING): number {
  // §C.1.2 — "200–350ms, distance-dependent, linear". We don't have real
  // world positions at Stage 1; splitting the difference to the midpoint
  // matches the intent and is fully deterministic for tests.
  return (timing.reachMinMs + timing.reachMaxMs) / 2;
}
