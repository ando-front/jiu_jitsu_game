// PURE — procedural pose synthesis for the Stage 1 blockman rigs.
//
// Maps the sim's FSM states (hand / foot / posture break / stamina) plus the
// live Layer-B intent onto joint-angle targets for an articulated rig:
//
//   - Bottom player: supine closed guard — torso curled toward the opponent,
//     legs wrapped (LOCKED) or framing (UNLOCKED), arms reaching for grip
//     zones overhead.
//   - Top player: kneeling combat base — posture break crumples the torso,
//     hands post toward base zones, fatigue rounds the shoulders.
//
// All outputs are *targets*; the platform rig (blockman.ts) smooths them with
// damped springs so motion stays continuous when FSM states snap. Periodic
// motion (breathing, lock-up effort wobble) is synthesised here from `nowMs`
// — the sim's game clock — so slow-mo judgment windows slow the body too.
// Tremor amplitudes are returned separately because high-frequency jitter
// must be added *after* spring smoothing or the spring filters it out.
//
// Conventions (mirrored by blockman.ts):
//   - pitch  rotX. Negative shoulder/hip pitch raises the limb toward the
//     character's chest front; negative torso pitch curls the chest front-ward.
//   - roll   rotZ, positive = limb swings out from the body (rig applies the
//     side mirror).
//   - yaw    rotY, positive = outward (rig mirrors).
//   - elbowBend / kneeBend ≥ 0, positive = joint folds.
//   - pelvis offsets are world-axis metres relative to the rig's base spot.

import type { GripZone } from "../input/intent.js";

export type ArmPose = Readonly<{
  shoulderPitch: number;
  shoulderRoll: number;
  shoulderYaw: number;
  elbowBend: number;
  // 0..1 post-smoothing jitter amplitude (grip strain, effort).
  tremor: number;
}>;

export type LegPose = Readonly<{
  hipPitch: number;
  hipYaw: number;
  hipRoll: number;
  kneeBend: number;
}>;

export type BodyPose = Readonly<{
  pelvisX: number;
  pelvisY: number;
  pelvisZ: number;
  pelvisPitch: number;
  pelvisYaw: number;
  pelvisRoll: number;
  torsoPitch: number;
  torsoYaw: number;
  torsoRoll: number;
  torsoTremor: number;
  headPitch: number;
  headYaw: number;
  // Raw −1..1 breathing oscillator; rig maps it to chest scale. Kept out of
  // the spring path so the rhythm survives smoothing untouched.
  breath: number;
  armL: ArmPose;
  armR: ArmPose;
  legL: LegPose;
  legR: LegPose;
}>;

export type LimbSnapshot = Readonly<{ state: string; target: string | null }>;

export type BottomPoseInput = Readonly<{
  nowMs: number;
  stamina: number; // 0..1
  guard: "CLOSED" | "OPEN";
  leftHand: LimbSnapshot;
  rightHand: LimbSnapshot;
  leftFootState: string;
  rightFootState: string;
  hipAngle: number;   // Layer B hip_angle_target (radians)
  hipPush: number;    // [-1, 1]
  hipLateral: number; // [-1, 1]
  gripStrengthL: number; // [0, 1]
  gripStrengthR: number;
}>;

export type TopPoseInput = Readonly<{
  nowMs: number;
  stamina: number;
  leftHand: LimbSnapshot;
  rightHand: LimbSnapshot;
  postureBreakX: number; // [-1, 1]
  postureBreakY: number;
  weightForward: number; // [-1, 1] defender hip intent
  weightLateral: number;
  armExtractedL: boolean;
  armExtractedR: boolean;
}>;

// -----------------------------------------------------------------------------
// Reach tables — where each grip / base zone sits relative to the reaching
// character. Values are shoulder targets at the moment of CONTACT; REACHING
// extends the elbow further, GRIPPED flexes it back in (pulling).

type ReachTarget = Readonly<{ pitch: number; roll: number; yaw: number; elbow: number }>;

