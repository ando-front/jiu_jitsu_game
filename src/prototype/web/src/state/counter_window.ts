// PURE — defensive counter window per docs/design/input_system_defense_v1.md §D.
//
// The counter window mirrors the attacker's JudgmentWindowFSM lifecycle but
// with its own candidate set (counter techniques only). A counter window
// only opens in the same frame an attacker window enters OPENING for a
// technique that has a registered counter.
//
// Success effects (§D.2):
//   SCISSOR_COUNTER → forces the attacker's window to CLOSING (DISRUPTED)
//   TRIANGLE_EARLY_STACK → same + resets top.arm_extracted both sides to false

import {
  TIME_SCALE,
  WINDOW_TIMING,
  type Technique,
} from "./judgment_window.js";

export type CounterTechnique = "SCISSOR_COUNTER" | "TRIANGLE_EARLY_STACK";

// §D.2 — counter table. The attacker's technique → the defender's
// available counter. Not every technique has a counter in M1.
export const COUNTER_FOR: Readonly<Partial<Record<Technique, CounterTechnique>>> =
  Object.freeze({
    SCISSOR_SWEEP: "SCISSOR_COUNTER",
    TRIANGLE: "TRIANGLE_EARLY_STACK",
  });

export type CounterWindowState = "CLOSED" | "OPENING" | "OPEN" | "CLOSING";

export type CounterWindow = Readonly<{
  state: CounterWindowState;
  stateEnteredMs: number;
  candidates: ReadonlyArray<CounterTechnique>;
  cooldownUntilMs: number;
}>;

export const INITIAL_COUNTER_WINDOW: CounterWindow = Object.freeze({
  state: "CLOSED" as const,
  stateEnteredMs: Number.NEGATIVE_INFINITY,
  candidates: Object.freeze([]),
  cooldownUntilMs: Number.NEGATIVE_INFINITY,
});

// Used by stepSimulation: when the attacker's window enters OPENING, look
// up counter candidates for each firing technique. Callers pass the
// attacker candidate list; this function returns the filtered counter
// list (empty if nothing has a counter).
export function counterCandidatesFor(
  attackerCandidates: ReadonlyArray<Technique>,
): ReadonlyArray<CounterTechnique> {
  const out: CounterTechnique[] = [];
  for (const t of attackerCandidates) {
    const c = COUNTER_FOR[t];
    if (c !== undefined && !out.includes(c)) out.push(c);
  }
  return Object.freeze(out);
}

export type CounterTickInput = Readonly<{
  nowMs: number;
  openAttackerWindow: boolean;           // true during frames where the attacker window is OPENING or OPEN
  openingSeed: ReadonlyArray<CounterTechnique>; // set at the frame we should enter OPENING (one-shot)
  confirmedCounter: CounterTechnique | null;
  dismissRequested: boolean;
}>;

export type CounterTickEvent =
  | { kind: "COUNTER_WINDOW_OPENING"; candidates: ReadonlyArray<CounterTechnique> }
  | { kind: "COUNTER_WINDOW_OPEN" }
  | { kind: "COUNTER_WINDOW_CLOSING"; reason: CounterCloseReason }
  | { kind: "COUNTER_WINDOW_CLOSED" }
  | { kind: "COUNTER_CONFIRMED"; counter: CounterTechnique };

export type CounterCloseReason =
  | "CONFIRMED"
  | "DISMISSED"
  | "TIMED_OUT"
  | "ATTACKER_CLOSED"; // attacker window shut for any reason — defender loses the chance

export function tickCounterWindow(
  prev: CounterWindow,
  input: CounterTickInput,
  timing = WINDOW_TIMING,
): { next: CounterWindow; events: readonly CounterTickEvent[]; timeScale: number } {
  const events: CounterTickEvent[] = [];
  let next = prev;
  let timeScale = TIME_SCALE.normal;

  switch (prev.state) {
    case "CLOSED": {
      // Open only if the attacker's window just entered OPENING AND there
      // is at least one counter candidate. Cooldown honoured.
      if (
        input.openingSeed.length > 0 &&
        input.nowMs >= prev.cooldownUntilMs
      ) {
        next = Object.freeze({
          state: "OPENING" as const,
          stateEnteredMs: input.nowMs,
          candidates: Object.freeze([...input.openingSeed]),
          cooldownUntilMs: prev.cooldownUntilMs,
        });
        events.push({
          kind: "COUNTER_WINDOW_OPENING",
          candidates: next.candidates,
        });
      }
      break;
    }

    case "OPENING": {
      const t = (input.nowMs - prev.stateEnteredMs) / timing.openingMs;
      timeScale = lerp(TIME_SCALE.normal, TIME_SCALE.open, clamp01(t));
      if (!input.openAttackerWindow) {
        // Attacker vanished before we fully opened — bail.
        next = enterClosing(prev, input.nowMs);
        events.push({ kind: "COUNTER_WINDOW_CLOSING", reason: "ATTACKER_CLOSED" });
        break;
      }
      if (t >= 1) {
        next = Object.freeze({ ...prev, state: "OPEN" as const, stateEnteredMs: input.nowMs });
        events.push({ kind: "COUNTER_WINDOW_OPEN" });
        timeScale = TIME_SCALE.open;
      }
      break;
    }

    case "OPEN": {
      timeScale = TIME_SCALE.open;

      if (input.confirmedCounter !== null && prev.candidates.includes(input.confirmedCounter)) {
        events.push({ kind: "COUNTER_CONFIRMED", counter: input.confirmedCounter });
        next = enterClosing(prev, input.nowMs);
        events.push({ kind: "COUNTER_WINDOW_CLOSING", reason: "CONFIRMED" });
        break;
      }

      if (input.dismissRequested) {
        next = enterClosing(prev, input.nowMs);
        events.push({ kind: "COUNTER_WINDOW_CLOSING", reason: "DISMISSED" });
        break;
      }

      // Attacker's window collapsed — we lose our chance.
      if (!input.openAttackerWindow) {
        next = enterClosing(prev, input.nowMs);
        events.push({ kind: "COUNTER_WINDOW_CLOSING", reason: "ATTACKER_CLOSED" });
        break;
      }

      if (input.nowMs - prev.stateEnteredMs >= timing.openMaxMs) {
        next = enterClosing(prev, input.nowMs);
        events.push({ kind: "COUNTER_WINDOW_CLOSING", reason: "TIMED_OUT" });
      }
      break;
    }

    case "CLOSING": {
      const t = (input.nowMs - prev.stateEnteredMs) / timing.closingMs;
      timeScale = lerp(TIME_SCALE.open, TIME_SCALE.normal, clamp01(t));
      if (t >= 1) {
        next = Object.freeze({
          state: "CLOSED" as const,
          stateEnteredMs: input.nowMs,
          candidates: Object.freeze([]),
          cooldownUntilMs: input.nowMs + timing.cooldownMs,
        });
        events.push({ kind: "COUNTER_WINDOW_CLOSED" });
        timeScale = TIME_SCALE.normal;
      }
      break;
    }
  }

  return { next, events: Object.freeze(events), timeScale };
}

function enterClosing(prev: CounterWindow, nowMs: number): CounterWindow {
  return Object.freeze({ ...prev, state: "CLOSING" as const, stateEnteredMs: nowMs });
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function clamp01(x: number): number {
  return x < 0 ? 0 : x > 1 ? 1 : x;
}
