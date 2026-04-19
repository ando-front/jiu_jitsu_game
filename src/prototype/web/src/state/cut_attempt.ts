// PURE — defender cut-attempt per docs/design/state_machines_v1.md §4.2.
//
// Each defender hand maintains its own CutAttempt slot. On CUT_ATTEMPT
// (with an RS direction), we pick the attacker's GRIPPED hand whose zone
// best matches the RS aim, start a 1500ms timer, and at expiry evaluate
// the attacker's current grip strength:
//   strength < 0.5  → cut SUCCEEDS (attacker hand forced into RETRACT)
//   strength ≥ 0.5  → cut FAILS (defender hand drops back to IDLE; no side
//                     effect on the attacker)
//
// During the 1500ms the attacker can raise their grip to defend, so the
// timing matters: a weak grip + a well-aimed cut = good play.

import type { HandFSM } from "./hand_fsm.js";
import type { GripZone } from "../input/intent.js";

export type CutSide = "L" | "R";

// Which attacker hand the cut is targeting. The cut always hits ONE
// attacker hand — either L or R, chosen at attempt-start based on whose
// GRIPPED zone best matches the defender's RS aim direction.
export type AttackerSide = "L" | "R";

export type CutAttemptSlot =
  | { kind: "IDLE" }
  | {
      kind: "IN_PROGRESS";
      startedMs: number;
      targetAttackerSide: AttackerSide;
      targetZone: GripZone;
    };

export type CutAttempts = Readonly<{
  left: CutAttemptSlot;   // defender's left hand
  right: CutAttemptSlot;  // defender's right hand
}>;

export const INITIAL_CUT_ATTEMPTS: CutAttempts = Object.freeze({
  left: Object.freeze({ kind: "IDLE" as const }),
  right: Object.freeze({ kind: "IDLE" as const }),
});

export const CUT_TIMING = Object.freeze({
  attemptMs: 1500,
});

export const CUT_SUCCESS_STRENGTH_THRESHOLD = 0.5;

// Grip zones are arranged symmetrically around the attacker. We match the
// defender's RS aim direction to the zone's canonical position — reusing
// the attacker's §B.2.1 table is tempting but the lookup is simpler if
// we just accept a pre-resolved zone from the caller.

// Decides which attacker hand (if any) the cut should target given the
// defender's RS vector and the attacker's current GRIPPED zones.
//
// Returns null if no attacker hand is GRIPPED (§B.4.1 "候補が0なら不発").
export function pickCutTarget(
  rs: Readonly<{ x: number; y: number }>,
  attackerLeft: HandFSM,
  attackerRight: HandFSM,
): { side: AttackerSide; zone: GripZone } | null {
  const rsMag = Math.hypot(rs.x, rs.y);
  if (rsMag < 1e-6) {
    // No RS direction — fall back to the first GRIPPED hand.
    if (attackerLeft.state === "GRIPPED" && attackerLeft.target !== null) {
      return { side: "L", zone: attackerLeft.target };
    }
    if (attackerRight.state === "GRIPPED" && attackerRight.target !== null) {
      return { side: "R", zone: attackerRight.target };
    }
    return null;
  }
  // With an aim direction, we just pick whichever attacker hand is
  // GRIPPED. The design doesn't specify how to choose when both are
  // GRIPPED; we mirror the RS x-sign:
  //   rs.x < 0 → prefer attacker's L, otherwise R
  const preferLeft = rs.x < 0;
  const l = attackerLeft.state === "GRIPPED" && attackerLeft.target !== null
    ? attackerLeft : null;
  const r = attackerRight.state === "GRIPPED" && attackerRight.target !== null
    ? attackerRight : null;
  if (l !== null && (r === null || preferLeft)) {
    return { side: "L", zone: l.target! };
  }
  if (r !== null) {
    return { side: "R", zone: r.target! };
  }
  return null;
}

export type CutTickInput = Readonly<{
  nowMs: number;
  // Per-defender-hand commit requests this tick, with RS snapshot. The
  // caller builds these from the CUT_ATTEMPT discrete intents.
  leftCommit: Readonly<{ rs: Readonly<{ x: number; y: number }> }> | null;
  rightCommit: Readonly<{ rs: Readonly<{ x: number; y: number }> }> | null;
  // Current attacker hand states — used for target picking AND for grip
  // strength evaluation at expiry.
  attackerLeft: HandFSM;
  attackerRight: HandFSM;
  attackerTriggerL: number;
  attackerTriggerR: number;
}>;

export type CutTickEvent =
  | { kind: "CUT_STARTED"; defender: CutSide; attackerSide: AttackerSide; zone: GripZone }
  | { kind: "CUT_SUCCEEDED"; defender: CutSide; attackerSide: AttackerSide }
  | { kind: "CUT_FAILED"; defender: CutSide };

export function tickCutAttempts(
  prev: CutAttempts,
  input: CutTickInput,
  timing = CUT_TIMING,
): { next: CutAttempts; events: readonly CutTickEvent[] } {
  const events: CutTickEvent[] = [];
  const nextLeft = tickOneSlot("L", prev.left, input.leftCommit, input, events, timing);
  const nextRight = tickOneSlot("R", prev.right, input.rightCommit, input, events, timing);
  return {
    next: Object.freeze({ left: nextLeft, right: nextRight }),
    events: Object.freeze(events),
  };
}

function tickOneSlot(
  defenderSide: CutSide,
  prev: CutAttemptSlot,
  commit: Readonly<{ rs: Readonly<{ x: number; y: number }> }> | null,
  input: CutTickInput,
  events: CutTickEvent[],
  timing: typeof CUT_TIMING,
): CutAttemptSlot {
  if (prev.kind === "IN_PROGRESS") {
    if (input.nowMs - prev.startedMs >= timing.attemptMs) {
      // Resolve: read current strength of the targeted attacker hand.
      const strength =
        prev.targetAttackerSide === "L" ? input.attackerTriggerL : input.attackerTriggerR;
      if (strength < CUT_SUCCESS_STRENGTH_THRESHOLD) {
        events.push({
          kind: "CUT_SUCCEEDED",
          defender: defenderSide,
          attackerSide: prev.targetAttackerSide,
        });
      } else {
        events.push({ kind: "CUT_FAILED", defender: defenderSide });
      }
      return { kind: "IDLE" };
    }
    // Still running; a second commit request is ignored.
    return prev;
  }

  // IDLE: honour a commit if one is provided and a target exists.
  if (commit !== null) {
    const picked = pickCutTarget(commit.rs, input.attackerLeft, input.attackerRight);
    if (picked !== null) {
      events.push({
        kind: "CUT_STARTED",
        defender: defenderSide,
        attackerSide: picked.side,
        zone: picked.zone,
      });
      return {
        kind: "IN_PROGRESS",
        startedMs: input.nowMs,
        targetAttackerSide: picked.side,
        targetZone: picked.zone,
      };
    }
    // No target → silent drop (§B.4.1 "音声フィードバックのみ").
  }
  return prev;
}
