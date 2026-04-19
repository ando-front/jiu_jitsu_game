// Tests for ControlLayer (initiative) — docs/design/state_machines_v1.md §7.

import { describe, expect, it } from "vitest";
import {
  INITIAL_CONTROL_LAYER,
  updateControlLayer,
  type ControlLayerInputs,
} from "../../src/state/control_layer.js";
import { INITIAL_JUDGMENT_WINDOW } from "../../src/state/judgment_window.js";
import { initialActorState, type ActorState } from "../../src/state/game_state.js";
import type { HandFSM } from "../../src/state/hand_fsm.js";

function gripped(side: "L" | "R"): HandFSM {
  return Object.freeze({
    side,
    state: "GRIPPED" as const,
    target: "SLEEVE_R" as const,
    stateEnteredMs: 0,
    reachDurationMs: 0,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

function actor(over: Partial<ActorState> = {}): ActorState {
  return Object.freeze({ ...initialActorState(0), ...over });
}

function inputs(over: Partial<ControlLayerInputs> = {}): ControlLayerInputs {
  return {
    judgmentWindow: INITIAL_JUDGMENT_WINDOW,
    bottom: actor(),
    top: actor(),
    defenderCutInProgress: false,
    ...over,
  };
}

describe("ControlLayer rule order (§7.2)", () => {
  it("judgment window OPEN locks initiative to the firing side", () => {
    const win = Object.freeze({
      ...INITIAL_JUDGMENT_WINDOW,
      state: "OPEN" as const,
      firedBy: "Bottom" as const,
      stateEnteredMs: 0,
      candidates: Object.freeze(["SCISSOR_SWEEP" as const]),
    });
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs({ judgmentWindow: win }));
    expect(out.initiative).toBe("Bottom");
    expect(out.lockedByWindow).toBe(true);
  });

  it("arm_extracted on bottom → Bottom", () => {
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs({
      bottom: actor({ armExtractedLeft: true }),
    }));
    expect(out.initiative).toBe("Bottom");
  });

  it("arm_extracted on top → Top", () => {
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs({
      top: actor({ armExtractedRight: true }),
    }));
    expect(out.initiative).toBe("Top");
  });

  it("bottom with ≥2 GRIPPED hands → Bottom", () => {
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs({
      bottom: actor({ leftHand: gripped("L"), rightHand: gripped("R") }),
    }));
    expect(out.initiative).toBe("Bottom");
  });

  it("defender cut in progress → Top", () => {
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs({
      defenderCutInProgress: true,
    }));
    expect(out.initiative).toBe("Top");
  });

  it("nothing applies → Neutral", () => {
    const out = updateControlLayer(INITIAL_CONTROL_LAYER, inputs());
    expect(out.initiative).toBe("Neutral");
    expect(out.lockedByWindow).toBe(false);
  });

  it("window lock releases when window returns to CLOSED", () => {
    const lockedPrev = Object.freeze({ initiative: "Bottom" as const, lockedByWindow: true });
    const out = updateControlLayer(lockedPrev, inputs());
    expect(out.lockedByWindow).toBe(false);
    expect(out.initiative).toBe("Neutral");
  });
});
