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

// Attacker (supine). Pitch stays inside (−π/2, 0) so the arm keeps a
// toward-the-opponent component; more negative = higher up the opponent.
const ATTACK_ZONE_REACH: Readonly<Record<string, ReachTarget>> = Object.freeze({
  COLLAR_L:      { pitch: -1.35, roll: 0.10, yaw: -0.20, elbow: 0.50 },
  COLLAR_R:      { pitch: -1.35, roll: 0.10, yaw: -0.20, elbow: 0.50 },
  SLEEVE_L:      { pitch: -1.05, roll: 0.12, yaw:  0.10, elbow: 0.55 },
  SLEEVE_R:      { pitch: -1.05, roll: 0.12, yaw:  0.10, elbow: 0.55 },
  WRIST_L:       { pitch: -0.95, roll: 0.35, yaw:  0.15, elbow: 0.40 },
  WRIST_R:       { pitch: -0.95, roll: 0.35, yaw:  0.15, elbow: 0.40 },
  BELT:          { pitch: -0.80, roll: 0.05, yaw: -0.25, elbow: 0.60 },
  POSTURE_BREAK: { pitch: -1.45, roll: 0.05, yaw: -0.10, elbow: 0.85 },
});

// Defender (kneeling, posting down onto the supine attacker).
const DEFENSE_ZONE_REACH: Readonly<Record<string, ReachTarget>> = Object.freeze({
  CHEST:   { pitch: -1.15, roll: 0.10, yaw: -0.10, elbow: 0.35 },
  HIP:     { pitch: -0.70, roll: 0.12, yaw:  0.00, elbow: 0.45 },
  KNEE_L:  { pitch: -0.50, roll: 0.25, yaw:  0.30, elbow: 0.40 },
  KNEE_R:  { pitch: -0.50, roll: 0.25, yaw:  0.30, elbow: 0.40 },
  BICEP_L: { pitch: -1.05, roll: 0.15, yaw: -0.05, elbow: 0.40 },
  BICEP_R: { pitch: -1.05, roll: 0.15, yaw: -0.05, elbow: 0.40 },
});

// Idle guard frame for the supine attacker: elbows in, hands up like a boxer.
const ATTACK_REST: ReachTarget = Object.freeze({ pitch: -0.90, roll: 0.14, yaw: 0, elbow: 1.25 });
// Defender rest: hands posted forward-down on the opponent's torso.
const DEFENSE_REST: ReachTarget = Object.freeze({ pitch: -1.00, roll: 0.16, yaw: 0, elbow: 0.45 });
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

// Closed-guard leg shapes. Thighs extend toward the opponent with a slight
// lift; LOCKED squeezes (negative roll = knees in) and folds the shins down
// behind the opponent's back, UNLOCKED frames with feet on the hips.
const LEG_LOCKED: LegPose = Object.freeze({ hipPitch: -0.60, hipRoll: -0.25, kneeBend: 1.55 });
const LEG_UNLOCKED_CLOSED: LegPose = Object.freeze({ hipPitch: -0.50, hipRoll: 0.35, kneeBend: 0.95 });
const LEG_UNLOCKED_OPEN: LegPose = Object.freeze({ hipPitch: -0.40, hipRoll: 0.45, kneeBend: 1.05 });

function bottomLegPose(footState: string, guard: "CLOSED" | "OPEN", nowMs: number): LegPose {
  const unlocked = guard === "OPEN" ? LEG_UNLOCKED_OPEN : LEG_UNLOCKED_CLOSED;
  switch (footState) {
    case "LOCKED":
      return LEG_LOCKED;
    case "LOCKING": {
      // Mid-way between open and locked, with a visible effort wobble while
      // the hook fights to close.
      const wobble = Math.sin((nowMs / 1000) * TWO_PI * 3.2) * 0.10;
      return {
        hipPitch: (LEG_LOCKED.hipPitch + unlocked.hipPitch) / 2 + wobble * 0.5,
        hipRoll: (LEG_LOCKED.hipRoll + unlocked.hipRoll) / 2,
        kneeBend: (LEG_LOCKED.kneeBend + unlocked.kneeBend) / 2 + wobble,
      };
    }
    default: // UNLOCKED
      return unlocked;
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
    headPitch: -0.42 - sitUp * 0.5 + fatigue * 0.35,
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
    // Posture break crumples the torso forward / sideways; fatigue rounds it.
    torsoPitch: -(pbY * 0.55 + input.weightForward * 0.10) - fatigue * 0.12 - breath * 0.03,
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
