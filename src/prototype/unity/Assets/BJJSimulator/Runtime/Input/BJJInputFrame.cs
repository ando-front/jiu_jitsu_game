// Ported 1:1 from src/prototype/web/src/input/types.ts.
// Layer A output — the single per-frame struct handed to Layer B.
// Matches docs/design/input_system_v1.md §A.4 exactly.

namespace BJJSimulator
{
    public enum DeviceKind
    {
        Xbox,
        DualSense,
        Keyboard,
    }

    // InputFrame mirrors the TS struct. timestamps are long (ms); Vec2 replaces
    // { x, y } objects; ButtonBit (already in BJJCoreTypes) is the bitmask type.

    public struct InputFrame
    {
        public long      Timestamp;    // ms (performance.now equivalent)
        public Vec2      Ls;           // left stick [-1,1]^2, post-deadzone + curve
        public Vec2      Rs;           // right stick
        public float     LTrigger;     // [0, 1]
        public float     RTrigger;
        public ButtonBit Buttons;      // held bitmask
        public ButtonBit ButtonEdges;  // bits set only on the frame the button went DOWN
        public DeviceKind DeviceKind;

        // Convenience factory: a zeroed-out frame at a given timestamp.
        public static InputFrame Zero(long timestamp = 0) => new InputFrame
        {
            Timestamp   = timestamp,
            Ls          = Vec2.Zero,
            Rs          = Vec2.Zero,
            LTrigger    = 0f,
            RTrigger    = 0f,
            Buttons     = ButtonBit.None,
            ButtonEdges = ButtonBit.None,
            DeviceKind  = DeviceKind.Keyboard,
        };
    }
}
