// Ported from:
//   src/prototype/web/src/input/types.ts       (ButtonBit)
//   src/prototype/web/src/input/intent.ts      (GripZone)
//   src/prototype/web/src/state/hand_fsm.ts    (HandSide / HandState)
//   src/prototype/web/src/state/foot_fsm.ts    (FootSide / FootState)
//
// Port policy: enums with a sentinel "None" keep the sentinel at value 0
// so default-initialised structs land on it automatically.
//
// See docs/design/state_machines_v1.md §2.1–2.2 for transition tables.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Sides
    // -------------------------------------------------------------------------

    public enum HandSide { L, R }

    public enum FootSide { L, R }

    // -------------------------------------------------------------------------
    // Grip zones (§B.2.1 of input_system_v1.md)
    // None at ordinal 0 = default-initialised "no grip target"
    // -------------------------------------------------------------------------

    public enum GripZone
    {
        None = 0,
        SleeveL,
        SleeveR,
        CollarL,
        CollarR,
        WristL,
        WristR,
        Belt,
        PostureBreak,
    }

    // -------------------------------------------------------------------------
    // Hand FSM states (§2.1 of state_machines_v1.md)
    // -------------------------------------------------------------------------

    public enum HandState
    {
        Idle,
        Reaching,
        Contact,
        Gripped,
        Parried,
        Retract,
    }

    // -------------------------------------------------------------------------
    // Foot FSM states (§2.2 of state_machines_v1.md)
    // -------------------------------------------------------------------------

    public enum FootState
    {
        Locked,
        Unlocked,
        Locking,
    }

    // -------------------------------------------------------------------------
    // Input button bitflags (§A.2.1 of input_system_v1.md)
    // Mirrors ButtonBit from src/prototype/web/src/input/types.ts.
    // Stored in a single uint per frame; bitwise tests only.
    // -------------------------------------------------------------------------

    [System.Flags]
    public enum ButtonBit : uint
    {
        None        = 0,
        LBumper     = 1 << 0,
        RBumper     = 1 << 1,
        BtnBase     = 1 << 2,   // A / ✕ / Space
        BtnRelease  = 1 << 3,   // B / ◯ / X-key
        BtnBreath   = 1 << 4,   // Y / △ / C-key
        BtnReserved = 1 << 5,   // X / □ / V-key
        BtnPause    = 1 << 6,   // Options / Esc
    }

    // -------------------------------------------------------------------------
    // Sentinel value for uninitialised timestamps.
    // Ports Number.NEGATIVE_INFINITY from Stage 1 TS.
    // Always compare with == BJJConst.SentinelTimeMs before using in
    // arithmetic — otherwise a long.MinValue subtraction overflows.
    // See docs/design/stage2_port_plan_v1.md §2.5.
    // -------------------------------------------------------------------------

    public static class BJJConst
    {
        public const long SentinelTimeMs = long.MinValue;
    }

    // -------------------------------------------------------------------------
    // 2-D float vector. Mirrors Vec2 from src/prototype/web/src/state/game_state.ts.
    // Used by PostureBreak, ControlLayer, and GameState for (lateral, sagittal)
    // posture coordinates and grip-pull direction vectors.
    // -------------------------------------------------------------------------

    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        /// <summary>Euclidean magnitude.</summary>
        public float Magnitude => (float)System.Math.Sqrt(X * X + Y * Y);

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
    }
}
