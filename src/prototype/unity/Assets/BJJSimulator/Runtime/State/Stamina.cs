// Ported 1:1 from src/prototype/web/src/state/stamina.ts.
// PURE — stamina update per docs/design/state_machines_v1.md §5.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Config (§5.2 rate table)
    // -------------------------------------------------------------------------

    public struct StaminaConfig
    {
        // §5.2
        public float HandActiveDrainPerSec;      // any hand REACHING / GRIPPED(≥0.5)
        public float PostureMaintainDrainPerSec; // × ‖posture_break‖
        public float ConfirmDrainFlat;           // applied once per confirm
        public float BreathRecoverPerSec;        // BTN_BREATH + no grips + static
        public float IdleRecoverPerSec;          // all IDLE, no grips

        public float HipInputStaticThreshold;    // §5.2 "hip_input < 0.2"

        // §5.3
        public float LowGripCap;                 // grip strength clamp at low stamina
        public float LowGripCapThreshold;        // stamina level below which cap activates
        public float NoReachThreshold;           // stamina below which REACHING is refused

        public static readonly StaminaConfig Default = new StaminaConfig
        {
            HandActiveDrainPerSec      = 0.02f,
            PostureMaintainDrainPerSec = 0.05f,
            ConfirmDrainFlat           = 0.1f,
            BreathRecoverPerSec        = 0.1f,
            IdleRecoverPerSec          = 0.03f,
            HipInputStaticThreshold    = 0.2f,
            LowGripCap                 = 0.6f,
            LowGripCapThreshold        = 0.2f,
            NoReachThreshold           = 0.05f,
        };
    }

    // -------------------------------------------------------------------------
    // Attacker (bottom) stamina inputs
    // -------------------------------------------------------------------------

    public struct StaminaInputs
    {
        public float     DtMs;
        public ActorState Actor;
        public HipIntent AttackerHip;
        public float     TriggerL;
        public float     TriggerR;
        public bool      BreathPressed; // BTN_BREATH held or edge this frame
    }

    // -------------------------------------------------------------------------
    // Defender (top) stamina inputs
    // -------------------------------------------------------------------------

    public struct StaminaDefenderInputs
    {
        public float     DtMs;
        public ActorState Actor;         // the top actor
        public float     LeftBasePressure;
        public float     RightBasePressure;
        public float     WeightForward;
        public float     WeightLateral;
        public bool      BreathPressed;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class StaminaOps
    {
        public static float UpdateStamina(
            float prev,
            StaminaInputs inp,
            StaminaConfig cfg = default)
        {
            if (cfg.BreathRecoverPerSec == 0f) cfg = StaminaConfig.Default;

            float dtSec = inp.DtMs / 1000f;
            float rate  = 0f;

            // Drain — hands active.
            if (HandIsActive(inp.Actor.LeftHand.State, inp.TriggerL) ||
                HandIsActive(inp.Actor.RightHand.State, inp.TriggerR))
            {
                rate -= cfg.HandActiveDrainPerSec;
            }

            // Drain — sustained posture break.
            float breakMag = PostureBreakOps.BreakMagnitude(inp.Actor.PostureBreak);
            if (breakMag > 0f)
                rate -= cfg.PostureMaintainDrainPerSec * breakMag;

            // Recovery — BTN_BREATH + no grips + static hip.
            float hipMag    = (float)System.Math.Sqrt(inp.AttackerHip.HipPush * inp.AttackerHip.HipPush + inp.AttackerHip.HipLateral * inp.AttackerHip.HipLateral);
            bool  staticHip = hipMag < cfg.HipInputStaticThreshold;

            if (inp.BreathPressed && !AnyHandGripped(inp.Actor) && staticHip)
            {
                rate += cfg.BreathRecoverPerSec;
            }
            else if (AllLimbsIdle(inp.Actor) && !AnyHandGripped(inp.Actor) && staticHip)
            {
                rate += cfg.IdleRecoverPerSec;
            }

            return Clamp01(prev + rate * dtSec);
        }

        public static float UpdateStaminaDefender(
            float prev,
            StaminaDefenderInputs inp,
            StaminaConfig cfg = default)
        {
            if (cfg.BreathRecoverPerSec == 0f) cfg = StaminaConfig.Default;

            float dtSec = inp.DtMs / 1000f;
            float rate  = 0f;

            bool leftActive  = inp.LeftBasePressure  >= 0.5f;
            bool rightActive = inp.RightBasePressure >= 0.5f;
            if (leftActive || rightActive)
                rate -= cfg.HandActiveDrainPerSec;

            float breakMag = (float)System.Math.Sqrt(inp.Actor.PostureBreak.X * inp.Actor.PostureBreak.X + inp.Actor.PostureBreak.Y * inp.Actor.PostureBreak.Y);
            if (breakMag > 0f)
                rate -= cfg.PostureMaintainDrainPerSec * breakMag;

            float weightMag   = (float)System.Math.Sqrt(inp.WeightForward * inp.WeightForward + inp.WeightLateral * inp.WeightLateral);
            bool  staticWeight = weightMag < cfg.HipInputStaticThreshold;
            bool  anyBaseActive = inp.LeftBasePressure > 0f || inp.RightBasePressure > 0f;

            if (inp.BreathPressed && !anyBaseActive && staticWeight)
            {
                rate += cfg.BreathRecoverPerSec;
            }
            else if (!anyBaseActive && staticWeight && AllLimbsIdle(inp.Actor))
            {
                rate += cfg.IdleRecoverPerSec;
            }

            return Clamp01(prev + rate * dtSec);
        }

        // §5.2 last row — confirming a technique deducts a flat cost.
        public static float ApplyConfirmCost(float prev, StaminaConfig cfg = default)
        {
            if (cfg.ConfirmDrainFlat == 0f) cfg = StaminaConfig.Default;
            return Clamp01(prev - cfg.ConfirmDrainFlat);
        }

        // §5.3 — grip-strength ceiling at low stamina.
        public static float GripStrengthCeiling(float stamina, StaminaConfig cfg = default)
        {
            if (cfg.LowGripCap == 0f) cfg = StaminaConfig.Default;
            return stamina < cfg.LowGripCapThreshold ? cfg.LowGripCap : 1f;
        }

        // §5.3 — below this stamina new REACHING is refused.
        public static bool CanStartReach(float stamina, StaminaConfig cfg = default)
        {
            if (cfg.NoReachThreshold == 0f) cfg = StaminaConfig.Default;
            return stamina >= cfg.NoReachThreshold;
        }

        // --- classification helpers -------------------------------------------

        static bool HandIsActive(HandState state, float triggerValue)
        {
            if (state == HandState.Reaching) return true;
            if (state == HandState.Gripped && triggerValue >= 0.5f) return true;
            return false;
        }

        static bool AnyHandGripped(ActorState actor) =>
            actor.LeftHand.State == HandState.Gripped || actor.RightHand.State == HandState.Gripped;

        static bool AllLimbsIdle(ActorState actor) =>
            actor.LeftHand.State  == HandState.Idle &&
            actor.RightHand.State == HandState.Idle &&
            actor.LeftFoot.State  == FootState.Locked &&
            actor.RightFoot.State == FootState.Locked;

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
