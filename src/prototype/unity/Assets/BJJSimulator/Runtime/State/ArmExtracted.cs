// Ported from src/prototype/web/src/state/arm_extracted.ts.
// See docs/design/state_machines_v1.md §4.1.
//
// The flag sits on the TOP actor (the one whose arm is being extracted).
// Transition rules:
//   - attacker grips TOP's WRIST_* / SLEEVE_* with strength ≥ 0.6 AND pulls
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
        public float RequiredSustainMs;   // 1500
        public float AutoResetAfterMs;    // 5000
        public float MinGripStrength;     // 0.6
        public float HipPullThreshold;   // hip_push ≤ -threshold

        public static readonly ArmExtractedConfig Default = new ArmExtractedConfig
        {
            RequiredSustainMs = 1500f,
            AutoResetAfterMs  = 5000f,
            MinGripStrength   = 0.6f,
            HipPullThreshold  = 0.3f,
        };
    }

    // -------------------------------------------------------------------------
    // State (value type)
    // -------------------------------------------------------------------------

    public struct ArmExtractedState
    {
        public bool  Left;
        public bool  Right;
        /// <summary>Sustained-pull accumulator per side (ms). Resets when any sustain condition breaks.</summary>
        public float LeftSustainMs;
        public float RightSustainMs;
        /// <summary>Absolute timestamp the flag went true (used for the 5s auto-reset). Sentinel = not set.</summary>
        public long  LeftSetAtMs;
        public long  RightSetAtMs;
    }

    // -------------------------------------------------------------------------
    // Inputs
    // -------------------------------------------------------------------------

    public struct ArmExtractedInputs
    {
        public long    NowMs;
        public float   DtMs;
        public HandFSM BottomLeftHand;
        public HandFSM BottomRightHand;
        public float   TriggerL;
        public float   TriggerR;
        public HipIntent AttackerHip;
        /// <summary>True if the defender's BTN_BASE is held (BICEP target ≥0.5).</summary>
        public bool    DefenderBaseHold;
    }

    // -------------------------------------------------------------------------
    // Event (unused externally but retained for symmetry with TS)
    // -------------------------------------------------------------------------

    public struct ArmExtractedEvent { }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class ArmExtractedOps
    {
        public static readonly ArmExtractedState Initial = new ArmExtractedState
        {
            Left           = false,
            Right          = false,
            LeftSustainMs  = 0f,
            RightSustainMs = 0f,
            LeftSetAtMs    = BJJConst.SentinelTimeMs,
            RightSetAtMs   = BJJConst.SentinelTimeMs,
        };

        public static ArmExtractedState Update(
            ArmExtractedState prev,
            ArmExtractedInputs inp,
            ArmExtractedConfig? cfg = null)
        {
            var c = cfg ?? ArmExtractedConfig.Default;

            bool leftPulling  = AttackerPullingSide(HandSide.L, inp, c);
            bool rightPulling = AttackerPullingSide(HandSide.R, inp, c);

            float leftSustain  = leftPulling  ? prev.LeftSustainMs  + inp.DtMs : 0f;
            float rightSustain = rightPulling ? prev.RightSustainMs + inp.DtMs : 0f;

            bool left      = prev.Left;
            bool right     = prev.Right;
            long leftSetAt = prev.LeftSetAtMs;
            long rightSetAt = prev.RightSetAtMs;

            if (!left  && leftSustain  >= c.RequiredSustainMs) { left  = true;  leftSetAt  = inp.NowMs; }
            if (!right && rightSustain >= c.RequiredSustainMs) { right = true;  rightSetAt = inp.NowMs; }

            // Clear conditions.
            bool leftSetValid  = leftSetAt  != BJJConst.SentinelTimeMs;
            bool rightSetValid = rightSetAt != BJJConst.SentinelTimeMs;

            if (left  && (!leftPulling  || inp.DefenderBaseHold ||
                          (leftSetValid  && inp.NowMs - leftSetAt  >= (long)c.AutoResetAfterMs)))
            {
                left = false; leftSustain = 0f; leftSetAt = BJJConst.SentinelTimeMs;
            }
            if (right && (!rightPulling || inp.DefenderBaseHold ||
                          (rightSetValid && inp.NowMs - rightSetAt >= (long)c.AutoResetAfterMs)))
            {
                right = false; rightSustain = 0f; rightSetAt = BJJConst.SentinelTimeMs;
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

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static bool AttackerPullingSide(
            HandSide opponentSide,
            ArmExtractedInputs inp,
            ArmExtractedConfig c)
        {
            if (inp.AttackerHip.HipPush > -c.HipPullThreshold) return false;

            var lh = inp.BottomLeftHand;
            var rh = inp.BottomRightHand;

            bool lhOk = lh.State == HandState.Gripped &&
                        IsExtractZone(lh.Target) &&
                        ZoneSideMatches(lh.Target, opponentSide) &&
                        inp.TriggerL >= c.MinGripStrength;

            bool rhOk = rh.State == HandState.Gripped &&
                        IsExtractZone(rh.Target) &&
                        ZoneSideMatches(rh.Target, opponentSide) &&
                        inp.TriggerR >= c.MinGripStrength;

            return lhOk || rhOk;
        }

        private static bool IsExtractZone(GripZone zone) =>
            zone == GripZone.WristL  || zone == GripZone.WristR  ||
            zone == GripZone.SleeveL || zone == GripZone.SleeveR;

        /// <summary>True when the zone's L/R suffix matches <paramref name="side"/>.</summary>
        private static bool ZoneSideMatches(GripZone zone, HandSide side)
        {
            if (side == HandSide.L)
                return zone == GripZone.WristL || zone == GripZone.SleeveL;
            return zone == GripZone.WristR || zone == GripZone.SleeveR;
        }
    }
}
