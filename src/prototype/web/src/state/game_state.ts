// PURE — GameState aggregate and the top-level stepSimulation tick function.
// Reference: docs/design/architecture_overview_v1.md §7, docs/design/state_machines_v1.md §10.
//
// Evaluation order (§10):
//   1. ActorState FSMs (hands / feet)
//   2. posture_break continuous update
//   3. stamina continuous update
//   4. arm_extracted flag update
//   5. sustained counters
//   6. GuardFSM check
//   7. ControlLayer (initiative)
//   8. JudgmentWindowFSM (fires, evaluates candidates using fresh state)

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
import {
  applyConfirmCost,
  gripStrengthCeiling,
  updateStamina,
  updateStaminaDefender,
} from "./stamina.js";
import {
  INITIAL_ARM_EXTRACTED,
  updateArmExtracted,
  type ArmExtractedState,
} from "./arm_extracted.js";
import {
  INITIAL_CONTROL_LAYER,
  updateControlLayer,
  type ControlLayer,
} from "./control_layer.js";
import {
  INITIAL_COUNTER_WINDOW,
  counterCandidatesFor,
  tickCounterWindow,
  type CounterTechnique,
  type CounterTickEvent,
  type CounterWindow,
} from "./counter_window.js";
import {
  INITIAL_PASS_ATTEMPT,
  isPassEligible,
  tickPassAttempt,
  type PassAttemptState,
  type PassTickEvent,
} from "./pass_attempt.js";
import type { DefenseIntent } from "../input/intent_defense.js";

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

export type SustainedCounters = Readonly<{
  hipPushMs: number;
}>;

export const INITIAL_SUSTAINED: SustainedCounters = Object.freeze({
  hipPushMs: 0,
});

export type GameState = Readonly<{
  bottom: ActorState;
  top: ActorState;
  guard: GuardState;
  judgmentWindow: JudgmentWindow;
  counterWindow: CounterWindow;
  passAttempt: PassAttemptState;
  sessionEnded: boolean;
  // §D.2 — sign snapshot of the attacker's lateral hip input captured at
  // attacker-window OPENING. Used by Layer D_defense to resolve
  // SCISSOR_COUNTER (which needs "opposite direction").
  attackerSweepLateralSign: number;
  time: TimeContext;
  sustained: SustainedCounters;
  topArmExtracted: ArmExtractedState;
  control: ControlLayer;
  frameIndex: number;
  nowMs: number;
}>;

export function initialGameState(nowMs: number = 0): GameState {
  return Object.freeze({
    bottom: initialActorState(nowMs),
    top: initialActorState(nowMs),
    guard: "CLOSED" as const,
    judgmentWindow: INITIAL_JUDGMENT_WINDOW,
    counterWindow: INITIAL_COUNTER_WINDOW,
    passAttempt: INITIAL_PASS_ATTEMPT,
    sessionEnded: false,
    attackerSweepLateralSign: 0,
    time: INITIAL_TIME_CONTEXT,
    sustained: INITIAL_SUSTAINED,
    topArmExtracted: INITIAL_ARM_EXTRACTED,
    control: INITIAL_CONTROL_LAYER,
    frameIndex: 0,
    nowMs,
  });
}

export type SimEvent =
  | HandTickEvent
  | FootTickEvent
  | JudgmentTickEvent
  | CounterTickEvent
  | PassTickEvent
  | { kind: "GUARD_OPENED" }
  | { kind: "SESSION_ENDED"; reason: "PASS_SUCCESS" | "TECHNIQUE_FINISHED" | "GUARD_OPENED" };

export type StepOptions = Readonly<{
  realDtMs: number;
  gameDtMs: number;
  confirmedTechnique: Technique | null;
  // Optional defender intent. Stage 1 keeps this null (passive top); wiring
  // a real DefenseIntent activates recovery pressure, base holds, and the
  // defender-base-hold flag feeding arm_extracted's clear clause.
  defenseIntent?: DefenseIntent | null;
  // §D.2 — defender counter commit resolved by Layer D_defense.
  confirmedCounter?: CounterTechnique | null;
}>;

