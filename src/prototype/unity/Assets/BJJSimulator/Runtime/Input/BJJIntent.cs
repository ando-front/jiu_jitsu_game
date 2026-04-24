// Ported from src/prototype/web/src/input/intent.ts.
// Layer B output types — the boundary handed to state machines.
// See docs/design/input_system_v1.md §B for contracts.
//
// GripZone is defined in BJJCoreTypes.cs (ordinal 0 = None).
// DiscreteIntent uses the fat-struct pattern (§2.3 of stage2_port_plan_v1.md).

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Hip intent (§B.1)
    // -------------------------------------------------------------------------

    public struct HipIntent
    {
        /// <summary>Radians, yaw around opponent-centric axis.</summary>
        public float HipAngleTarget;
        /// <summary>[-1, 1]: +1 = push away, -1 = pull in.</summary>
        public float HipPush;
        /// <summary>[-1, 1]: ±1 = side-cut direction.</summary>
        public float HipLateral;

        public static readonly HipIntent Zero = new HipIntent
        {
            HipAngleTarget = 0f,
            HipPush        = 0f,
            HipLateral     = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Grip intent (§B.2)
    // -------------------------------------------------------------------------

    public struct GripIntent
    {
        /// <summary>GripZone.None = no target selected.</summary>
        public GripZone LHandTarget;
        public float    LGripStrength;
        public GripZone RHandTarget;
        public float    RGripStrength;

        public static readonly GripIntent Zero = new GripIntent
        {
            LHandTarget   = GripZone.None,
            LGripStrength = 0f,
            RHandTarget   = GripZone.None,
            RGripStrength = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Discrete / edge intents (§B.3)
    // Fat-struct pattern: Kind selects which payload fields matter.
    // -------------------------------------------------------------------------

    public enum DiscreteIntentKind
    {
        FootHookToggle,   // side: L or R bumper edge
        BaseHold,         // A/✕/Space held
        GripReleaseAll,   // B/◯/X edge → BTN_RELEASE
        BreathStart,      // Y/△/C edge
        Pause,            // Options/Esc
    }

    public struct DiscreteIntent
    {
        public DiscreteIntentKind Kind;
        /// <summary>Only meaningful for <see cref="DiscreteIntentKind.FootHookToggle"/>.</summary>
        public FootSide Side;
    }

    // -------------------------------------------------------------------------
    // Full intent aggregate handed to state machines each tick.
    // -------------------------------------------------------------------------

    public struct Intent
    {
        public HipIntent               Hip;
        public GripIntent              Grip;
        /// <summary>
        /// Zero or more discrete events this frame. Allocated only when
        /// non-empty; callers that produce no discrete events may leave
        /// this null (treated the same as an empty list).
        /// </summary>
        public List<DiscreteIntent>    Discrete;

        public static readonly Intent Zero = new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = null,
        };

        /// <summary>True when any element in Discrete matches the given kind.</summary>
        public bool HasDiscrete(DiscreteIntentKind kind)
        {
            if (Discrete == null) return false;
            foreach (var d in Discrete)
                if (d.Kind == kind) return true;
            return false;
        }

        /// <summary>True when any FootHookToggle for the given side is present.</summary>
        public bool HasFootToggle(FootSide side)
        {
            if (Discrete == null) return false;
            foreach (var d in Discrete)
                if (d.Kind == DiscreteIntentKind.FootHookToggle && d.Side == side) return true;
            return false;
        }
    }
}