// Attacker (supine). Pitch −θ tilts the arm from "along the body toward the
// opponent" (θ→0) up toward "straight off the chest" (θ→π/2); the kneeling
// opponent's grips live in between, higher zones = steeper.
const ATTACK_ZONE_REACH: Readonly<Record<string, ReachTarget>> = Object.freeze({
  COLLAR_L:      { pitch: -0.85, roll: 0.10, yaw: -0.20, elbow: 0.50 },
  COLLAR_R:      { pitch: -0.85, roll: 0.10, yaw: -0.20, elbow: 0.50 },
  SLEEVE_L:      { pitch: -0.65, roll: 0.12, yaw:  0.10, elbow: 0.55 },
  SLEEVE_R:      { pitch: -0.65, roll: 0.12, yaw:  0.10, elbow: 0.55 },
  WRIST_L:       { pitch: -0.60, roll: 0.35, yaw:  0.15, elbow: 0.40 },
  WRIST_R:       { pitch: -0.60, roll: 0.35, yaw:  0.15, elbow: 0.40 },
  BELT:          { pitch: -0.40, roll: 0.05, yaw: -0.25, elbow: 0.60 },
  POSTURE_BREAK: { pitch: -0.95, roll: 0.05, yaw: -0.10, elbow: 0.85 },
});

// Defender (kneeling): the attacker is *below*, so posting pitches are
// shallow (arm angled down-forward); BICEP targets the attacker's raised
// arms and sits higher.
const DEFENSE_ZONE_REACH: Readonly<Record<string, ReachTarget>> = Object.freeze({
  CHEST:   { pitch: -0.75, roll: 0.10, yaw: -0.10, elbow: 0.30 },
  HIP:     { pitch: -0.45, roll: 0.12, yaw:  0.00, elbow: 0.35 },
  KNEE_L:  { pitch: -0.50, roll: 0.25, yaw:  0.30, elbow: 0.35 },
  KNEE_R:  { pitch: -0.50, roll: 0.25, yaw:  0.30, elbow: 0.35 },
  BICEP_L: { pitch: -1.00, roll: 0.15, yaw: -0.05, elbow: 0.35 },
  BICEP_R: { pitch: -1.00, roll: 0.15, yaw: -0.05, elbow: 0.35 },
});

// Idle guard frame for the supine attacker: elbows in, hands up like a boxer.
const ATTACK_REST: ReachTarget = Object.freeze({ pitch: -0.90, roll: 0.10, yaw: 0, elbow: 1.25 });
// Defender rest: hands posted down onto the opponent's torso below.
const DEFENSE_REST: ReachTarget = Object.freeze({ pitch: -0.70, roll: 0.14, yaw: 0, elbow: 0.35 });
// Arm knocked aside by a parry — flung wide, nearly straight.
const PARRIED_ARM: ReachTarget = Object.freeze({ pitch: -0.75, roll: 0.90, yaw: 0.30, elbow: 0.30 });
// Defender arm dragged across centre line (omoplata / triangle setups).
const EXTRACTED_ARM: ReachTarget = Object.freeze({ pitch: -1.30, roll: 0.05, yaw: -0.60, elbow: 0.50 });

const TWO_PI = Math.PI * 2;

function clamp01(v: number): number {
  return Math.max(0, Math.min(1, v));
}

function armPoseFrom(
  hand: LimbSnapshot,
  table: Readonly<Record<string, ReachTarget>>,
  rest: ReachTarget,
  gripStrength: number,
): ArmPose {
  const zone = hand.target !== null ? table[hand.target] : undefined;
  const reach = zone ?? rest;
  switch (hand.state) {
    case "REACHING":
      // Arm shoots out: elbow extends past the contact pose for a visible lunge.
      return {
        shoulderPitch: reach.pitch,
        shoulderRoll: reach.roll,
        shoulderYaw: reach.yaw,
        elbowBend: reach.elbow * 0.45,
        tremor: 0,
      };
    case "CONTACT":
      return {
        shoulderPitch: reach.pitch,
        shoulderRoll: reach.roll,
        shoulderYaw: reach.yaw,
        elbowBend: reach.elbow,
        tremor: 0.15,
      };
    case "GRIPPED":
      // Pulling on the grip: elbow flexes in, strain tremor scales with squeeze.
      return {
        shoulderPitch: reach.pitch + 0.10,
        shoulderRoll: reach.roll,
        shoulderYaw: reach.yaw,
        elbowBend: reach.elbow + 0.35,
        tremor: 0.25 + clamp01(gripStrength) * 0.45,
      };
    case "PARRIED":
      return {
        shoulderPitch: PARRIED_ARM.pitch,
        shoulderRoll: PARRIED_ARM.roll,
        shoulderYaw: PARRIED_ARM.yaw,
        elbowBend: PARRIED_ARM.elbow,
        tremor: 0,
      };
    default: // IDLE / RETRACT → back to the rest frame.
      return {
        shoulderPitch: rest.pitch,
        shoulderRoll: rest.roll,
        shoulderYaw: rest.yaw,
        elbowBend: rest.elbow,
        tremor: 0,
      };
  }
}

