// PURE — Layer B (defender) per docs/design/input_system_defense_v1.md §B.
//
// Transforms an InputFrame into a DefenseIntent. Structurally parallel to
// input/layerB.ts but with different semantics for each control:
//   LS → weight_forward / weight_lateral
//   RS → base support zone (trigger held) or cut direction (bumper edge)
//   L/R TRIGGER → base pressure (continuous)
//   L/R BUMPER → cut attempt (edge)
//   BTN_BASE → recovery hold
//   BTN_RELEASE → base release
//   BTN_BREATH → breath recovery
//   BTN_RESERVED → pass commit

import {
  BASE_ZONE_DIRECTIONS,
  ZERO_TOP_BASE,
  type BaseZone,
  type DefenseDiscreteIntent,
  type DefenseIntent,
  type TopBaseIntent,
  type TopHipIntent,
} from "./intent_defense.js";
import { ButtonBit, type InputFrame } from "./types.js";

export type LayerBDefenseState = Readonly<{
  lastBaseZone: BaseZone | null;
}>;

export const INITIAL_LAYER_B_DEFENSE_STATE: LayerBDefenseState = Object.freeze({
  lastBaseZone: null,
});

// Same hysteresis logic as attacker Layer B, just operating on BaseZone.
const ZONE_SELECT_COS_THRESHOLD = Math.cos((30 * Math.PI) / 180);
const RS_MAGNITUDE_THRESHOLD = 0.2;

export function transformLayerBDefense(
  frame: InputFrame,
  prev: LayerBDefenseState,
): { intent: DefenseIntent; nextState: LayerBDefenseState } {
  const hip = computeTopHipIntent(frame);
  const { base, nextZone } = computeTopBaseIntent(frame, prev.lastBaseZone);
  const discrete = computeDefenseDiscreteIntents(frame);

  return {
    intent: Object.freeze({ hip, base, discrete }),
    nextState: Object.freeze({ lastBaseZone: nextZone }),
  };
}

export function computeTopHipIntent(frame: InputFrame): TopHipIntent {
  return Object.freeze({
    weight_forward: frame.ls.y,
    weight_lateral: frame.ls.x,
  });
}

// Base zones are used for trigger-held "support hand" placement. During
// a bumper edge, RS is repurposed for cut direction — base zone selection
// is suppressed that frame to avoid ambiguity.
export function computeTopBaseIntent(
  frame: InputFrame,
  lastZone: BaseZone | null,
): { base: TopBaseIntent; nextZone: BaseZone | null } {
  const bumperEdge =
    (frame.button_edges & (ButtonBit.L_BUMPER | ButtonBit.R_BUMPER)) !== 0;
  const anyTrigger = frame.l_trigger > 0 || frame.r_trigger > 0;
  const rsMag = Math.hypot(frame.rs.x, frame.rs.y);

  let nextZone: BaseZone | null = lastZone;

  // RS only feeds base-zone selection while a trigger is held AND no
  // bumper is edging this frame.
  if (!bumperEdge && anyTrigger && rsMag >= RS_MAGNITUDE_THRESHOLD) {
    const nx = frame.rs.x / rsMag;
    const ny = frame.rs.y / rsMag;
    const best = pickBestBaseZone(nx, ny);
    if (lastZone === null || best.zone === lastZone) {
      nextZone = best.zone;
    } else if (best.dot >= ZONE_SELECT_COS_THRESHOLD) {
      nextZone = best.zone;
    }
  } else if (!anyTrigger) {
    // No trigger held → nothing to place. Drop the zone so the next press
    // starts fresh.
    nextZone = null;
  }

  const l_hand_target = frame.l_trigger > 0 ? nextZone : null;
  const r_hand_target = frame.r_trigger > 0 ? nextZone : null;

  const base: TopBaseIntent =
    l_hand_target === null && r_hand_target === null
      ? ZERO_TOP_BASE
      : Object.freeze({
          l_hand_target,
          l_base_pressure: frame.l_trigger,
          r_hand_target,
          r_base_pressure: frame.r_trigger,
        });

  return { base, nextZone };
}

function pickBestBaseZone(nx: number, ny: number): { zone: BaseZone; dot: number } {
  let bestDot = -Infinity;
  let bestZone: BaseZone = BASE_ZONE_DIRECTIONS[0]!.zone;
  for (const { zone, dir } of BASE_ZONE_DIRECTIONS) {
    const d = nx * dir.x + ny * dir.y;
    if (d > bestDot) {
      bestDot = d;
      bestZone = zone;
    }
  }
  return { zone: bestZone, dot: bestDot };
}

export function computeDefenseDiscreteIntents(
  frame: InputFrame,
): ReadonlyArray<DefenseDiscreteIntent> {
  const out: DefenseDiscreteIntent[] = [];
  if ((frame.button_edges & ButtonBit.L_BUMPER) !== 0) {
    out.push({ kind: "CUT_ATTEMPT", side: "L", rs: frame.rs });
  }
  if ((frame.button_edges & ButtonBit.R_BUMPER) !== 0) {
    out.push({ kind: "CUT_ATTEMPT", side: "R", rs: frame.rs });
  }
  if ((frame.buttons & ButtonBit.BTN_BASE) !== 0) {
    out.push({ kind: "RECOVERY_HOLD" });
  }
  if ((frame.button_edges & ButtonBit.BTN_RELEASE) !== 0) {
    out.push({ kind: "BASE_RELEASE_ALL" });
  }
  if ((frame.button_edges & ButtonBit.BTN_BREATH) !== 0) {
    out.push({ kind: "BREATH_START" });
  }
  if ((frame.button_edges & ButtonBit.BTN_RESERVED) !== 0) {
    out.push({ kind: "PASS_COMMIT", rs: frame.rs });
  }
  if ((frame.button_edges & ButtonBit.BTN_PAUSE) !== 0) {
    out.push({ kind: "PAUSE" });
  }
  return Object.freeze(out);
}
