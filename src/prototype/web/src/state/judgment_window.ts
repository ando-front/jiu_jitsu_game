// PURE — JudgmentWindowFSM per docs/design/state_machines_v1.md §8.
//
// Lifecycle: CLOSED → OPENING (0.2s) → OPEN (≤1.5s) → CLOSING (0.3s) → CLOSED
// with a 400ms cooldown before the next OPENING can begin.
//
// Firing conditions for the six M1 techniques live in §8.2. The predicate
// functions here take a narrow "JudgmentContext" snapshot rather than the
// full GameState so the tests can drive each technique in isolation
// without constructing a whole actor tree.

import type { ActorState } from "./game_state.js";
import { breakMagnitude } from "./posture_break.js";

export type Technique =
  | "SCISSOR_SWEEP"
  | "FLOWER_SWEEP"
  | "TRIANGLE"
  | "OMOPLATA"
  | "HIP_BUMP"
  | "CROSS_COLLAR";

export type JudgmentWindowState = "CLOSED" | "OPENING" | "OPEN" | "CLOSING";

// §8.1 timing table.
export const WINDOW_TIMING = Object.freeze({
  openingMs: 200,
  openMaxMs: 1500,
  closingMs: 300,
  cooldownMs: 400,
});

// §8.1 time-scale values.
export const TIME_SCALE: Readonly<{ normal: number; open: number }> = Object.freeze({
  normal: 1.0,
  open: 0.3,
});

export type JudgmentWindow = Readonly<{
  state: JudgmentWindowState;
  stateEnteredMs: number;
  // Frozen at OPENING entry per §8.3.
  candidates: ReadonlyArray<Technique>;
  // Cooldown guard: OPENING cannot start before this timestamp.
  cooldownUntilMs: number;
  // Side that fired the window — for ControlLayer bookkeeping (not used
  // for gating transitions). Null while CLOSED.
  firedBy: "Bottom" | "Top" | null;
}>;

export const INITIAL_JUDGMENT_WINDOW: JudgmentWindow = Object.freeze({
  state: "CLOSED" as const,
  stateEnteredMs: Number.NEGATIVE_INFINITY,
  candidates: Object.freeze([]),
  cooldownUntilMs: Number.NEGATIVE_INFINITY,
  firedBy: null,
});

// Snapshot of the world pieces the firing predicates need. Keeping this
// narrow makes the predicates trivially unit-testable.
export type JudgmentContext = Readonly<{
  bottom: ActorState;
  top: ActorState;
  // Attacker hip input reference — ports easily from HipIntent.
  bottomHipYaw: number;       // from intent.hip.hip_angle_target
  bottomHipPush: number;      // from intent.hip.hip_push
  // Sustained-condition counters (see §D.1.1 "hip_bump 300ms維持"). These
  // are maintained outside the FSM by stepSimulation because they need to
  // accumulate across ticks regardless of window state.
  sustainedHipPushMs: number; // ms so far of hip_push ≥ 0.5 sustained
}>;

// --- Firing predicates (§8.2) ----------------------------------------------

function anyHandGrippedAt(
  actor: ActorState,
  matchesZone: (zone: string) => boolean,
  minStrength: number,
  strengthL: number,
  strengthR: number,
): { side: "L" | "R" | null; strength: number } {
  const l = actor.leftHand;
  if (l.state === "GRIPPED" && l.target !== null && matchesZone(l.target) && strengthL >= minStrength) {
    return { side: "L", strength: strengthL };
  }
  const r = actor.rightHand;
  if (r.state === "GRIPPED" && r.target !== null && matchesZone(r.target) && strengthR >= minStrength) {
    return { side: "R", strength: strengthR };
  }
  return { side: null, strength: 0 };
}

export function scissorSweepConditions(
  ctx: JudgmentContext,
  strengthL: number,
  strengthR: number,
): boolean {
  const b = ctx.bottom;
  if (b.leftFoot.state !== "LOCKED" || b.rightFoot.state !== "LOCKED") return false;
  const sleeve = anyHandGrippedAt(b, (z) => z === "SLEEVE_L" || z === "SLEEVE_R", 0.6, strengthL, strengthR);
  if (sleeve.side === null) return false;
  return breakMagnitude(ctx.top.postureBreak) >= 0.4;
}