// -----------------------------------------------------------------------------
// Leg solver. Guard legs are authored as *direction pairs* in the supine
// body frame — where the thigh points (hip → knee) and where the shin points
// (knee → ankle) — and converted analytically into hip Euler angles (YXZ
// order, matching the rig's hip groups) plus a knee bend. This is what lets
// the closed guard genuinely wrap: the hip yaw rotates the knee's fold plane
// so the shin sweeps *around* the opponent's flank instead of folding flat.
//
// Body frame (right leg; the rig mirrors the left): +x = body right,
// +y = toward the head, +z = chest front. The opponent sits toward −y and
// "up off the mat" is +z.

type V3 = readonly [number, number, number];

function v3norm(v: V3): V3 {
  const len = Math.hypot(v[0], v[1], v[2]);
  return len < 1e-9 ? [0, -1, 0] : [v[0] / len, v[1] / len, v[2] / len];
}

function v3cross(a: V3, b: V3): V3 {
  return [
    a[1] * b[2] - a[2] * b[1],
    a[2] * b[0] - a[0] * b[2],
    a[0] * b[1] - a[1] * b[0],
  ];
}

function v3dot(a: V3, b: V3): number {
  return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

function v3lerp(a: V3, b: V3, t: number): V3 {
  return [
    a[0] + (b[0] - a[0]) * t,
    a[1] + (b[1] - a[1]) * t,
    a[2] + (b[2] - a[2]) * t,
  ];
}

const clampUnit = (v: number): number => Math.max(-1, Math.min(1, v));

// Exported for tests: given thigh direction d1 and shin direction d2 (body
// frame, right leg), produce hip YXZ Euler angles + knee bend such that the
// rig's thigh aligns with d1 and the knee fold lands the shin on d2.
export function solveLeg(d1raw: V3, d2raw: V3): LegPose {
  const d1 = v3norm(d1raw);
  const d2 = v3norm(d2raw);
  // Knee bend axis: rotating d1 toward d2 about +x_local must be a positive
  // bend, so the local x axis maps onto normalize(d1 × d2).
  const crossed = v3cross(d1, d2);
  const crossLen = Math.hypot(crossed[0], crossed[1], crossed[2]);
  let xAxis: V3;
  if (crossLen < 1e-5) {
    // Straight (or fully folded) knee — any perpendicular works; pick one
    // orthogonal to d1 biased toward the body's lateral axis.
    const fallback = v3cross(d1, [0, 0, 1] as const);
    const fbLen = Math.hypot(fallback[0], fallback[1], fallback[2]);
    xAxis = fbLen < 1e-5 ? [1, 0, 0] : v3norm(fallback);
  } else {
    xAxis = [crossed[0] / crossLen, crossed[1] / crossLen, crossed[2] / crossLen];
  }
  const yAxis: V3 = [-d1[0], -d1[1], -d1[2]]; // local +y maps to −d1 (limb hangs along −y)
  const zAxis = v3cross(xAxis, yAxis);

  // Decompose the rotation matrix [xAxis yAxis zAxis] in YXZ order — same
  // formulas as THREE.Euler.setFromRotationMatrix("YXZ").
  const m13 = zAxis[0], m23 = zAxis[1], m33 = zAxis[2];
  const m21 = xAxis[1], m22 = yAxis[1];
  const m11 = xAxis[0], m31 = xAxis[2];
  const hipPitch = Math.asin(-clampUnit(m23));
  let hipYaw: number;
  let hipRoll: number;
  if (Math.abs(m23) < 0.9999) {
    hipYaw = Math.atan2(m13, m33);
    hipRoll = Math.atan2(m21, m22);
  } else {
    hipYaw = Math.atan2(-m31, m11);
    hipRoll = 0;
  }
  const kneeBend = Math.acos(clampUnit(v3dot(d1, d2)));
  return { hipPitch, hipYaw, hipRoll, kneeBend };
}

// Forward kinematics, exported for tests: reconstruct the body-frame thigh /
// shin directions a LegPose produces on the rig (right leg).
export function legDirections(pose: LegPose): { thigh: V3; shin: V3 } {
  const rot = (v: V3): V3 => {
    // Rz (roll) → Rx (pitch) → Ry (yaw), i.e. v' = Ry·Rx·Rz·v (YXZ order).
    const [cz, sz] = [Math.cos(pose.hipRoll), Math.sin(pose.hipRoll)];
    const [cx, sx] = [Math.cos(pose.hipPitch), Math.sin(pose.hipPitch)];
    const [cy, sy] = [Math.cos(pose.hipYaw), Math.sin(pose.hipYaw)];
    let [x, y, z] = [v[0] * cz - v[1] * sz, v[0] * sz + v[1] * cz, v[2]];
    [y, z] = [y * cx - z * sx, y * sx + z * cx];
    [x, z] = [x * cy + z * sy, -x * sy + z * cy];
    return [x, y, z];
  };
  const thigh = rot([0, -1, 0]);
  const ck = Math.cos(pose.kneeBend);
  const sk = Math.sin(pose.kneeBend);
  // Knee folds about hip-local +x: (0,−1,0) → (0,−cosθ,−sinθ).
  const shin = rot([0, -ck, -sk]);
  return { thigh, shin };
}

// Direction pairs per foot state (right leg, body frame). The locked wrap
// runs the thigh up the opponent's flank and the shin across behind their
// back, so the ankles cross past the midline; unlocked frames put the foot
// on the opponent's hip; open guard widens the frame.
const LEG_DIR_LOCKED = Object.freeze({
  thigh: [0.31, -0.76, 0.56] as V3,
  shin: [-0.84, -0.53, -0.16] as V3,
});
const LEG_DIR_UNLOCKED = Object.freeze({
  thigh: [0.52, -0.69, 0.49] as V3,
  shin: [-0.75, -0.66, 0.0] as V3,
});
const LEG_DIR_OPEN = Object.freeze({
  thigh: [0.62, -0.65, 0.35] as V3,
  shin: [-0.45, -0.85, -0.2] as V3,
});

function bottomLegPose(footState: string, guard: "CLOSED" | "OPEN", nowMs: number): LegPose {
  const unlocked = guard === "OPEN" ? LEG_DIR_OPEN : LEG_DIR_UNLOCKED;
  switch (footState) {
    case "LOCKED":
      return solveLeg(LEG_DIR_LOCKED.thigh, LEG_DIR_LOCKED.shin);
    case "LOCKING": {
      // Fighting to close: oscillate between half-closed and nearly-closed
      // so the squeeze effort is visible.
      const mix = 0.6 + Math.sin((nowMs / 1000) * TWO_PI * 3.2) * 0.25;
      return solveLeg(
        v3lerp(unlocked.thigh, LEG_DIR_LOCKED.thigh, mix),
        v3lerp(unlocked.shin, LEG_DIR_LOCKED.shin, mix),
      );
    }
    default: // UNLOCKED
      return solveLeg(unlocked.thigh, unlocked.shin);
  }
}

// Zones whose pursuit visibly curls the attacker up off the mat.
const HIGH_REACH_ZONES: ReadonlySet<string> = new Set<GripZone>([
  "COLLAR_L",
  "COLLAR_R",
  "POSTURE_BREAK",
]);

function isActiveHigh(hand: LimbSnapshot): boolean {
  if (hand.target === null || !HIGH_REACH_ZONES.has(hand.target)) return false;
  return hand.state === "REACHING" || hand.state === "CONTACT" || hand.state === "GRIPPED";
}

// -----------------------------------------------------------------------------

export function computeBottomPose(input: BottomPoseInput): BodyPose {
  const fatigue = clamp01(1 - input.stamina);
  // Breathing speeds up as stamina drains (§5.4's "呼吸が重い" zone).
  const breathHz = 0.28 + fatigue * 0.55;
  const breath = Math.sin((input.nowMs / 1000) * TWO_PI * breathHz);

  // Sit-up crunch when actively attacking the collar / posture-break line.
  const sitUp = isActiveHigh(input.leftHand) || isActiveHigh(input.rightHand) ? 0.22 : 0;
  const legsLocked =
    input.leftFootState === "LOCKED" && input.rightFootState === "LOCKED";

  return {
    pelvisX: input.hipLateral * 0.20,
    pelvisY: 0.26 + (legsLocked ? 0.05 : 0) + Math.abs(input.hipPush) * 0.03,
    pelvisZ: input.hipPush * 0.30,
    // Supine: −π/2 lays the body flat with the chest facing up and (after
    // the rig's yaw flip) the head toward the camera; the small addition
    // keeps the shoulders just off the mat.
    pelvisPitch: -Math.PI / 2 + 0.15,
    pelvisYaw: input.hipAngle,
    pelvisRoll: input.hipLateral * 0.25,
    // Negative pitch curls the chest up toward the opponent.
    torsoPitch: -(0.30 + sitUp + breath * 0.04) + fatigue * 0.18,
    torsoYaw: input.hipAngle * 0.35,
    torsoRoll: input.hipLateral * 0.18,
    torsoTremor: 0,
    headPitch: -0.55 - sitUp * 0.5 + fatigue * 0.4,
    headYaw: input.hipAngle * 0.3,
    breath,
    armL: armPoseFrom(input.leftHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthL),
    armR: armPoseFrom(input.rightHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthR),
    legL: bottomLegPose(input.leftFootState, input.guard, input.nowMs),
    legR: bottomLegPose(input.rightFootState, input.guard, input.nowMs),
  };
}

export function computeTopPose(input: TopPoseInput): BodyPose {
  const fatigue = clamp01(1 - input.stamina);
  const breathHz = 0.28 + fatigue * 0.55;
  // Phase-offset so the two bodies never breathe in lockstep.
  const breath = Math.sin((input.nowMs / 1000) * TWO_PI * breathHz + Math.PI * 0.6);

  const pbX = input.postureBreakX;
  const pbY = input.postureBreakY;
  const pbMag = clamp01(Math.hypot(pbX, pbY));
  // Strain tremor while fighting a deep posture break.
  const strain = pbMag > 0.45 ? (pbMag - 0.45) * 0.9 : 0;

  const armL = input.armExtractedL
    ? {
        shoulderPitch: EXTRACTED_ARM.pitch,
        shoulderRoll: EXTRACTED_ARM.roll,
        shoulderYaw: EXTRACTED_ARM.yaw,
        elbowBend: EXTRACTED_ARM.elbow,
        tremor: 0.25,
      }
    : armPoseFrom(input.leftHand, DEFENSE_ZONE_REACH, DEFENSE_REST, 0.5);
  const armR = input.armExtractedR
    ? {
        shoulderPitch: EXTRACTED_ARM.pitch,
        shoulderRoll: EXTRACTED_ARM.roll,
        shoulderYaw: EXTRACTED_ARM.yaw,
        elbowBend: EXTRACTED_ARM.elbow,
        tremor: 0.25,
      }
    : armPoseFrom(input.rightHand, DEFENSE_ZONE_REACH, DEFENSE_REST, 0.5);

  // Kneeling combat base; weight intent rocks the hips, posture break drags
  // the whole pelvis toward the attacker.
  const kneel: LegPose = {
    hipPitch: -0.55 + input.weightForward * 0.15,
    hipYaw: 0,
    hipRoll: 0.30,
    kneeBend: 2.00,
  };

  return {
    pelvisX: pbX * 0.25 + input.weightLateral * 0.22,
    pelvisY: 0.50 - fatigue * 0.04 - pbMag * 0.06,
    pelvisZ: pbY * 0.30 + input.weightForward * 0.22,
    pelvisPitch: 0,
    pelvisYaw: 0,
    pelvisRoll: input.weightLateral * 0.10,
    // Combat-base hunch (constant −0.10), then posture break crumples the
    // torso forward / sideways and fatigue rounds it further.
    torsoPitch: -0.10 - (pbY * 0.55 + input.weightForward * 0.10) - fatigue * 0.12 - breath * 0.03,
    torsoYaw: pbX * 0.20,
    torsoRoll: pbX * 0.45,
    torsoTremor: strain,
    // Eyes stay on the opponent below; exhaustion drops the chin further.
    headPitch: 0.45 + fatigue * 0.25 + Math.max(0, pbY) * 0.20,
    headYaw: -pbX * 0.25,
    breath,
    armL,
    armR,
    legL: kneel,
    legR: kneel,
  };
}
