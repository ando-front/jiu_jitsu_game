// Unit tests for Layer B (Intent transforms).
// References docs/design/input_system_v1.md §B.1, §B.2, §B.3.

import { describe, expect, it } from "vitest";
import {
  DEFAULT_LAYER_B_CONFIG,
  INITIAL_LAYER_B_STATE,
  computeDiscreteIntents,
  computeGripIntent,
  computeHipIntent,
  transformLayerB,
  type LayerBState,
} from "../src/input/layerB.js";
import type { DiscreteIntent, GripZone } from "../src/input/intent.js";
import { ButtonBit, type InputFrame, type Vec2 } from "../src/input/types.js";

function makeFrame(overrides: Partial<InputFrame> = {}): InputFrame {
  const base: InputFrame = {
    timestamp: 0,
    ls: { x: 0, y: 0 },
    rs: { x: 0, y: 0 },
    l_trigger: 0,
    r_trigger: 0,
    buttons: 0,
    button_edges: 0,
    device_kind: "Keyboard",
  };
  return Object.freeze({ ...base, ...overrides }) as InputFrame;
}

function ls(x: number, y: number): Vec2 {
  return { x, y };
}

describe("hip intent (§B.1)", () => {
  it("centred stick produces zero hip intent", () => {
    const hip = computeHipIntent(makeFrame(), DEFAULT_LAYER_B_CONFIG);
    expect(hip.hip_angle_target).toBe(0);
    expect(hip.hip_push).toBe(0);
    expect(hip.hip_lateral).toBe(0);
  });

  it("stick pointing up yields zero angle (convention: atan2(x=0, y=+1) = 0)", () => {
    const hip = computeHipIntent(makeFrame({ ls: ls(0, 1) }), DEFAULT_LAYER_B_CONFIG);
    expect(hip.hip_angle_target).toBeCloseTo(0, 5);
    expect(hip.hip_push).toBe(1);
    expect(hip.hip_lateral).toBe(0);
  });

  it("pure-right stick yields +kAngleScale * π/2 yaw", () => {
    const hip = computeHipIntent(makeFrame({ ls: ls(1, 0) }), DEFAULT_LAYER_B_CONFIG);
    // atan2(1, 0) = π/2; scaled by 0.6.
    expect(hip.hip_angle_target).toBeCloseTo((Math.PI / 2) * 0.6, 5);
    expect(hip.hip_lateral).toBe(1);
  });

  it("hip_push and hip_lateral mirror LS exactly", () => {
    const hip = computeHipIntent(makeFrame({ ls: ls(-0.3, 0.8) }), DEFAULT_LAYER_B_CONFIG);
    expect(hip.hip_push).toBeCloseTo(0.8, 5);
    expect(hip.hip_lateral).toBeCloseTo(-0.3, 5);
  });
});

describe("grip zone selection (§B.2.1)", () => {
  it("centred stick + no triggers → no hand targets", () => {
    const { grip, nextZone } = computeGripIntent(
      makeFrame(),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(grip.l_hand_target).toBe(null);
    expect(grip.r_hand_target).toBe(null);
    expect(nextZone).toBe(null);
  });

  it("RS up-left selects COLLAR_L", () => {
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(-0.7, 0.7) }),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("COLLAR_L" satisfies GripZone);
  });

  it("RS pure-down selects BELT", () => {
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(0, -1) }),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("BELT" satisfies GripZone);
  });

  it("RS pure-up selects POSTURE_BREAK", () => {
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(0, 1) }),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("POSTURE_BREAK" satisfies GripZone);
  });

  it("both triggers + RS aiming → both hands share the zone (§B.2.2)", () => {
    const { grip } = computeGripIntent(
      makeFrame({ rs: ls(-1, 0), l_trigger: 0.8, r_trigger: 0.5 }),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(grip.l_hand_target).toBe("WRIST_L" satisfies GripZone);
    expect(grip.r_hand_target).toBe("WRIST_L" satisfies GripZone);
    expect(grip.l_grip_strength).toBeCloseTo(0.8);
    expect(grip.r_grip_strength).toBeCloseTo(0.5);
  });

  it("only L_TRIGGER down → only left hand has a target", () => {
    const { grip } = computeGripIntent(
      makeFrame({ rs: ls(1, 0), l_trigger: 1, r_trigger: 0 }),
      null,
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(grip.l_hand_target).toBe("WRIST_R" satisfies GripZone);
    expect(grip.r_hand_target).toBe(null);
  });
});

