// PURE — arm_extracted flag update per docs/design/state_machines_v1.md §4.1.
//
// The flag sits on the TOP actor (the one whose arm is being extracted).
// Transition rules:
//   - attacker grips TOP's WRIST_* / SLEEVE_* with strength ≥ 0.6 AND pulls
//     in hip_pull direction for 1.5s sustained → flag := true (that side)
//   - if grip releases OR opponent base-holds to retract → flag := false
//   - auto-reset after 5s
//
// The "pull direction" is not a single axis; we interpret it as hip_push <
// 0 (bottom's pelvis moves toward camera = pulls opponent in). This matches
// §B.1 sign convention where hip_push=-1 pulls the opponent closer.

import type { HandFSM } from "./hand_fsm.js";
import type { HipIntent } from "../input/intent.js";

export type ArmSide = "L" | "R";

export type ArmExtractedState = Readonly<{
  left: boolean;
  right: boolean;
  // Sustained-pull accumulator per side, milliseconds. Resets when any
  // of the sustain conditions breaks.
  leftSustainMs: number;
  rightSustainMs: number;
  // Absolute timestamp the flag went true (used for the 5s auto-reset).
  leftSetAtMs: number;
  rightSetAtMs: number;
}>;

export const INITIAL_ARM_EXTRACTED: ArmExtractedState = Object.freeze({
  left: false,
  right: false,
  leftSustainMs: 0,
  rightSustainMs: 0,
  leftSetAtMs: Number.NEGATIVE_INFINITY,
  rightSetAtMs: Number.NEGATIVE_INFINITY,
});

export type ArmExtractedConfig = Readonly<{
  requiredSustainMs: number;    // 1500
  autoResetAfterMs: number;     // 5000
  minGripStrength: number;      // 0.6
  hipPullThreshold: number;     // hip_push ≤ -threshold
}>;

export const DEFAULT_ARM_EXTRACTED_CONFIG: ArmExtractedConfig = Object.freeze({
  requiredSustainMs: 1500,
  autoResetAfterMs: 5000,
  minGripStrength: 0.6,
  hipPullThreshold: 0.3,
});

export type ArmExtractedInputs = Readonly<{
  nowMs: number;
  dtMs: number;
  // Bottom (attacker) hands + trigger values at this tick.
  bottomLeftHand: HandFSM;
  bottomRightHand: HandFSM;
  triggerL: number;
  triggerR: number;
  attackerHip: HipIntent;
  // True if the defender's BTN_BASE is held this frame. Stage 1 passes
  // false until defender input lands; predicate is kept in the signature
  // so the transition clause is complete.
  defenderBaseHold: boolean;
}>;

export function updateArmExtracted(
  prev: ArmExtractedState,
  inp: ArmExtractedInputs,
  cfg: ArmExtractedConfig = DEFAULT_ARM_EXTRACTED_CONFIG,
): ArmExtractedState {
  // Which attacker hand is currently pulling which side of the opponent?
  // Convention: L_hand on WRIST_R / SLEEVE_R extracts the opponent's RIGHT
  // arm. Opponent's LEFT arm is extracted by grips on its L zones.
  // Playing it strictly: we keyed "side" off the zone suffix here, not
  // the attacker hand.
  const leftPulling = attackerPullingSide("L", inp, cfg);
  const rightPulling = attackerPullingSide("R", inp, cfg);

  // Sustain accumulators: grow while the pull is sustained, reset otherwise.
  let leftSustain = leftPulling ? prev.leftSustainMs + inp.dtMs : 0;
  let rightSustain = rightPulling ? prev.rightSustainMs + inp.dtMs : 0;

  // Transition to true when the sustain threshold is crossed.
  let left = prev.left;
  let right = prev.right;
  let leftSetAt = prev.leftSetAtMs;
  let rightSetAt = prev.rightSetAtMs;

  if (!left && leftSustain >= cfg.requiredSustainMs) {
    left = true;
    leftSetAt = inp.nowMs;
  }
  if (!right && rightSustain >= cfg.requiredSustainMs) {
    right = true;
    rightSetAt = inp.nowMs;
  }

  // Clear when the pull stops, the defender base-holds, or 5s elapses.
  if (left && (!leftPulling || inp.defenderBaseHold || inp.nowMs - leftSetAt >= cfg.autoResetAfterMs)) {
    left = false;
    leftSustain = 0;
    leftSetAt = Number.NEGATIVE_INFINITY;
  }
  if (right && (!rightPulling || inp.defenderBaseHold || inp.nowMs - rightSetAt >= cfg.autoResetAfterMs)) {
    right = false;
    rightSustain = 0;
    rightSetAt = Number.NEGATIVE_INFINITY;
  }

  return Object.freeze({
    left,
    right,
    leftSustainMs: leftSustain,
    rightSustainMs: rightSustain,
    leftSetAtMs: leftSetAt,
    rightSetAtMs: rightSetAt,
  });
}

function attackerPullingSide(
  opponentSide: ArmSide,
  inp: ArmExtractedInputs,
  cfg: ArmExtractedConfig,
): boolean {
  // Pull must meet both strength and hip-pull conditions.
  if (inp.attackerHip.hip_push > -cfg.hipPullThreshold) return false;

  const targetSuffix = opponentSide === "L" ? "_L" : "_R";
  // Any attacker hand GRIPPED on WRIST_?/SLEEVE_? of the opponent side.
  const lhSuffix = endsWith(inp.bottomLeftHand.target, targetSuffix);
  const rhSuffix = endsWith(inp.bottomRightHand.target, targetSuffix);
  const lh =
    inp.bottomLeftHand.state === "GRIPPED" &&
    isExtractZone(inp.bottomLeftHand.target) &&
    lhSuffix &&
    inp.triggerL >= cfg.minGripStrength;
  const rh =
    inp.bottomRightHand.state === "GRIPPED" &&
    isExtractZone(inp.bottomRightHand.target) &&
    rhSuffix &&
    inp.triggerR >= cfg.minGripStrength;
  return lh || rh;
}

function isExtractZone(zone: string | null): boolean {
  if (zone === null) return false;
  return (
    zone === "WRIST_L" || zone === "WRIST_R" ||
    zone === "SLEEVE_L" || zone === "SLEEVE_R"
  );
}

function endsWith(s: string | null, suffix: string): boolean {
  return s !== null && s.endsWith(suffix);
}