export function stepSimulation(
  prev: GameState,
  frame: InputFrame,
  intent: Intent,
  opts: StepOptions = { realDtMs: 0, gameDtMs: 0, confirmedTechnique: null },
): { nextState: GameState; events: readonly SimEvent[] } {
  const events: SimEvent[] = [];
  const nowMs = frame.timestamp;
  const defense = opts.defenseIntent ?? null;

  // §5.3 — clamp trigger values by the stamina ceiling BEFORE FSMs read
  // them. This enforces "cross-collar at strength ≥ 0.7 becomes impossible
  // below stamina 0.2" naturally.
  const bottomCeiling = gripStrengthCeiling(prev.bottom.stamina);
  const effectiveTriggerL = Math.min(frame.l_trigger, bottomCeiling);
  const effectiveTriggerR = Math.min(frame.r_trigger, bottomCeiling);

  // 1. ActorState FSMs.
  const nextBottomFsm = tickBottomActor(prev.bottom, prev.top, frame, intent, nowMs, events, effectiveTriggerL, effectiveTriggerR);
  const nextTopFsm = tickTopActorPassive(prev.top, nowMs);

  // 2. posture_break. Bottom's inputs drive the TOP actor's break.
  const gripPulls: Vec2[] = [];
  if (nextBottomFsm.leftHand.state === "GRIPPED" && nextBottomFsm.leftHand.target !== null) {
    gripPulls.push(gripPullVector(nextBottomFsm.leftHand.target, effectiveTriggerL));
  }
  if (nextBottomFsm.rightHand.state === "GRIPPED" && nextBottomFsm.rightHand.target !== null) {
    gripPulls.push(gripPullVector(nextBottomFsm.rightHand.target, effectiveTriggerR));
  }
  const defenderRecovery = computeDefenderRecovery(defense);
  const nextTopPosture = updatePostureBreak(
    prev.top.postureBreak,
    {
      dtMs: opts.gameDtMs,
      attackerHip: intent.hip,
      gripPulls,
      defenderRecovery,
    },
  );

  // 3. arm_extracted (sits on the TOP actor — that's whose arm gets pulled).
  const nextArmExtracted = updateArmExtracted(prev.topArmExtracted, {
    nowMs,
    dtMs: opts.gameDtMs,
    bottomLeftHand: nextBottomFsm.leftHand,
    bottomRightHand: nextBottomFsm.rightHand,
    triggerL: effectiveTriggerL,
    triggerR: effectiveTriggerR,
    attackerHip: intent.hip,
    defenderBaseHold: defenderIsBasingBicep(defense),
  });
  const nextTop: ActorState = Object.freeze({
    ...nextTopFsm,
    postureBreak: nextTopPosture,
    armExtractedLeft: nextArmExtracted.left,
    armExtractedRight: nextArmExtracted.right,
  });

  // 4. stamina — bottom driven by attacker input, top driven (if defender
  // intent is provided) by base pressure + weight. Without defense intent
  // the top stamina sits on a no-op tick (no drain, no recovery), which
  // is the correct "passive opponent" behaviour for Stage 1.
  const breathPressed = (frame.buttons & ButtonBit.BTN_BREATH) !== 0;
  const nextBottomStamina = updateStamina(prev.bottom.stamina, {
    dtMs: opts.gameDtMs,
    actor: nextBottomFsm,
    attackerHip: intent.hip,
    triggerL: effectiveTriggerL,
    triggerR: effectiveTriggerR,
    breathPressed,
  });
  const nextTopStamina = defense !== null
    ? updateStaminaDefender(prev.top.stamina, {
        dtMs: opts.gameDtMs,
        actor: nextTop,
        leftBasePressure: defense.base.l_base_pressure,
        rightBasePressure: defense.base.r_base_pressure,
        weightForward: defense.hip.weight_forward,
        weightLateral: defense.hip.weight_lateral,
        breathPressed: defense.discrete.some((d) => d.kind === "BREATH_START"),
      })
    : prev.top.stamina;

  // 5. Sustained counters (hip_bump's 300ms rolling push).
  const pushActive = intent.hip.hip_push >= 0.5;
  const nextSustained: SustainedCounters = Object.freeze({
    hipPushMs: pushActive ? prev.sustained.hipPushMs + opts.gameDtMs : 0,
  });

  // 6. GuardFSM — one-way CLOSED → OPEN when both feet are UNLOCKED.
  let nextGuard = prev.guard;
  if (
    prev.guard === "CLOSED" &&
    nextBottomFsm.leftFoot.state === "UNLOCKED" &&
    nextBottomFsm.rightFoot.state === "UNLOCKED"
  ) {
    nextGuard = "OPEN";
    events.push({ kind: "GUARD_OPENED" });
  }

  const nextBottom: ActorState = Object.freeze({
    ...nextBottomFsm,
    stamina: nextBottomStamina,
  });

  // 7. JudgmentWindowFSM.
  const ctx: JudgmentContext = {
    bottom: nextBottom,
    top: nextTop,
    bottomHipYaw: intent.hip.hip_angle_target,
    bottomHipPush: intent.hip.hip_push,
    sustainedHipPushMs: nextSustained.hipPushMs,
  };
  const satisfied = evaluateAllTechniques(ctx, effectiveTriggerL, effectiveTriggerR);
  const dismissRequested = (frame.button_edges & ButtonBit.BTN_RELEASE) !== 0;
  const winResult = tickJudgmentWindow(
    prev.judgmentWindow,
    satisfied,
    { nowMs, confirmedTechnique: opts.confirmedTechnique, dismissRequested },
  );
  for (const e of winResult.events) events.push(e);

  // 7b. Counter window (§D of input_system_defense_v1.md).
  // Seed OPENING only on the frame attacker transitions to OPENING (same
  // frame). We detect that via the WINDOW_OPENING event we just pushed.
  const attackerOpeningThisTick = winResult.events.find(
    (e): e is Extract<JudgmentTickEvent, { kind: "WINDOW_OPENING" }> =>
      e.kind === "WINDOW_OPENING",
  );
  const openingSeed = attackerOpeningThisTick
    ? counterCandidatesFor(attackerOpeningThisTick.candidates)
    : [];
  const attackerWindowActive =
    winResult.next.state === "OPENING" || winResult.next.state === "OPEN";

  const counterResult = tickCounterWindow(prev.counterWindow, {
    nowMs,
    openAttackerWindow: attackerWindowActive,
    openingSeed,
    confirmedCounter: opts.confirmedCounter ?? null,
    dismissRequested,
  });
  for (const e of counterResult.events) events.push(e);

  // Snapshot the attacker's lateral hip sign at attacker OPENING so
  // Layer D_defense can evaluate SCISSOR_COUNTER this tick.
  const attackerSweepLateralSign = attackerOpeningThisTick
    ? (intent.hip.hip_lateral > 0 ? 1 : intent.hip.hip_lateral < 0 ? -1 : 0)
    : prev.attackerSweepLateralSign;

  // Counter success side-effects (§D.2).
  let finalJudgmentWindow = winResult.next;
  let armExtractedAfterCounter = nextArmExtracted;
  const counterConfirmed = counterResult.events.find(
    (e): e is Extract<CounterTickEvent, { kind: "COUNTER_CONFIRMED" }> =>
      e.kind === "COUNTER_CONFIRMED",
  );
  if (counterConfirmed) {
    // Force the attacker's window into CLOSING — the attack is disrupted.
    // We mirror the enterClosing shape used inside tickJudgmentWindow,
    // including freshing the cooldownUntilMs when it reaches CLOSED on a
    // later tick. Here we just flip the state flag; its own FSM will
    // drive the rest of the lifecycle.
    if (finalJudgmentWindow.state === "OPEN" || finalJudgmentWindow.state === "OPENING") {
      finalJudgmentWindow = Object.freeze({
        ...finalJudgmentWindow,
        state: "CLOSING" as const,
        stateEnteredMs: nowMs,
      });
    }
    if (counterConfirmed.counter === "TRIANGLE_EARLY_STACK") {
      // §D.2 — triangle counter resets arm_extracted both sides to false.
      armExtractedAfterCounter = Object.freeze({
        ...nextArmExtracted,
        left: false,
        right: false,
        leftSustainMs: 0,
        rightSustainMs: 0,
        leftSetAtMs: Number.NEGATIVE_INFINITY,
        rightSetAtMs: Number.NEGATIVE_INFINITY,
      });
    }
  }

  // 5.2 last row — confirming a technique deducts a flat stamina cost.
  const confirmedThisTick = winResult.events.some((e) => e.kind === "TECHNIQUE_CONFIRMED");
  const bottomAfterConfirm: ActorState = confirmedThisTick
    ? Object.freeze({ ...nextBottom, stamina: applyConfirmCost(nextBottom.stamina) })
    : nextBottom;

  // Rebuild top with possibly-cleared arm_extracted after counter + new
  // stamina. Counter confirmation also costs defender stamina, symmetric
  // with attacker's technique confirm.
  const counterConfirmedThisTick = counterResult.events.some(
    (e) => e.kind === "COUNTER_CONFIRMED",
  );
  const topStaminaFinal = counterConfirmedThisTick
    ? applyConfirmCost(nextTopStamina)
    : nextTopStamina;
  const topAfterCounter: ActorState = Object.freeze({
    ...nextTop,
    armExtractedLeft: armExtractedAfterCounter.left,
    armExtractedRight: armExtractedAfterCounter.right,
    stamina: topStaminaFinal,
  });

  // Time scale: if either window is producing slow-mo, pick the most
  // aggressive (lowest) scale, per §D.3 "slow-mo doesn't double up".
  const combinedScale = Math.min(winResult.timeScale, counterResult.timeScale);
  const nextTime: TimeContext = Object.freeze({
    scale: combinedScale,
    realDtMs: opts.realDtMs,
    gameDtMs: opts.gameDtMs,
  });

  // 8. ControlLayer (presentation-only, see §7.3).
  const nextControl = updateControlLayer(prev.control, {
    judgmentWindow: finalJudgmentWindow,
    bottom: bottomAfterConfirm,
    top: topAfterCounter,
    defenderCutInProgress: false,
  });

  // 9. Pass attempt (§B.7). Commit eligibility depends on the FRESH actor
  // states we just computed. A commit-requested-but-ineligible intent
  // fires nothing (per §B.7.1 "演出のみ") — the PassTickEvent stream
  // stays silent unless the pass actually starts.
  const passCommitRequested = defense !== null
    && defense.discrete.some((d) => d.kind === "PASS_COMMIT");
  const passEligibleNow = defense !== null
    ? isPassEligible({
        bottom: bottomAfterConfirm,
        top: topAfterCounter,
        defenderStamina: topAfterCounter.stamina,
        leftBasePressure: defense.base.l_base_pressure,
        rightBasePressure: defense.base.r_base_pressure,
        leftBaseZone: defense.base.l_hand_target,
        rightBaseZone: defense.base.r_hand_target,
        rsY: frame.rs.y,
        guard: nextGuard,
      })
    : false;
  const triangleConfirmedThisTick = winResult.events.some(
    (e) => e.kind === "TECHNIQUE_CONFIRMED" && "technique" in e && e.technique === "TRIANGLE",
  );
  const passResult = tickPassAttempt(prev.passAttempt, {
    nowMs,
    commitRequested: passCommitRequested,
    eligibleNow: passEligibleNow,
    attackerTriangleConfirmedThisTick: triangleConfirmedThisTick,
  });
  for (const e of passResult.events) events.push(e);

  // Session termination (§6 M1 scope: PASS_SUCCESS / guard open / technique
  // confirmed all end the session on a placeholder).
  let sessionEnded = prev.sessionEnded;
  if (!sessionEnded) {
    if (passResult.events.some((e) => e.kind === "PASS_SUCCEEDED")) {
      sessionEnded = true;
      events.push({ kind: "SESSION_ENDED", reason: "PASS_SUCCESS" });
    } else if (confirmedThisTick) {
      sessionEnded = true;
      events.push({ kind: "SESSION_ENDED", reason: "TECHNIQUE_FINISHED" });
    } else if (nextGuard === "OPEN" && prev.guard === "CLOSED") {
      sessionEnded = true;
      events.push({ kind: "SESSION_ENDED", reason: "GUARD_OPENED" });
    }
  }

  const nextState: GameState = Object.freeze({
    bottom: bottomAfterConfirm,
    top: topAfterCounter,
    guard: nextGuard,
    judgmentWindow: finalJudgmentWindow,
    counterWindow: counterResult.next,
    passAttempt: passResult.next,
    sessionEnded,
    attackerSweepLateralSign,
    time: nextTime,
    sustained: nextSustained,
    topArmExtracted: armExtractedAfterCounter,
    control: nextControl,
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
  effectiveTriggerL: number,
  effectiveTriggerR: number,
): ActorState {
  const forceReleaseAll = (frame.button_edges & ButtonBit.BTN_RELEASE) !== 0;

  const leftResult = tickHand(prev.leftHand, {
    nowMs,
    triggerValue: effectiveTriggerL,
    targetZone: intent.grip.l_hand_target,
    forceReleaseAll,
    opponentDefendsThisZone: false,
    opponentCutSucceeded: false,
    targetOutOfReach: false,
  });
  pushAll(eventsOut, leftResult.events);

  const rightResult = tickHand(prev.rightHand, {
    nowMs,
    triggerValue: effectiveTriggerR,
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

// Defender contributions to continuous updates. Stage 1 keeps these
// intentionally narrow: recovery shoves posture_break toward origin when
// weight_forward is set, and CHEST/HIP base pressure reinforces it.
// BICEP pressure is the one signal that feeds arm_extracted's clear
// clause (§4.1 "BTN_BASE hold retracts the arm").
function computeDefenderRecovery(defense: DefenseIntent | null): Vec2 {
  if (defense === null) return ZERO_VEC2;
  // Weight-forward reads as the defender "pushing back" on the attacker's
  // sagittal break. Weight-lateral mirrors on the lateral axis.
  let x = defense.hip.weight_lateral;
  let y = defense.hip.weight_forward;

  // CHEST or HIP base pressure contributes additively to sagittal recovery.
  if (defense.base.l_hand_target === "CHEST" || defense.base.l_hand_target === "HIP") {
    y += defense.base.l_base_pressure * 0.5;
  }
  if (defense.base.r_hand_target === "CHEST" || defense.base.r_hand_target === "HIP") {
    y += defense.base.r_base_pressure * 0.5;
  }

  // Clamp to keep the recovery vector in a sane range.
  const mag = Math.hypot(x, y);
  if (mag > 1) {
    const s = 1 / mag;
    x *= s;
    y *= s;
  }
  return Object.freeze({ x, y });
}

function defenderIsBasingBicep(defense: DefenseIntent | null): boolean {
  if (defense === null) return false;
  const lBicep =
    (defense.base.l_hand_target === "BICEP_L" || defense.base.l_hand_target === "BICEP_R") &&
    defense.base.l_base_pressure >= 0.6;
  const rBicep =
    (defense.base.r_hand_target === "BICEP_L" || defense.base.r_hand_target === "BICEP_R") &&
    defense.base.r_base_pressure >= 0.6;
  // Also consider the RECOVERY_HOLD discrete intent (§B.6.1) as a general
  // "defender is actively retracting" signal that qualifies for clearing
  // arm_extracted.
  const recoveryHold = defense.discrete.some((d) => d.kind === "RECOVERY_HOLD");
  return lBicep || rBicep || recoveryHold;
}

export function handOf(actor: ActorState, side: HandSide): HandFSM {
  return side === "L" ? actor.leftHand : actor.rightHand;
}
export function footOf(actor: ActorState, side: FootSide): FootFSM {
  return side === "L" ? actor.leftFoot : actor.rightFoot;
}

export { TIME_SCALE };
