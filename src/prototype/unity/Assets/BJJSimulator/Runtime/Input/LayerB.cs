// Ported 1:1 from src/prototype/web/src/input/layerB.ts.
// PURE — Layer B: InputFrame → Intent transformation.
// Reference: docs/design/input_system_v1.md §B.1 (hip), §B.2 (grip), §B.3 (discrete).
//
// Layer B keeps one piece of per-player state (lastZone for hysteresis).
// That state is threaded externally so this class stays a pure transform.

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Zone direction table (§B.2.1)
    // SQRT1_2 = 1/√2 ≈ 0.7071
    // -------------------------------------------------------------------------

    public struct GripZoneDir
    {
        public GripZone Zone;
        public Vec2     Dir;
    }

    // -------------------------------------------------------------------------
    // Config
    // -------------------------------------------------------------------------

    public struct LayerBConfig
    {
        public float KAngleScale;
        public float ZoneSelectCosThreshold;
        public float RsMagnitudeThreshold;

        public static readonly LayerBConfig Default = new LayerBConfig
        {
            KAngleScale             = 0.6f,
            ZoneSelectCosThreshold  = 0.866f,  // cos(30°)
            RsMagnitudeThreshold    = 0.2f,
        };
    }

    // -------------------------------------------------------------------------
    // Per-player state
    // -------------------------------------------------------------------------

    public struct LayerBState
    {
        public GripZone LastZone; // GripZone.None = no zone selected yet

        public static readonly LayerBState Initial = new LayerBState { LastZone = GripZone.None };
    }

    // -------------------------------------------------------------------------
    // Pure transform
    // -------------------------------------------------------------------------

    public static class LayerBOps
    {
        // §B.2.1 zone table (RS unit vectors, attacker viewpoint)
        private const float S = 0.7071068f; // 1/√2
        public static readonly GripZoneDir[] GripZoneDirections = new GripZoneDir[]
        {
            new GripZoneDir { Zone = GripZone.SleeveL,      Dir = new Vec2(-S,  -S) },
            new GripZoneDir { Zone = GripZone.SleeveR,      Dir = new Vec2( S,  -S) },
            new GripZoneDir { Zone = GripZone.CollarL,      Dir = new Vec2(-S,   S) },
            new GripZoneDir { Zone = GripZone.CollarR,      Dir = new Vec2( S,   S) },
            new GripZoneDir { Zone = GripZone.WristL,       Dir = new Vec2(-1f,  0f) },
            new GripZoneDir { Zone = GripZone.WristR,       Dir = new Vec2( 1f,  0f) },
            new GripZoneDir { Zone = GripZone.Belt,         Dir = new Vec2( 0f, -1f) },
            new GripZoneDir { Zone = GripZone.PostureBreak, Dir = new Vec2( 0f,  1f) },
        };

        // -----------------------------------------------------------------------

        public static (Intent Intent, LayerBState NextState) Transform(
            InputFrame frame,
            LayerBState prev,
            LayerBConfig cfg = default)
        {
            if (cfg.KAngleScale == 0f) cfg = LayerBConfig.Default;

            var hip  = ComputeHipIntent(frame, cfg);
            var (grip, nextZone) = ComputeGripIntent(frame, prev.LastZone, cfg);
            var discrete = ComputeDiscreteIntents(frame);

            var intent    = new Intent { Hip = hip, Grip = grip, Discrete = discrete };
            var nextState = new LayerBState { LastZone = nextZone };
            return (intent, nextState);
        }

        // §B.1 — LS → hip angles
        public static HipIntent ComputeHipIntent(InputFrame frame, LayerBConfig cfg)
        {
            float angle = (float)System.Math.Atan2(frame.Ls.X, frame.Ls.Y) * cfg.KAngleScale;
            return new HipIntent
            {
                HipAngleTarget = angle,
                HipPush        = frame.Ls.Y,
                HipLateral     = frame.Ls.X,
            };
        }

        // §B.2 — RS + triggers → grip intent with hysteresis
        public static (GripIntent Grip, GripZone NextZone) ComputeGripIntent(
            InputFrame frame, GripZone lastZone, LayerBConfig cfg)
        {
            float rsMag        = frame.Rs.Magnitude;
            bool  anyTrigger   = frame.LTrigger > 0f || frame.RTrigger > 0f;
            GripZone nextZone  = lastZone;

            if (rsMag >= cfg.RsMagnitudeThreshold)
            {
                float nx = frame.Rs.X / rsMag;
                float ny = frame.Rs.Y / rsMag;
                var best = PickBestGripZone(nx, ny);

                if (lastZone == GripZone.None || best.Zone == lastZone)
                    nextZone = best.Zone;
                else if (best.Dot >= cfg.ZoneSelectCosThreshold)
                    nextZone = best.Zone;
                // else: keep lastZone (hysteresis hold)
            }
            else if (!anyTrigger)
            {
                nextZone = GripZone.None;
            }

            GripZone lTarget = frame.LTrigger > 0f ? nextZone : GripZone.None;
            GripZone rTarget = frame.RTrigger > 0f ? nextZone : GripZone.None;

            GripIntent grip;
            if (lTarget == GripZone.None && rTarget == GripZone.None)
            {
                grip = GripIntent.Zero;
            }
            else
            {
                grip = new GripIntent
                {
                    LHandTarget   = lTarget,
                    LGripStrength = frame.LTrigger,
                    RHandTarget   = rTarget,
                    RGripStrength = frame.RTrigger,
                };
            }

            return (grip, nextZone);
        }

        // §B.3 — button edges → discrete intents
        public static DiscreteIntent[] ComputeDiscreteIntents(InputFrame frame)
        {
            var buf = new System.Collections.Generic.List<DiscreteIntent>(4);
            if ((frame.ButtonEdges & ButtonBit.LBumper) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = FootSide.L });
            if ((frame.ButtonEdges & ButtonBit.RBumper) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.FootHookToggle, FootSide = FootSide.R });
            if ((frame.Buttons & ButtonBit.BtnBase) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.BaseHold });
            if ((frame.ButtonEdges & ButtonBit.BtnRelease) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.GripReleaseAll });
            if ((frame.ButtonEdges & ButtonBit.BtnBreath) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.BreathStart });
            if ((frame.ButtonEdges & ButtonBit.BtnPause) != 0)
                buf.Add(new DiscreteIntent { Kind = DiscreteIntentKind.Pause });
            return buf.ToArray();
        }

        // -----------------------------------------------------------------------

        private struct BestZone { public GripZone Zone; public float Dot; }

        private static BestZone PickBestGripZone(float nx, float ny)
        {
            float bestDot  = float.NegativeInfinity;
            GripZone best  = GripZone.None;
            foreach (var e in GripZoneDirections)
            {
                float d = nx * e.Dir.X + ny * e.Dir.Y;
                if (d > bestDot) { bestDot = d; best = e.Zone; }
            }
            return new BestZone { Zone = best, Dot = bestDot };
        }
    }
}
