// Tests for the defender-side stamina update — docs/design/state_machines_v1.md §5.

import { describe, expect, it } from "vitest";
import {
  DEFAULT_STAMINA_CONFIG,
  updateStaminaDefender,
  type StaminaDefenderInputs,
} from "../../src/state/stamina.js";
import { initialActorState, type ActorState } from "../../src/state/game_state.js";

function actor(over: Partial<ActorState> = {}): ActorState {
  return Object.freeze({ ...initialActorState(0), ...over });
}

function inputs(over: Partial<StaminaDefenderInputs> = {}): StaminaDefenderInputs {
  return {
    dtMs: 1000,
    actor: actor(),
    leftBasePressure: 0,
    rightBasePressure: 0,
    weightForward: 0,
    weightLateral: 0,
    breathPressed: false,
    ...over,
  };
}

describe("defender stamina drain", () => {
  it("left base pressure ≥ 0.5 drains at handActiveDrainPerSec", () => {
    const s = updateStaminaDefender(1, inputs({ leftBasePressure: 0.6 }));
    expect(s).toBeCloseTo(1 - DEFAULT_STAMINA_CONFIG.handActiveDrainPerSec, 4);
  });

  it("pressure below 0.5 does NOT trigger hand-active drain", () => {
    // Hand-active drain is gated by pressure ≥ 0.5. Recovery is also
    // gated by "no base active at all" (anyBaseActive > 0) so a 0.3
    // pressure sits in a zero-delta band — neither drain nor recovery.
    const s = updateStaminaDefender(0.5, inputs({ leftBasePressure: 0.3 }));
    expect(s).toBeCloseTo(0.5, 5);
  });

  it("defender's own posture_break drains proportionally", () => {
    const s = updateStaminaDefender(1, inputs({
      actor: actor({ postureBreak: { x: 0.5, y: 0 } }),
      leftBasePressure: 0.6, // hand active so idle recovery can't mask it
    }));
    const expected = 1 - (DEFAULT_STAMINA_CONFIG.handActiveDrainPerSec + 0.025);
    expect(s).toBeCloseTo(expected, 4);
  });
});

describe("defender stamina recovery", () => {
  it("breath + no base + static weight → breath recovery", () => {
    const s = updateStaminaDefender(0.5, inputs({ breathPressed: true }));
    expect(s).toBeCloseTo(0.5 + DEFAULT_STAMINA_CONFIG.breathRecoverPerSec, 4);
  });

  it("weight moving → recovery suppressed", () => {
    const s = updateStaminaDefender(0.5, inputs({
      breathPressed: true,
      weightForward: 0.6,
    }));
    expect(s).toBeCloseTo(0.5, 4);
  });

  it("all limbs idle + no base + static → idle recovery", () => {
    const s = updateStaminaDefender(0.5, inputs());
    expect(s).toBeCloseTo(0.5 + DEFAULT_STAMINA_CONFIG.idleRecoverPerSec, 4);
  });

  it("any base pressure (even weak) disables idle recovery", () => {
    const s = updateStaminaDefender(0.5, inputs({ leftBasePressure: 0.1 }));
    expect(s).toBeCloseTo(0.5, 4);
  });
});

describe("defender stamina clamps", () => {
  it("clamps to [0, 1]", () => {
    expect(updateStaminaDefender(0.99, inputs({ breathPressed: true }))).toBeLessThanOrEqual(1);
    expect(updateStaminaDefender(0.01, inputs({
      actor: actor({ postureBreak: { x: 1, y: 0 } }),
      leftBasePressure: 1, rightBasePressure: 1,
    }))).toBeGreaterThanOrEqual(0);
  });
});
