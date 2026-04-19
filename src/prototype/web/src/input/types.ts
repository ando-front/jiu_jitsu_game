// Layer A output — the single per-frame struct handed to Layer B.
// Matches docs/design/input_system_v1.md §A.4 exactly.

export type DeviceKind = "Xbox" | "DualSense" | "Keyboard";

export type Vec2 = Readonly<{ x: number; y: number }>;

// Button bitmask layout. Do not reorder — tests rely on these indices.
export const ButtonBit = {
  L_BUMPER: 1 << 0,
  R_BUMPER: 1 << 1,
  BTN_BASE: 1 << 2,
  BTN_RELEASE: 1 << 3,
  BTN_BREATH: 1 << 4,
  BTN_RESERVED: 1 << 5,
  BTN_PAUSE: 1 << 6,
} as const;

export type ButtonName = keyof typeof ButtonBit;

export type InputFrame = Readonly<{
  timestamp: number; // ms (performance.now)
  ls: Vec2; // [-1,1]^2, post-deadzone + curve
  rs: Vec2;
  l_trigger: number; // [0,1]
  r_trigger: number;
  buttons: number; // bitmask; see ButtonBit
  button_edges: number; // bits set only on the frame the button went down
  device_kind: DeviceKind;
}>;

export const ZERO_VEC2: Vec2 = Object.freeze({ x: 0, y: 0 });

export function buttonIsDown(frame: InputFrame, name: ButtonName): boolean {
  return (frame.buttons & ButtonBit[name]) !== 0;
}

export function buttonWasPressed(
  frame: InputFrame,
  name: ButtonName,
): boolean {
  return (frame.button_edges & ButtonBit[name]) !== 0;
}
