// Ported from src/prototype/web/src/state/control_layer.ts.
// See docs/design/state_machines_v1.md §7.
//
// Scope: affects presentation only (camera, post-process, audio).
// Never changes FSM transitions.
// Locked to the side that fires a judgment window until that window fully closes.

namespace BJJSimulator
{
    public enum Initiative { Neutral, Bottom, Top }

    public struct ControlLayer
    {
        public Initiative Initiative;
        /// <summary>
        /// While a judgment window is any state other than CLOSED, initiative
        /// is locked to whichever side fired it.
        /// </summary>
        public bool LockedByWindow;
    }

    public struct ControlLayerInputs
    {
        public JudgmentWindow JudgmentWindow;
        public ActorState     Bottom;
        public ActorState     Top;
        /// <summary>True while the top actor has a cut attempt IN_PROGRESS.</summary>
        public bool           DefenderCutInProgress;
    }

    public static class ControlLayerOps
    {
        public static readonly ControlLayer Initial = new ControlLayer
        {
            Initiative     = Initiative.Neutral,
            LockedByWindow = false,
        };

        public static ControlLayer Update(ControlLayer prev, ControlLayerInputs inp)
        {
            var win         = inp.JudgmentWindow;
            bool windowActive = win.State != JudgmentWindowState.Closed;

            // §7.2 highest priority: judgment window lock.
            if (windowActive && win.HasFiredBy)
            {
                return new ControlLayer
                {
                    Initiative     = win.FiredByBottom ? Initiative.Bottom : Initiative.Top,
                    LockedByWindow = true,
                };
            }

            // §7.2 rule 2: arm_extracted → that side owns initiative.
            if (inp.Bottom.ArmExtractedLeft || inp.Bottom.ArmExtractedRight)
                return new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = false };
            if (inp.Top.ArmExtractedLeft || inp.Top.ArmExtractedRight)
                return new ControlLayer { Initiative = Initiative.Top,    LockedByWindow = false };

            // §7.2 rule 3: attacker has ≥2 active grips → Bottom.
            if (CountActiveGrips(inp.Bottom) >= 2)
                return new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = false };

            // §7.2 rule 4: defender cut in progress → Top.
            if (inp.DefenderCutInProgress)
                return new ControlLayer { Initiative = Initiative.Top,    LockedByWindow = false };

            // §7.2 rule 5: Neutral.
            if (prev.Initiative == Initiative.Neutral && !prev.LockedByWindow) return prev;
            return new ControlLayer { Initiative = Initiative.Neutral, LockedByWindow = false };
        }

        private static int CountActiveGrips(ActorState actor)
        {
            int n = 0;
            if (actor.LeftHand.State  == HandState.Gripped) n++;
            if (actor.RightHand.State == HandState.Gripped) n++;
            return n;
        }
    }
}
