// Tests for Layer D commit resolver — docs/design/input_system_v1.md §D.1.1.

import { describe, expect, it } from "vitest";
import {
  INITIAL_LAYER_D_STATE,
  LAYER_D_TIMING,
  resolveLayerD,
  type LayerDInputs,
  type LayerDState,
} from "../src/input/layerD.js";
import { ButtonBit, type InputFrame } from "../src/input/types.js";
import type { HipIntent } from "../src/input/intent.js";
import type { Technique } from "../src/state/judgment_window.js";

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

function hip(over: Partial<HipIntent> = {}): HipIntent {
  return { hip_angle_target: 0, hip_push: 0, hip_lateral: 0, ...over };
}

type FrameOverride = {
  timestamp?: number;
  ls?: { x: number; y: number };
  rs?: { x: number; y: number };
  l_trigger?: number;
  r_trigger?: number;
  buttons?: number;
  button_edges?: number;
  device_kind?: InputFrame["device_kind"];
};

function inputs(
  over: {
    nowMs?: number;
    dtMs?: number;
    frame?: FrameOverride;
    hip?: Partial<HipIntent>;
    candidates?: ReadonlyArray<Technique>;
    windowIsOpen?: boolean;
  } = {},
): LayerDInputs {
  return {
    nowMs: over.nowMs ?? 0,
    dtMs: over.dtMs ?? 16.67,
    frame: frame(over.frame ?? {}),
    hip: hip(over.hip ?? {}),
    candidates: over.candidates ?? [],
    windowIsOpen: over.windowIsOpen ?? true,
  };
}

describe("Layer D inactive outside OPEN window", () => {
  it("never confirms while window is closed", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      windowIsOpen: false,
      candidates: ["SCISSOR_SWEEP"] as Technique[],
      frame: { button_edges: ButtonBit.L_BUMPER, buttons: ButtonBit.L_BUMPER, ls: { x: 1, y: 0 } },
    }));
    expect(out.confirmedTechnique).toBe(null);
  });
});

describe("Layer D per-technique commits (§D.1.1)", () => {
  it("SCISSOR_SWEEP: L_BUMPER edge + LS horizontal", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["SCISSOR_SWEEP"] as Technique[],
      frame: {
        button_edges: ButtonBit.L_BUMPER,
        buttons: ButtonBit.L_BUMPER,
        ls: { x: 1, y: 0 },
      },
    }));
    expect(out.confirmedTechnique).toBe("SCISSOR_SWEEP");
  });

  it("SCISSOR_SWEEP does NOT confirm if not a candidate", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["FLOWER_SWEEP"] as Technique[],
      frame: {
        button_edges: ButtonBit.L_BUMPER,
        buttons: ButtonBit.L_BUMPER,
        ls: { x: 1, y: 0 },
      },
    }));
    expect(out.confirmedTechnique).toBe(null);
  });

  it("FLOWER_SWEEP: R_BUMPER edge + LS up", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["FLOWER_SWEEP"] as Technique[],
      frame: {
        button_edges: ButtonBit.R_BUMPER,
        buttons: ButtonBit.R_BUMPER,
        ls: { x: 0, y: 1 },
      },
    }));
    expect(out.confirmedTechnique).toBe("FLOWER_SWEEP");
  });

  it("TRIANGLE: BTN_BASE held ≥ 500ms", () => {
    let s: LayerDState = INITIAL_LAYER_D_STATE;
    const stepMs = 50;
    const frames = Math.ceil(LAYER_D_TIMING.triangleHoldMs / stepMs);
    let confirmed: Technique | null = null;
    for (let i = 0; i < frames; i += 1) {
      const out = resolveLayerD(s, inputs({
        nowMs: i * stepMs,
        dtMs: stepMs,
        candidates: ["TRIANGLE"] as Technique[],
        frame: { buttons: ButtonBit.BTN_BASE },
      }));
      s = out.next;
      if (out.confirmedTechnique) {
        confirmed = out.confirmedTechnique;
        break;
      }
    }
    expect(confirmed).toBe("TRIANGLE");
  });

  it("TRIANGLE: short tap does NOT confirm", () => {
    let s: LayerDState = INITIAL_LAYER_D_STATE;
    const out = resolveLayerD(s, inputs({
      nowMs: 0,
      dtMs: 100,
      candidates: ["TRIANGLE"] as Technique[],
      frame: { buttons: ButtonBit.BTN_BASE },
    }));
    expect(out.confirmedTechnique).toBe(null);
  });

  it("OMOPLATA: L_BUMPER edge + RS up", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["OMOPLATA"] as Technique[],
      frame: {
        button_edges: ButtonBit.L_BUMPER,
        buttons: ButtonBit.L_BUMPER,
        rs: { x: 0, y: 1 },
      },
    }));
    expect(out.confirmedTechnique).toBe("OMOPLATA");
  });

  it("HIP_BUMP: R_TRIGGER rapid 0→1 within 200ms window", () => {
    // Tick 1: released, nowMs = 0.
    let s: LayerDState = INITIAL_LAYER_D_STATE;
    let r = resolveLayerD(s, inputs({
      nowMs: 0,
      candidates: ["HIP_BUMP"] as Technique[],
      frame: { r_trigger: 0 },
    }));
    s = r.next;
    expect(r.confirmedTechnique).toBe(null);

    // Tick 2: pressed to max within the window.
    r = resolveLayerD(s, inputs({
      nowMs: 100,
      candidates: ["HIP_BUMP"] as Technique[],
      frame: { r_trigger: 1 },
    }));
    expect(r.confirmedTechnique).toBe("HIP_BUMP");
  });

  it("HIP_BUMP: press is too slow (> 200ms) — no commit", () => {
    let s: LayerDState = INITIAL_LAYER_D_STATE;
    let r = resolveLayerD(s, inputs({
      nowMs: 0,
      candidates: ["HIP_BUMP"] as Technique[],
      frame: { r_trigger: 0 },
    }));
    s = r.next;
    r = resolveLayerD(s, inputs({
      nowMs: 300,
      candidates: ["HIP_BUMP"] as Technique[],
      frame: { r_trigger: 1 },
    }));
    expect(r.confirmedTechnique).toBe(null);
  });

  it("CROSS_COLLAR: both triggers ≥ 0.95 simultaneously", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["CROSS_COLLAR"] as Technique[],
      frame: { l_trigger: 1, r_trigger: 1 },
    }));
    expect(out.confirmedTechnique).toBe("CROSS_COLLAR");
  });

  it("CROSS_COLLAR: only one trigger maxed → no commit", () => {
    const out = resolveLayerD(INITIAL_LAYER_D_STATE, inputs({
      candidates: ["CROSS_COLLAR"] as Technique[],
      frame: { l_trigger: 1, r_trigger: 0.5 },
    }));
    expect(out.confirmedTechnique).toBe(null);
  });
});
