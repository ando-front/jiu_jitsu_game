// Pure transforms for Layer A.
// Kept side-effect-free and framework-free so they port 1:1 to UE5 C++.
// Contract references docs/design/input_system_v1.md §A.3.

import type { Vec2 } from "./types.js";

// §A.3.1 — stick deadzone & curve
const STICK_INNER_DEADZONE = 0.15;
const STICK_OUTER_DEADZONE = 0.95;
const STICK_CURVE_EXPONENT = 1.5;

// §A.3.2 — trigger deadzone
const TRIGGER_LOWER_DEADZONE = 0.05;
const TRIGGER_UPPER_DEADZONE = 0.95;

export function applyStickDeadzoneAndCurve(raw: Vec2): Vec2 {
  const mag = Math.hypot(raw.x, raw.y);
  if (mag < STICK_INNER_DEADZONE) {
    return { x: 0, y: 0 };
  }
  // Rescale magnitude into [0,1] relative to the inner deadzone,
  // then saturate near the outer edge.
  const rescaled = Math.min(
    1,
    (mag - STICK_INNER_DEADZONE) / (STICK_OUTER_DEADZONE - STICK_INNER_DEADZONE),
  );
  const curved = Math.pow(rescaled, STICK_CURVE_EXPONENT);
  const nx = raw.x / mag;
  const ny = raw.y / mag;
  return { x: nx * curved, y: ny * curved };
}

export function applyTriggerDeadzone(raw: number): number {
  if (raw <= TRIGGER_LOWER_DEADZONE) return 0;
  if (raw >= TRIGGER_UPPER_DEADZONE) return 1;
  // Linear rescale in the active band. Trigger response is intentionally
  // linear per §A.3.2 — the "grip strength" metaphor is direct.
  return (
    (raw - TRIGGER_LOWER_DEADZONE) /
    (TRIGGER_UPPER_DEADZONE - TRIGGER_LOWER_DEADZONE)
  );
}

// Edge bits = bits that are newly set this frame vs. last frame.
export function computeButtonEdges(
  prev: number,
  current: number,
): number {
  return current & ~prev;
}

// Keyboard-specific: normalize 8-direction digital input to a unit vector.
// Matches §A.2.2 — diagonal = (±0.707, ±0.707).
export function eightWayFromDigital(up: boolean, down: boolean, left: boolean, right: boolean): Vec2 {
  const x = (right ? 1 : 0) - (left ? 1 : 0);
  const y = (up ? 1 : 0) - (down ? 1 : 0);
  if (x === 0 && y === 0) return { x: 0, y: 0 };
  const inv = 1 / Math.hypot(x, y);
  return { x: x * inv, y: y * inv };
}