describe("grip zone hysteresis (§B.2.1)", () => {
  it("boundary nudge does NOT flip an already-selected zone", () => {
    // Held WRIST_L (pure left). Nudge RS toward up-left: direction is now
    // on the 45° boundary between WRIST_L and COLLAR_L — hysteresis should
    // keep us on WRIST_L.
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(-0.92, 0.38) }), // ≈ 22° above pure-left, still near WRIST_L wedge
      "WRIST_L",
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("WRIST_L" satisfies GripZone);
  });

  it("firm move past the threshold flips the zone", () => {
    // Now fully into the COLLAR_L wedge.
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(-0.6, 0.8) }),
      "WRIST_L",
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("COLLAR_L" satisfies GripZone);
  });

  it("releasing RS clears the zone (only when no trigger held)", () => {
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(0, 0) }),
      "WRIST_L",
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe(null);
  });

  it("releasing RS keeps the zone if a trigger is still held", () => {
    // Player is squeezing the trigger — we should not drop the target
    // zone just because they centred the stick while gripping.
    const { nextZone } = computeGripIntent(
      makeFrame({ rs: ls(0, 0), l_trigger: 1 }),
      "COLLAR_R",
      DEFAULT_LAYER_B_CONFIG,
    );
    expect(nextZone).toBe("COLLAR_R" satisfies GripZone);
  });
});

describe("discrete intents (§B.3)", () => {
  it("L_BUMPER edge emits FOOT_HOOK_TOGGLE(L)", () => {
    const list = computeDiscreteIntents(
      makeFrame({ button_edges: ButtonBit.L_BUMPER }),
    );
    expect(list).toEqual<DiscreteIntent[]>([{ kind: "FOOT_HOOK_TOGGLE", side: "L" }]);
  });

  it("BTN_BASE held (not edge) emits BASE_HOLD every frame", () => {
    const list = computeDiscreteIntents(
      makeFrame({ buttons: ButtonBit.BTN_BASE, button_edges: 0 }),
    );
    expect(list).toEqual<DiscreteIntent[]>([{ kind: "BASE_HOLD" }]);
  });

  it("BTN_RELEASE edge emits GRIP_RELEASE_ALL", () => {
    const list = computeDiscreteIntents(
      makeFrame({ button_edges: ButtonBit.BTN_RELEASE }),
    );
    expect(list).toEqual<DiscreteIntent[]>([{ kind: "GRIP_RELEASE_ALL" }]);
  });

  it("multiple simultaneous events all surface in one list", () => {
    const list = computeDiscreteIntents(
      makeFrame({
        buttons: ButtonBit.BTN_BASE,
        button_edges:
          ButtonBit.L_BUMPER |
          ButtonBit.BTN_BREATH |
          ButtonBit.BTN_RELEASE,
      }),
    );
    expect(list).toContainEqual<DiscreteIntent>({ kind: "FOOT_HOOK_TOGGLE", side: "L" });
    expect(list).toContainEqual<DiscreteIntent>({ kind: "BASE_HOLD" });
    expect(list).toContainEqual<DiscreteIntent>({ kind: "GRIP_RELEASE_ALL" });
    expect(list).toContainEqual<DiscreteIntent>({ kind: "BREATH_START" });
    expect(list).toHaveLength(4);
  });

  it("no buttons → empty list", () => {
    expect(computeDiscreteIntents(makeFrame())).toEqual([]);
  });
});

describe("transformLayerB integration", () => {
  it("threads state across frames so hysteresis survives", () => {
    let state: LayerBState = INITIAL_LAYER_B_STATE;

    // Frame 1: aim at WRIST_L firmly.
    const r1 = transformLayerB(
      makeFrame({ rs: ls(-1, 0) }),
      state,
    );
    state = r1.nextState;
    expect(state.lastZone).toBe("WRIST_L" satisfies GripZone);

    // Frame 2: drift toward the boundary but not enough to flip.
    const r2 = transformLayerB(
      makeFrame({ rs: ls(-0.92, 0.38) }),
      state,
    );
    state = r2.nextState;
    expect(state.lastZone).toBe("WRIST_L" satisfies GripZone);

    // Frame 3: move firmly into COLLAR_L — flip.
    const r3 = transformLayerB(
      makeFrame({ rs: ls(-0.6, 0.8) }),
      state,
    );
    expect(r3.nextState.lastZone).toBe("COLLAR_L" satisfies GripZone);
  });
});
