// PURE — pass-attempt state per docs/design/input_system_defense_v1.md §B.7.
//
// M1 simplification: we skip the 5-second animation phase and produce one
// of three terminal outcomes on commit:
//   - ineligible     → commit silently rejected (no state change, no event)
//   - counter hits   → PASS_FAILED (attacker triangle-counters during the
//                      5s window; represented as counterConfirmed=true)
//   - otherwise      → PASS_SUCCEEDED after passWindowMs elapses
//
// The pass attempt lives on GameState so stepSimulation can tick its
// timer. Caller supplies the raw PASS_COMMIT intent; we gate it here.

import type { ActorState, GuardState } from "./game_state.js";

export type PassAttemptState =
  | { kind: "IDLE" }
  | { kind: "IN_PROGRESS"; startedMs: number };

export const INITIAL_PASS_ATTEMPT: PassAttemptState = Object.freeze({ kind: "IDLE" });

export const PASS_TIMING = Object.freeze({
  windowMs: 5000,
});

// §B.7.1 — eligibility predicates.
export function isPassEligible(params: Readonly<{
  bottom: ActorState;
  top: ActorState;
  defenderStamina: number;
  leftBasePressure: number;
  rightBasePressure: number;
  leftBaseZone: string | null;
  rightBaseZone: string | null;
  rsY: number;
  guard: GuardState;
}>): boolean {
  if (params.guard !== "CLOSED") return false;
  // One foot must be UNLOCKED (closed guard not completely sealing).
  const oneFootUnlocked =
    params.bottom.leftFoot.state === "UNLOCKED" ||
    params.bottom.rightFoot.state === "UNLOCKED";
  if (!oneFootUnlocked) return false;

  if (params.defenderStamina < 0.2) return false;

  // Both hands pressing ≥ 0.5 on BICEP_* or KNEE_*.
  const controlZone = (z: string | null) =>
    z === "BICEP_L" || z === "BICEP_R" || z === "KNEE_L" || z === "KNEE_R";
  if (
    !controlZone(params.leftBaseZone) ||
    !controlZone(params.rightBaseZone) ||
    params.leftBasePressure < 0.5 ||
    params.rightBasePressure < 0.5
  ) {
    return false;
  }

  // RS pointing downward (rsY ≤ 0 with some magnitude) per §B.7.1.
  if (params.rsY > -0.3) return false;

  return true;
}

export type PassTickInput = Readonly<{
  nowMs: number;
  commitRequested: boolean;
  eligibleNow: boolean;
  // If the attacker pulls off a TRIANGLE during the 5s window the pass
  // fails outright. In Stage 1 we receive this as a signal from the
  // judgment-window layer.
  attackerTriangleConfirmedThisTick: boolean;
}>;

export type PassTickEvent =
  | { kind: "PASS_STARTED" }
  | { kind: "PASS_FAILED" }
  | { kind: "PASS_SUCCEEDED" };

export function tickPassAttempt(
  prev: PassAttemptState,
  inp: PassTickInput,
  timing = PASS_TIMING,
): { next: PassAttemptState; events: readonly PassTickEvent[] } {
  const events: PassTickEvent[] = [];

  if (prev.kind === "IDLE") {
    if (inp.commitRequested && inp.eligibleNow) {
      events.push({ kind: "PASS_STARTED" });
      return {
        next: Object.freeze({ kind: "IN_PROGRESS" as const, startedMs: inp.nowMs }),
        events: Object.freeze(events),
      };
    }
    return { next: prev, events: Object.freeze(events) };
  }

  // IN_PROGRESS: attacker's triangle aborts the pass immediately.
  if (inp.attackerTriangleConfirmedThisTick) {
    events.push({ kind: "PASS_FAILED" });
    return { next: INITIAL_PASS_ATTEMPT, events: Object.freeze(events) };
  }

  if (inp.nowMs - prev.startedMs >= timing.windowMs) {
    events.push({ kind: "PASS_SUCCEEDED" });
    return { next: INITIAL_PASS_ATTEMPT, events: Object.freeze(events) };
  }

  return { next: prev, events: Object.freeze(events) };
}
