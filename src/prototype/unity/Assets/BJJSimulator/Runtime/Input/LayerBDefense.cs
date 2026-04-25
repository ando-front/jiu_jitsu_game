// Ported 1:1 from src/prototype/web/src/input/layerB_defense.ts.
// PURE — Layer B (defender): InputFrame → DefenseIntent.
// Reference: docs/design/input_system_defense_v1.md §B.
//
// Structurally parallel to LayerB.cs but with defender-specific semantics.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Base-zone direction table (§B.2 top-side controls)
    // -------------------------------------------------------------------------

    public struct BaseZoneDir
    {
        public BaseZone Zone;
        public Vec2     Dir;
    }

    // -------------------------------------------------------------------------
    // Per-player state
    // -------------------------------------------------------------------------

    public struct LayerBDefenseState
    {
        public BaseZone LastBaseZone;

        public static readonly LayerBDefenseState Initial =
            new LayerBDefenseState { LastBaseZone = BaseZone.None };
    }

    // -------------------------------------------------------------------------
    // Pure transform
    // -------------------------------------------------------------------------

    public static class LayerBDefenseOps
    {
        private const float S = 0.7071068f; // 1/√2
        public static readonly BaseZoneDir[] BaseZoneDirections = new BaseZoneDir[]
        {
            new BaseZoneDir { Zone = BaseZone.Chest,  Dir = new Vec2( 0f,  1f) },
            new BaseZoneDir { Zone = BaseZone.Hip,    Dir = new Vec2( 0f, -1f) },
            new BaseZoneDir { Zone = BaseZone.KneeL,  Dir = new Vec2(-S,  -S) },
            new BaseZoneDir { Zone = BaseZone.KneeR,  Dir = new Vec2( S,  -S) },
            new BaseZoneDir { Zone = BaseZone.BicepL, Dir = new Vec2(-S,   S) },
            new BaseZoneDir { Zone = BaseZone.BicepR, Dir = new Vec2( S,   S) },
        };

        private const float ZoneSelectCosThreshold = 0.866f; // cos(30°)
        private const float RsMagnitudeThreshold   = 0.2f;

        // -----------------------------------------------------------------------

        public static (DefenseIntent Intent, LayerBDefenseState NextState) Transform(
            InputFrame frame,
            LayerBDefenseState prev)
        {
            var hip               = ComputeTopHipIntent(frame);
            var (base_, nextZone) = ComputeTopBaseIntent(frame, prev.LastBaseZone);
            var discrete          = ComputeDefenseDiscreteIntents(frame);

            return (
                new DefenseIntent { Hip = hip, Base = base_, Discrete = discrete },
                new LayerBDefenseState { LastBaseZone = nextZone }
            );
        }

        public static TopHipIntent ComputeTopHipIntent(InputFrame frame) =>
            new TopHipIntent { WeightForward = frame.Ls.Y, WeightLateral = frame.Ls.X };

        public static (TopBaseIntent Base, BaseZone NextZone) ComputeTopBaseIntent(
            InputFrame frame, BaseZone lastZone)
        {
            bool bumperEdge = (frame.ButtonEdges & (ButtonBit.LBumper | ButtonBit.RBumper)) != 0;
            bool anyTrigger = frame.LTrigger > 0f || frame.RTrigger > 0f;
            float rsMag     = frame.Rs.Magnitude;
            BaseZone next   = lastZone;

            if (!bumperEdge && anyTrigger && rsMag >= RsMagnitudeThreshold)
            {
                float nx = frame.Rs.X / rsMag;
                float ny = frame.Rs.Y / rsMag;
                var best = PickBestBaseZone(nx, ny);
                if (lastZone == BaseZone.None || best.Zone == lastZone)
                    next = best.Zone;
                else if (best.Dot >= ZoneSelectCosThreshold)
                    next = best.Zone;
            }
            else if (!anyTrigger)
            {
                next = BaseZone.None;
            }

            BaseZone lTarget = frame.LTrigger > 0f ? next : BaseZone.None;
            BaseZone rTarget = frame.RTrigger > 0f ? next : BaseZone.None;

            TopBaseIntent base_;
            if (lTarget == BaseZone.None && rTarget == BaseZone.None)
            {
                base_ = TopBaseIntent.Zero;
            }
            else
            {
                base_ = new TopBaseIntent
                {
                    LHandTarget   = lTarget,
                    LBasePressure = frame.LTrigger,
                    RHandTarget   = rTarget,
                    RBasePressure = frame.RTrigger,
                };
            }

            return (base_, next);
        }

        public static DefenseDiscreteIntent[] ComputeDefenseDiscreteIntents(InputFrame frame)
        {
            var buf = new System.Collections.Generic.List<DefenseDiscreteIntent>(4);
            if ((frame.ButtonEdges & ButtonBit.LBumper) != 0)
                buf.Add(new DefenseDiscreteIntent
                {
                    Kind    = DefenseDiscreteIntentKind.CutAttempt,
                    CutSide = HandSide.L,
                    Rs      = frame.Rs,
                });
            if ((frame.ButtonEdges & ButtonBit.RBumper) != 0)
                buf.Add(new DefenseDiscreteIntent
                {
                    Kind    = DefenseDiscreteIntentKind.CutAttempt,
                    CutSide = HandSide.R,
                    Rs      = frame.Rs,
                });
            if ((frame.Buttons & ButtonBit.BtnBase) != 0)
                buf.Add(new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.RecoveryHold });
            if ((frame.ButtonEdges & ButtonBit.BtnRelease) != 0)
                buf.Add(new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.BaseReleaseAll });
            if ((frame.ButtonEdges & ButtonBit.BtnBreath) != 0)
                buf.Add(new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.BreathStart });
            if ((frame.ButtonEdges & ButtonBit.BtnReserved) != 0)
                buf.Add(new DefenseDiscreteIntent
                {
                    Kind = DefenseDiscreteIntentKind.PassCommit,
                    Rs   = frame.Rs,
                });
            if ((frame.ButtonEdges & ButtonBit.BtnPause) != 0)
                buf.Add(new DefenseDiscreteIntent { Kind = DefenseDiscreteIntentKind.Pause });
            return buf.ToArray();
        }

        // -----------------------------------------------------------------------

        private struct BestBase { public BaseZone Zone; public float Dot; }

        private static BestBase PickBestBaseZone(float nx, float ny)
        {
            float    bestDot = float.NegativeInfinity;
            BaseZone best    = BaseZone.None;
            foreach (var e in BaseZoneDirections)
            {
                float d = nx * e.Dir.X + ny * e.Dir.Y;
                if (d > bestDot) { bestDot = d; best = e.Zone; }
            }
            return new BestBase { Zone = best, Dot = bestDot };
        }
    }
}
