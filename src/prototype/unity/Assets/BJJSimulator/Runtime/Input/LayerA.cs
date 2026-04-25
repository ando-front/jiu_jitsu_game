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

        // Keyboard activity hints — used by the assembler to ignore a noisy
        // gamepad while the human is actually typing. KbLastEventMs uses the
        // BJJConst.SentinelTimeMs sentinel for "no event seen yet"; guard any
        // subtraction with `!= SentinelTimeMs` to avoid overflow.
        public bool KbAnyHeld;
        public long KbLastEventMs;
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
        // Keyboard wins over the gamepad for at least this long after any
        // tracked-key event. Mirrors KB_RECENT_MS in
        // src/prototype/web/src/input/layerA.ts §A.4 — picked to outlast a
        // single rAF / Update tick but not so long that releasing the
        // keyboard leaves the pad ignored forever.
        public const long KbRecentMs = 1500L;

        /// <summary>
        /// Build one InputFrame from the current hardware snapshot.
        /// Returns the new frame AND the updated LayerAState (prevButtons).
        /// </summary>
        public static (InputFrame Frame, LayerAState NextState) Assemble(
            LayerAState prev,
            RawHardwareSnapshot hw,
            long nowMs)
        {
            // Tie-breaker: if the keyboard is currently held OR was touched
            // within KbRecentMs, ignore the gamepad even if it reports
            // activity. Protects against noisy IR remotes / dead-zone-broken
            // pads that pin axes at -1 and would otherwise lock keyboard
            // input out forever.
            bool kbActive =
                hw.KbAnyHeld ||
                (hw.KbLastEventMs != BJJConst.SentinelTimeMs &&
                 (nowMs - hw.KbLastEventMs) < KbRecentMs);

            bool useGamepad = hw.GamepadConnected && !kbActive &&
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
