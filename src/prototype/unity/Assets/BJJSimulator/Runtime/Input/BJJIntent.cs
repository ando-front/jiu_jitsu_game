// Ported 1:1 from src/prototype/web/src/input/intent.ts.
// Layer B output types — boundary handed from input pipeline to state machines.
// See docs/design/input_system_v1.md §B for contracts.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Hip intent (§B.1)
    // -------------------------------------------------------------------------

    public struct HipIntent
    {
        public float HipAngleTarget; // radians, yaw around the opponent-centric axis
        public float HipPush;        // [-1, 1], + = push away, - = pull in
        public float HipLateral;     // [-1, 1], ± = side-cut direction

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
        public GripZone LHandTarget;    // GripZone.None = no target
        public float    LGripStrength;  // [0, 1]
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
    // Fat struct: Kind decides which payload fields are meaningful.
    // -------------------------------------------------------------------------

    public enum DiscreteIntentKind
    {
        FootHookToggle,  // LBumper / RBumper edge; side in FootSide
        BaseHold,        // held, not edge
        GripReleaseAll,
        BreathStart,
        Pause,
    }

    public struct DiscreteIntent
    {
        public DiscreteIntentKind Kind;
        public FootSide            FootSide; // FootHookToggle only
    }

    // -------------------------------------------------------------------------
    // Aggregate intent (one per frame, output of Layer B)
    // -------------------------------------------------------------------------

    public struct Intent
    {
        public HipIntent        Hip;
        public GripIntent       Grip;
        public DiscreteIntent[] Discrete; // may be null or empty

        public static readonly Intent Zero = new Intent
        {
            Hip      = HipIntent.Zero,
            Grip     = GripIntent.Zero,
            Discrete = System.Array.Empty<DiscreteIntent>(),
        };
    }
}
