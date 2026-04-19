// PURE — Layer B output types.
// See docs/design/input_system_v1.md §B for contracts.
// These structures are the boundary handed to state machines; any change
// here propagates to state/* modules and must be reflected in the design doc.

export type HipIntent = Readonly<{
  hip_angle_target: number; // radians, Yaw around the opponent-centric axis
  hip_push: number;         // [-1, 1], + = push away, - = pull in
  hip_lateral: number;      // [-1, 1], ± = side-cut direction
}>;

// §B.2.1 — eight grip target zones plus "no target" (None).
export type GripZone =
  | "SLEEVE_L"
  | "SLEEVE_R"
  | "COLLAR_L"
  | "COLLAR_R"
  | "WRIST_L"
  | "WRIST_R"
  | "BELT"
  | "POSTURE_BREAK";

// Zone directions in RS space (attacker viewpoint). Stored as unit vectors
// so the zone selector can use a plain dot product; the §B.2.1 table is
// encoded here once and tested in tests/layerB.test.ts.
export const GRIP_ZONE_DIRECTIONS: ReadonlyArray<
  Readonly<{ zone: GripZone; dir: Readonly<{ x: number; y: number }> }>
> = Object.freeze([
  // Down-left / down-right (sleeves). RS y is "up positive" per gamepad.ts.
  { zone: "SLEEVE_L", dir: Object.freeze({ x: -Math.SQRT1_2, y: -Math.SQRT1_2 }) },
  { zone: "SLEEVE_R", dir: Object.freeze({ x:  Math.SQRT1_2, y: -Math.SQRT1_2 }) },
  // Up-left / up-right (collars).
  { zone: "COLLAR_L", dir: Object.freeze({ x: -Math.SQRT1_2, y:  Math.SQRT1_2 }) },
  { zone: "COLLAR_R", dir: Object.freeze({ x:  Math.SQRT1_2, y:  Math.SQRT1_2 }) },
  // Pure left / right (wrists).
  { zone: "WRIST_L",  dir: Object.freeze({ x: -1, y:  0 }) },
  { zone: "WRIST_R",  dir: Object.freeze({ x:  1, y:  0 }) },
  // Straight down (belt).
  { zone: "BELT",     dir: Object.freeze({ x:  0, y: -1 }) },
  // Straight up (posture break, requires two hands).
  { zone: "POSTURE_BREAK", dir: Object.freeze({ x: 0, y: 1 }) },
]);

export type GripIntent = Readonly<{
  l_hand_target: GripZone | null;
  l_grip_strength: number; // [0, 1]
  r_hand_target: GripZone | null;
  r_grip_strength: number;
}>;

// §B.3 — discrete/edge intents. Encoded as a tagged list so multiple can
// fire in one frame without loss (e.g. BTN_RELEASE + L_BUMPER on the same
// frame is legal and must be preserved).
export type DiscreteIntent =
  | { kind: "FOOT_HOOK_TOGGLE"; side: "L" | "R" }
  | { kind: "BASE_HOLD" }                 // held, not edge
  | { kind: "GRIP_RELEASE_ALL" }
  | { kind: "BREATH_START" }
  | { kind: "PAUSE" };

export type Intent = Readonly<{
  hip: HipIntent;
  grip: GripIntent;
  discrete: ReadonlyArray<DiscreteIntent>;
}>;

export const ZERO_HIP: HipIntent = Object.freeze({
  hip_angle_target: 0,
  hip_push: 0,
  hip_lateral: 0,
});

export const ZERO_GRIP: GripIntent = Object.freeze({
  l_hand_target: null,
  l_grip_strength: 0,
  r_hand_target: null,
  r_grip_strength: 0,
});
