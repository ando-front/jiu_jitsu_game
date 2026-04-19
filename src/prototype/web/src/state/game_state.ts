// PURE — GameState aggregate and the top-level stepSimulation tick function.
// Reference: docs/design/architecture_overview_v1.md §7, docs/design/state_machines_v1.md §10.
//
// Evaluation order (§10): ActorState → GuardFSM → (ControlLayer — deferred)
// → JudgmentWindowFSM. This module owns that order.

import type { Intent } from "../input/intent.js";
import type { InputFrame } from "../input/types.js";
import { ButtonBit } from "../input/types.js";
import {
  initialFoot,
  tickFoot,
  type FootFSM,
  type FootSide,
  type FootTickEvent,
} from "./foot_fsm.js";
import {
  initialHand,
  tickHand,
  type HandFSM,
  type HandSide,
  type HandTickEvent,
} from "./hand_fsm.js";
import {
  gripPullVector,
  updatePostureBreak,
} from "./posture_break.js";
import {
  INITIAL_JUDGMENT_WINDOW,
  TIME_SCALE,
  evaluateAllTechniques,
  tickJudgmentWindow,
  type JudgmentContext,
  type JudgmentTickEvent,
  type JudgmentWindow,
  type Technique,
} from "./judgment_window.js";

export type Vec2 = Readonly<{ x: number; y: number }>;

export const ZERO_VEC2: Vec2 = Object.freeze({ x: 0, y: 0 });

export type ActorState = Readonly<{
  leftHand: HandFSM;
  rightHand: HandFSM;
  leftFoot: FootFSM;
  rightFoot: FootFSM;
  postureBreak: Vec2;
  stamina: number;
  armExtractedLeft: boolean;
  armExtractedRight: boolean;
}>;

export function initialActorState(nowMs: number): ActorState {
  return Object.freeze({
    leftHand: initialHand("L", nowMs),
    rightHand: initialHand("R", nowMs),
    leftFoot: initialFoot("L", nowMs),
    rightFoot: initialFoot("R", nowMs),
    postureBreak: ZERO_VEC2,
    stamina: 1,
    armExtractedLeft: false,
    armExtractedRight: false,
  });
}

export type GuardState = "CLOSED" | "OPEN";

// §9 — time context exposed so downstream (camera / animation / scene)
// can read the active scale. We track realDt and gameDt both for parity
// with the design doc, even though Stage 1's fixed step only uses gameDt.
export type TimeContext = Readonly<{
  scale: number;
  realDtMs: number;
  gameDtMs: number;
}>;

export const INITIAL_TIME_CONTEXT: TimeContext = Object.freeze({
  scale: 1,
  realDtMs: 0,
  gameDtMs: 0,
});

// Small side-effect-free accumulator for sustained-condition tracking
// (currently: hip_bump's 300ms requirement). Living on GameState keeps
// the whole tick deterministic.
export type SustainedCounters = Readonly<{
  hipPushMs: number; // total consecutive ms of bottom.hip_push >= 0.5
}>;

export const INITIAL_SUSTAINED: SustainedCounters = Object.freeze({
  hipPushMs: 0,
});

export type GameState = Readonly<{
  bottom: ActorState;
  top: ActorState;
  guard: GuardState;
  judgmentWindow: JudgmentWindow;
  time: TimeContext;
  sustained: SustainedCounters;
  frameIndex: number;
  nowMs: number;
}>;

export function initialGameState(nowMs: number = 0): GameState {
  return Object.freeze({
    bottom: initialActorState(nowMs),
    top: initialActorState(nowMs),
    guard: "CLOSED" as const,
    judgmentWindow: INITIAL_JUDGMENT_WINDOW,
    time: INITIAL_TIME_CONTEXT,
    sustained: INITIAL_SUSTAINED,
    frameIndex: 0,
    nowMs,
  });
}

export type SimEvent =
  | HandTickEvent
  | FootTickEvent
  | JudgmentTickEvent
  | { kind: "GUARD_OPENED" };

