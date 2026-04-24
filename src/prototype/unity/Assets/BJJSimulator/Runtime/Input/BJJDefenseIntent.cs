// Ported from src/prototype/web/src/input/intent_defense.ts.
// Defender-side Intent types.
// See docs/design/input_system_defense_v1.md §B for contracts.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Base zones (§B.2.1 of input_system_defense_v1.md)
    // None at ordinal 0 = default-initialised "no target"
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
    // Top hip intent (weight input for posture recovery, §B.1)
    // -------------------------------------------------------------------------

    public struct TopHipIntent
    {
        /// <summary>[-1, 1] — defender pushing their weight forward.</summary>
        public float WeightForward;
        /// <summary>[-1, 1] — defender shifting weight laterally.</summary>
        public float WeightLateral;

        public static readonly TopHipIntent Zero = new TopHipIntent
        {
            WeightForward = 0f,
            WeightLateral = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Top base intent (§B.2 — two base hands)
    // -------------------------------------------------------------------------

    public struct TopBaseIntent
    {
        public BaseZone LHandTarget;
        public float    LBasePressure;   // [0, 1]
        public BaseZone RHandTarget;
        public float    RBasePressure;   // [0, 1]

        public static readonly TopBaseIntent Zero = new TopBaseIntent
        {
            LHandTarget   = BaseZone.None,
            LBasePressure = 0f,
            RHandTarget   = BaseZone.None,
            RBasePressure = 0f,
        };
    }

    // -------------------------------------------------------------------------
    // Discrete / edge intents (§B.3)
    // -------------------------------------------------------------------------

    public enum DefenseDiscreteKind
    {
        CutAttempt,       // §4.2 / §B.4 — RS-directed cut commit
        RecoveryHold,     // §B.6.1 — BTN_BASE hold for posture recovery
        BaseReleaseAll,   // §B.6 — BTN_RELEASE
        BreathStart,      // §B.5
        PassCommit,       // §B.7 — RS-directed pass commit
        Pause,
    }

    public struct DefenseDiscreteIntent
    {
        public DefenseDiscreteKind Kind;
        /// <summary>Which defender hand fires the cut. Only for <see cref="DefenseDiscreteKind.CutAttempt"/>.</summary>
        public HandSide CutSide;
        /// <summary>RS direction at the moment of the edge. Used by CutAttempt and PassCommit.</summary>
        public Vec2     RS;
    }

    // -------------------------------------------------------------------------
    // Full defense intent aggregate
    // -------------------------------------------------------------------------

    public struct DefenseIntent
    {
        public TopHipIntent               Hip;
        public TopBaseIntent              Base;
        /// <summary>Zero or more discrete events this frame. May be null (no events).</summary>
        public List<DefenseDiscreteIntent> Discrete;

        public static readonly DefenseIntent Zero = new DefenseIntent
        {
            Hip      = TopHipIntent.Zero,
            Base     = TopBaseIntent.Zero,
            Discrete = null,
        };

        public bool HasDiscrete(DefenseDiscreteKind kind)
        {
            if (Discrete == null) return false;
            foreach (var d in Discrete)
                if (d.Kind == kind) return true;
            return false;
        }
    }
}
