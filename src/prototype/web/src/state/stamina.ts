// PURE — stamina update per docs/design/state_machines_v1.md §5.
//
// Model: single scalar [0,1]. Per-second rates sum, then integrate over dt.
// Consumption and recovery rates are design-constant; §5.4 notes that M1
// implements consumption + recovery + threshold clamps but not visuals.

import { breakMagnitude } from "./posture_break.js";
import type { ActorState } from "./game_state.js";
import type { HipIntent } from "../input/intent.js";

export type StaminaConfig = Readonly<{
  // §5.2
  handActiveDrainPerSec: number;     // any hand REACHING / GRIPPED(≥0.5)
  postureMaintainDrainPerSec: number; // × ‖posture_break‖
  confirmDrainFlat: number;           // applied once per confirm via applyConfirmCost
  breathRecoverPerSec: number;        // BTN_BREATH + no grips + static
  idleRecoverPerSec: number;          // all IDLE, no grips

  hipInputStaticThreshold: number;    // §5.2 "hip_input < 0.2"

  // §5.3
  lowGripCap: number;                  // grip strength clamp at low stamina
  lowGripCapThreshold: number;         // stamina level below which the cap activates
  noReachThreshold: number;            // stamina level below which REACHING is refused
}>;

export const DEFAULT_STAMINA_CONFIG: StaminaConfig = Object.freeze({
  handActiveDrainPerSec: 0.02,
  postureMaintainDrainPerSec: 0.05,
  confirmDrainFlat: 0.1,
  breathRecoverPerSec: 0.1,
  idleRecoverPerSec: 0.03,
  hipInputStaticThreshold: 0.2,
  lowGripCap: 0.6,
  lowGripCapThreshold: 0.2,
  noReachThreshold: 0.05,
});

export type StaminaInputs = Readonly<{
  dtMs: number;
  actor: ActorState;
  attackerHip: HipIntent;
  triggerL: number;
  triggerR: number;
  breathPressed: boolean;   // BTN_BREATH held or edge this frame (treat as "trying to breathe")
}>;

// Classification helpers ----------------------------------------------------

function handIsActive(handState: string, triggerValue: number): boolean {
  if (handState === "REACHING") return true;
  if (handState === "GRIPPED" && triggerValue >= 0.5) return true;
  return false;
}

function anyHandGripped(actor: ActorState): boolean {
  return actor.leftHand.state === "GRIPPED" || actor.rightHand.state === "GRIPPED";
}

function allLimbsIdle(actor: ActorState): boolean {
  return (
    actor.leftHand.state === "IDLE" &&
    actor.rightHand.state === "IDLE" &&
    actor.leftFoot.state === "LOCKED" &&
    actor.rightFoot.state === "LOCKED"
  );
}

// -----------------------------------------------------------------------------

export function updateStamina(
  prev: number,
  inputs: StaminaInputs,
  cfg: StaminaConfig = DEFAULT_STAMINA_CONFIG,
): number {
  const dtSec = inputs.dtMs / 1000;
  let ratePerSec = 0;

  // Drain — hands active.
  if (handIsActive(inputs.actor.leftHand.state, inputs.triggerL) ||
      handIsActive(inputs.actor.rightHand.state, inputs.triggerR)) {
    ratePerSec -= cfg.handActiveDrainPerSec;
  }

  // Drain — sustained posture break (scaled by magnitude).
  // §5.2 phrases this as "攻め側" so we read the TOP actor's break from
  // the caller's `actor` only if this stamina update is for the top;
  // bottom actor's own break is what drains THEM. Stage 1 drives only
  // bottom stamina right now, so we read bottom.postureBreak — which is
  // zero until top-side wiring lands. The code is future-proof for the
  // day defender stamina is wired.
  const breakMag = breakMagnitude(inputs.actor.postureBreak);
  if (breakMag > 0) {
    ratePerSec -= cfg.postureMaintainDrainPerSec * breakMag;
  }

  // Recovery — BTN_BREATH + no grips + static hip.
  const hipMag = Math.hypot(inputs.attackerHip.hip_push, inputs.attackerHip.hip_lateral);
  const staticHip = hipMag < cfg.hipInputStaticThreshold;
  if (inputs.breathPressed && !anyHandGripped(inputs.actor) && staticHip) {
    ratePerSec += cfg.breathRecoverPerSec;
  } else if (allLimbsIdle(inputs.actor) && !anyHandGripped(inputs.actor)) {
    // Idle recovery only applies when nothing is happening. Breath
    // recovery takes priority because it's explicit player intent.
    ratePerSec += cfg.idleRecoverPerSec;
  }

  const next = prev + ratePerSec * dtSec;
  return clamp01(next);
}

// §5.2 last row — confirming a technique deducts a flat cost.
export function applyConfirmCost(
  prev: number,
  cfg: StaminaConfig = DEFAULT_STAMINA_CONFIG,
): number {
  return clamp01(prev - cfg.confirmDrainFlat);
}

// §5.3 — clamp grip strength ceiling at low stamina. Returns a ceiling in
// [0,1] that downstream should apply as `min(raw_trigger, ceiling)`.
export function gripStrengthCeiling(
  stamina: number,
  cfg: StaminaConfig = DEFAULT_STAMINA_CONFIG,
): number {
  if (stamina < cfg.lowGripCapThreshold) return cfg.lowGripCap;
  return 1;
}

// §5.3 — below this stamina, new REACHING is refused.
export function canStartReach(
  stamina: number,
  cfg: StaminaConfig = DEFAULT_STAMINA_CONFIG,
): boolean {
  return stamina >= cfg.noReachThreshold;
}

function clamp01(x: number): number {
  if (x < 0) return 0;
  if (x > 1) return 1;
  return x;
}