export function flowerSweepConditions(
  ctx: JudgmentContext,
  strengthL: number,
  strengthR: number,
): boolean {
  const b = ctx.bottom;
  if (b.leftFoot.state !== "LOCKED" || b.rightFoot.state !== "LOCKED") return false;
  const wrist = anyHandGrippedAt(b, (z) => z === "WRIST_L" || z === "WRIST_R", 0, strengthL, strengthR);
  if (wrist.side === null) return false;
  return ctx.top.postureBreak.y >= 0.5;
}

export function triangleConditions(ctx: JudgmentContext): boolean {
  const b = ctx.bottom;
  const oneFootUnlocked = b.leftFoot.state === "UNLOCKED" || b.rightFoot.state === "UNLOCKED";
  if (!oneFootUnlocked) return false;
  const eitherArmExtracted = ctx.top.armExtractedLeft || ctx.top.armExtractedRight;
  if (!eitherArmExtracted) return false;
  const collarGripped =
    (b.leftHand.state === "GRIPPED" && isCollar(b.leftHand.target)) ||
    (b.rightHand.state === "GRIPPED" && isCollar(b.rightHand.target));
  return collarGripped;
}

export function omoplataConditions(ctx: JudgmentContext): boolean {
  const b = ctx.bottom;
  // One-hand sleeve grip.
  const sleeveHand = pickSleeveHand(b);
  if (sleeveHand === null) return false;

  if (ctx.top.postureBreak.y < 0.6) return false;

  // §8.2 "sign一致": sleeveHand side sign must match lateral break sign.
  const sideSign = sleeveHand === "L" ? -1 : 1;
  if (Math.sign(ctx.top.postureBreak.x) !== sideSign) return false;

  // Bottom hip yaw abs ≥ π/3.
  return Math.abs(ctx.bottomHipYaw) >= Math.PI / 3;
}

export function hipBumpConditions(ctx: JudgmentContext): boolean {
  if (ctx.top.postureBreak.y < 0.7) return false;
  // §D.1.1: bottom.hip_push ≥ 0.5 sustained 300ms.
  return ctx.sustainedHipPushMs >= 300;
}

export function crossCollarConditions(
  ctx: JudgmentContext,
  strengthL: number,
  strengthR: number,
): boolean {
  const b = ctx.bottom;
  const lOk = b.leftHand.state === "GRIPPED" && isCollar(b.leftHand.target) && strengthL >= 0.7;
  const rOk = b.rightHand.state === "GRIPPED" && isCollar(b.rightHand.target) && strengthR >= 0.7;
  if (!(lOk && rOk)) return false;
  return breakMagnitude(ctx.top.postureBreak) >= 0.5;
}

function pickSleeveHand(actor: ActorState): "L" | "R" | null {
  if (actor.leftHand.state === "GRIPPED" && isSleeve(actor.leftHand.target)) return "L";
  if (actor.rightHand.state === "GRIPPED" && isSleeve(actor.rightHand.target)) return "R";
  return null;
}

function isCollar(z: string | null): boolean {
  return z === "COLLAR_L" || z === "COLLAR_R";
}

function isSleeve(z: string | null): boolean {
  return z === "SLEEVE_L" || z === "SLEEVE_R";
}

// Evaluate every technique predicate; return the techniques currently
// satisfied. Used both for OPENING fire-check and for §8.3 "condition
// collapse" monitoring during OPEN.
export function evaluateAllTechniques(
  ctx: JudgmentContext,
  strengthL: number,
  strengthR: number,
): ReadonlyArray<Technique> {
  const out: Technique[] = [];
  if (scissorSweepConditions(ctx, strengthL, strengthR)) out.push("SCISSOR_SWEEP");
  if (flowerSweepConditions(ctx, strengthL, strengthR)) out.push("FLOWER_SWEEP");
  if (triangleConditions(ctx)) out.push("TRIANGLE");
  if (omoplataConditions(ctx)) out.push("OMOPLATA");
  if (hipBumpConditions(ctx)) out.push("HIP_BUMP");
  if (crossCollarConditions(ctx, strengthL, strengthR)) out.push("CROSS_COLLAR");
  return Object.freeze(out);
}

// --- FSM --------------------------------------------------------------------

export type JudgmentTickInput = Readonly<{
  nowMs: number;
  // Set when the player commits a technique this frame. Stage 1 leaves
  // the input-to-technique mapping to a later commit; the caller produces
  // this already-resolved signal.
  confirmedTechnique: Technique | null;
  // Any "dismiss" input (e.g. BTN_RELEASE) closes the window per §D.2.
  dismissRequested: boolean;
}>;

