// PURE — FootFSM per docs/design/state_machines_v1.md §2.2.

export type FootSide = "L" | "R";

export type FootState = "LOCKED" | "UNLOCKED" | "LOCKING";

export const FOOT_TIMING = Object.freeze({
  lockingMs: 300, // §2.2 — re-lock attempt takes 300ms
});

// §2.2 — LOCKING succeeds when the opponent's posture is forward-broken
// (posture_break.sagittal ≥ 0.3). The threshold is read from state at
// the moment the timer expires, so a briefly-forward posture doesn't
// win the lock by accident.
export const LOCKING_POSTURE_THRESHOLD = 0.3;

export type FootFSM = Readonly<{
  side: FootSide;
  state: FootState;
  stateEnteredMs: number;
}>;

export function initialFoot(side: FootSide, nowMs: number = 0): FootFSM {
  return Object.freeze({
    side,
    state: "LOCKED" as const, // closed guard starts with both hooks locked
    stateEnteredMs: nowMs,
  });
}

export type FootTickInput = Readonly<{
  nowMs: number;
  bumperEdge: boolean;                  // L/R bumper edge for this side
  opponentPostureSagittal: number;      // used at LOCKING → LOCKED resolution
}>;

export type FootTickEvent =
  | { kind: "UNLOCKED"; side: FootSide }
  | { kind: "LOCKING_STARTED"; side: FootSide }
  | { kind: "LOCK_SUCCEEDED"; side: FootSide }
  | { kind: "LOCK_FAILED"; side: FootSide };

export function tickFoot(
  prev: FootFSM,
  input: FootTickInput,
  timing = FOOT_TIMING,
): { next: FootFSM; events: readonly FootTickEvent[] } {
  const events: FootTickEvent[] = [];
  let next = prev;

  switch (prev.state) {
    case "LOCKED": {
      if (input.bumperEdge) {
        next = Object.freeze({ ...prev, state: "UNLOCKED" as const, stateEnteredMs: input.nowMs });
        events.push({ kind: "UNLOCKED", side: prev.side });
      }
      break;
    }
    case "UNLOCKED": {
      if (input.bumperEdge) {
        next = Object.freeze({ ...prev, state: "LOCKING" as const, stateEnteredMs: input.nowMs });
        events.push({ kind: "LOCKING_STARTED", side: prev.side });
      }
      break;
    }
    case "LOCKING": {
      // Player cancelled the re-lock attempt by pressing bumper again.
      // This isn't explicitly in §2.2, but it's the only safe resolution
      // for a mid-LOCKING bumper press — otherwise the player is stuck
      // waiting for the timer. Design doc update candidate.
      if (input.bumperEdge) {
        next = Object.freeze({ ...prev, state: "UNLOCKED" as const, stateEnteredMs: input.nowMs });
        break;
      }
      if (input.nowMs - prev.stateEnteredMs >= timing.lockingMs) {
        if (input.opponentPostureSagittal >= LOCKING_POSTURE_THRESHOLD) {
          next = Object.freeze({ ...prev, state: "LOCKED" as const, stateEnteredMs: input.nowMs });
          events.push({ kind: "LOCK_SUCCEEDED", side: prev.side });
        } else {
          next = Object.freeze({ ...prev, state: "UNLOCKED" as const, stateEnteredMs: input.nowMs });
          events.push({ kind: "LOCK_FAILED", side: prev.side });
        }
      }
      break;
    }
  }

  return { next, events: Object.freeze(events) };
}
