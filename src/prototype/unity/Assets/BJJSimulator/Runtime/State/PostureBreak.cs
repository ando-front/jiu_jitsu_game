// Ported 1:1 from src/prototype/web/src/state/posture_break.ts.
// PURE — posture_break update per docs/design/state_machines_v1.md §3.
//
// Model: 2D vector (lateral, sagittal). Updated each sim step by combining:
//   1. exponential decay toward origin (τ = 800ms)
//   2. attacker hip input contribution
//   3. GRIPPED-hand "pull" contribution
//   4. defender recovery subtraction

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Config (§3.3 coefficient table — numeric values are design placeholders)
    // -------------------------------------------------------------------------

    public struct PostureBreakConfig
    {
        public float DecayTauMs;           // §3.3 time constant for self-decay
        public float KHipImpact;           // per-second magnitude from 1.0 hip input
        public float KGripImpact;          // per-second magnitude from 1.0-strength grip
        public float KRecovery;            // per-second magnitude subtracted by recovery
        public float MaxMagnitude;         // final clamp; 1.0 is the normative upper bound

        public static readonly PostureBreakConfig Default = new PostureBreakConfig
        {
            DecayTauMs  = 800f,
            KHipImpact  = 0.9f,
            KGripImpact = 0.6f,
            KRecovery   = 1.2f,
            MaxMagnitude = 1f,
        };
    }

    // -------------------------------------------------------------------------
    // Inputs
    // -------------------------------------------------------------------------

    public struct PostureBreakInputs
    {
        public float     DtMs;             // game_dt (already time-scaled), ms
        public HipIntent AttackerHip;
        // Per-hand active pull vectors (non-zero only when GRIPPED).
        // Caller assembles via PostureBreakOps.GripPullVector.
        public Vec2[]    GripPulls;        // may be null or empty
        // Defender recovery input (unit vector × recovery gain). Caller passes
        // Vec2.Zero until defender input is wired.
        public Vec2      DefenderRecovery;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class PostureBreakOps
    {
        public static Vec2 Update(
            Vec2 prev,
            PostureBreakInputs inp,
            PostureBreakConfig cfg = default)
        {
            if (cfg.DecayTauMs == 0f) cfg = PostureBreakConfig.Default;

            float dtSec = inp.DtMs / 1000f;

            // 1. Exponential decay. Over one dt the state retains exp(-dt/τ).
            float decay = (float)System.Math.Exp(-inp.DtMs / cfg.DecayTauMs);
            float x = prev.X * decay;
            float y = prev.Y * decay;

            // 2. Attacker hip contribution.
            x += inp.AttackerHip.HipLateral * cfg.KHipImpact * dtSec;
            y += inp.AttackerHip.HipPush    * cfg.KHipImpact * dtSec;

            // 3. GRIPPED-hand pulls.
            if (inp.GripPulls != null)
            {
                foreach (var p in inp.GripPulls)
                {
                    x += p.X * cfg.KGripImpact * dtSec;
                    y += p.Y * cfg.KGripImpact * dtSec;
                }
            }

            // 4. Defender recovery subtracts in the direction of the recovery input.
            x -= inp.DefenderRecovery.X * cfg.KRecovery * dtSec;
            y -= inp.DefenderRecovery.Y * cfg.KRecovery * dtSec;

            // Clamp magnitude to the unit disc.
            float mag = (float)System.Math.Sqrt(x * x + y * y);
            if (mag > cfg.MaxMagnitude)
            {
                float s = cfg.MaxMagnitude / mag;
                x *= s;
                y *= s;
            }

            return new Vec2(x, y);
        }

        // §3.2 — derived query: overall break magnitude.
        public static float BreakMagnitude(Vec2 v) =>
            (float)System.Math.Sqrt(v.X * v.X + v.Y * v.Y);

        // §3.4 — paper-proto bucket (0..4). HUD / debug helper.
        public static int BreakBucket(Vec2 v)
        {
            float m = BreakMagnitude(v);
            if (m < 0.1f) return 0;
            if (m < 0.3f) return 1;
            if (m < 0.5f) return 2;
            if (m < 0.75f) return 3;
            return 4;
        }

        // Encode a per-hand grip pull for §3.3 bullet 3.
        // GripZone → unit direction × strength in (lateral, sagittal) space.
        public static Vec2 GripPullVector(GripZone zone, float strength)
        {
            Vec2 dir = ZonePullDir(zone);
            return new Vec2(dir.X * strength, dir.Y * strength);
        }

        static Vec2 ZonePullDir(GripZone zone)
        {
            switch (zone)
            {
                case GripZone.SleeveL:      return new Vec2(-0.5f,  0.87f);
                case GripZone.SleeveR:      return new Vec2( 0.5f,  0.87f);
                case GripZone.CollarL:      return new Vec2(-0.3f,  0.95f);
                case GripZone.CollarR:      return new Vec2( 0.3f,  0.95f);
                case GripZone.WristL:       return new Vec2(-1f,    0f);
                case GripZone.WristR:       return new Vec2( 1f,    0f);
                case GripZone.Belt:         return new Vec2( 0f,    0.9f);
                case GripZone.PostureBreak: return new Vec2( 0f,    1f);
                default:                    return Vec2.Zero;
            }
        }
    }
}
