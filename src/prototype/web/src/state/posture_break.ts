// PURE — posture_break update per docs/design/state_machines_v1.md §3.
//
// Model: 2D vector (lateral, sagittal). Updated each sim step by combining:
//   1. exponential decay toward origin (τ = 800ms)
//   2. attacker hip input contribution
//   3. GRIPPED-hand "pull" contribution
//   4. defender recovery subtraction
//
// Coefficient values (K*) are placeholders; the design doc explicitly
// defers numeric tuning to post-M1. They live in one place so the
// playtest-driven rebalance is a single edit.

import type { HipIntent } from "../input/intent.js";
import type { Vec2 } from "./game_state.js";

export type PostureBreakConfig = Readonly<{
  decayTauMs: number;       // §3.3 time constant for self-decay
  kHipImpact: number;       // per-second magnitude contributed by 1.0 hip input
  kGripImpact: number;      // per-second magnitude contributed by a 1.0-strength grip
  kRecovery: number;        // per-second magnitude subtracted by 1.0 recovery input
  maxMagnitude: number;     // final clamp; 1.0 is the normative upper bound
}>;

export const DEFAULT_POSTURE_CONFIG: PostureBreakConfig = Object.freeze({
  decayTauMs: 800,
  kHipImpact: 0.9,
  kGripImpact: 0.6,
  kRecovery: 1.2,
  maxMagnitude: 1,
});

// What the update step needs to know from the world. Kept narrow so the
// caller can assemble it without pulling the whole GameState.
export type PostureBreakInputs = Readonly<{
  dtMs: number;              // game_dt (already time-scaled)
  // Attacker (bottom) contributions — drive posture break of the TOP actor.
  attackerHip: HipIntent;
  // Per-hand active pull: non-zero only when that hand is GRIPPED. The
  // direction encodes where the pull is dragging the opponent's upper body
  // in (lateral, sagittal) space. Caller is responsible for mapping
  // GripZone + grip_strength into a direction vector.
  gripPulls: ReadonlyArray<Vec2>;
  // Defender (top) recovery input: unit vector aimed at the origin scaled
  // by the recovery gain (e.g. from a BTN_BASE hold on the top side).
  // Stage 1 currently always passes ZERO_VEC2 here because top-side input
  // is not wired yet; preserved in the signature so the defender commit
  // is a single call-site change.
  defenderRecovery: Vec2;
}>;

export function updatePostureBreak(
  prev: Vec2,
  inputs: PostureBreakInputs,
  cfg: PostureBreakConfig = DEFAULT_POSTURE_CONFIG,
): Vec2 {
  const dtSec = inputs.dtMs / 1000;

  // 1. Exponential decay. Over one dt the state retains exp(-dt/τ).
  const decay = Math.exp(-inputs.dtMs / cfg.decayTauMs);
  let x = prev.x * decay;
  let y = prev.y * decay;

  // 2. Attacker hip contribution. hip_lateral maps to lateral push, and
  // hip_push maps to sagittal — a forward hip thrust (+) tips the opponent
  // forward. The mapping is a design choice that could be revisited, but
  // it's the most direct reading of §B.1 sign conventions.
  x += inputs.attackerHip.hip_lateral * cfg.kHipImpact * dtSec;
  y += inputs.attackerHip.hip_push * cfg.kHipImpact * dtSec;

  // 3. GRIPPED-hand pulls. Each entry is a direction vector with magnitude
  // proportional to (grip_strength · pull_efficacy); the caller already
  // baked those in. Summed in because "more grips = more pulling power"
  // is the desired BJJ feel.
  for (const p of inputs.gripPulls) {
    x += p.x * cfg.kGripImpact * dtSec;
    y += p.y * cfg.kGripImpact * dtSec;
  }

  // 4. Defender recovery subtracts in the direction of the recovery input.
  x -= inputs.defenderRecovery.x * cfg.kRecovery * dtSec;
  y -= inputs.defenderRecovery.y * cfg.kRecovery * dtSec;

  // Clamp magnitude so the vector stays inside the unit disc. Over-shoot
  // is physically meaningless (the opponent can't be "more than broken")
  // and would make §3.4's quantization mapping to paper-proto buckets 0–4
  // leak beyond 4.
  const mag = Math.hypot(x, y);
  if (mag > cfg.maxMagnitude) {
    const s = cfg.maxMagnitude / mag;
    x *= s;
    y *= s;
  }

  return Object.freeze({ x, y });
}

// §3.2 — derived query: is the overall break magnitude above a threshold?
// Useful for judgment-window conditions that reference "‖posture_break‖".
export function breakMagnitude(v: Vec2): number {
  return Math.hypot(v.x, v.y);
}

// §3.4 — paper-proto bucket (0..4). Primarily a HUD / debugging helper.
export function breakBucket(v: Vec2): 0 | 1 | 2 | 3 | 4 {
  const m = breakMagnitude(v);
  if (m < 0.1) return 0;
  if (m < 0.3) return 1;
  if (m < 0.5) return 2;
  if (m < 0.75) return 3;
  return 4;
}

// Encode a per-hand grip pull for §3.3 bullet 3.
// GripZone → unit direction in (lateral, sagittal) space. These are
// intentionally coarse; the real authority for grip→break mapping is
// playtest data, not this table.
import type { GripZone } from "../input/intent.js";

const ZONE_PULL_DIR: Readonly<Record<GripZone, Vec2>> = Object.freeze({
  // Sleeves pulled toward the attacker → forward-break on that side.
  SLEEVE_L: Object.freeze({ x: -0.5, y:  0.87 }), // forward + left
  SLEEVE_R: Object.freeze({ x:  0.5, y:  0.87 }),
  // Collar pulls are strong sagittal break (dumps the opponent forward).
  COLLAR_L: Object.freeze({ x: -0.3, y:  0.95 }),
  COLLAR_R: Object.freeze({ x:  0.3, y:  0.95 }),
  // Wrists pulled laterally drag the opponent sideways.
  WRIST_L:  Object.freeze({ x: -1,   y:  0 }),
  WRIST_R:  Object.freeze({ x:  1,   y:  0 }),
  // Belt pull drags the opponent forward and down.
  BELT:     Object.freeze({ x:  0,   y:  0.9 }),
  // POSTURE_BREAK pulls straight forward with full sagittal weight.
  POSTURE_BREAK: Object.freeze({ x: 0, y: 1 }),
});

export function gripPullVector(zone: GripZone, strength: number): Vec2 {
  const dir = ZONE_PULL_DIR[zone];
  return Object.freeze({ x: dir.x * strength, y: dir.y * strength });
}
