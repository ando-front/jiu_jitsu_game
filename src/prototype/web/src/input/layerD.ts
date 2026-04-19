// PURE — Layer D: resolves "technique confirm" inputs while the judgment
// window is OPEN. Reference: docs/design/input_system_v1.md §D.1.1 last column.
//
// This module owns the physical-input → Technique mapping that fires only
// while a window is OPEN. A few technique commits are patterns (e.g.
// "R_TRIGGER 0→1 within 200ms", "BTN_BASE hold 500ms") so Layer D keeps a
// small temporal state object. Like Layer B it is a pure transform that
// threads its state through the caller.

import { ButtonBit, type InputFrame } from "./types.js";
import type { HipIntent } from "./intent.js";
import type { Technique } from "../state/judgment_window.js";

// §D.1.1 thresholds — kept central for playtest tuning.
export const LAYER_D_TIMING = Object.freeze({
  triangleHoldMs: 500,        // BTN_BASE long press for triangle
  hipBumpPressWindowMs: 200,  // R_TRIGGER 0→1 must happen inside this window
});

export const LAYER_D_THRESHOLDS = Object.freeze({
  lsHorizontalAbs: 0.8,       // "LS pure horizontal"
  lsUp: 0.8,                  // "LS up"
  rsUp: 0.8,                  // "RS up"
  hipBumpReleasedMaxTrigger: 0.1,
  hipBumpPressedMinTrigger: 0.9,
  crossCollarBothMin: 0.95,   // §D.1.1 "L+R TRIGGER simultaneous max"
});

export type LayerDState = Readonly<{
  // Triangle long-press: frame count BTN_BASE has been held.
  btnBaseHeldMs: number;
  // Hip-bump rapid press tracking: timestamp when R_TRIGGER was most
  // recently seen at ≤ releasedMaxTrigger. If R_TRIGGER then crosses
  // pressedMinTrigger within `hipBumpPressWindowMs`, the commit fires.
  rTriggerLastReleasedMs: number;
  rTriggerPrevValue: number;
  // Cross-collar requires both triggers simultaneously at max — one tick
  // of "both ≥ threshold" is enough; no sustain needed.
}>;

export const INITIAL_LAYER_D_STATE: LayerDState = Object.freeze({
  btnBaseHeldMs: 0,
  rTriggerLastReleasedMs: Number.NEGATIVE_INFINITY,
  rTriggerPrevValue: 0,
});

export type LayerDInputs = Readonly<{
  nowMs: number;
  dtMs: number;
  frame: InputFrame;
  hip: HipIntent;
  // Candidate set currently lit by the judgment window. Layer D only
  // resolves commits for techniques actually in this list — otherwise a
  // pattern match could "confirm" a technique that wasn't a candidate.
  candidates: ReadonlyArray<Technique>;
  // Only active during window state OPEN per §8.1 — caller is responsible
  // for passing false when the window is CLOSED / OPENING / CLOSING.
  windowIsOpen: boolean;
}>;

export function resolveLayerD(
  prev: LayerDState,
  inp: LayerDInputs,
): { next: LayerDState; confirmedTechnique: Technique | null } {
  // Always update the per-frame tracking state, even when the window is
  // closed — otherwise the rapid-press window would start fresh the moment
  // OPENING fires and deny the intended "pre-loaded" hip bump input.
  const nextState = updateTrackingState(prev, inp);

  if (!inp.windowIsOpen) {
    return { next: nextState, confirmedTechnique: null };
  }

  // Resolve in the §D.1.1 table order. Returning on the first match
  // matches the design intent: one technique commits per frame.
  const candSet = new Set(inp.candidates);

  // SCISSOR_SWEEP: L_BUMPER edge + LS horizontal.
  if (
    candSet.has("SCISSOR_SWEEP") &&
    (inp.frame.button_edges & ButtonBit.L_BUMPER) !== 0 &&
    Math.abs(inp.frame.ls.x) >= LAYER_D_THRESHOLDS.lsHorizontalAbs
  ) {
    return { next: nextState, confirmedTechnique: "SCISSOR_SWEEP" };
  }

  // FLOWER_SWEEP: R_BUMPER edge + LS up.
  if (
    candSet.has("FLOWER_SWEEP") &&
    (inp.frame.button_edges & ButtonBit.R_BUMPER) !== 0 &&
    inp.frame.ls.y >= LAYER_D_THRESHOLDS.lsUp
  ) {
    return { next: nextState, confirmedTechnique: "FLOWER_SWEEP" };
  }

  // TRIANGLE: BTN_BASE held ≥ 500ms this frame.
  if (
    candSet.has("TRIANGLE") &&
    nextState.btnBaseHeldMs >= LAYER_D_TIMING.triangleHoldMs
  ) {
    return { next: nextState, confirmedTechnique: "TRIANGLE" };
  }

  // OMOPLATA: L_BUMPER edge + RS up.
  if (
    candSet.has("OMOPLATA") &&
    (inp.frame.button_edges & ButtonBit.L_BUMPER) !== 0 &&
    inp.frame.rs.y >= LAYER_D_THRESHOLDS.rsUp
  ) {
    return { next: nextState, confirmedTechnique: "OMOPLATA" };
  }

  // HIP_BUMP: R_TRIGGER 0→1 within the 200ms window.
  if (
    candSet.has("HIP_BUMP") &&
    inp.frame.r_trigger >= LAYER_D_THRESHOLDS.hipBumpPressedMinTrigger &&
    prev.rTriggerPrevValue < LAYER_D_THRESHOLDS.hipBumpPressedMinTrigger &&
    inp.nowMs - prev.rTriggerLastReleasedMs <= LAYER_D_TIMING.hipBumpPressWindowMs
  ) {
    return { next: nextState, confirmedTechnique: "HIP_BUMP" };
  }

  // CROSS_COLLAR: both triggers simultaneously at max.
  if (
    candSet.has("CROSS_COLLAR") &&
    inp.frame.l_trigger >= LAYER_D_THRESHOLDS.crossCollarBothMin &&
    inp.frame.r_trigger >= LAYER_D_THRESHOLDS.crossCollarBothMin
  ) {
    return { next: nextState, confirmedTechnique: "CROSS_COLLAR" };
  }

  return { next: nextState, confirmedTechnique: null };
}

function updateTrackingState(prev: LayerDState, inp: LayerDInputs): LayerDState {
  const baseHeld = (inp.frame.buttons & ButtonBit.BTN_BASE) !== 0;
  const btnBaseHeldMs = baseHeld ? prev.btnBaseHeldMs + inp.dtMs : 0;

  // Track the most recent "released" timestamp for the hip bump window.
  // If the current value is below the release threshold, remember now.
  const rel = inp.frame.r_trigger <= LAYER_D_THRESHOLDS.hipBumpReleasedMaxTrigger;
  const rTriggerLastReleasedMs = rel ? inp.nowMs : prev.rTriggerLastReleasedMs;

  return Object.freeze({
    btnBaseHeldMs,
    rTriggerLastReleasedMs,
    rTriggerPrevValue: inp.frame.r_trigger,
  });
}
