// Ported from src/prototype/web/src/input/types.ts.
// See docs/design/input_system_v1.md §A.4 for the per-frame contract.
//
// Stage 2 note: LayerA (keyboard / gamepad sampling) is replaced by the
// New Input System wrapper (Runtime/Input/LayerA.cs, ⚪ pending). This
// struct is the boundary both stages share; all downstream logic reads
// InputFrame only.

namespace BJJSimulator
{
    public enum DeviceKind { Xbox, DualSense, Keyboard }

    /// <summary>
    /// Single per-frame raw-input snapshot produced by Layer A.
    /// Mirrors <c>InputFrame</c> in Stage 1 TypeScript.
    /// </summary>
    public struct InputFrame
    {
        /// <summary>Absolute timestamp in milliseconds (Unity Time.time * 1000).</summary>
        public long      TimestampMs;
        /// <summary>Left stick [-1, 1]² post-deadzone.</summary>
        public Vec2      LS;
        /// <summary>Right stick [-1, 1]² post-deadzone.</summary>
        public Vec2      RS;
        /// <summary>Left trigger [0, 1].</summary>
        public float     LTrigger;
        /// <summary>Right trigger [0, 1].</summary>
        public float     RTrigger;
        /// <summary>Bitmask of currently-held buttons; see <see cref="ButtonBit"/>.</summary>
        public ButtonBit Buttons;
        /// <summary>Bits set only on the frame the button went down (edge detect).</summary>
        public ButtonBit ButtonEdges;
        public DeviceKind Device;

        /// <summary>Returns true if the named button bit is currently held.</summary>
        public bool IsDown(ButtonBit bit) => (Buttons & bit) != 0;

        /// <summary>Returns true if the named button bit was pressed this frame.</summary>
        public bool WasPressed(ButtonBit bit) => (ButtonEdges & bit) != 0;

        public static readonly InputFrame Zero = new InputFrame
        {
            TimestampMs  = 0,
            LS           = Vec2.Zero,
            RS           = Vec2.Zero,
            LTrigger     = 0f,
            RTrigger     = 0f,
            Buttons      = ButtonBit.None,
            ButtonEdges  = ButtonBit.None,
            Device       = DeviceKind.Keyboard,
        };
    }
}
