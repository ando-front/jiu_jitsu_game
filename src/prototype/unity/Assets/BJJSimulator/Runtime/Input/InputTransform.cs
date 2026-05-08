// Ported 1:1 from src/prototype/web/src/input/transform.ts.
// Pure transforms for Layer A assembly.
// Reference: docs/design/input_system_v1.md §A.3.
//
// All functions are static and side-effect-free; they port 1:1 to UE5 C++.

namespace BJJSimulator
{
    public static class InputTransform
    {
        // -----------------------------------------------------------------------
        // §A.3.1 — stick deadzone & response curve
        // -----------------------------------------------------------------------

        public const float StickInnerDeadzone  = 0.15f;
        public const float StickOuterDeadzone  = 0.95f;
        public const float StickCurveExponent  = 1.5f;

        /// <summary>
        /// Apply inner/outer deadzone and power-curve to a raw stick value.
        /// </summary>
        // Implementation note: arithmetic runs in double, the per-axis scale
        // factor is computed once, and only the final products are cast to
        // float. The earlier float-only path produced a 2-ULP X/Y asymmetry
        // for diagonal inputs (e.g. (0.5, 0.5) → r.X = 0x3ED161F1,
        // r.Y = 0x3ED161EF) on Mono ARM64 — almost certainly a JIT FMA /
        // vectorisation reorder. Computing `scale = curved / mag` once and
        // multiplying both axes by the same double makes symmetry obvious to
        // any reordering pass and matches the Stage 1 TS reference (Number
        // is double-precision in JS).
        public static Vec2 ApplyStickDeadzoneAndCurve(Vec2 raw)
        {
            double rx = raw.X;
            double ry = raw.Y;
            double magSq = rx * rx + ry * ry;
            double inner = StickInnerDeadzone;
            if (magSq < inner * inner)
                return Vec2.Zero;

            double mag = System.Math.Sqrt(magSq);
            double rescaled = System.Math.Min(
                1d,
                (mag - inner) / (StickOuterDeadzone - inner));
            double curved = System.Math.Pow(rescaled, StickCurveExponent);
            double scale  = curved / mag;
            float mx = (float)(rx * scale);
            float my = (float)(ry * scale);
            return new Vec2(mx, my);
        }

        // -----------------------------------------------------------------------
        // §A.3.2 — trigger deadzone (linear in active band)
        // -----------------------------------------------------------------------

        public const float TriggerLowerDeadzone = 0.05f;
        public const float TriggerUpperDeadzone = 0.95f;

        public static float ApplyTriggerDeadzone(float raw)
        {
            if (raw <= TriggerLowerDeadzone) return 0f;
            if (raw >= TriggerUpperDeadzone) return 1f;
            return (raw - TriggerLowerDeadzone) / (TriggerUpperDeadzone - TriggerLowerDeadzone);
        }

        // -----------------------------------------------------------------------
        // Button edges
        // -----------------------------------------------------------------------

        /// <summary>
        /// Bits that are newly SET this frame vs the previous frame.
        /// </summary>
        public static ButtonBit ComputeButtonEdges(ButtonBit prev, ButtonBit current)
            => current & ~prev;

        // -----------------------------------------------------------------------
        // §A.2.2 — keyboard 8-direction digital input → unit vector
        // -----------------------------------------------------------------------

        public static Vec2 EightWayFromDigital(bool up, bool down, bool left, bool right)
        {
            float x = (right ? 1f : 0f) - (left ? 1f : 0f);
            float y = (up    ? 1f : 0f) - (down ? 1f : 0f);
            if (x == 0f && y == 0f) return Vec2.Zero;
            float mag = (float)System.Math.Sqrt(x * x + y * y);
            return new Vec2(x / mag, y / mag);
        }

        // -----------------------------------------------------------------------
        // Gamepad activity heuristic (used by LayerA to pick active device)
        // -----------------------------------------------------------------------

        public const float GamepadActivityStickThreshold   = 0.2f;
        public const float GamepadActivityTriggerThreshold = 0.1f;

        public static bool GamepadHasActivity(Vec2 ls, Vec2 rs, float lTrigger, float rTrigger, ButtonBit buttons)
        {
            bool anyStick   = ls.Magnitude > GamepadActivityStickThreshold || rs.Magnitude > GamepadActivityStickThreshold;
            bool anyTrigger = lTrigger > GamepadActivityTriggerThreshold  || rTrigger > GamepadActivityTriggerThreshold;
            bool anyButton  = buttons != ButtonBit.None;
            return anyStick || anyTrigger || anyButton;
        }
    }
}
