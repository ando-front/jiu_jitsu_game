// Unit tests for Layer A pure transforms + assembler.
// References docs/design/input_system_v1.md §A.3 / §A.4 / §A.2.2.

import { describe, expect, it } from "vitest";
import {
  applyStickDeadzoneAndCurve,
  applyTriggerDeadzone,
  computeButtonEdges,
  eightWayFromDigital,
} from "../src/input/transform.js";
import { KeyboardSource } from "../src/input/keyboard.js";
import { GamepadSource } from "../src/input/gamepad.js";
import { LayerA } from "../src/input/layerA.js";
import { ButtonBit } from "../src/input/types.js";

describe("stick deadzone + curve (§A.3.1)", () => {
  it("zeroes out inputs inside the inner deadzone (0.15)", () => {
    const out = applyStickDeadzoneAndCurve({ x: 0.10, y: 0.05 });
    expect(out).toEqual({ x: 0, y: 0 });
  });

  it("saturates at the outer deadzone (≥0.95 → 1.0 magnitude)", () => {
    const out = applyStickDeadzoneAndCurve({ x: 0.96, y: 0 });
    expect(out.x).toBeCloseTo(1, 5);
    expect(out.y).toBeCloseTo(0, 5);
  });

  it("applies exponent-1.5 curve in the middle band", () => {
    // Input half-way through the active band: rescaled ≈ 0.5 → curved ≈ 0.5^1.5 ≈ 0.3536
    const raw = { x: (0.15 + 0.95) / 2, y: 0 };
    const out = applyStickDeadzoneAndCurve(raw);
    expect(out.x).toBeCloseTo(Math.pow(0.5, 1.5), 4);
    expect(out.y).toBe(0);
  });

  it("preserves direction after curve", () => {
    const out = applyStickDeadzoneAndCurve({ x: 0.5, y: 0.5 });
    expect(Math.abs(out.x - out.y)).toBeLessThan(1e-9);
  });
});

describe("trigger deadzone (§A.3.2)", () => {
  it("clamps values ≤0.05 to 0", () => {
    expect(applyTriggerDeadzone(0.04)).toBe(0);
  });

  it("clamps values ≥0.95 to 1", () => {
    expect(applyTriggerDeadzone(0.97)).toBe(1);
  });

  it("is linear in the active band", () => {
    // Midpoint of the active band [0.05, 0.95] is 0.5 → expect 0.5.
    expect(applyTriggerDeadzone(0.5)).toBeCloseTo(0.5, 5);
  });
});

describe("eight-way digital normalization (§A.2.2)", () => {
  it("diagonal magnitudes are unit length", () => {
    const out = eightWayFromDigital(true, false, true, false); // up-left
    expect(Math.hypot(out.x, out.y)).toBeCloseTo(1, 5);
    expect(out.x).toBeCloseTo(-Math.SQRT1_2, 5);
    expect(out.y).toBeCloseTo(Math.SQRT1_2, 5);
  });

  it("opposing keys cancel to zero", () => {
    expect(eightWayFromDigital(true, true, false, false)).toEqual({ x: 0, y: 0 });
  });

  it("no keys → zero vector", () => {
    expect(eightWayFromDigital(false, false, false, false)).toEqual({ x: 0, y: 0 });
  });
});

describe("button edge detection (§B.3 rationale — edges vs holds)", () => {
  it("edge fires only on the transition 0→1", () => {
    expect(computeButtonEdges(0b0000, 0b0101)).toBe(0b0101);
  });

  it("held buttons produce no edge", () => {
    expect(computeButtonEdges(0b0101, 0b0101)).toBe(0);
  });

  it("release produces no edge bit", () => {
    expect(computeButtonEdges(0b1111, 0b0000)).toBe(0);
  });

  it("partially new bits are isolated", () => {
    // Bit 0 was already held, bit 2 is newly pressed → only bit 2 edges.
    expect(computeButtonEdges(0b0001, 0b0101)).toBe(0b0100);
  });
});

describe("LayerA assembler (§A.4)", () => {
  // Happy path without a connected gamepad: keyboard path is used.
  it("falls back to keyboard when no gamepad is connected", () => {
    const gp = new GamepadSource();
    const kb = new KeyboardSource();
    kb.setKeyForTest("KeyD", true); // ls right
    kb.setKeyForTest("KeyF", true); // l_trigger
    kb.setKeyForTest("Space", true); // BTN_BASE

    const a = new LayerA(gp, kb);
    const frame = a.sample(1000);

    expect(frame.device_kind).toBe("Keyboard");
    expect(frame.ls.x).toBeCloseTo(1, 5);
    expect(frame.ls.y).toBeCloseTo(0, 5);
    expect(frame.l_trigger).toBe(1);
    expect((frame.buttons & ButtonBit.BTN_BASE) !== 0).toBe(true);
    expect((frame.button_edges & ButtonBit.BTN_BASE) !== 0).toBe(true);
  });

  it("timestamp passes through the injected now value", () => {
    const a = new LayerA(new GamepadSource(), new KeyboardSource());
    const frame = a.sample(42);
    expect(frame.timestamp).toBe(42);
  });

  it("edge bits are computed across successive samples", () => {
    const kb = new KeyboardSource();
    const a = new LayerA(new GamepadSource(), kb);

    kb.setKeyForTest("KeyC", true); // BTN_BREATH
    const f1 = a.sample(16);
    expect((f1.button_edges & ButtonBit.BTN_BREATH) !== 0).toBe(true);

    // Still held — no edge this frame.
    const f2 = a.sample(32);
    expect((f2.button_edges & ButtonBit.BTN_BREATH) !== 0).toBe(false);
    expect((f2.buttons & ButtonBit.BTN_BREATH) !== 0).toBe(true);
  });
});
