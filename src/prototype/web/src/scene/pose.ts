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
//   - pitch  rotX. Limbs hang along −y, so *negative* shoulder/hip pitch
//     swings them toward the chest front; torso and head extend along +y,
//     so *positive* torso/head pitch curls them toward the chest front.
//   - roll   rotZ, positive = limb swings out from the body (rig applies the
//     side mirror).
//   - yaw    rotY, positive = outward (rig mirrors).
//   - elbowBend / kneeBend ≥ 0, positive = joint folds.
//   - pelvis offsets are world-axis metres relative to the rig's base spot.

import type { GripZone } from "../input/intent.js";
import type { Technique } from "../state/judgment_window.js";
import type { CounterTechnique } from "../state/counter_window.js";

export type ArmPose = Readonly<{
  shoulderPitch: number;
  shoulderRoll: number;
  shoulderYaw: number;
  elbowBend: number;
  // 0..1 post-smoothing jitter amplitude (grip strain, effort).
  tremor: number;
  // 0 = open splayed hand, 1 = clenched fist. Omitted ≈ relaxed (0.25).
  grip?: number;
}>;

export type LegPose = Readonly<{
  hipPitch: number;
  hipYaw: number;
  hipRoll: number;
  kneeBend: number;
  // Ankle flex: + = plantarflexed (toes point, hooking behind the back),
  // − = dorsiflexed (ball of the foot framing on the hip). Omitted ≈ 0.
  ankle?: number;
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

// sinceMs = time spent in the current state; omitted means "long settled".
export type LimbSnapshot = Readonly<{ state: string; target: string | null; sinceMs?: number }>;

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
  // Judgment window OPEN/OPENING — the attacker coils, loading the hips.
  windowOpen: boolean;
  // Leading judgment-window candidate (or null). When the window is open,
  // the attacker loads the *specific* entry for this technique — hips climb
  // for a triangle, turn out for an omoplata, sit up for a hip bump, etc.
  windowTechnique?: Technique | null;
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
  // ms since the pass attempt started, or null when idle. Drives the
  // forward passing pressure (lead knee up, torso low).
  passElapsedMs: number | null;
  // ms since each hand's grip-cut attempt started, or null. Drives the
  // windup → strike → recover chop on that arm.
  cutElapsedLMs: number | null;
  cutElapsedRMs: number | null;
  // Counter window OPEN/OPENING — the defender braces upright.
  counterWindowOpen: boolean;
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
const ATTACK_REST: ReachTarget = Object.freeze({ pitch: -0.70, roll: 0.14, yaw: 0.05, elbow: 0.80 });
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

// Two-octave breathing oscillator in [-1, 1] — a dominant breath rhythm plus
// a slower harmonic, so the depth wanders instead of looping perfectly.
// `phase` lets the two bodies breathe out of sync.
function breathOscillator(nowMs: number, hz: number, phase = 0): number {
  const t = nowMs / 1000;
  return 0.82 * Math.sin(t * TWO_PI * hz + phase) +
    0.18 * Math.sin(t * TWO_PI * hz * 0.5 + phase * 1.7);
}

// Idle micro-sway: three incommensurate sines so the re-balancing never
// repeats on a short loop. `phaseSet` 0 = bottom-flavoured, 1 = top-flavoured.
function idleSway(nowMs: number, fatigue: number, phaseSet: number): number {
  const t = nowMs / 1000;
  const amp = 1 + fatigue * 1.2;
  const p = phaseSet === 0 ? [1.3, 0.0, 5.5] : [3.1, 0.7, 2.3];
  return (
    Math.sin(t * TWO_PI * 0.31 + p[0]!) * 0.5 +
    Math.sin(t * TWO_PI * 0.47 + p[1]!) * 0.32 +
    Math.sin(t * TWO_PI * 0.17 + p[2]!) * 0.18
  ) * amp;
}

function idleSway2(nowMs: number, fatigue: number, phaseSet: number): number {
  const t = nowMs / 1000;
  const amp = 1 + fatigue * 1.2;
  const base = phaseSet === 0 ? 4.0 : 1.9;
  return (
    Math.sin(t * TWO_PI * 0.23 + base) * 0.7 +
    Math.sin(t * TWO_PI * 0.13 + base * 0.6) * 0.3
  ) * amp;
}

function handBusyState(h: LimbSnapshot): boolean {
  return h.state === "REACHING" || h.state === "CONTACT" || h.state === "GRIPPED";
}

// Deterministic [0,1) hash — same action-start time always yields the same
// "style", but different occurrences differ. Cheap fract(sin) noise.
function hash01(x: number): number {
  const s = Math.sin(x * 0.0173 + 1.0) * 43758.5453;
  return s - Math.floor(s);
}

function armPoseFrom(
  hand: LimbSnapshot,
  table: Readonly<Record<string, ReachTarget>>,
  rest: ReachTarget,
  gripStrength: number,
  nowMs: number,
): ArmPose {
  const zone = hand.target !== null ? table[hand.target] : undefined;
  const reach = zone ?? rest;
  const sinceMs = hand.sinceMs ?? 1000;
  // Per-action style seed, stable for this state instance. Two unit-centred
  // variations in [-1, 1] so each reach/grip looks a little different.
  const startMs = nowMs - sinceMs;
  const styleA = hash01(startMs) * 2 - 1;
  const styleB = hash01(startMs + 137) * 2 - 1;
  switch (hand.state) {
    case "REACHING": {
      // Anticipation: the first beat of a reach pulls *back* (elbow coils,
      // shoulder loads past the rest frame); the spring then whips the arm
      // out to the extended lunge target. Windup length varies per attempt.
      const windupMs = 110 + styleA * 25; // 85–135 ms
      if (sinceMs < windupMs) {
        return {
          shoulderPitch: rest.pitch + 0.20,
          shoulderRoll: rest.roll + 0.10,
          shoulderYaw: rest.yaw,
          elbowBend: rest.elbow + 0.18,
          tremor: 0,
          grip: 0.1, // hand opens, anticipating the grab
        };
      }
      // Arm shoots out: elbow extends past the contact pose for a visible
      // lunge. Roll/yaw/extension jitter so no two reaches trace one line.
      return {
        shoulderPitch: reach.pitch + styleB * 0.05,
        shoulderRoll: reach.roll + styleA * 0.08,
        shoulderYaw: reach.yaw + styleB * 0.10,
        elbowBend: reach.elbow * (0.45 + styleA * 0.12),
        tremor: 0,
        grip: 0.0, // splayed open, ready to clamp
      };
    }
    case "CONTACT":
      return {
        shoulderPitch: reach.pitch,
        shoulderRoll: reach.roll + styleA * 0.05,
        shoulderYaw: reach.yaw + styleB * 0.06,
        elbowBend: reach.elbow,
        tremor: 0.15,
        grip: 0.55, // fingers wrapping
      };
    case "GRIPPED": {
      // Pulling on the grip: elbow flexes in, strain tremor scales with
      // squeeze. A slow re-grip "pump" keeps the held grip alive — the
      // constant micro-adjustment of fighting for the pull. Phase + rate
      // vary per grip instance.
      const pumpHz = 0.7 + (styleB * 0.5 + 0.5) * 0.6; // 0.7–1.3 Hz
      const pump = Math.sin((nowMs / 1000) * TWO_PI * pumpHz + styleA * Math.PI);
      return {
        shoulderPitch: reach.pitch + 0.10,
        shoulderRoll: reach.roll,
        shoulderYaw: reach.yaw,
        elbowBend: reach.elbow + 0.35 + pump * 0.07,
        tremor: 0.25 + clamp01(gripStrength) * 0.45,
        grip: 0.85 + clamp01(gripStrength) * 0.15, // clenched fist
      };
    }
    case "PARRIED":
      return {
        shoulderPitch: PARRIED_ARM.pitch,
        shoulderRoll: PARRIED_ARM.roll,
        shoulderYaw: PARRIED_ARM.yaw,
        elbowBend: PARRIED_ARM.elbow,
        tremor: 0,
        grip: 0.1, // knocked open
      };
    default: // IDLE / RETRACT → back to the rest frame.
      return {
        shoulderPitch: rest.pitch,
        shoulderRoll: rest.roll,
        shoulderYaw: rest.yaw,
        elbowBend: rest.elbow,
        tremor: 0,
        grip: 0.3, // relaxed
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
  shin: [-0.78, -0.62, -0.10] as V3,
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
      // Feet hooked behind the back → toes pointed hard (plantarflexed).
      return { ...solveLeg(LEG_DIR_LOCKED.thigh, LEG_DIR_LOCKED.shin), ankle: 0.55 };
    case "LOCKING": {
      // Fighting to close: oscillate between half-closed and nearly-closed
      // so the squeeze effort is visible.
      const mix = 0.6 + Math.sin((nowMs / 1000) * TWO_PI * 3.2) * 0.25;
      return {
        ...solveLeg(
          v3lerp(unlocked.thigh, LEG_DIR_LOCKED.thigh, mix),
          v3lerp(unlocked.shin, LEG_DIR_LOCKED.shin, mix),
        ),
        ankle: 0.2 + mix * 0.3,
      };
    }
    default: // UNLOCKED — ball of the foot framing on the hip → toes up.
      return { ...solveLeg(unlocked.thigh, unlocked.shin), ankle: -0.35 };
  }
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function lerpArm(a: ArmPose, b: ArmPose, t: number): ArmPose {
  return {
    shoulderPitch: lerp(a.shoulderPitch, b.shoulderPitch, t),
    shoulderRoll: lerp(a.shoulderRoll, b.shoulderRoll, t),
    shoulderYaw: lerp(a.shoulderYaw, b.shoulderYaw, t),
    elbowBend: lerp(a.elbowBend, b.elbowBend, t),
    tremor: lerp(a.tremor, b.tremor, t),
    grip: lerp(a.grip ?? 0.25, b.grip ?? 0.25, t),
  };
}

function lerpLeg(a: LegPose, b: LegPose, t: number): LegPose {
  return {
    hipPitch: lerp(a.hipPitch, b.hipPitch, t),
    hipYaw: lerp(a.hipYaw, b.hipYaw, t),
    hipRoll: lerp(a.hipRoll, b.hipRoll, t),
    kneeBend: lerp(a.kneeBend, b.kneeBend, t),
    ankle: lerp(a.ankle ?? 0, b.ankle ?? 0, t),
  };
}

const mkArm = (
  pitch: number,
  roll: number,
  yaw: number,
  elbow: number,
  tremor = 0,
  grip = 0.25,
): ArmPose => ({
  shoulderPitch: pitch,
  shoulderRoll: roll,
  shoulderYaw: yaw,
  elbowBend: elbow,
  tremor,
  grip,
});

// Grip-cut chop (defender, §4.2): raise out, swat across the centre line,
// recover. Timed against CUT_TIMING.attemptMs = 1500.
const CUT_WINDUP = mkArm(-1.25, 0.55, 0.2, 0.5, 0.1);
const CUT_STRIKE = mkArm(-0.5, 0.05, -0.45, 0.2, 0);

function cutChopArm(base: ArmPose, elapsedMs: number): ArmPose {
  if (elapsedMs < 280) return lerpArm(base, CUT_WINDUP, elapsedMs / 280);
  if (elapsedMs < 620) return lerpArm(CUT_WINDUP, CUT_STRIKE, (elapsedMs - 280) / 340);
  return lerpArm(CUT_STRIKE, base, clamp01((elapsedMs - 620) / 880));
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

// Technique-specific entry "loading" the attacker shows while the judgment
// window is open — a partial pre-shape of the finish, smoothed by the rig
// springs. Deltas are added on top of the live pose; legs are full overrides
// where the entry redefines the leg shape (returned via legL/legR), else
// null to keep the FSM-driven guard legs.
type WindowEntry = Readonly<{
  pelvisY?: number;
  pelvisRoll?: number;
  pelvisYaw?: number;
  torsoPitch?: number;
  torsoRoll?: number;
  legL?: LegPose;
  legR?: LegPose;
  armL?: ArmPose;
  armR?: ArmPose;
}>;

function windowEntry(tech: Technique): WindowEntry {
  switch (tech) {
    case "TRIANGLE":
      // Hips climb, the near leg rides up toward the shoulder line, hands
      // reach to frame the head.
      return {
        pelvisY: 0.06,
        torsoPitch: 0.18,
        // Near leg climbs up over the shoulder line: thigh & shin both lift
        // toward the chest-front (+z) instead of wrapping low behind.
        legL: { ...solveLeg([0.22, -0.45, 0.86], [-0.78, -0.30, 0.08]), ankle: 0.4 },
        armL: mkArm(-0.65, 0.12, -0.14, 0.85, 0.3, 0.6),
        armR: mkArm(-0.65, 0.12, -0.14, 0.85, 0.3, 0.6),
      };
    case "OMOPLATA":
      // Hips turn out, the far leg starts swinging over the shoulder.
      return {
        pelvisYaw: 0.45,
        torsoPitch: 0.10,
        legL: { ...solveLeg([-0.20, -0.55, 0.78], [-0.55, -0.62, -0.30]), ankle: 0.3 },
      };
    case "HIP_BUMP":
      // Post a hand back and start sitting up hard.
      return {
        pelvisY: 0.08,
        torsoPitch: 0.55,
        armR: mkArm(0.45, 0.30, 0, 0.20, 0, 0.2), // posting hand behind
        armL: mkArm(-0.90, 0.25, -0.10, 0.45, 0.2, 0.7),
      };
    case "CROSS_COLLAR":
      // Both hands drive deep to the collar, chest curls in.
      return {
        torsoPitch: 0.22,
        armL: mkArm(-0.80, 0.06, -0.45, 0.70, 0.3, 0.85),
        armR: mkArm(-0.80, 0.06, -0.45, 0.70, 0.3, 0.85),
      };
    case "SCISSOR_SWEEP":
      // Load to one side: knee across, hips cocked, sleeve pull.
      return {
        pelvisRoll: 0.18,
        torsoRoll: 0.14,
        legR: { ...solveLeg([0.55, -0.60, 0.45], [-0.30, -0.80, -0.20]), ankle: -0.1 },
      };
    case "FLOWER_SWEEP":
      // Hips swing under, far arm reaches across for the leg.
      return {
        pelvisRoll: -0.18,
        torsoRoll: -0.14,
        armR: mkArm(-0.40, 0.30, -0.45, 0.50, 0.2, 0.4),
      };
    default:
      return {};
  }
}

// -----------------------------------------------------------------------------

export function computeBottomPose(input: BottomPoseInput): BodyPose {
  const fatigue = clamp01(1 - input.stamina);
  // Breathing speeds up as stamina drains (§5.4's "呼吸が重い" zone). Two
  // octaves give the breath a non-repeating depth — the occasional deeper
  // inhale instead of a perfect metronome.
  const breathHz = 0.28 + fatigue * 0.55;
  const breath = breathOscillator(input.nowMs, breathHz);
  // Idle micro-sway: three incommensurate sines ≈ a living body constantly
  // re-balancing. Fatigue makes the adjustments bigger and sloppier.
  const sway = idleSway(input.nowMs, fatigue, 0);
  const sway2 = idleSway2(input.nowMs, fatigue, 0);

  // Sit-up crunch when actively attacking the collar / posture-break line.
  const sitUp = isActiveHigh(input.leftHand) || isActiveHigh(input.rightHand) ? 0.22 : 0;
  const legsLocked =
    input.leftFootState === "LOCKED" && input.rightFootState === "LOCKED";
  // Judgment window: the body coils — hips load, torso curls in tighter.
  const coil = input.windowOpen ? 1 : 0;

  // Guard-retention shuffle: a closed guard is never still — when idle (no
  // active hands, window shut) the hips micro-rotate and the legs make small
  // rhythmic re-grips, the constant fight to keep the position.
  const idleGuard =
    !input.windowOpen &&
    !handBusyState(input.leftHand) && !handBusyState(input.rightHand) &&
    legsLocked;
  const shuffleT = (input.nowMs / 1000) * TWO_PI;
  const shuffle = idleGuard ? 1 : 0;
  const shuffleYaw = shuffle * Math.sin(shuffleT * 0.6 + 0.5) * 0.05;
  const shuffleRoll = shuffle * Math.sin(shuffleT * 0.45) * 0.04;
  const shuffleSqueeze = shuffle * (0.5 + 0.5 * Math.sin(shuffleT * 0.8)) * 0.12;
  // Idle head scan: glance side to side hunting for the opening.
  const headScan = idleGuard ? Math.sin(shuffleT * 0.35 + 2.0) * 0.22 : 0;
  // Action-reaction: a fresh reach drives the same-side shoulder forward,
  // twisting the torso against the punch-out. Fades after the lunge.
  const reachTwistFor = (hand: LimbSnapshot, side: number): number => {
    if (hand.state !== "REACHING") return 0;
    const sinceMs = hand.sinceMs ?? 1000;
    return sinceMs < 320 ? side * 0.09 : side * 0.03;
  };
  const reachTwist = reachTwistFor(input.leftHand, -1) + reachTwistFor(input.rightHand, 1);

  // Technique-specific window entry: only while the window is open and a
  // candidate is named. Engaged hands keep their reach/grip pose (so a live
  // grip isn't overwritten by the entry's framing arms).
  const entry: WindowEntry =
    input.windowOpen && input.windowTechnique != null
      ? windowEntry(input.windowTechnique)
      : {};
  const handBusy = (h: LimbSnapshot): boolean =>
    h.state === "REACHING" || h.state === "CONTACT" || h.state === "GRIPPED";

  return {
    pelvisX: input.hipLateral * 0.20 + sway * 0.012,
    pelvisY:
      0.26 + (legsLocked ? 0.10 : 0) + Math.abs(input.hipPush) * 0.03 + coil * 0.04 +
      (entry.pelvisY ?? 0),
    pelvisZ: input.hipPush * 0.42 + sway2 * 0.008,
    // Supine: −π/2 lays the body flat with the chest facing up and (after
    // the rig's yaw flip) the head toward the camera; the small addition
    // keeps the shoulders just off the mat.
    pelvisPitch: -Math.PI / 2 + 0.15,
    pelvisYaw: input.hipAngle + (entry.pelvisYaw ?? 0) + shuffleYaw,
    pelvisRoll: input.hipLateral * 0.45 + (entry.pelvisRoll ?? 0) + shuffleRoll,
    // Positive pitch curls the chest up toward the opponent (supine front
    // = world up); fatigue sags it back toward the mat.
    torsoPitch: 0.20 + sitUp * 0.7 + coil * 0.10 + breath * 0.04 - fatigue * 0.15 + (entry.torsoPitch ?? 0),
    torsoYaw: input.hipAngle * 0.35 + sway2 * 0.02 + reachTwist,
    torsoRoll: input.hipLateral * 0.28 + sway * 0.015 + (entry.torsoRoll ?? 0),
    torsoTremor: 0,
    headPitch: 0.45 + sitUp * 0.4 - fatigue * 0.35 + breath * 0.02,
    headYaw: input.hipAngle * 0.3 + headScan,
    breath,
    armL: handBusy(input.leftHand)
      ? armPoseFrom(input.leftHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthL, input.nowMs)
      : entry.armL ?? armPoseFrom(input.leftHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthL, input.nowMs),
    armR: handBusy(input.rightHand)
      ? armPoseFrom(input.rightHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthR, input.nowMs)
      : entry.armR ?? armPoseFrom(input.rightHand, ATTACK_ZONE_REACH, ATTACK_REST, input.gripStrengthR, input.nowMs),
    legL: shuffleLeg(entry.legL ?? bottomLegPose(input.leftFootState, input.guard, input.nowMs), shuffleSqueeze),
    legR: shuffleLeg(entry.legR ?? bottomLegPose(input.rightFootState, input.guard, input.nowMs), shuffleSqueeze),
  };
}

// Adds a rhythmic squeeze to a locked-guard leg (knee folds tighter, knees
// pinch in) — the constant re-grip of guard retention.
function shuffleLeg(leg: LegPose, squeeze: number): LegPose {
  if (squeeze === 0) return leg;
  return { ...leg, kneeBend: leg.kneeBend + squeeze, hipRoll: leg.hipRoll - squeeze * 0.4 };
}

// Balance recovery: when the defender is broken down hard and the relevant
// hand is free, they throw it out to post on the mat and catch themselves.
// Returns the posting side and a 0..1 commitment, or null. Shared by
// computeTopPose (canned reaching-out arm) and computeScenePoses (mat IK).
export function balancePost(
  input: TopPoseInput,
): { side: "L" | "R"; t: number } | null {
  const pbMag = Math.hypot(input.postureBreakX, input.postureBreakY);
  if (pbMag < 0.5) return null;
  const side: "L" | "R" = input.postureBreakX >= 0 ? "R" : "L";
  const busy = (h: LimbSnapshot, extracted: boolean, cut: number | null): boolean =>
    extracted || cut !== null || h.state === "CONTACT" || h.state === "GRIPPED";
  if (side === "L" && busy(input.leftHand, input.armExtractedL, input.cutElapsedLMs)) return null;
  if (side === "R" && busy(input.rightHand, input.armExtractedR, input.cutElapsedRMs)) return null;
  return { side, t: clamp01((pbMag - 0.5) / 0.4) };
}

// Canned reaching-out post arm (used before IK / in headless preview).
const POST_ARM: ArmPose = Object.freeze({
  shoulderPitch: 0.15,
  shoulderRoll: 0.65, // swung out to the side
  shoulderYaw: 0.1,
  elbowBend: 0.18, // nearly straight, bracing
  tremor: 0.2,
  grip: 0.0, // flat palm on the mat
});

export function computeTopPose(input: TopPoseInput): BodyPose {
  const fatigue = clamp01(1 - input.stamina);
  const breathHz = 0.28 + fatigue * 0.55;
  // Phase-offset so the two bodies never breathe in lockstep.
  const breath = breathOscillator(input.nowMs, breathHz, Math.PI * 0.6);
  // Idle micro-sway (phase-offset from the bottom player's).
  const sway = idleSway(input.nowMs, fatigue, 1);
  const sway2 = idleSway2(input.nowMs, fatigue, 1);

  const pbX = input.postureBreakX;
  const pbY = input.postureBreakY;
  const pbMag = clamp01(Math.hypot(pbX, pbY));
  // Strain tremor while fighting a deep posture break.
  const strain = pbMag > 0.45 ? (pbMag - 0.45) * 0.9 : 0;

  // Pressure weave: a passer hunting the angle rocks weight side-to-side and
  // probes forward. Only while engaged-but-not-committed (posture still
  // intact, not deep into a pass) — it's the search before the drive.
  const searching = pbMag < 0.4 && (input.passElapsedMs === null);
  const weaveT = (input.nowMs / 1000) * TWO_PI;
  const weave = searching ? 1 : 0;
  const weaveX = weave * Math.sin(weaveT * 0.5 + 0.4) * 0.06;
  const weaveProbe = weave * (0.5 + 0.5 * Math.sin(weaveT * 0.7)) * 0.05;
  const weaveRoll = weave * Math.sin(weaveT * 0.5 + 0.4) * 0.08;

  let armL = input.armExtractedL
    ? mkArm(EXTRACTED_ARM.pitch, EXTRACTED_ARM.roll, EXTRACTED_ARM.yaw, EXTRACTED_ARM.elbow, 0.25)
    : armPoseFrom(input.leftHand, DEFENSE_ZONE_REACH, DEFENSE_REST, 0.5, input.nowMs);
  let armR = input.armExtractedR
    ? mkArm(EXTRACTED_ARM.pitch, EXTRACTED_ARM.roll, EXTRACTED_ARM.yaw, EXTRACTED_ARM.elbow, 0.25)
    : armPoseFrom(input.rightHand, DEFENSE_ZONE_REACH, DEFENSE_REST, 0.5, input.nowMs);
  // A grip cut overrides the hand FSM read for the chopping arm.
  if (input.cutElapsedLMs !== null) armL = cutChopArm(armL, input.cutElapsedLMs);
  if (input.cutElapsedRMs !== null) armR = cutChopArm(armR, input.cutElapsedRMs);

  // Balance recovery: throw the free hand out to post on the mat.
  const post = balancePost(input);
  if (post !== null) {
    if (post.side === "L") armL = lerpArm(armL, POST_ARM, post.t);
    else armR = lerpArm(armR, POST_ARM, post.t);
  }

  // Kneeling combat base; weight intent rocks the hips, posture break drags
  // the whole pelvis toward the attacker.
  const kneel: LegPose = {
    hipPitch: -0.55 + input.weightForward * 0.15,
    hipYaw: 0,
    hipRoll: 0.30,
    kneeBend: 1.65,
  };

  // Pass attempt: drive in low — lead knee steps up (knee-slice shape),
  // trail leg extends back, torso drops, hips surge forward.
  const passT = input.passElapsedMs === null ? 0 : clamp01(input.passElapsedMs / 400);
  const passSurge =
    input.passElapsedMs === null
      ? 0
      : Math.sin((input.passElapsedMs / 1000) * TWO_PI * 1.5) * 0.04;
  const driveRight = input.weightLateral >= 0;
  const leadLeg = lerpLeg(kneel, { hipPitch: -1.15, hipYaw: 0, hipRoll: 0.25, kneeBend: 1.45 }, passT);
  const trailLeg = lerpLeg(kneel, { hipPitch: -0.20, hipYaw: 0, hipRoll: 0.30, kneeBend: 0.70 }, passT);

  // Counter window: brace — posture up a touch, ready to spring.
  const brace = input.counterWindowOpen ? 1 : 0;

  // Weight shift: load drifts over whichever hand is posted/gripping, and
  // the body dips toward a side whose arm has been dragged across.
  const postLoad = (hand: LimbSnapshot, side: number): number =>
    hand.state === "CONTACT" || hand.state === "GRIPPED" ? side * 0.028 : 0;
  const weightShiftX =
    postLoad(input.leftHand, -1) + postLoad(input.rightHand, 1) +
    (input.armExtractedL ? -0.045 : 0) + (input.armExtractedR ? 0.045 : 0);
  const extractDip = (input.armExtractedL ? -0.07 : 0) + (input.armExtractedR ? 0.07 : 0);

  return {
    pelvisX: pbX * 0.25 + input.weightLateral * 0.22 + (driveRight ? 1 : -1) * passT * 0.10 + sway * 0.014 + weightShiftX + weaveX,
    pelvisY: 0.50 - fatigue * 0.04 - pbMag * 0.06 - passT * 0.06 + brace * 0.02,
    pelvisZ: pbY * 0.30 + input.weightForward * 0.22 + passT * (0.18 + passSurge) + sway2 * 0.010 + weaveProbe,
    pelvisPitch: 0,
    pelvisYaw: 0,
    pelvisRoll: input.weightLateral * 0.10 + sway * 0.018 + extractDip + weaveRoll,
    // Combat-base hunch (constant +0.10 forward), then posture break
    // crumples the torso forward / sideways; the pass drive and fatigue
    // round it further, while a counter brace straightens it.
    torsoPitch:
      0.10 + pbY * 0.55 + input.weightForward * 0.10 + fatigue * 0.12 + breath * 0.03 +
      passT * 0.30 - brace * 0.08,
    torsoYaw: pbX * 0.20 + sway2 * 0.025,
    torsoRoll: pbX * 0.45 + sway * 0.02,
    torsoTremor: strain + passT * 0.15,
    // Eyes stay on the opponent below; exhaustion drops the chin further.
    headPitch: 0.45 + fatigue * 0.25 + Math.max(0, pbY) * 0.20 + breath * 0.02,
    headYaw: -pbX * 0.25,
    breath,
    armL,
    armR,
    legL: driveRight ? trailLeg : leadLeg,
    legR: driveRight ? leadLeg : trailLeg,
  };
}

// -----------------------------------------------------------------------------
// Finish tableaux. When a technique / counter / pass resolves, the sim's
// FSM-driven pose stops telling the story — the bodies need to land in the
// recognisable end position of that move. `computeFinishPoses` returns both
// bodies' poses for a confirmed outcome; `tMs` is time since confirmation
// (drives squeeze pulses and heavy post-scramble breathing). The rig's
// springs handle the transition into the tableau, so these are pure holds.

export type FinishKind = Technique | CounterTechnique | "PASS" | "SCRAMBLE";

export type FinishPoses = Readonly<{ bottom: BodyPose; top: BodyPose }>;

export function computeFinishPoses(kind: FinishKind, tMs: number): FinishPoses {
  // Post-resolution breathing is heavy regardless of stamina.
  const breathB = Math.sin((tMs / 1000) * TWO_PI * 0.9);
  const breathT = Math.sin((tMs / 1000) * TWO_PI * 0.9 + Math.PI * 0.6);
  // Submission squeeze pulse, 0..1.
  const squeeze = 0.5 + 0.5 * Math.sin((tMs / 1000) * TWO_PI * 1.2);

  // Neutral supine / kneeling bases; each tableau overrides what it needs.
  const bottom: { -readonly [K in keyof BodyPose]: BodyPose[K] } = {
    pelvisX: 0, pelvisY: 0.28, pelvisZ: 0,
    pelvisPitch: -Math.PI / 2 + 0.15, pelvisYaw: 0, pelvisRoll: 0,
    torsoPitch: 0.35 + breathB * 0.04, torsoYaw: 0, torsoRoll: 0, torsoTremor: 0,
    headPitch: 0.55, headYaw: 0,
    breath: breathB,
    armL: mkArm(ATTACK_REST.pitch, ATTACK_REST.roll, ATTACK_REST.yaw, ATTACK_REST.elbow),
    armR: mkArm(ATTACK_REST.pitch, ATTACK_REST.roll, ATTACK_REST.yaw, ATTACK_REST.elbow),
    legL: solveLeg(LEG_DIR_LOCKED.thigh, LEG_DIR_LOCKED.shin),
    legR: solveLeg(LEG_DIR_LOCKED.thigh, LEG_DIR_LOCKED.shin),
  };
  const top: { -readonly [K in keyof BodyPose]: BodyPose[K] } = {
    pelvisX: 0, pelvisY: 0.50, pelvisZ: 0,
    pelvisPitch: 0, pelvisYaw: 0, pelvisRoll: 0,
    torsoPitch: 0.10 + breathT * 0.03, torsoYaw: 0, torsoRoll: 0, torsoTremor: 0,
    headPitch: 0.45, headYaw: 0,
    breath: breathT,
    armL: mkArm(DEFENSE_REST.pitch, DEFENSE_REST.roll, DEFENSE_REST.yaw, DEFENSE_REST.elbow),
    armR: mkArm(DEFENSE_REST.pitch, DEFENSE_REST.roll, DEFENSE_REST.yaw, DEFENSE_REST.elbow),
    legL: { hipPitch: -0.55, hipYaw: 0, hipRoll: 0.30, kneeBend: 2.0 },
    legR: { hipPitch: -0.55, hipYaw: 0, hipRoll: 0.30, kneeBend: 2.0 },
  };

  switch (kind) {
    case "TRIANGLE": {
      // Legs locked high around the neck, both hands pulling the head down;
      // the defender is folded deep, posting wide and shuddering.
      bottom.pelvisY = 0.34 + squeeze * 0.015;
      bottom.torsoPitch = 0.45;
      bottom.legR = solveLeg([0.18, -0.50, 0.85], [-0.90, -0.25, -0.35]);
      bottom.legL = solveLeg([0.35, -0.55, 0.76], [-0.70, -0.50, -0.50]);
      bottom.armL = mkArm(-0.70, 0.10, -0.15, 1.05 + squeeze * 0.08, 0.5, 0.9);
      bottom.armR = mkArm(-0.70, 0.10, -0.15, 1.05 + squeeze * 0.08, 0.5, 0.9);
      top.pelvisY = 0.42;
      top.pelvisZ = 0.18;
      top.torsoPitch = 0.85;
      top.headPitch = 0.95;
      top.torsoTremor = 0.45;
      top.armL = mkArm(-0.50, 0.55, 0.1, 0.25, 0.4);
      top.armR = mkArm(-0.50, 0.55, 0.1, 0.25, 0.4);
      break;
    }
    case "OMOPLATA": {
      // Attacker turned out, one leg thrown over the trapped shoulder; the
      // defender is folded face-down with the arm wound behind.
      bottom.pelvisYaw = 0.80;
      bottom.pelvisY = 0.30;
      bottom.torsoPitch = 0.60;
      bottom.legL = solveLeg([-0.35, -0.55, 0.75], [-0.60, -0.70, -0.30]);
      bottom.legR = solveLeg([0.70, -0.50, 0.40], [-0.20, -0.90, -0.20]);
      bottom.armL = mkArm(-0.50, 0.15, -0.10, 0.70, 0.3);
      bottom.armR = mkArm(-0.55, 0.20, -0.10, 0.65, 0.3);
      top.pelvisY = 0.34;
      top.pelvisZ = 0.25;
      top.torsoPitch = 1.15;
      top.headPitch = 1.0;
      top.torsoTremor = 0.35;
      top.armL = mkArm(0.70, 0.10, -0.50, 0.90, 0.5); // wound behind the back
      top.armR = mkArm(-0.50, 0.50, 0.10, 0.30, 0.2);
      break;
    }
    case "SCISSOR_SWEEP": {
      // Defender toppled sideways onto his back; attacker rides up after him.
      bottom.pelvisY = 0.38;
      bottom.pelvisZ = -0.12;
      bottom.torsoPitch = 0.85;
      bottom.headPitch = 0.75;
      bottom.legR = solveLeg([0.40, -0.70, 0.35], [-0.50, -0.80, -0.10]);
      bottom.legL = solveLeg([0.10, -0.95, 0.15], [0.10, -0.90, -0.30]);
      bottom.armL = mkArm(-0.70, 0.10, -0.10, 0.90, 0.3, 0.9);
      bottom.armR = mkArm(-0.70, 0.10, -0.10, 0.90, 0.3, 0.9);
      top.pelvisX = 0.50;
      top.pelvisY = 0.28;
      top.pelvisRoll = 1.30;
      top.torsoPitch = -0.15;
      top.headPitch = -0.20;
      top.armL = mkArm(-1.20, 0.70, 0.0, 0.25, 0.2);
      top.armR = mkArm(-1.20, 0.70, 0.0, 0.25, 0.2);
      top.legL = { hipPitch: -0.90, hipYaw: 0, hipRoll: 0.35, kneeBend: 1.0 };
      top.legR = { hipPitch: -0.40, hipYaw: 0, hipRoll: 0.40, kneeBend: 1.4 };
      break;
    }
    case "FLOWER_SWEEP": {
      // Same story, mirrored, with the sweeping leg arcing high.
      bottom.pelvisY = 0.38;
      bottom.pelvisZ = -0.12;
      bottom.torsoPitch = 0.85;
      bottom.headPitch = 0.75;
      bottom.legL = solveLeg([0.50, -0.40, 0.75], [-0.60, -0.60, -0.30]);
      bottom.legR = solveLeg([0.10, -0.95, 0.15], [0.10, -0.90, -0.30]);
      bottom.armL = mkArm(-0.70, 0.10, -0.10, 0.90, 0.3, 0.9);
      bottom.armR = mkArm(-0.70, 0.10, -0.10, 0.90, 0.3, 0.9);
      top.pelvisX = -0.50;
      top.pelvisY = 0.28;
      top.pelvisRoll = -1.30;
      top.torsoPitch = -0.15;
      top.headPitch = -0.20;
      top.armL = mkArm(-1.20, 0.70, 0.0, 0.25, 0.2);
      top.armR = mkArm(-1.20, 0.70, 0.0, 0.25, 0.2);
      top.legL = { hipPitch: -0.40, hipYaw: 0, hipRoll: 0.40, kneeBend: 1.4 };
      top.legR = { hipPitch: -0.90, hipYaw: 0, hipRoll: 0.35, kneeBend: 1.0 };
      break;
    }
    case "HIP_BUMP": {
      // Attacker sat up hard off a posted hand; defender knocked backward.
      bottom.pelvisY = 0.42;
      bottom.pelvisZ = -0.10;
      bottom.torsoPitch = 1.05;
      bottom.headPitch = 0.20;
      bottom.armR = mkArm(0.50, 0.30, 0.0, 0.15); // posted behind
      bottom.armL = mkArm(-1.10, 0.30, -0.10, 0.30, 0.2); // swinging over
      bottom.legL = solveLeg([0.55, -0.75, 0.25], [-0.40, -0.85, -0.15]);
      bottom.legR = solveLeg([0.40, -0.80, 0.30], [-0.45, -0.80, -0.15]);
      top.pelvisY = 0.46;
      top.pelvisZ = -0.18;
      top.torsoPitch = -0.45;
      top.headPitch = -0.30;
      top.armL = mkArm(-1.40, 0.40, 0.0, 0.20, 0.2);
      top.armR = mkArm(-1.40, 0.40, 0.0, 0.20, 0.2);
      top.legL = { hipPitch: -0.35, hipYaw: 0, hipRoll: 0.35, kneeBend: 1.6 };
      top.legR = { hipPitch: -0.35, hipYaw: 0, hipRoll: 0.35, kneeBend: 1.6 };
      break;
    }
    case "CROSS_COLLAR": {
      // Wrists crossed deep in the collar; defender slumped over the choke.
      bottom.torsoPitch = 0.60;
      bottom.armL = mkArm(-0.80, 0.05, -0.55, 0.85 + squeeze * 0.06, 0.6, 0.9);
      bottom.armR = mkArm(-0.80, 0.05, -0.55, 0.85 + squeeze * 0.06, 0.6, 0.9);
      top.pelvisY = 0.44;
      top.pelvisZ = 0.12;
      top.torsoPitch = 0.70;
      top.headPitch = 1.0;
      top.torsoTremor = 0.4;
      top.armL = mkArm(-0.35, 0.30, 0.0, 0.30, 0.3);
      top.armR = mkArm(-0.35, 0.30, 0.0, 0.30, 0.3);
      break;
    }
    case "SCISSOR_COUNTER": {
      // Defender wins the exchange: postured up tall, pinning the knees;
      // attacker flattened with arms knocked wide.
      bottom.pelvisY = 0.22;
      bottom.torsoPitch = 0.10;
      bottom.headPitch = 0.30;
      bottom.armL = mkArm(PARRIED_ARM.pitch, PARRIED_ARM.roll, PARRIED_ARM.yaw, PARRIED_ARM.elbow);
      bottom.armR = mkArm(PARRIED_ARM.pitch, PARRIED_ARM.roll, PARRIED_ARM.yaw, PARRIED_ARM.elbow);
      bottom.legL = solveLeg([0.65, -0.70, 0.15], [-0.30, -0.90, -0.10]);
      bottom.legR = solveLeg([0.65, -0.70, 0.15], [-0.30, -0.90, -0.10]);
      top.pelvisY = 0.52;
      top.torsoPitch = -0.10;
      top.headPitch = 0.35;
      top.armL = mkArm(-0.55, 0.20, 0.0, 0.20);
      top.armR = mkArm(-0.55, 0.20, 0.0, 0.20);
      break;
    }
    case "TRIANGLE_EARLY_STACK": {
      // Defender stacks through the early triangle: hips high, driving the
      // attacker's folded legs back over their own head.
      bottom.pelvisY = 0.36;
      bottom.torsoPitch = 0.15;
      bottom.headPitch = 0.70;
      bottom.legL = solveLeg([0.25, -0.35, 0.90], [-0.75, -0.35, -0.40]);
      bottom.legR = solveLeg([0.25, -0.35, 0.90], [-0.75, -0.35, -0.40]);
      bottom.armL = mkArm(-0.60, 0.25, 0.0, 0.60, 0.3);
      bottom.armR = mkArm(-0.60, 0.25, 0.0, 0.60, 0.3);
      top.pelvisY = 0.55;
      top.pelvisZ = 0.32;
      top.torsoPitch = 0.75;
      top.headPitch = 0.7;
      top.torsoTremor = 0.3;
      top.armL = mkArm(-0.90, 0.25, 0.0, 0.10, 0.2);
      top.armR = mkArm(-0.90, 0.25, 0.0, 0.10, 0.2);
      top.legL = { hipPitch: -0.30, hipYaw: 0, hipRoll: 0.30, kneeBend: 1.2 };
      top.legR = { hipPitch: -0.30, hipYaw: 0, hipRoll: 0.30, kneeBend: 1.2 };
      break;
    }
    case "SCRAMBLE": {
      // Guard opened: the bottom player scrambles up to a seated base while
      // the top player backs out of range, both re-setting their frames.
      bottom.pelvisY = 0.24;
      bottom.pelvisPitch = -0.85;
      bottom.torsoPitch = 0.70;
      bottom.headPitch = 0.10;
      bottom.legL = solveLeg([0.50, -0.65, 0.40], [-0.15, -0.55, -0.80]);
      bottom.legR = solveLeg([0.50, -0.65, 0.40], [-0.15, -0.55, -0.80]);
      bottom.armL = mkArm(-0.90, 0.20, 0, 0.90);
      bottom.armR = mkArm(-0.90, 0.20, 0, 0.90);
      top.pelvisZ = -0.28;
      top.pelvisY = 0.55;
      top.torsoPitch = 0.05;
      top.headPitch = 0.30;
      top.armL = mkArm(-0.95, 0.20, 0, 0.50);
      top.armR = mkArm(-0.95, 0.20, 0, 0.50);
      // Combat base rising: one knee up, foot planted.
      top.legR = { hipPitch: -1.30, hipYaw: 0, hipRoll: 0.25, kneeBend: 1.45 };
      break;
    }
    case "PASS": {
      // Guard passed: defender settled chest-on-chest past the legs, both
      // of the attacker's legs swept to one side.
      bottom.pelvisY = 0.20;
      bottom.torsoPitch = 0.15;
      bottom.headPitch = 0.35;
      bottom.legR = solveLeg([-0.55, -0.70, 0.20], [-0.30, -0.85, -0.20]);
      bottom.legL = solveLeg([0.60, -0.75, 0.15], [0.30, -0.90, -0.15]);
      bottom.armL = mkArm(-0.75, 0.15, -0.20, 0.70, 0.2, 0.9);
      bottom.armR = mkArm(-0.60, 0.20, -0.35, 0.55, 0.2, 0.9);
      top.pelvisX = 0.50;
      top.pelvisY = 0.38;
      top.pelvisZ = 0.30;
      top.pelvisYaw = -0.50;
      top.torsoPitch = 0.55;
      top.headPitch = 0.55;
      top.armL = mkArm(-0.75, 0.20, -0.10, 0.60, 0.2);
      top.armR = mkArm(-0.75, 0.20, -0.10, 0.60, 0.2);
      top.legL = { hipPitch: -0.15, hipYaw: 0, hipRoll: 0.30, kneeBend: 0.5 };
      top.legR = { hipPitch: -0.15, hipYaw: 0, hipRoll: 0.35, kneeBend: 0.6 };
      break;
    }
  }

  // Execution phase: the first ~450 ms ramps the motion finishes from a
  // mid-action keyframe into the settled hold (smoothstep, on top of the
  // rig's springs). Submissions stay as isometric holds.
  const phaseT = clamp01(tMs / 450);
  const phase = phaseT * phaseT * (3 - 2 * phaseT);
  const ramp = (from: number, to: number): number => from + (to - from) * phase;
  switch (kind) {
    case "SCISSOR_SWEEP":
    case "FLOWER_SWEEP": {
      const sign = kind === "SCISSOR_SWEEP" ? 1 : -1;
      top.pelvisRoll = ramp(sign * 0.35, top.pelvisRoll);
      top.pelvisX = ramp(sign * 0.15, top.pelvisX);
      top.pelvisY = ramp(0.45, top.pelvisY);
      bottom.torsoPitch = ramp(0.35, bottom.torsoPitch);
      break;
    }
    case "HIP_BUMP":
      bottom.torsoPitch = ramp(0.30, bottom.torsoPitch);
      top.torsoPitch = ramp(0.15, top.torsoPitch);
      top.pelvisZ = ramp(0, top.pelvisZ);
      break;
    case "PASS":
      top.pelvisX = ramp(0.10, top.pelvisX);
      top.pelvisZ = ramp(0.05, top.pelvisZ);
      break;
    default:
      break;
  }

  return { bottom, top };
}

// -----------------------------------------------------------------------------
// Contact IK — the layer that makes hands actually land on the opponent.
//
// Forward kinematics (computeBodyFrames) reconstructs world-space joint
// positions for a posed rig: torso frame, shoulders, hands, knees, biceps,
// head. Zone anchors map each grip/base zone onto the *current* opponent
// body, and a two-bone arm solver plants the reaching hand there. The result:
// grips visibly connect, and stay connected while the opponent crumples,
// because the anchor moves with their pose.
//
// computeScenePoses orchestrates the (acyclic) dependency order:
//   1. pose both bodies with state-table arms,
//   2. defender arms IK → anchors on the attacker's torso-level frame,
//   3. attacker arms IK → anchors on the *final* defender frame (so sleeve
//      grips track the defender's hands),
//   4. heads track the opponent's head.

// 3×3 rotation matrices, row-major. Transpose = inverse for pure rotations.
type M3 = readonly [number, number, number, number, number, number, number, number, number];

const M3_ID: M3 = [1, 0, 0, 0, 1, 0, 0, 0, 1];

function m3Mul(a: M3, b: M3): M3 {
  const r = new Array<number>(9);
  for (let i = 0; i < 3; i += 1) {
    for (let j = 0; j < 3; j += 1) {
      r[i * 3 + j] =
        a[i * 3]! * b[j]! + a[i * 3 + 1]! * b[3 + j]! + a[i * 3 + 2]! * b[6 + j]!;
    }
  }
  return r as unknown as M3;
}

function m3MulV(m: M3, v: V3): V3 {
  return [
    m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
    m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
    m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
  ];
}

function m3T(m: M3): M3 {
  return [m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]];
}

function m3RotX(t: number): M3 {
  const c = Math.cos(t), s = Math.sin(t);
  return [1, 0, 0, 0, c, -s, 0, s, c];
}

function m3RotY(t: number): M3 {
  const c = Math.cos(t), s = Math.sin(t);
  return [c, 0, s, 0, 1, 0, -s, 0, c];
}

function m3RotZ(t: number): M3 {
  const c = Math.cos(t), s = Math.sin(t);
  return [c, -s, 0, s, c, 0, 0, 0, 1];
}

// three.js default Euler order "XYZ": R = Rx·Ry·Rz (pelvis / torso groups).
function m3EulerXYZ(x: number, y: number, z: number): M3 {
  return m3Mul(m3RotX(x), m3Mul(m3RotY(y), m3RotZ(z)));
}

// Rig joint groups with order "YXZ": R = Ry·Rx·Rz (hips, shoulders).
function m3EulerYXZ(x: number, y: number, z: number): M3 {
  return m3Mul(m3RotY(y), m3Mul(m3RotX(x), m3RotZ(z)));
}

function v3add(a: V3, b: V3): V3 {
  return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

function v3sub(a: V3, b: V3): V3 {
  return [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}

function v3scale(a: V3, s: number): V3 {
  return [a[0] * s, a[1] * s, a[2] * s];
}

function v3len(a: V3): number {
  return Math.hypot(a[0], a[1], a[2]);
}

// Skeleton dimensions, single-sourced here and consumed by blockman.ts so
// the FK below never drifts from the rendered rig.
export const RIG_DIMS = Object.freeze({
  pelvisToTorso: 0.10,
  shoulderX: 0.24,
  shoulderY: 0.44,
  headY: 0.56,
  headCenterY: 0.12,
  upperArm: 0.28, // shoulder pivot → elbow pivot
  foreArm: 0.27,  // elbow pivot → hand centre
  hipX: 0.11,
  hipY: -0.05,
  thigh: 0.38,    // hip pivot → knee pivot
  shin: 0.36,     // knee pivot → ankle
});

// Where each rig's root sits in the world. yawPi rigs are turned 180° (the
// supine player) and mirror their pelvis x/z offsets in blockman.ts — the
// FK accounts for both so positions here are true world space.
export type RigPlacement = Readonly<{ origin: V3; yawPi: boolean }>;
export const BOTTOM_PLACEMENT: RigPlacement = Object.freeze({ origin: [0, 0, 0] as V3, yawPi: true });
export const TOP_PLACEMENT: RigPlacement = Object.freeze({ origin: [0, 0, -0.5] as V3, yawPi: false });

export type BodyFrames = Readonly<{
  pelvisPos: V3;
  pelvisRot: M3;
  torsoPos: V3;
  torsoRot: M3;
  headPos: V3;
  shoulderL: V3;
  shoulderR: V3;
  handL: V3;
  handR: V3;
  bicepL: V3;
  bicepR: V3;
  kneeL: V3;
  kneeR: V3;
}>;

export function computeBodyFrames(pose: BodyPose, place: RigPlacement): BodyFrames {
  const root = place.yawPi ? m3RotY(Math.PI) : M3_ID;
  // blockman mirrors pelvis x/z for yawPi rigs, and the root rotation
  // un-mirrors them — net effect: pose pelvis offsets are world-axis.
  const pelvisPos = v3add(place.origin, [pose.pelvisX, pose.pelvisY, pose.pelvisZ]);
  const pelvisRot = m3Mul(root, m3EulerXYZ(pose.pelvisPitch, pose.pelvisYaw, pose.pelvisRoll));
  const torsoPos = v3add(pelvisPos, m3MulV(pelvisRot, [0, RIG_DIMS.pelvisToTorso, 0]));
  const torsoRot = m3Mul(pelvisRot, m3EulerXYZ(pose.torsoPitch, pose.torsoYaw, pose.torsoRoll));
  const headPos = v3add(torsoPos, m3MulV(torsoRot, [0, RIG_DIMS.headY + RIG_DIMS.headCenterY, 0]));

  const armPoint = (arm: ArmPose, sideSign: number): { shoulder: V3; bicep: V3; hand: V3 } => {
    const shoulder = v3add(
      torsoPos,
      m3MulV(torsoRot, [RIG_DIMS.shoulderX * sideSign, RIG_DIMS.shoulderY, 0]),
    );
    const armRot = m3Mul(
      torsoRot,
      m3EulerYXZ(arm.shoulderPitch, arm.shoulderYaw * sideSign, arm.shoulderRoll * sideSign),
    );
    const bicep = v3add(shoulder, m3MulV(armRot, [0, -RIG_DIMS.upperArm / 2, 0]));
    const elbowPos = v3add(shoulder, m3MulV(armRot, [0, -RIG_DIMS.upperArm, 0]));
    const foreRot = m3Mul(armRot, m3RotX(-arm.elbowBend));
    const hand = v3add(elbowPos, m3MulV(foreRot, [0, -RIG_DIMS.foreArm, 0]));
    return { shoulder, bicep, hand };
  };
  const aL = armPoint(pose.armL, -1);
  const aR = armPoint(pose.armR, 1);

  const kneePoint = (leg: LegPose, sideSign: number): V3 => {
    const hip = v3add(
      pelvisPos,
      m3MulV(pelvisRot, [RIG_DIMS.hipX * sideSign, RIG_DIMS.hipY, 0]),
    );
    const legRot = m3Mul(
      pelvisRot,
      m3EulerYXZ(leg.hipPitch, leg.hipYaw * sideSign, leg.hipRoll * sideSign),
    );
    return v3add(hip, m3MulV(legRot, [0, -RIG_DIMS.thigh, 0]));
  };

  return {
    pelvisPos,
    pelvisRot,
    torsoPos,
    torsoRot,
    headPos,
    shoulderL: aL.shoulder,
    shoulderR: aR.shoulder,
    handL: aL.hand,
    handR: aR.hand,
    bicepL: aL.bicep,
    bicepR: aR.bicep,
    kneeL: kneePoint(pose.legL, -1),
    kneeR: kneePoint(pose.legR, 1),
  };
}

// World-space anchor for an attacker grip zone on the defender's body.
export function gripZoneAnchor(zone: string, opp: BodyFrames): V3 | null {
  switch (zone) {
    case "COLLAR_L":
      return v3add(opp.torsoPos, m3MulV(opp.torsoRot, [-0.10, 0.50, 0.08]));
    case "COLLAR_R":
      return v3add(opp.torsoPos, m3MulV(opp.torsoRot, [0.10, 0.50, 0.08]));
    case "SLEEVE_L":
    case "WRIST_L":
      return opp.handL;
    case "SLEEVE_R":
    case "WRIST_R":
      return opp.handR;
    case "BELT":
      return v3add(opp.pelvisPos, m3MulV(opp.pelvisRot, [0, 0.02, 0.12]));
    case "POSTURE_BREAK":
      return v3add(opp.torsoPos, m3MulV(opp.torsoRot, [0, 0.40, 0.10]));
    default:
      return null;
  }
}

// World-space anchor for a defender base zone on the attacker's body.
export function baseZoneAnchor(zone: string, opp: BodyFrames): V3 | null {
  switch (zone) {
    case "CHEST":
      return v3add(opp.torsoPos, m3MulV(opp.torsoRot, [0, 0.30, 0.14]));
    case "HIP":
      return v3add(opp.pelvisPos, m3MulV(opp.pelvisRot, [0, 0, 0.12]));
    case "KNEE_L":
      return opp.kneeL;
    case "KNEE_R":
      return opp.kneeR;
    case "BICEP_L":
      return opp.bicepL;
    case "BICEP_R":
      return opp.bicepR;
    default:
      return null;
  }
}

// Two-bone arm IK. Plants the hand centre on `targetWorld` (clamped to the
// reachable sphere — an out-of-range anchor reads as a full-extension
// strain). The elbow settles toward a down-and-out pole, and the solution
// comes back as the same YXZ shoulder Euler + elbow bend the rig consumes.
function solveArmIK(
  targetWorld: V3,
  shoulderWorld: V3,
  torsoRot: M3,
  sideSign: number,
  baseTremor: number,
  grip = 0.25,
): ArmPose {
  const a = RIG_DIMS.upperArm;
  const b = RIG_DIMS.foreArm;
  // Target in the shoulder's parent (torso) frame.
  const tRaw = m3MulV(m3T(torsoRot), v3sub(targetWorld, shoulderWorld));
  const dRaw = v3len(tRaw);
  const d = Math.max(0.10, Math.min(a + b - 0.01, dRaw));
  const dir = dRaw < 1e-6 ? ([0, -1, 0] as V3) : v3scale(tRaw, 1 / dRaw);

  const elbowBend = Math.PI - Math.acos(clampUnit((a * a + b * b - d * d) / (2 * a * b)));
  const alpha = Math.acos(clampUnit((a * a + d * d - b * b) / (2 * a * d)));

  // Elbow pole: down along the torso and slightly out/back, mirrored per side.
  const pole: V3 = [0.45 * sideSign, -0.8, -0.35];
  let axis = v3cross(dir, pole);
  if (v3len(axis) < 1e-5) axis = v3cross(dir, [1, 0, 0] as const);
  axis = v3norm(axis);
  // Rotate `dir` by α about `axis` (Rodrigues, axis ⊥ dir) → upper-arm dir.
  const u = v3add(
    v3scale(dir, Math.cos(alpha)),
    v3scale(v3cross(axis, dir), Math.sin(alpha)),
  );
  // Forearm direction follows from the (clamped) target.
  const f = v3norm(v3sub(v3scale(dir, d), v3scale(u, a)));

  // Build the shoulder rotation: local −y → u, and the elbow hinge (+x with
  // a *negative* rig rotation) demands xAxis = normalize(f × u).
  let xAxis = v3cross(f, u);
  if (v3len(xAxis) < 1e-5) xAxis = v3cross(u, [0, 0, 1] as const);
  xAxis = v3norm(xAxis);
  const yAxis: V3 = [-u[0], -u[1], -u[2]];
  const zAxis = v3cross(xAxis, yAxis);

  const m13 = zAxis[0], m23 = zAxis[1], m33 = zAxis[2];
  const m21 = xAxis[1], m22 = yAxis[1];
  const m11 = xAxis[0], m31 = xAxis[2];
  const pitch = Math.asin(-clampUnit(m23));
  let yaw: number;
  let roll: number;
  if (Math.abs(m23) < 0.9999) {
    yaw = Math.atan2(m13, m33);
    roll = Math.atan2(m21, m22);
  } else {
    yaw = Math.atan2(-m31, m11);
    roll = 0;
  }
  return {
    shoulderPitch: pitch,
    // The rig multiplies yaw/roll by sideSign; de-mirror so it round-trips.
    shoulderYaw: yaw * sideSign,
    shoulderRoll: roll * sideSign,
    elbowBend,
    tremor: baseTremor,
    grip,
  };
}

// Hand states whose arm should be IK-planted on the live anchor.
function ikEngaged(state: string): boolean {
  return state === "REACHING" || state === "CONTACT" || state === "GRIPPED";
}

function ikArm(
  hand: LimbSnapshot,
  cannedArm: ArmPose,
  anchorTable: "grip" | "base",
  oppFrames: BodyFrames,
  ownFrames: BodyFrames,
  side: "L" | "R",
): ArmPose {
  if (!ikEngaged(hand.state) || hand.target === null) return cannedArm;
  const anchor =
    anchorTable === "grip"
      ? gripZoneAnchor(hand.target, oppFrames)
      : baseZoneAnchor(hand.target, oppFrames);
  if (anchor === null) return cannedArm;
  const sideSign = side === "L" ? -1 : 1;
  const shoulder = side === "L" ? ownFrames.shoulderL : ownFrames.shoulderR;
  // GRIPPED: drag the grip a few cm toward one's own chest — reads as pull.
  let target = anchor;
  if (hand.state === "GRIPPED") {
    const chest = v3add(ownFrames.torsoPos, m3MulV(ownFrames.torsoRot, [0, 0.3, 0.1]));
    const toChest = v3sub(chest, anchor);
    const len = v3len(toChest);
    if (len > 1e-6) target = v3add(anchor, v3scale(toChest, Math.min(0.06, len) / len));
  }
  return solveArmIK(target, shoulder, ownFrames.torsoRot, sideSign, cannedArm.tremor, cannedArm.grip);
}

// Gaze: aim the head's face (+z when neutral) at the opponent's head.
// Returns clamped head pitch/yaw in the torso frame; blended on top of the
// pose's base head angles by computeScenePoses.
function lookAt(ownPose: BodyPose, ownFrames: BodyFrames, targetWorld: V3): { pitch: number; yaw: number } {
  const headOrigin = v3add(ownFrames.torsoPos, m3MulV(ownFrames.torsoRot, [0, RIG_DIMS.headY, 0]));
  const tLocal = v3norm(m3MulV(m3T(ownFrames.torsoRot), v3sub(targetWorld, headOrigin)));
  // Face = Rx(p)·Ry(yw)·ẑ = (sin yw, −cos yw·sin p, cos yw·cos p).
  const yaw = Math.atan2(tLocal[0], Math.max(0.15, tLocal[2]));
  const pitch = -Math.atan2(tLocal[1], Math.hypot(tLocal[0], tLocal[2]));
  const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v));
  return {
    pitch: clamp(ownPose.headPitch * 0.3 + pitch * 0.7, -0.6, 1.1),
    yaw: clamp(ownPose.headYaw * 0.3 + yaw * 0.7, -0.75, 0.75),
  };
}

export type ScenePoses = Readonly<{ bottom: BodyPose; top: BodyPose }>;

export function computeScenePoses(
  bottomIn: BottomPoseInput,
  topIn: TopPoseInput,
): ScenePoses {
  const b0 = computeBottomPose(bottomIn);
  const t0 = computeTopPose(topIn);

  // 1. Defender hands plant on the attacker's torso-level anchors. (Cut
  //    chops and extracted arms already replaced the canned pose in
  //    computeTopPose and must keep priority over IK.)
  const bFrames0 = computeBodyFrames(b0, BOTTOM_PLACEMENT);
  const tFrames0 = computeBodyFrames(t0, TOP_PLACEMENT);
  const topArmL =
    topIn.cutElapsedLMs !== null || topIn.armExtractedL
      ? t0.armL
      : ikArm(topIn.leftHand, t0.armL, "base", bFrames0, tFrames0, "L");
  const topArmR =
    topIn.cutElapsedRMs !== null || topIn.armExtractedR
      ? t0.armR
      : ikArm(topIn.rightHand, t0.armR, "base", bFrames0, tFrames0, "R");

  // Balance post: plant the recovering hand flat on the mat (y≈0) out to
  // the side, instead of leaving it on the canned reaching-out angle.
  const post = balancePost(topIn);
  let topArmLPosted = topArmL;
  let topArmRPosted = topArmR;
  if (post !== null) {
    const sideSign = post.side === "L" ? -1 : 1;
    const shoulder = post.side === "L" ? tFrames0.shoulderL : tFrames0.shoulderR;
    const matPoint: V3 = [shoulder[0] + sideSign * 0.18, 0.04, shoulder[2] + 0.06];
    const planted = solveArmIK(matPoint, shoulder, tFrames0.torsoRot, sideSign, 0.2, 0.0);
    if (post.side === "L") topArmLPosted = lerpArm(topArmL, planted, post.t);
    else topArmRPosted = lerpArm(topArmR, planted, post.t);
  }
  const topMid: BodyPose = { ...t0, armL: topArmLPosted, armR: topArmRPosted };

  // 2. Attacker hands plant on the *final* defender frame, so sleeve/wrist
  //    grips track the defender's actual hands.
  const tFrames = computeBodyFrames(topMid, TOP_PLACEMENT);
  const bottomArmL = ikArm(bottomIn.leftHand, b0.armL, "grip", tFrames, bFrames0, "L");
  const bottomArmR = ikArm(bottomIn.rightHand, b0.armR, "grip", tFrames, bFrames0, "R");
  const bottomMid: BodyPose = { ...b0, armL: bottomArmL, armR: bottomArmR };

  // 3. Grip coupling: a held sleeve/wrist drags the *defender's* arm along —
  //    their wrist is re-solved onto the gripping hand, so the pull reads on
  //    both bodies and the two hands visibly stay connected.
  const bFrames = computeBodyFrames(bottomMid, BOTTOM_PLACEMENT);
  const grippedSide = (zoneSide: "L" | "R"): V3 | null => {
    for (const [hand, pos] of [
      [bottomIn.leftHand, bFrames.handL],
      [bottomIn.rightHand, bFrames.handR],
    ] as const) {
      if (
        hand.state === "GRIPPED" &&
        (hand.target === `SLEEVE_${zoneSide}` || hand.target === `WRIST_${zoneSide}`)
      ) {
        return pos;
      }
    }
    return null;
  };
  const dragArm = (
    side: "L" | "R",
    current: ArmPose,
    blocked: boolean,
  ): ArmPose => {
    if (blocked) return current;
    const grabPoint = grippedSide(side);
    if (grabPoint === null) return current;
    const shoulder = side === "L" ? tFrames.shoulderL : tFrames.shoulderR;
    const dragged = solveArmIK(
      grabPoint,
      shoulder,
      tFrames.torsoRot,
      side === "L" ? -1 : 1,
      Math.max(current.tremor, 0.3), // fighting the grip
      0.15, // hand pried open by the grip
    );
    return dragged;
  };
  // A posting hand is committed to the mat — never re-purpose it as a drag.
  const topArmLFinal = dragArm(
    "L",
    topArmLPosted,
    topIn.cutElapsedLMs !== null || topIn.armExtractedL || post?.side === "L",
  );
  const topArmRFinal = dragArm(
    "R",
    topArmRPosted,
    topIn.cutElapsedRMs !== null || topIn.armExtractedR || post?.side === "R",
  );
  const topDragged: BodyPose = { ...topMid, armL: topArmLFinal, armR: topArmRFinal };

  // 4. Heads track the opponent.
  const bGaze = lookAt(bottomMid, bFrames, tFrames.headPos);
  const tGaze = lookAt(topDragged, tFrames, bFrames.headPos);

  const bottom: BodyPose = {
    ...bottomMid,
    headPitch: bGaze.pitch,
    headYaw: bGaze.yaw,
  };
  const top: BodyPose = {
    ...topDragged,
    headPitch: tGaze.pitch,
    headYaw: tGaze.yaw,
  };
  return { bottom, top };
}
