// PURE — Layer B: InputFrame → Intent transformation.
// Contracts: docs/design/input_system_v1.md §B.1 (hip), §B.2 (grip), §B.3 (discrete).
// No DOM / no Three.js; importable from tests and (eventually) UE5 ports.

import {
  GRIP_ZONE_DIRECTIONS,
  type DiscreteIntent,
  type GripIntent,
  type GripZone,
  type HipIntent,
  type Intent,
  ZERO_GRIP,
} from "./intent.js";
import { ButtonBit, type InputFrame } from "./types.js";

// §B.1 — LS scaling constants. hip_angle_target caps at ≈ ±π/2 * 0.6 ≈ ±0.94 rad
// because a stick-full tilt should not rotate the pelvis beyond the natural
// seated range in closed guard. Full hip-escape uses a separate move.
const K_ANGLE_SCALE = 0.6;

// §B.2.1 — hysteresis: once a zone is selected, switching requires the new
// stick direction to be within ±15° of the new zone centre. Expressed as
// a dot product threshold: cos(45° - 15°) = cos 30° ≈ 0.866 for "inside
// the new zone wedge" and cos(45°) ≈ 0.707 for "on the boundary".
const ZONE_SELECT_COS_THRESHOLD = Math.cos((30 * Math.PI) / 180); // ≈ 0.866

// Below this stick magnitude the grip "target" is considered absent.
// Without this the zone selector would snap to whatever tiny residual
// direction exists when the player has centred RS.
const RS_MAGNITUDE_THRESHOLD = 0.2;

export type LayerBConfig = Readonly<{
  kAngleScale: number;
  zoneSelectCosThreshold: number;
  rsMagnitudeThreshold: number;
}>;

export const DEFAULT_LAYER_B_CONFIG: LayerBConfig = Object.freeze({
  kAngleScale: K_ANGLE_SCALE,
  zoneSelectCosThreshold: ZONE_SELECT_COS_THRESHOLD,
  rsMagnitudeThreshold: RS_MAGNITUDE_THRESHOLD,
});

// Layer B carries minimal per-player state:
//   - lastZone: previous frame's selected zone (for hysteresis)
// State is kept external so Layer B itself stays a pure transform.
export type LayerBState = Readonly<{
  lastZone: GripZone | null;
}>;

export const INITIAL_LAYER_B_STATE: LayerBState = Object.freeze({
  lastZone: null,
});

export function transformLayerB(
  frame: InputFrame,
  prev: LayerBState,
  config: LayerBConfig = DEFAULT_LAYER_B_CONFIG,
): { intent: Intent; nextState: LayerBState } {
  const hip = computeHipIntent(frame, config);
  const { grip, nextZone } = computeGripIntent(frame, prev.lastZone, config);
  const discrete = computeDiscreteIntents(frame);

  const intent: Intent = Object.freeze({ hip, grip, discrete });
  const nextState: LayerBState = Object.freeze({ lastZone: nextZone });
  return { intent, nextState };
}

// §B.1
export function computeHipIntent(frame: InputFrame, cfg: LayerBConfig): HipIntent {
  const ls = frame.ls;
  // atan2(x, y) keeps 0 when LS points purely "up" (away from camera in our
  // sign convention). Scaled by kAngleScale (§B.1 rationale).
  const angle = Math.atan2(ls.x, ls.y) * cfg.kAngleScale;
  return Object.freeze({
    hip_angle_target: angle,
    hip_push: ls.y,
    hip_lateral: ls.x,
  });
}

// §B.2 — grip intent with hysteresis.
// The stick direction picks the *candidate* zone; the previous zone stays
// selected unless the new direction is within the tighter threshold of a
// different zone. This prevents grip "jitter" at the 45° boundaries.
export function computeGripIntent(
  frame: InputFrame,
  lastZone: GripZone | null,
  cfg: LayerBConfig,
): { grip: GripIntent; nextZone: GripZone | null } {
  const rsMag = Math.hypot(frame.rs.x, frame.rs.y);
  const anyTriggerDown = frame.l_trigger > 0 || frame.r_trigger > 0;

  // Zone stays meaningful only while (a) the player is actively aiming (RS
  // above threshold) OR (b) they are already holding a trigger on a
  // previously-chosen zone. When both fail, we drop back to None so the
  // FSM can route the hand to neutral.
  let nextZone: GripZone | null = lastZone;
  if (rsMag >= cfg.rsMagnitudeThreshold) {
    const nx = frame.rs.x / rsMag;
    const ny = frame.rs.y / rsMag;
    const best = pickBestZone(nx, ny);
    if (lastZone === null || best.zone === lastZone) {
      nextZone = best.zone;
    } else if (best.dot >= cfg.zoneSelectCosThreshold) {
      // New zone only wins if the stick is firmly inside its wedge.
      nextZone = best.zone;
    }
    // else: keep lastZone (hysteresis hold)
  } else if (!anyTriggerDown) {
    nextZone = null;
  }

  // §B.2.2 — each trigger independently gates whether its hand has a target.
  // RS is shared: both hands aim at the same zone when both triggers are down.
  const l_hand_target = frame.l_trigger > 0 ? nextZone : null;
  const r_hand_target = frame.r_trigger > 0 ? nextZone : null;

  const grip: GripIntent =
    l_hand_target === null && r_hand_target === null
      ? ZERO_GRIP
      : Object.freeze({
          l_hand_target,
          l_grip_strength: frame.l_trigger,
          r_hand_target,
          r_grip_strength: frame.r_trigger,
        });

  return { grip, nextZone };
}

function pickBestZone(nx: number, ny: number): { zone: GripZone; dot: number } {
  let bestDot = -Infinity;
  let bestZone: GripZone = GRIP_ZONE_DIRECTIONS[0]!.zone;
  for (const { zone, dir } of GRIP_ZONE_DIRECTIONS) {
    const d = nx * dir.x + ny * dir.y;
    if (d > bestDot) {
      bestDot = d;
      bestZone = zone;
    }
  }
  return { zone: bestZone, dot: bestDot };
}

// §B.3 — button → discrete intents.
// Edge intents are emitted only on the frame the edge occurs; BASE_HOLD
// appears every frame the button is held so downstream can react
// continuously.
export function computeDiscreteIntents(frame: InputFrame): ReadonlyArray<DiscreteIntent> {
  const out: DiscreteIntent[] = [];
  if ((frame.button_edges & ButtonBit.L_BUMPER) !== 0) {
    out.push({ kind: "FOOT_HOOK_TOGGLE", side: "L" });
  }
  if ((frame.button_edges & ButtonBit.R_BUMPER) !== 0) {
    out.push({ kind: "FOOT_HOOK_TOGGLE", side: "R" });
  }
  if ((frame.buttons & ButtonBit.BTN_BASE) !== 0) {
    out.push({ kind: "BASE_HOLD" });
  }
  if ((frame.button_edges & ButtonBit.BTN_RELEASE) !== 0) {
    out.push({ kind: "GRIP_RELEASE_ALL" });
  }
  if ((frame.button_edges & ButtonBit.BTN_BREATH) !== 0) {
    out.push({ kind: "BREATH_START" });
  }
  if ((frame.button_edges & ButtonBit.BTN_PAUSE) !== 0) {
    out.push({ kind: "PAUSE" });
  }
  return Object.freeze(out);
}
