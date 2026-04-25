// Ported 1:1 from src/prototype/web/src/state/control_layer.ts.
// PURE — ControlLayer (initiative) per docs/design/state_machines_v1.md §7.
//
// Scope: affects presentation only (camera, post-process, audio).
// Never changes FSM transitions. Locked to the side that fires a judgment
// window until that window fully closes.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------

    public enum Initiative { Neutral, Bottom, Top }

    public struct ControlLayer
    {
        public Initiative Initiative;
        public bool        LockedByWindow;

        public static readonly ControlLayer Initial = new ControlLayer
        {
            Initiative     = Initiative.Neutral,
            LockedByWindow = false,
        };
    }

    public struct ControlLayerInputs
    {
        public JudgmentWindow JudgmentWindow;
        public ActorState     Bottom;
        public ActorState     Top;
        public bool           DefenderCutInProgress;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class ControlLayerOps
    {
        public static ControlLayer Update(ControlLayer prev, ControlLayerInputs inp)
        {
            var win = inp.JudgmentWindow;
            bool windowActive = win.State != JudgmentWindowState.Closed;

            // §7.2 — highest priority: judgment window lock.
            if (windowActive && win.FiredBy != WindowSide.None)
            {
                return new ControlLayer
                {
                    Initiative     = win.FiredBy == WindowSide.Bottom ? Initiative.Bottom : Initiative.Top,
                    LockedByWindow = true,
                };
            }

            // §7.2 rule 2: any arm_extracted → that side owns initiative.
            if (inp.Bottom.ArmExtractedLeft || inp.Bottom.ArmExtractedRight)
                return new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = false };
            if (inp.Top.ArmExtractedLeft || inp.Top.ArmExtractedRight)
                return new ControlLayer { Initiative = Initiative.Top, LockedByWindow = false };

            // §7.2 rule 3: attacker has ≥2 active grips → Bottom.
            if (CountActiveGrips(inp.Bottom) >= 2)
                return new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = false };

            // §7.2 rule 4: defender cut in progress → Top.
            if (inp.DefenderCutInProgress)
                return new ControlLayer { Initiative = Initiative.Top, LockedByWindow = false };

            // §7.2 rule 5: Neutral.
            if (prev.Initiative == Initiative.Neutral && !prev.LockedByWindow) return prev;
            return new ControlLayer { Initiative = Initiative.Neutral, LockedByWindow = false };
        }

        static int CountActiveGrips(ActorState actor)
        {
            int n = 0;
            if (actor.LeftHand.State  == HandState.Gripped) n++;
            if (actor.RightHand.State == HandState.Gripped) n++;
            return n;
        }
    }
}
