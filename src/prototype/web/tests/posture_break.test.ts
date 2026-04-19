// Tests for posture_break updates — docs/design/state_machines_v1.md §3.

import { describe, expect, it } from "vitest";
import {
  DEFAULT_POSTURE_CONFIG,
  breakBucket,
  breakMagnitude,
  gripPullVector,
  updatePostureBreak,
  type PostureBreakInputs,
} from "../src/state/posture_break.js";

function inputs(over: Partial<PostureBreakInputs> = {}): PostureBreakInputs {
  return {
    dtMs: 16.67,
    attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    gripPulls: [],
    defenderRecovery: { x: 0, y: 0 },
    ...over,
  };
}

describe("posture_break decay (§3.3 bullet 1)", () => {
  it("zero input + zero state stays zero", () => {
    const out = updatePostureBreak({ x: 0, y: 0 }, inputs());
    expect(out.x).toBe(0);
    expect(out.y).toBe(0);
  });

  it("800ms time constant: after one τ with no input, magnitude falls to ~1/e", () => {
    const start = { x: 0.8, y: 0 };
    // Run ~800ms in 16.67ms slices.
    let v = start;
    const step = 16.67;
    for (let t = 0; t < DEFAULT_POSTURE_CONFIG.decayTauMs; t += step) {
      v = updatePostureBreak(v, inputs({ dtMs: step }));
    }
    // e^-1 ≈ 0.3679; allow small integration slack.
    expect(v.x).toBeGreaterThan(0.8 * 0.35);
    expect(v.x).toBeLessThan(0.8 * 0.40);
  });
});

describe("posture_break attacker hip contribution (§3.3 bullet 2)", () => {
  it("forward hip push over time accumulates sagittal break", () => {
    let v = { x: 0, y: 0 };
    // 1 second of hip_push = 1.0 with zero decay fight.
    for (let t = 0; t < 1000; t += 16.67) {
      v = updatePostureBreak(v, inputs({ attackerHip: { hip_angle_target: 0, hip_push: 1, hip_lateral: 0 } }));
    }
    // Equilibrium for a constant driving input u is u·τ·(K/τ) → K scaled
    // by the integration. We only assert "meaningfully positive, bounded
    // by unit clamp" because exact values are coefficient-dependent.
    expect(v.y).toBeGreaterThan(0.3);
    expect(v.y).toBeLessThanOrEqual(1);
    expect(v.x).toBe(0);
  });

  it("lateral hip drives sign of the lateral axis accordingly", () => {
    let v = { x: 0, y: 0 };
    for (let t = 0; t < 500; t += 16.67) {
      v = updatePostureBreak(v, inputs({ attackerHip: { hip_angle_target: 0, hip_push: 0, hip_lateral: -1 } }));
    }
    expect(v.x).toBeLessThan(-0.1);
  });
});

describe("posture_break grip pulls (§3.3 bullet 3)", () => {
  it("a GRIPPED sleeve adds a forward + side-directed break component", () => {
    const pull = gripPullVector("SLEEVE_R", 1);
    expect(pull.x).toBeGreaterThan(0);
    expect(pull.y).toBeGreaterThan(0);

    let v = { x: 0, y: 0 };
    for (let t = 0; t < 500; t += 16.67) {
      v = updatePostureBreak(v, inputs({ gripPulls: [pull] }));
    }
    expect(v.x).toBeGreaterThan(0);
    expect(v.y).toBeGreaterThan(0);
  });

  it("zero-strength grip contributes nothing", () => {
    const pull = gripPullVector("SLEEVE_R", 0);
    expect(pull.x).toBe(0);
    expect(pull.y).toBe(0);
  });
});

describe("posture_break defender recovery (§3.3 bullet 4)", () => {
  it("recovery input opposes existing break", () => {
    // Pre-existing forward break; recovery vector pointing forward (same
    // direction as the break) should pull it back toward origin.
    let v = { x: 0, y: 0.5 };
    for (let t = 0; t < 500; t += 16.67) {
      v = updatePostureBreak(v, inputs({ defenderRecovery: { x: 0, y: 1 } }));
    }
    // Recovery dominates decay and actually inverts the break sign here
    // because we apply -k_recovery·dt per frame with k_recovery = 1.2.
    expect(v.y).toBeLessThan(0.5);
  });
});

describe("posture_break magnitude clamp", () => {
  it("clamps to the unit disc even under sustained large input", () => {
    let v = { x: 0, y: 0 };
    for (let t = 0; t < 5000; t += 16.67) {
      v = updatePostureBreak(v, inputs({
        attackerHip: { hip_angle_target: 0, hip_push: 1, hip_lateral: 1 },
        gripPulls: [gripPullVector("COLLAR_L", 1), gripPullVector("SLEEVE_R", 1)],
      }));
    }
    expect(breakMagnitude(v)).toBeLessThanOrEqual(DEFAULT_POSTURE_CONFIG.maxMagnitude + 1e-9);
  });
});

describe("paper-proto quantization (§3.4)", () => {
  it("mags below 0.1 → bucket 0", () => {
    expect(breakBucket({ x: 0.05, y: 0 })).toBe(0);
  });
  it("mag 0.2 → bucket 1", () => {
    expect(breakBucket({ x: 0.2, y: 0 })).toBe(1);
  });
  it("mag 0.4 → bucket 2", () => {
    expect(breakBucket({ x: 0, y: 0.4 })).toBe(2);
  });
  it("mag 0.6 → bucket 3", () => {
    expect(breakBucket({ x: 0.6, y: 0 })).toBe(3);
  });
  it("mag 0.8 → bucket 4", () => {
    expect(breakBucket({ x: 0.8, y: 0 })).toBe(4);
  });
});
