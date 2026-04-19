// PURE — defender-side Layer D commit resolver per
// docs/design/input_system_defense_v1.md §D.2.
//
// Only resolves commits while the defender's counter window is OPEN.
// Mirrors the attacker's Layer D structure but keys on different input
// patterns:
//   SCISSOR_COUNTER:       LS max opposite to the attacker's sweep direction
//   TRIANGLE_EARLY_STACK:  BTN_BASE hold ≥ 500ms AND LS straight up

import { ButtonBit, type InputFrame } from "./types.js";
import type { CounterTechnique } from "../state/counter_window.js";

export const LAYER_D_DEFENSE_TIMING = Object.freeze({
  stackHoldMs: 500,
});

export const LAYER_D_DEFENSE_THRESHOLDS = Object.freeze({
  lsOppositeAbs: 0.8,
  lsUp: 0.8,
});

export type LayerDDefenseState = Readonly<{
  btnBaseHeldMs: number;
}>;

export const INITIAL_LAYER_D_DEFENSE_STATE: LayerDDefenseState = Object.freeze({
  btnBaseHeldMs: 0,
});

export type LayerDDefenseInputs = Readonly<{
  nowMs: number;
  dtMs: number;
  frame: InputFrame;
  candidates: ReadonlyArray<CounterTechnique>;
  windowIsOpen: boolean;
  // §D.2 — SCISSOR_COUNTER requires LS pointing OPPOSITE to the attacker's
  // sweep direction. The attacker's sweep direction is proxied by the
  // sign of their bottom hip lateral input at window OPEN. The caller
  // snapshots it at OPENING entry and feeds it here.
  attackerSweepLateralSign: number; // -1 or +1; 0 if not applicable
}>;

export function resolveLayerDDefense(
  prev: LayerDDefenseState,
  inp: LayerDDefenseInputs,
): { next: LayerDDefenseState; confirmedCounter: CounterTechnique | null } {
  const baseHeld = (inp.frame.buttons & ButtonBit.BTN_BASE) !== 0;
  const next: LayerDDefenseState = Object.freeze({
    btnBaseHeldMs: baseHeld ? prev.btnBaseHeldMs + inp.dtMs : 0,
  });

  if (!inp.windowIsOpen) {
    return { next, confirmedCounter: null };
  }

  const cand = new Set(inp.candidates);

  // SCISSOR_COUNTER: LS max in the opposite direction to the sweep.
  // If attackerSweepLateralSign is +1 (sweep pushing opponent's right),
  // defender must push LS to -X (their left) at ≥ threshold magnitude.
  if (
    cand.has("SCISSOR_COUNTER") &&
    inp.attackerSweepLateralSign !== 0 &&
    Math.sign(inp.frame.ls.x) === -Math.sign(inp.attackerSweepLateralSign) &&
    Math.abs(inp.frame.ls.x) >= LAYER_D_DEFENSE_THRESHOLDS.lsOppositeAbs
  ) {
    return { next, confirmedCounter: "SCISSOR_COUNTER" };
  }

  // TRIANGLE_EARLY_STACK: BTN_BASE held ≥ 500ms AND LS up.
  if (
    cand.has("TRIANGLE_EARLY_STACK") &&
    next.btnBaseHeldMs >= LAYER_D_DEFENSE_TIMING.stackHoldMs &&
    inp.frame.ls.y >= LAYER_D_DEFENSE_THRESHOLDS.lsUp
  ) {
    return { next, confirmedCounter: "TRIANGLE_EARLY_STACK" };
  }

  return { next, confirmedCounter: null };
}
