// Ported 1:1 from src/prototype/web/src/state/arm_extracted.ts.
// PURE — arm_extracted flag update per docs/design/state_machines_v1.md §4.1.
//
// The flag sits on the TOP actor. Transition rules:
//   - attacker grips TOP's WRIST_*/SLEEVE_* with strength ≥ 0.6 AND pulls
//     in hip_pull direction for 1.5s sustained → flag := true (that side)
//   - if grip releases OR opponent base-holds to retract → flag := false
//   - auto-reset after 5s

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Config
    // -------------------------------------------------------------------------

    public struct ArmExtractedConfig
    {
        public int   RequiredSustainMs; // 1500
        public int   AutoResetAfterMs;  // 5000
        public float MinGripStrength;   // 0.6
        public float HipPullThreshold;  // hip_push ≤ -threshold

        public static readonly ArmExtractedConfig Default = new ArmExtractedConfig
        {
            RequiredSustainMs = 1500,
            AutoResetAfterMs  = 5000,
            MinGripStrength   = 0.6f,
            HipPullThreshold  = 0.3f,
        };
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public struct ArmExtractedState
    {
        public bool  Left;
        public bool  Right;
        // Sustained-pull accumulator per side, ms. Resets when any sustain condition breaks.
        public float LeftSustainMs;
        public float RightSustainMs;
        // Absolute timestamp the flag went true (used for the 5s auto-reset).
        public long  LeftSetAtMs;
        public long  RightSetAtMs;

        public static readonly ArmExtractedState Initial = new ArmExtractedState
        {
            Left           = false,
            Right          = false,
            LeftSustainMs  = 0f,
            RightSustainMs = 0f,
            LeftSetAtMs    = BJJConst.SentinelTimeMs,
            RightSetAtMs   = BJJConst.SentinelTimeMs,
        };
    }

    // -------------------------------------------------------------------------
    // Inputs
    // -------------------------------------------------------------------------

    public struct ArmExtractedInputs
    {
        public long     NowMs;
        public float    DtMs;
        public HandFSM  BottomLeftHand;
        public HandFSM  BottomRightHand;
        public float    TriggerL;
        public float    TriggerR;
        public HipIntent AttackerHip;
        // True if the defender's BTN_BASE is held this frame.
        public bool     DefenderBaseHold;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class ArmExtractedOps
    {
        public static ArmExtractedState Update(
            ArmExtractedState prev,
            ArmExtractedInputs inp,
            ArmExtractedConfig cfg = default)
        {
            if (cfg.RequiredSustainMs == 0) cfg = ArmExtractedConfig.Default;

            bool leftPulling  = AttackerPullingSide(HandSide.L, inp, cfg);
            bool rightPulling = AttackerPullingSide(HandSide.R, inp, cfg);

            float leftSustain  = leftPulling  ? prev.LeftSustainMs  + inp.DtMs : 0f;
            float rightSustain = rightPulling ? prev.RightSustainMs + inp.DtMs : 0f;

            bool left  = prev.Left;
            bool right = prev.Right;
            long leftSetAt  = prev.LeftSetAtMs;
            long rightSetAt = prev.RightSetAtMs;

            if (!left && leftSustain >= cfg.RequiredSustainMs)
            {
                left      = true;
                leftSetAt = inp.NowMs;
            }
            if (!right && rightSustain >= cfg.RequiredSustainMs)
            {
                right      = true;
                rightSetAt = inp.NowMs;
            }

            // Clear when pull stops, defender base-holds, or 5s elapses.
            if (left && (!leftPulling || inp.DefenderBaseHold ||
                (leftSetAt != BJJConst.SentinelTimeMs && inp.NowMs - leftSetAt >= cfg.AutoResetAfterMs)))
            {
                left      = false;
                leftSustain = 0f;
                leftSetAt = BJJConst.SentinelTimeMs;
            }
            if (right && (!rightPulling || inp.DefenderBaseHold ||
                (rightSetAt != BJJConst.SentinelTimeMs && inp.NowMs - rightSetAt >= cfg.AutoResetAfterMs)))
            {
                right       = false;
                rightSustain = 0f;
                rightSetAt  = BJJConst.SentinelTimeMs;
            }

            return new ArmExtractedState
            {
                Left           = left,
                Right          = right,
                LeftSustainMs  = leftSustain,
                RightSustainMs = rightSustain,
                LeftSetAtMs    = leftSetAt,
                RightSetAtMs   = rightSetAt,
            };
        }

        // -----------------------------------------------------------------------

        static bool AttackerPullingSide(
            HandSide opponentSide,
            ArmExtractedInputs inp,
            ArmExtractedConfig cfg)
        {
            if (inp.AttackerHip.HipPush > -cfg.HipPullThreshold) return false;

            bool wantLeft = opponentSide == HandSide.L;

            // Attacker's left hand gripping the opponent's side?
            bool lh = inp.BottomLeftHand.State == HandState.Gripped &&
                      IsExtractZone(inp.BottomLeftHand.Target) &&
                      ZoneMatchesSide(inp.BottomLeftHand.Target, wantLeft) &&
                      inp.TriggerL >= cfg.MinGripStrength;

            bool rh = inp.BottomRightHand.State == HandState.Gripped &&
                      IsExtractZone(inp.BottomRightHand.Target) &&
                      ZoneMatchesSide(inp.BottomRightHand.Target, wantLeft) &&
                      inp.TriggerR >= cfg.MinGripStrength;

            return lh || rh;
        }

        static bool IsExtractZone(GripZone z) =>
            z == GripZone.WristL || z == GripZone.WristR ||
            z == GripZone.SleeveL || z == GripZone.SleeveR;

        static bool ZoneMatchesSide(GripZone z, bool wantLeft)
        {
            if (wantLeft)
                return z == GripZone.SleeveL || z == GripZone.WristL;
            return z == GripZone.SleeveR || z == GripZone.WristR;
        }
    }
}
