// Ported from src/prototype/web/src/state/posture_break.ts.
// See docs/design/state_machines_v1.md §3.
//
// Model: 2D vector (lateral, sagittal).
// Updated each sim step by combining:
//   1. Exponential decay toward origin (τ = 800ms)
//   2. Attacker hip input contribution
//   3. GRIPPED-hand "pull" contribution
//   4. Defender recovery subtraction
//
// Coefficient values (K*) are placeholders; numeric tuning deferred to
// post-M1 per the design doc. They live here in one place for easy rebalance.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Config
    // -------------------------------------------------------------------------

    public struct PostureBreakConfig
    {
        /// <summary>§3.3 time constant for self-decay (ms).</summary>
        public float DecayTauMs;
        /// <summary>Per-second magnitude contributed by 1.0 hip input.</summary>
        public float KHipImpact;
        /// <summary>Per-second magnitude contributed by a 1.0-strength grip pull.</summary>
        public float KGripImpact;
        /// <summary>Per-second magnitude subtracted by 1.0 recovery input.</summary>
        public float KRecovery;
        /// <summary>Final clamp; 1.0 is the normative upper bound (unit disc).</summary>
        public float MaxMagnitude;

        public static readonly PostureBreakConfig Default = new PostureBreakConfig
        {
            DecayTauMs    = 800f,
            KHipImpact    = 0.9f,
            KGripImpact   = 0.6f,
            KRecovery     = 1.2f,
            MaxMagnitude  = 1f,
        };
    }

    // -------------------------------------------------------------------------
    // Update inputs
    // -------------------------------------------------------------------------

    public struct PostureBreakInputs
    {
        /// <summary>Game dt (already time-scaled), ms.</summary>
        public float DtMs;
        public HipIntent  AttackerHip;
        /// <summary>
        /// Per-hand active pull vectors. Non-null; may be empty.
        /// Each entry already has magnitude scaled by (grip_strength × pull_efficacy).
        /// </summary>
        public List<Vec2> GripPulls;
        /// <summary>
        /// Defender recovery vector (unit, aimed at origin). Stage 1 passes
        /// Vec2.Zero until top-side input is wired.
        /// </summary>
        public Vec2       DefenderRecovery;
    }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class PostureBreakOps
    {
        /// <summary>One integration step. Returns the next posture-break vector.</summary>
        public static Vec2 Update(
            Vec2 prev,
            PostureBreakInputs inputs,
            PostureBreakConfig? cfg = null)
        {
            var c      = cfg ?? PostureBreakConfig.Default;
            float dtSec = inputs.DtMs / 1000f;

            // 1. Exponential decay.
            float decay = (float)System.Math.Exp(-inputs.DtMs / c.DecayTauMs);
            float x     = prev.X * decay;
            float y     = prev.Y * decay;

            // 2. Attacker hip contribution.
            x += inputs.AttackerHip.HipLateral * c.KHipImpact * dtSec;
            y += inputs.AttackerHip.HipPush    * c.KHipImpact * dtSec;

            // 3. GRIPPED-hand pulls.
            if (inputs.GripPulls != null)
            {
                foreach (var p in inputs.GripPulls)
                {
                    x += p.X * c.KGripImpact * dtSec;
                    y += p.Y * c.KGripImpact * dtSec;
                }
            }

            // 4. Defender recovery.
            x -= inputs.DefenderRecovery.X * c.KRecovery * dtSec;
            y -= inputs.DefenderRecovery.Y * c.KRecovery * dtSec;

            // Clamp to unit disc.
            float mag = (float)System.Math.Sqrt(x * x + y * y);
            if (mag > c.MaxMagnitude)
            {
                float s = c.MaxMagnitude / mag;
                x *= s;
                y *= s;
            }

            return new Vec2(x, y);
        }

        /// <summary>§3.2 — Euclidean magnitude of the break vector.</summary>
        public static float Magnitude(Vec2 v) =>
            (float)System.Math.Sqrt(v.X * v.X + v.Y * v.Y);

        /// <summary>§3.4 — Paper-proto bucket 0..4.</summary>
        public static int Bucket(Vec2 v)
        {
            float m = Magnitude(v);
            if (m < 0.1f) return 0;
            if (m < 0.3f) return 1;
            if (m < 0.5f) return 2;
            if (m < 0.75f) return 3;
            return 4;
        }

        // -------------------------------------------------------------------------
        // Grip-pull direction table (§3.3 bullet 3)
        // -------------------------------------------------------------------------

        // These unit vectors encode zone → (lateral, sagittal) pull direction.
        // Intentionally coarse; tuning via playtest data.
        private static Vec2 ZonePullDir(GripZone zone)
        {
            switch (zone)
            {
                case GripZone.SleeveL:      return new Vec2(-0.5f,   0.87f);
                case GripZone.SleeveR:      return new Vec2( 0.5f,   0.87f);
                case GripZone.CollarL:      return new Vec2(-0.3f,   0.95f);
                case GripZone.CollarR:      return new Vec2( 0.3f,   0.95f);
                case GripZone.WristL:       return new Vec2(-1f,     0f);
                case GripZone.WristR:       return new Vec2( 1f,     0f);
                case GripZone.Belt:         return new Vec2( 0f,     0.9f);
                case GripZone.PostureBreak: return new Vec2( 0f,     1f);
                default:                   return Vec2.Zero;
            }
        }

        /// <summary>
        /// Returns the pull vector for a given grip zone and grip strength.
        /// Scales the canonical zone direction by <paramref name="strength"/>.
        /// </summary>
        public static Vec2 GripPullVector(GripZone zone, float strength)
        {
            var dir = ZonePullDir(zone);
            return new Vec2(dir.X * strength, dir.Y * strength);
        }
    }
}
