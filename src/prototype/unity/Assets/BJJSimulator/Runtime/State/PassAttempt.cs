// Ported 1:1 from src/prototype/web/src/state/pass_attempt.ts.
// PURE — pass-attempt state per docs/design/input_system_defense_v1.md §B.7.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public enum PassAttemptKind { Idle, InProgress }

    public struct PassAttemptState
    {
        public PassAttemptKind Kind;
        public long            StartedMs; // valid only when Kind == InProgress

        public static readonly PassAttemptState Idle = new PassAttemptState
        {
            Kind      = PassAttemptKind.Idle,
            StartedMs = BJJConst.SentinelTimeMs,
        };
    }

    // -------------------------------------------------------------------------
    // Timing
    // -------------------------------------------------------------------------

    public struct PassTiming
    {
        public int WindowMs; // 5000

        public static readonly PassTiming Default = new PassTiming { WindowMs = 5000 };
    }

    // -------------------------------------------------------------------------
    // Eligibility inputs (§B.7.1)
    // -------------------------------------------------------------------------

    public struct PassEligibilityParams
    {
        public ActorState Bottom;
        public ActorState Top;
        public float      DefenderStamina;
        public float      LeftBasePressure;
        public float      RightBasePressure;
        public BaseZone   LeftBaseZone;
        public BaseZone   RightBaseZone;
        public float      RsY;
        public GuardState Guard;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct PassTickInput
    {
        public long NowMs;
        public bool CommitRequested;
        public bool EligibleNow;
        // If the attacker pulls off a TRIANGLE during the 5s window, the pass fails.
        public bool AttackerTriangleConfirmedThisTick;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum PassEventKind { PassStarted, PassFailed, PassSucceeded }

    public struct PassTickEvent
    {
        public PassEventKind Kind;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class PassAttemptOps
    {
        // §B.7.1 — eligibility check.
        public static bool IsPassEligible(PassEligibilityParams p)
        {
            if (p.Guard != GuardState.Closed) return false;

            bool oneFootUnlocked =
                p.Bottom.LeftFoot.State == FootState.Unlocked ||
                p.Bottom.RightFoot.State == FootState.Unlocked;
            if (!oneFootUnlocked) return false;

            if (p.DefenderStamina < 0.2f) return false;

            if (!IsControlZone(p.LeftBaseZone) || !IsControlZone(p.RightBaseZone)) return false;
            if (p.LeftBasePressure < 0.5f || p.RightBasePressure < 0.5f) return false;

            if (p.RsY > -0.3f) return false;

            return true;
        }

        public static PassAttemptState Tick(
            PassAttemptState prev,
            PassTickInput inp,
            List<PassTickEvent> events,
            PassTiming timing = default)
        {
            if (timing.WindowMs == 0) timing = PassTiming.Default;

            if (prev.Kind == PassAttemptKind.Idle)
            {
                if (inp.CommitRequested && inp.EligibleNow)
                {
                    events.Add(new PassTickEvent { Kind = PassEventKind.PassStarted });
                    return new PassAttemptState { Kind = PassAttemptKind.InProgress, StartedMs = inp.NowMs };
                }
                return prev;
            }

            // IN_PROGRESS
            if (inp.AttackerTriangleConfirmedThisTick)
            {
                events.Add(new PassTickEvent { Kind = PassEventKind.PassFailed });
                return PassAttemptState.Idle;
            }

            if (inp.NowMs - prev.StartedMs >= timing.WindowMs)
            {
                events.Add(new PassTickEvent { Kind = PassEventKind.PassSucceeded });
                return PassAttemptState.Idle;
            }

            return prev;
        }

        // §B.7.1 control zones: BICEP_* or KNEE_*
        static bool IsControlZone(BaseZone z) =>
            z == BaseZone.BicepL || z == BaseZone.BicepR ||
            z == BaseZone.KneeL  || z == BaseZone.KneeR;
    }
}