// Extra per-tick options that the caller (sim loop) supplies.
// - realDtMs: wall-clock delta for time-context bookkeeping.
// - gameDtMs: scaled delta used by posture_break and any other continuous
//   integrator. The caller is responsible for computing gameDtMs from the
//   previous frame's timeScale, NOT from the post-judgment scale — that's
//   already the convention that keeps the §9 "A層 real_dt / B層以降 game_dt"
//   rule honest.
// - confirmedTechnique: if the player commits a technique this frame
//   (resolved outside this module, typically via a Layer D adapter),
//   pass it here. Stage 1 main.ts passes null.
export type StepOptions = Readonly<{
  realDtMs: number;
  gameDtMs: number;
  confirmedTechnique: Technique | null;
}>;

export function stepSimulation(
  prev: GameState,
  frame: InputFrame,
  intent: Intent,
  opts: StepOptions = { realDtMs: 0, gameDtMs: 0, confirmedTechnique: null },
): { nextState: GameState; events: readonly SimEvent[] } {
  const events: SimEvent[] = [];
  const nowMs = frame.timestamp;

  // 1. ActorState updates — hands / feet (via FSM ticks).
  const nextBottom = tickBottomActor(prev.bottom, prev.top, frame, intent, nowMs, events);
  const nextTopPassive = tickTopActorPassive(prev.top, nowMs);

  // 2. posture_break continuous update. Attacker (bottom) drives the TOP
  // actor's posture break; we pass defenderRecovery=zero until defender
  // input is wired.
  const gripPulls: Vec2[] = [];
  if (nextBottom.leftHand.state === "GRIPPED" && nextBottom.leftHand.target !== null) {
    gripPulls.push(gripPullVector(nextBottom.leftHand.target, frame.l_trigger));
  }
  if (nextBottom.rightHand.state === "GRIPPED" && nextBottom.rightHand.target !== null) {
    gripPulls.push(gripPullVector(nextBottom.rightHand.target, frame.r_trigger));
  }
  const nextTopPosture = updatePostureBreak(
    prev.top.postureBreak,
    {
      dtMs: opts.gameDtMs,
      attackerHip: intent.hip,
      gripPulls,
      defenderRecovery: ZERO_VEC2,
    },
  );
  const nextTop: ActorState = Object.freeze({ ...nextTopPassive, postureBreak: nextTopPosture });

  // 3. Sustained counters. hip_bump needs 300ms of hip_push ≥ 0.5 in a row.
  const pushActive = intent.hip.hip_push >= 0.5;
  const nextSustained: SustainedCounters = Object.freeze({
    hipPushMs: pushActive ? prev.sustained.hipPushMs + opts.gameDtMs : 0,
  });

  // 4. GuardFSM — one-way CLOSED → OPEN when both feet are UNLOCKED.
  let nextGuard = prev.guard;
  if (
    prev.guard === "CLOSED" &&
    nextBottom.leftFoot.state === "UNLOCKED" &&
    nextBottom.rightFoot.state === "UNLOCKED"
  ) {
    nextGuard = "OPEN";
    events.push({ kind: "GUARD_OPENED" });
  }

  // 5. JudgmentWindowFSM. evaluateAllTechniques consults the FRESH actor
  // states + posture break so same-frame transitions (e.g. grip just
  // became GRIPPED) contribute to the fire check.
  const ctx: JudgmentContext = {
    bottom: nextBottom,
    top: nextTop,
    bottomHipYaw: intent.hip.hip_angle_target,
    bottomHipPush: intent.hip.hip_push,
    sustainedHipPushMs: nextSustained.hipPushMs,
  };
  const satisfied = evaluateAllTechniques(ctx, frame.l_trigger, frame.r_trigger);
  const dismissRequested = (frame.button_edges & ButtonBit.BTN_RELEASE) !== 0;
  const winResult = tickJudgmentWindow(
    prev.judgmentWindow,
    satisfied,
    { nowMs, confirmedTechnique: opts.confirmedTechnique, dismissRequested },
  );
  for (const e of winResult.events) events.push(e);

  // 6. TimeContext — scale returned by the window tick is the canonical
  // time scale for this frame.
  const nextTime: TimeContext = Object.freeze({
    scale: winResult.timeScale,
    realDtMs: opts.realDtMs,
    gameDtMs: opts.gameDtMs,
  });

  const nextState: GameState = Object.freeze({
    bottom: nextBottom,
    top: nextTop,
    guard: nextGuard,
    judgmentWindow: winResult.next,
    time: nextTime,
    sustained: nextSustained,
    frameIndex: prev.frameIndex + 1,
    nowMs,
  });

  return { nextState, events: Object.freeze(events) };
}

