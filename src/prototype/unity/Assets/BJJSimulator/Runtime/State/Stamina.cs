// Ported from src/prototype/web/src/state/stamina.ts.
// See docs/design/state_machines_v1.md §5.
//
// Model: single scalar [0, 1].
// Per-second rates sum, then integrate over dt.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Config
    // -------------------------------------------------------------------------

    public struct StaminaConfig
    {
        // §5.2 drain rates
        public float HandActiveDrainPerSec;        // any hand REACHING or GRIPPED(≥0.5)
        public float PostureMaintainDrainPerSec;   // × ‖posture_break‖
        public float ConfirmDrainFlat;             // applied once per confirm

        // §5.2 recovery rates
        public float BreathRecoverPerSec;          // BTN_BREATH + no grips + static hip
        public float IdleRecoverPerSec;            // all IDLE, no grips

        // §5.2 threshold
        public float HipInputStaticThreshold;     // "hip_input < 0.2"

        // §5.3 caps
        public float LowGripCap;                  // grip strength clamp at low stamina
        public float LowGripCapThreshold;          // stamina level below which the cap activates
        public float NoReachThreshold;             // stamina level below which REACHING is refused

        public static readonly StaminaConfig Default = new StaminaConfig
        {
            HandActiveDrainPerSec       = 0.02f,
            PostureMaintainDrainPerSec  = 0.05f,
            ConfirmDrainFlat            = 0.1f,
            BreathRecoverPerSec         = 0.1f,
            IdleRecoverPerSec           = 0.03f,
            HipInputStaticThreshold     = 0.2f,
            LowGripCap                  = 0.6f,
            LowGripCapThreshold         = 0.2f,
            NoReachThreshold            = 0.05f,
        };
    }

    // -------------------------------------------------------------------------
    // Attacker stamina inputs
    // -------------------------------------------------------------------------

    public struct StaminaInputs
    {
        public float       DtMs;
        public ActorState  Actor;
        public HipIntent   AttackerHip;
        public float       TriggerL;
        public float       TriggerR;
        public bool        BreathPressed;
    }

    // -------------------------------------------------------------------------
    // Defender stamina inputs
    // -------------------------------------------------------------------------

    public struct StaminaDefenderInputs
    {
        public float      DtMs;
        public ActorState Actor;           // top actor
        public float      LeftBasePressure;
        public float      RightBasePressure;
        public float      WeightForward;
        public float      WeightLateral;
        public bool       BreathPressed;
    }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class StaminaOps
    {
        public static float Update(
            float prev,
            StaminaInputs inputs,
            StaminaConfig? cfg = null)
        {
            var c = cfg ?? StaminaConfig.Default;
            float dtSec = inputs.DtMs / 1000f;
            float rate  = 0f;

            // Drain — hands active.
            if (HandIsActive(inputs.Actor.LeftHand.State,  inputs.TriggerL) ||
                HandIsActive(inputs.Actor.RightHand.State, inputs.TriggerR))
                rate -= c.HandActiveDrainPerSec;

            // Drain — posture maintenance.
            float breakMag = PostureBreakOps.Magnitude(inputs.Actor.PostureBreak);
            if (breakMag > 0f)
                rate -= c.PostureMaintainDrainPerSec * breakMag;

            // Recovery.
            float hipMag    = (float)System.Math.Sqrt(
                inputs.AttackerHip.HipPush    * inputs.AttackerHip.HipPush +
                inputs.AttackerHip.HipLateral * inputs.AttackerHip.HipLateral);
            bool staticHip  = hipMag < c.HipInputStaticThreshold;
            bool anyGripped = AnyHandGripped(inputs.Actor);

            if (inputs.BreathPressed && !anyGripped && staticHip)
                rate += c.BreathRecoverPerSec;
            else if (AllLimbsIdle(inputs.Actor) && !anyGripped && staticHip)
                rate += c.IdleRecoverPerSec;

            return Clamp01(prev + rate * dtSec);
        }

        public static float UpdateDefender(
            float prev,
            StaminaDefenderInputs inputs,
            StaminaConfig? cfg = null)
        {
            var c = cfg ?? StaminaConfig.Default;
            float dtSec = inputs.DtMs / 1000f;
            float rate  = 0f;

            bool leftActive  = inputs.LeftBasePressure  >= 0.5f;
            bool rightActive = inputs.RightBasePressure >= 0.5f;
            if (leftActive || rightActive)
                rate -= c.HandActiveDrainPerSec;

            float breakMag = (float)System.Math.Sqrt(
                inputs.Actor.PostureBreak.X * inputs.Actor.PostureBreak.X +
                inputs.Actor.PostureBreak.Y * inputs.Actor.PostureBreak.Y);
            if (breakMag > 0f)
                rate -= c.PostureMaintainDrainPerSec * breakMag;

            float weightMag = (float)System.Math.Sqrt(
                inputs.WeightForward  * inputs.WeightForward +
                inputs.WeightLateral  * inputs.WeightLateral);
            bool staticWeight  = weightMag < c.HipInputStaticThreshold;
            bool anyBaseActive = inputs.LeftBasePressure > 0f || inputs.RightBasePressure > 0f;

            if (inputs.BreathPressed && !anyBaseActive && staticWeight)
                rate += c.BreathRecoverPerSec;
            else if (!anyBaseActive && staticWeight && AllLimbsIdle(inputs.Actor))
                rate += c.IdleRecoverPerSec;

            return Clamp01(prev + rate * dtSec);
        }

        /// <summary>§5.2 last row — flat cost applied once on technique confirm.</summary>
        public static float ApplyConfirmCost(float prev, StaminaConfig? cfg = null)
        {
            var c = cfg ?? StaminaConfig.Default;
            return Clamp01(prev - c.ConfirmDrainFlat);
        }

        /// <summary>§5.3 — returns the grip-strength ceiling for the given stamina level.</summary>
        public static float GripStrengthCeiling(float stamina, StaminaConfig? cfg = null)
        {
            var c = cfg ?? StaminaConfig.Default;
            return stamina < c.LowGripCapThreshold ? c.LowGripCap : 1f;
        }

        /// <summary>§5.3 — true when new REACHING is allowed.</summary>
        public static bool CanStartReach(float stamina, StaminaConfig? cfg = null)
        {
            var c = cfg ?? StaminaConfig.Default;
            return stamina >= c.NoReachThreshold;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static bool HandIsActive(HandState state, float trigger)
        {
            if (state == HandState.Reaching) return true;
            if (state == HandState.Gripped && trigger >= 0.5f) return true;
            return false;
        }

        private static bool AnyHandGripped(ActorState actor) =>
            actor.LeftHand.State  == HandState.Gripped ||
            actor.RightHand.State == HandState.Gripped;

        private static bool AllLimbsIdle(ActorState actor) =>
            actor.LeftHand.State  == HandState.Idle   &&
            actor.RightHand.State == HandState.Idle   &&
            actor.LeftFoot.State  == FootState.Locked &&
            actor.RightFoot.State == FootState.Locked;

        private static float Clamp01(float x)
        {
            if (x < 0f) return 0f;
            if (x > 1f) return 1f;
            return x;
        }
    }
}
