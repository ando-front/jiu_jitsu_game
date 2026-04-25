// Ported 1:1 from src/prototype/web/src/input/intent_defense.ts.
// Defender-side intent types (Layer B, top actor).
// See docs/design/input_system_defense_v1.md §B for contracts.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Base zones (§B.2 top-side controls)
    // None at ordinal 0 = default-initialised "no base target"
    // -------------------------------------------------------------------------

    public enum BaseZone
    {
        None = 0,
        Chest,
        Hip,
        KneeL,
        KneeR,
        BicepL,
        BicepR,
    }

    // -------------------------------------------------------------------------
    // Top hip intent (weight distribution)
    // -------------------------------------------------------------------------

    public struct TopHipIntent
    {
        public float WeightForward; // [-1, 1]
        public float WeightLateral; // [-1, 1]

        public static readonly TopHipIntent Zero = new TopHipIntent
        {
            WeightForward = 0f,
            WeightLateral = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Top base intent (hands pressing into the bottom's body)
    // -------------------------------------------------------------------------

    public struct TopBaseIntent
    {
        public BaseZone LHandTarget;    // BaseZone.None = no target
        public float    LBasePressure;  // [0, 1]
        public BaseZone RHandTarget;
        public float    RBasePressure;

        public static readonly TopBaseIntent Zero = new TopBaseIntent
        {
            LHandTarget  = BaseZone.None,
            LBasePressure = 0f,
            RHandTarget  = BaseZone.None,
            RBasePressure = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Discrete defense intents (§B.4–B.7)
    // Fat struct: Kind decides which payload fields are meaningful.
    // -------------------------------------------------------------------------

    public enum DefenseDiscreteIntentKind
    {
        CutAttempt,      // side + rs direction
        RecoveryHold,    // §B.6.1
        BaseReleaseAll,  // §B.6
        BreathStart,
        PassCommit,      // §B.7 + rs direction
        Pause,
    }

    public struct DefenseDiscreteIntent
    {
        public DefenseDiscreteIntentKind Kind;
        public HandSide CutSide;  // CutAttempt only (L = left defender hand)
        public Vec2     Rs;       // CutAttempt and PassCommit
    }

    // -------------------------------------------------------------------------
    // Aggregate defense intent (one per frame, output of Layer B defense)
    // -------------------------------------------------------------------------

    public struct DefenseIntent
    {
        public TopHipIntent           Hip;
        public TopBaseIntent          Base;
        public DefenseDiscreteIntent[] Discrete; // may be null or empty

        public static readonly DefenseIntent Zero = new DefenseIntent
        {
            Hip      = TopHipIntent.Zero,
            Base     = TopBaseIntent.Zero,
            Discrete = System.Array.Empty<DefenseDiscreteIntent>(),
        };
    }
}
