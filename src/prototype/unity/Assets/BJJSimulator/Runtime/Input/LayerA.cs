// Ported from src/prototype/web/src/input/layerA.ts.
// Layer A assembler — combines hardware snapshots into a single InputFrame.
// Reference: docs/design/input_system_v1.md §A.4.
//
// In Stage 2 the keyboard/gamepad sources from Stage 1 are replaced by
// Unity's New Input System (com.unity.inputsystem). This file provides:
//
//   1. A pure-C# struct (LayerAState) that holds prevButtons across frames.
//   2. A static pure helper (LayerAOps.Assemble) that builds an InputFrame
//      from raw device values — testable without Unity.
//
// For the actual MonoBehaviour that calls Assemble() each FixedUpdate, see
// GameManager.cs (to be created in the rendering layer phase).
//
// Device-priority rule (§A.1 last bullet): gamepad wins whenever it has
// recent activity; keyboard is the fallback. Gamepad wins ties.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Raw hardware snapshot (hardware-agnostic; filled by MonoBehaviour)
    // -------------------------------------------------------------------------

    public struct RawHardwareSnapshot
    {
        // Gamepad
        public bool  GamepadConnected;
        public Vec2  GamepadLs;           // raw [-1,1], before deadzone
        public Vec2  GamepadRs;
        public float GamepadLTrigger;     // [0,1]
        public float GamepadRTrigger;
        public ButtonBit GamepadButtons;
        public DeviceKind DeviceKind;     // Xbox / DualSense

        // Keyboard (already binarised)
        public bool LsUp, LsDown, LsLeft, LsRight;
        public bool RsUp, RsDown, RsLeft, RsRight;
        public bool KbLTrigger, KbRTrigger;
        public ButtonBit KbButtons;
    }

    // -------------------------------------------------------------------------
    // Stateful per-player assembler state
    // -------------------------------------------------------------------------

    public struct LayerAState
    {
        public ButtonBit PrevButtons;

        public static readonly LayerAState Initial = new LayerAState
        {
            PrevButtons = ButtonBit.None,
        };
    }

    // -------------------------------------------------------------------------
    // Pure assembly logic
    // -------------------------------------------------------------------------

    public static class LayerAOps
    {
        /// <summary>
        /// Build one InputFrame from the current hardware snapshot.
        /// Returns the new frame AND the updated LayerAState (prevButtons).
        /// </summary>
        public static (InputFrame Frame, LayerAState NextState) Assemble(
            LayerAState prev,
            RawHardwareSnapshot hw,
            long nowMs)
        {
            bool useGamepad = hw.GamepadConnected &&
                InputTransform.GamepadHasActivity(
                    hw.GamepadLs, hw.GamepadRs, hw.GamepadLTrigger, hw.GamepadRTrigger, hw.GamepadButtons);

            Vec2 ls, rs;
            float lTrigger, rTrigger;
            ButtonBit buttons;
            DeviceKind deviceKind;

            if (useGamepad)
            {
                ls         = InputTransform.ApplyStickDeadzoneAndCurve(hw.GamepadLs);
                rs         = InputTransform.ApplyStickDeadzoneAndCurve(hw.GamepadRs);
                lTrigger   = InputTransform.ApplyTriggerDeadzone(hw.GamepadLTrigger);
                rTrigger   = InputTransform.ApplyTriggerDeadzone(hw.GamepadRTrigger);
                buttons    = hw.GamepadButtons;
                deviceKind = hw.DeviceKind;
            }
            else
            {
                ls         = InputTransform.EightWayFromDigital(hw.LsUp, hw.LsDown, hw.LsLeft, hw.LsRight);
                rs         = InputTransform.EightWayFromDigital(hw.RsUp, hw.RsDown, hw.RsLeft, hw.RsRight);
                lTrigger   = hw.KbLTrigger ? 1f : 0f;
                rTrigger   = hw.KbRTrigger ? 1f : 0f;
                buttons    = hw.KbButtons;
                deviceKind = DeviceKind.Keyboard;
            }

            ButtonBit edges = InputTransform.ComputeButtonEdges(prev.PrevButtons, buttons);

            var frame = new InputFrame
            {
                Timestamp   = nowMs,
                Ls          = ls,
                Rs          = rs,
                LTrigger    = lTrigger,
                RTrigger    = rTrigger,
                Buttons     = buttons,
                ButtonEdges = edges,
                DeviceKind  = deviceKind,
            };

            return (frame, new LayerAState { PrevButtons = buttons });
        }
    }
}