// -- internal helpers --------------------------------------------------------

function tickBottomActor(
  prev: ActorState,
  top: ActorState,
  frame: InputFrame,
  intent: Intent,
  nowMs: number,
  eventsOut: SimEvent[],
): ActorState {
  const forceReleaseAll = (frame.button_edges & ButtonBit.BTN_RELEASE) !== 0;

  const leftResult = tickHand(prev.leftHand, {
    nowMs,
    triggerValue: frame.l_trigger,
    targetZone: intent.grip.l_hand_target,
    forceReleaseAll,
    opponentDefendsThisZone: false,
    opponentCutSucceeded: false,
    targetOutOfReach: false,
  });
  pushAll(eventsOut, leftResult.events);

  const rightResult = tickHand(prev.rightHand, {
    nowMs,
    triggerValue: frame.r_trigger,
    targetZone: intent.grip.r_hand_target,
    forceReleaseAll,
    opponentDefendsThisZone: false,
    opponentCutSucceeded: false,
    targetOutOfReach: false,
  });
  pushAll(eventsOut, rightResult.events);

  const leftBumperEdge = intent.discrete.some(
    (d) => d.kind === "FOOT_HOOK_TOGGLE" && d.side === "L",
  );
  const rightBumperEdge = intent.discrete.some(
    (d) => d.kind === "FOOT_HOOK_TOGGLE" && d.side === "R",
  );

  const leftFootResult = tickFoot(prev.leftFoot, {
    nowMs,
    bumperEdge: leftBumperEdge,
    opponentPostureSagittal: top.postureBreak.y,
  });
  pushAll(eventsOut, leftFootResult.events);

  const rightFootResult = tickFoot(prev.rightFoot, {
    nowMs,
    bumperEdge: rightBumperEdge,
    opponentPostureSagittal: top.postureBreak.y,
  });
  pushAll(eventsOut, rightFootResult.events);

  return Object.freeze({
    ...prev,
    leftHand: leftResult.next,
    rightHand: rightResult.next,
    leftFoot: leftFootResult.next,
    rightFoot: rightFootResult.next,
  });
}

function tickTopActorPassive(prev: ActorState, nowMs: number): ActorState {
  const rest = { nowMs, bumperEdge: false, opponentPostureSagittal: 0 };
  const leftFoot = tickFoot(prev.leftFoot, rest).next;
  const rightFoot = tickFoot(prev.rightFoot, rest).next;

  const handRest = {
    nowMs,
    triggerValue: 0,
    targetZone: null,
    forceReleaseAll: false,
    opponentDefendsThisZone: false,
    opponentCutSucceeded: false,
    targetOutOfReach: false,
  };
  const leftHand = tickHand(prev.leftHand, handRest).next;
  const rightHand = tickHand(prev.rightHand, handRest).next;

  return Object.freeze({
    ...prev,
    leftHand,
    rightHand,
    leftFoot,
    rightFoot,
  });
}

function pushAll<T>(target: T[], src: readonly T[]): void {
  for (const x of src) target.push(x);
}

export function handOf(actor: ActorState, side: HandSide): HandFSM {
  return side === "L" ? actor.leftHand : actor.rightHand;
}
export function footOf(actor: ActorState, side: FootSide): FootFSM {
  return side === "L" ? actor.leftFoot : actor.rightFoot;
}

// Re-export for convenience so main.ts and tests don't need to import
// multiple files for common symbols.
export { TIME_SCALE };
