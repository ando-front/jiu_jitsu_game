// PURE — GameState aggregate and the top-level stepSimulation tick function.
// Reference: docs/design/architecture_overview_v1.md §7, docs/design/state_machines_v1.md §10.
//
// This module owns the per-frame evaluation order across sub-FSMs. It MUST
// remain pure: no DOM, no Three.js, no console.log in production paths.
//
// Current Stage 1 scope (deliberately narrow): bottom actor's hands/feet
// driven by player input, plus posture_break placeholder + minimal guard
// FSM. Top actor, stamina, arm_extracted, judgment window, and control
// layer are stubbed and will be filled in subsequent commits.

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

export type Vec2 = Readonly<{ x: number; y: number }>;

export const ZERO_VEC2: Vec2 = Object.freeze({ x: 0, y: 0 });

// §1.2 — ActorState. Stage-1 partial; top actor fields remain even when
// stubbed so the structure is symmetric (per §0.1 principle 4).
export type ActorState = Readonly<{
  leftHand: HandFSM;
  rightHand: HandFSM;
  leftFoot: FootFSM;
  rightFoot: FootFSM;
  postureBreak: Vec2;
  stamina: number; // [0,1]
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

// §6 — GuardFSM is tiny in Stage 1: CLOSED → OPEN is a one-way exit that
// also ends the session. No OPEN→CLOSED recovery in M1 scope.
export type GuardState = "CLOSED" | "OPEN";

export type GameState = Readonly<{
  bottom: ActorState;
  top: ActorState;
  guard: GuardState;
  frameIndex: number;
  nowMs: number;
}>;

export function initialGameState(nowMs: number = 0): GameState {
  return Object.freeze({
    bottom: initialActorState(nowMs),
    top: initialActorState(nowMs),
    guard: "CLOSED",
    frameIndex: 0,
    nowMs,
  });
}

export type SimEvent =
  | HandTickEvent
  | FootTickEvent
  | { kind: "GUARD_OPENED" };

// The one entry point. Platform code (main.ts / sim loop) calls this per
// fixed timestep with a fresh InputFrame + Intent. Returns a new GameState
// and the events that fired this tick.
//
// Stage 1 non-goals: top actor is NOT driven by input here. A top-side
// input adapter is deferred to the Layer B defense pass. Top-side state
// still ticks (so its FSMs don't freeze), just with an empty intent.
export function stepSimulation(
  prev: GameState,
  frame: InputFrame,
  intent: Intent,
): { nextState: GameState; events: readonly SimEvent[] } {
  const events: SimEvent[] = [];
  const nowMs = frame.timestamp;

  const nextBottom = tickBottomActor(prev.bottom, prev.top, frame, intent, nowMs, events);

  // Stage 1: top actor is passive. We still let its feet drift forward in
  // time (stateEnteredMs etc.) by running a no-op tick, so the LOCKING
  // timer on top-side would resolve correctly once we wire top input.
  const nextTop = tickTopActorPassive(prev.top, nowMs);

  // §6 — two-foot UNLOCKED opens the guard. One-shot transition.
  let nextGuard = prev.guard;
  if (
    prev.guard === "CLOSED" &&
    nextBottom.leftFoot.state === "UNLOCKED" &&
    nextBottom.rightFoot.state === "UNLOCKED"
  ) {
    nextGuard = "OPEN";
    events.push({ kind: "GUARD_OPENED" });
  }

  const nextState: GameState = Object.freeze({
    bottom: nextBottom,
    top: nextTop,
    guard: nextGuard,
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

  // Per-hand contact resolution needs to know (a) what the opponent is
  // defending and (b) whether the target is out of reach. Stage 1 keeps
  // both stubbed (no top AI / no reach physics), so PARRIED can only come
  // from the 400ms short-memory path. This is sufficient for logic tests;
  // opponent-defence wiring arrives with the defender input pass.
  const leftResult = tickHand(
    prev.leftHand,
    {
      nowMs,
      triggerValue: frame.l_trigger,
      targetZone: intent.grip.l_hand_target,
      forceReleaseAll,
      opponentDefendsThisZone: false,
      opponentCutSucceeded: false,
      targetOutOfReach: false,
    },
  );
  pushAll(eventsOut, leftResult.events);

  const rightResult = tickHand(
    prev.rightHand,
    {
      nowMs,
      triggerValue: frame.r_trigger,
      targetZone: intent.grip.r_hand_target,
      forceReleaseAll,
      opponentDefendsThisZone: false,
      opponentCutSucceeded: false,
      targetOutOfReach: false,
    },
  );
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

  // posture_break, stamina, arm_extracted are not yet driven — preserved
  // from prev. See §3.3 / §5 / §4.1 for the update rules to implement.
  return Object.freeze({
    ...prev,
    leftHand: leftResult.next,
    rightHand: rightResult.next,
    leftFoot: leftFootResult.next,
    rightFoot: rightFootResult.next,
  });
}

// Passive top actor: hands stay IDLE, feet stay LOCKED / whatever they
// were. We still advance time indices so tests that inspect timing fields
// see a monotonic clock on the top side too.
function tickTopActorPassive(prev: ActorState, nowMs: number): ActorState {
  const rest = {
    nowMs,
    bumperEdge: false,
    opponentPostureSagittal: 0,
  };
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

// Small accessors for HUD / tests — keeps main.ts from poking at internals.
export function handOf(actor: ActorState, side: HandSide): HandFSM {
  return side === "L" ? actor.leftHand : actor.rightHand;
}
export function footOf(actor: ActorState, side: FootSide): FootFSM {
  return side === "L" ? actor.leftFoot : actor.rightFoot;
}
