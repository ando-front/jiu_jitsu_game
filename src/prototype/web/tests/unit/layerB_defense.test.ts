// Tests for defender Layer B — docs/design/input_system_defense_v1.md §B.

import { describe, expect, it } from "vitest";
import {
  INITIAL_LAYER_B_DEFENSE_STATE,
  computeDefenseDiscreteIntents,
  computeTopBaseIntent,
  computeTopHipIntent,
  transformLayerBDefense,
} from "../../src/input/layerB_defense.js";
import type { BaseZone, DefenseDiscreteIntent } from "../../src/input/intent_defense.js";
import { ButtonBit, type InputFrame } from "../../src/input/types.js";

function frame(over: Partial<InputFrame> = {}): InputFrame {
  return Object.freeze({
    timestamp: 0,
    ls: { x: 0, y: 0 },
    rs: { x: 0, y: 0 },
    l_trigger: 0,
    r_trigger: 0,
    buttons: 0,
    button_edges: 0,
    device_kind: "Keyboard" as const,
    ...over,
  });
}

describe("TopHipIntent (§B.3)", () => {
  it("LS x/y maps straight to weight_lateral / weight_forward", () => {
    const hip = computeTopHipIntent(frame({ ls: { x: -0.4, y: 0.8 } }));
    expect(hip.weight_forward).toBe(0.8);
    expect(hip.weight_lateral).toBe(-0.4);
  });
});

describe("TopBaseIntent zone selection (§B.4.2)", () => {
  it("RS up + trigger held → CHEST", () => {
    const { base, nextZone } = computeTopBaseIntent(
      frame({ rs: { x: 0, y: 1 }, l_trigger: 0.5 }),
      null,
    );
    expect(nextZone).toBe("CHEST" satisfies BaseZone);
    expect(base.l_hand_target).toBe("CHEST");
  });

  it("RS down-right + trigger held → KNEE_R", () => {
    const { nextZone } = computeTopBaseIntent(
      frame({ rs: { x: 0.7, y: -0.7 }, r_trigger: 0.6 }),
      null,
    );
    expect(nextZone).toBe("KNEE_R" satisfies BaseZone);
  });

  it("no trigger held → base is ZERO_TOP_BASE and zone clears", () => {
    const { base, nextZone } = computeTopBaseIntent(
      frame({ rs: { x: 1, y: 0 } }),
      "BICEP_L",
    );
    expect(base.l_hand_target).toBe(null);
    expect(base.r_hand_target).toBe(null);
    expect(nextZone).toBe(null);
  });

  it("bumper edge suppresses zone selection (§B.4 RS re-routed to cut)", () => {
    const { nextZone } = computeTopBaseIntent(
      frame({
        button_edges: ButtonBit.L_BUMPER,
        buttons: ButtonBit.L_BUMPER,
        rs: { x: 0, y: 1 }, // would otherwise select CHEST
        l_trigger: 0.5,
      }),
      "BICEP_L",
    );
    expect(nextZone).toBe("BICEP_L"); // held by hysteresis, not overridden
  });

  it("both triggers down → both hands share the zone", () => {
    const { base } = computeTopBaseIntent(
      frame({ rs: { x: -1, y: 0 }, l_trigger: 0.8, r_trigger: 0.6 }),
      null,
    );
    // RS pure left is an even dot product with KNEE_L and BICEP_L.
    // Whichever wins, both hands target the same zone.
    expect(base.l_hand_target).not.toBe(null);
    expect(base.l_hand_target).toBe(base.r_hand_target);
  });
});

describe("Defender discrete intents (§B.6)", () => {
  it("L_BUMPER edge emits CUT_ATTEMPT(L) with RS snapshot", () => {
    const list = computeDefenseDiscreteIntents(frame({
      button_edges: ButtonBit.L_BUMPER,
      buttons: ButtonBit.L_BUMPER,
      rs: { x: -0.8, y: 0.4 },
    }));
    const cut = list.find((d): d is Extract<DefenseDiscreteIntent, { kind: "CUT_ATTEMPT" }> =>
      d.kind === "CUT_ATTEMPT");
    expect(cut).toBeDefined();
    expect(cut?.side).toBe("L");
    expect(cut?.rs.x).toBe(-0.8);
  });

  it("BTN_BASE held → RECOVERY_HOLD each frame", () => {
    const list = computeDefenseDiscreteIntents(frame({ buttons: ButtonBit.BTN_BASE }));
    expect(list).toContainEqual<DefenseDiscreteIntent>({ kind: "RECOVERY_HOLD" });
  });

  it("BTN_RESERVED edge → PASS_COMMIT with RS direction", () => {
    const list = computeDefenseDiscreteIntents(frame({
      button_edges: ButtonBit.BTN_RESERVED,
      buttons: ButtonBit.BTN_RESERVED,
      rs: { x: 0.2, y: -0.9 },
    }));
    const commit = list.find((d): d is Extract<DefenseDiscreteIntent, { kind: "PASS_COMMIT" }> =>
      d.kind === "PASS_COMMIT");
    expect(commit).toBeDefined();
    expect(commit?.rs.y).toBe(-0.9);
  });
});

describe("transformLayerBDefense threading", () => {
  it("returns ZERO base when no trigger is held", () => {
    const { intent } = transformLayerBDefense(
      frame({ rs: { x: 1, y: 0 } }),
      INITIAL_LAYER_B_DEFENSE_STATE,
    );
    expect(intent.base.l_hand_target).toBe(null);
    expect(intent.base.r_hand_target).toBe(null);
  });

  it("passes hip unchanged while producing a base intent under trigger", () => {
    const { intent } = transformLayerBDefense(
      frame({ ls: { x: 0.3, y: -0.4 }, rs: { x: 0, y: 1 }, l_trigger: 0.7 }),
      INITIAL_LAYER_B_DEFENSE_STATE,
    );
    expect(intent.hip.weight_forward).toBe(-0.4);
    expect(intent.hip.weight_lateral).toBe(0.3);
    expect(intent.base.l_hand_target).toBe("CHEST");
    expect(intent.base.l_base_pressure).toBe(0.7);
  });
});
