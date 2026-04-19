// Tests for Layer D (defender) commit resolver — docs/design/input_system_defense_v1.md §D.2.

import { describe, expect, it } from "vitest";
import {
  INITIAL_LAYER_D_DEFENSE_STATE,
  LAYER_D_DEFENSE_TIMING,
  resolveLayerDDefense,
  type LayerDDefenseInputs,
  type LayerDDefenseState,
} from "../../src/input/layerD_defense.js";
import { ButtonBit, type InputFrame } from "../../src/input/types.js";
import type { CounterTechnique } from "../../src/state/counter_window.js";

function frame(over: {
  timestamp?: number;
  ls?: { x: number; y: number };
  rs?: { x: number; y: number };
  l_trigger?: number;
  r_trigger?: number;
  buttons?: number;
  button_edges?: number;
  device_kind?: InputFrame["device_kind"];
} = {}): InputFrame {
  return Object.freeze({
    timestamp: over.timestamp ?? 0,
    ls: over.ls ?? { x: 0, y: 0 },
    rs: over.rs ?? { x: 0, y: 0 },
    l_trigger: over.l_trigger ?? 0,
    r_trigger: over.r_trigger ?? 0,
    buttons: over.buttons ?? 0,
    button_edges: over.button_edges ?? 0,
    device_kind: over.device_kind ?? "Keyboard",
  });
}

function inputs(over: {
  nowMs?: number;
  dtMs?: number;
  frame?: Parameters<typeof frame>[0];
  candidates?: ReadonlyArray<CounterTechnique>;
  windowIsOpen?: boolean;
  attackerSweepLateralSign?: number;
} = {}): LayerDDefenseInputs {
  return {
    nowMs: over.nowMs ?? 0,
    dtMs: over.dtMs ?? 16.67,
    frame: frame(over.frame ?? {}),
    candidates: over.candidates ?? [],
    windowIsOpen: over.windowIsOpen ?? true,
    attackerSweepLateralSign: over.attackerSweepLateralSign ?? 0,
  };
}

describe("Layer D (defender) inactive outside OPEN window", () => {
  it("never confirms while window is closed", () => {
    const r = resolveLayerDDefense(INITIAL_LAYER_D_DEFENSE_STATE, inputs({
      windowIsOpen: false,
      candidates: ["SCISSOR_COUNTER"],
      attackerSweepLateralSign: 1,
      frame: { ls: { x: -1, y: 0 } },
    }));
    expect(r.confirmedCounter).toBe(null);
  });
});

describe("SCISSOR_COUNTER commit", () => {
  it("fires when LS pushes opposite to the attacker's sweep direction", () => {
    // Attacker sweep sign = +1 → defender must push LS to -x ≥ 0.8 mag.
    const r = resolveLayerDDefense(INITIAL_LAYER_D_DEFENSE_STATE, inputs({
      candidates: ["SCISSOR_COUNTER"],
      attackerSweepLateralSign: 1,
      frame: { ls: { x: -1, y: 0 } },
    }));
    expect(r.confirmedCounter).toBe("SCISSOR_COUNTER");
  });

  it("does NOT fire when LS is in the SAME direction as the sweep", () => {
    const r = resolveLayerDDefense(INITIAL_LAYER_D_DEFENSE_STATE, inputs({
      candidates: ["SCISSOR_COUNTER"],
      attackerSweepLateralSign: 1,
      frame: { ls: { x: 1, y: 0 } },
    }));
    expect(r.confirmedCounter).toBe(null);
  });

  it("does NOT fire on weak magnitude even if direction is correct", () => {
    const r = resolveLayerDDefense(INITIAL_LAYER_D_DEFENSE_STATE, inputs({
      candidates: ["SCISSOR_COUNTER"],
      attackerSweepLateralSign: 1,
      frame: { ls: { x: -0.5, y: 0 } },
    }));
    expect(r.confirmedCounter).toBe(null);
  });
});

describe("TRIANGLE_EARLY_STACK commit", () => {
  it("fires after 500ms BTN_BASE hold + LS up", () => {
    let s: LayerDDefenseState = INITIAL_LAYER_D_DEFENSE_STATE;
    const step = 50;
    let confirmed: CounterTechnique | null = null;
    const frames = Math.ceil(LAYER_D_DEFENSE_TIMING.stackHoldMs / step);
    for (let i = 0; i < frames; i += 1) {
      const r = resolveLayerDDefense(s, inputs({
        nowMs: i * step, dtMs: step,
        candidates: ["TRIANGLE_EARLY_STACK"],
        frame: { buttons: ButtonBit.BTN_BASE, ls: { x: 0, y: 1 } },
      }));
      s = r.next;
      if (r.confirmedCounter !== null) {
        confirmed = r.confirmedCounter;
        break;
      }
    }
    expect(confirmed).toBe("TRIANGLE_EARLY_STACK");
  });

  it("does NOT fire if LS is not pointing up", () => {
    // Hold BASE long enough but LS sideways → no commit.
    let s: LayerDDefenseState = INITIAL_LAYER_D_DEFENSE_STATE;
    const step = 50;
    const frames = Math.ceil(LAYER_D_DEFENSE_TIMING.stackHoldMs / step) + 2;
    let confirmed: CounterTechnique | null = null;
    for (let i = 0; i < frames; i += 1) {
      const r = resolveLayerDDefense(s, inputs({
        nowMs: i * step, dtMs: step,
        candidates: ["TRIANGLE_EARLY_STACK"],
        frame: { buttons: ButtonBit.BTN_BASE, ls: { x: 1, y: 0 } }, // sideways, not up
      }));
      s = r.next;
      if (r.confirmedCounter !== null) confirmed = r.confirmedCounter;
    }
    expect(confirmed).toBe(null);
  });
});