export type JudgmentTickEvent =
  | { kind: "WINDOW_OPENING"; candidates: ReadonlyArray<Technique>; firedBy: "Bottom" | "Top" }
  | { kind: "WINDOW_OPEN" }
  | { kind: "WINDOW_CLOSING"; reason: WindowCloseReason }
  | { kind: "WINDOW_CLOSED" }
  | { kind: "TECHNIQUE_CONFIRMED"; technique: Technique };

export type WindowCloseReason =
  | "CONFIRMED"
  | "DISMISSED"
  | "TIMED_OUT"
  | "DISRUPTED"; // §8.3 — all candidates collapsed

export function tickJudgmentWindow(
  prev: JudgmentWindow,
  currentlySatisfied: ReadonlyArray<Technique>,
  tick: JudgmentTickInput,
  timing = WINDOW_TIMING,
): { next: JudgmentWindow; events: readonly JudgmentTickEvent[]; timeScale: number } {
  const events: JudgmentTickEvent[] = [];
  let next = prev;
  let timeScale = TIME_SCALE.normal;

  switch (prev.state) {
    case "CLOSED": {
      // Cooldown guard + any satisfied technique → OPENING.
      if (tick.nowMs >= prev.cooldownUntilMs && currentlySatisfied.length > 0) {
        next = Object.freeze({
          state: "OPENING" as const,
          stateEnteredMs: tick.nowMs,
          candidates: Object.freeze([...currentlySatisfied]),
          cooldownUntilMs: prev.cooldownUntilMs,
          firedBy: "Bottom" as const, // Stage 1: only bottom triggers windows
        });
        events.push({
          kind: "WINDOW_OPENING",
          candidates: next.candidates,
          firedBy: "Bottom",
        });
      }
      break;
    }

    case "OPENING": {
      // Interpolate the scale across the opening window.
      const t = (tick.nowMs - prev.stateEnteredMs) / timing.openingMs;
      timeScale = lerp(TIME_SCALE.normal, TIME_SCALE.open, clamp01(t));
      if (t >= 1) {
        next = Object.freeze({
          ...prev,
          state: "OPEN" as const,
          stateEnteredMs: tick.nowMs,
        });
        events.push({ kind: "WINDOW_OPEN" });
        timeScale = TIME_SCALE.open;
      }
      break;
    }

    case "OPEN": {
      timeScale = TIME_SCALE.open;

      // Confirm has priority over everything else.
      if (tick.confirmedTechnique !== null && prev.candidates.includes(tick.confirmedTechnique)) {
        events.push({ kind: "TECHNIQUE_CONFIRMED", technique: tick.confirmedTechnique });
        next = enterClosing(prev, tick.nowMs);
        events.push({ kind: "WINDOW_CLOSING", reason: "CONFIRMED" });
        break;
      }

      if (tick.dismissRequested) {
        next = enterClosing(prev, tick.nowMs);
        events.push({ kind: "WINDOW_CLOSING", reason: "DISMISSED" });
        break;
      }

      // §8.3: disruption if every candidate lost its conditions.
      const anyStillValid = prev.candidates.some((t) => currentlySatisfied.includes(t));
      if (!anyStillValid) {
        next = enterClosing(prev, tick.nowMs);
        events.push({ kind: "WINDOW_CLOSING", reason: "DISRUPTED" });
        break;
      }

      if (tick.nowMs - prev.stateEnteredMs >= timing.openMaxMs) {
        next = enterClosing(prev, tick.nowMs);
        events.push({ kind: "WINDOW_CLOSING", reason: "TIMED_OUT" });
      }
      break;
    }

    case "CLOSING": {
      const t = (tick.nowMs - prev.stateEnteredMs) / timing.closingMs;
      timeScale = lerp(TIME_SCALE.open, TIME_SCALE.normal, clamp01(t));
      if (t >= 1) {
        next = Object.freeze({
          state: "CLOSED" as const,
          stateEnteredMs: tick.nowMs,
          candidates: Object.freeze([]),
          cooldownUntilMs: tick.nowMs + timing.cooldownMs,
          firedBy: null,
        });
        events.push({ kind: "WINDOW_CLOSED" });
        timeScale = TIME_SCALE.normal;
      }
      break;
    }
  }

  return { next, events: Object.freeze(events), timeScale };
}

function enterClosing(prev: JudgmentWindow, nowMs: number): JudgmentWindow {
  return Object.freeze({
    ...prev,
    state: "CLOSING" as const,
    stateEnteredMs: nowMs,
  });
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function clamp01(x: number): number {
  return x < 0 ? 0 : x > 1 ? 1 : x;
}
