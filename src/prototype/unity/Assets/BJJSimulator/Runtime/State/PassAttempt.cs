// Ported from src/prototype/web/src/state/pass_attempt.ts.
// See docs/design/input_system_defense_v1.md §B.7.
//
// M1 simplification: skip the 5-second animation phase; produce one of three
// terminal outcomes on commit:
//   - ineligible        → commit silently rejected
//   - counterConfirmed  → PASS_FAILED (attacker triangle-counters during window)
//   - otherwise         → PASS_SUCCEEDED after passWindowMs elapses

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Pass attempt state (tagged union via Kind enum)
    // -------------------------------------------------------------------------

    public enum PassSlotKind { Idle, InProgress }

    public struct PassAttemptState
    {
        public PassSlotKind Kind;
        /// <summary>Timestamp at which the pass started. Valid only when Kind == InProgress.</summary>
        public long         StartedMs;
    }

    // -------------------------------------------------------------------------
    // Timing
    // -------------------------------------------------------------------------

    public static class PassTiming
    {
        public const int WindowMs = 5000;
    }

    // -------------------------------------------------------------------------
    // Eligibility params (§B.7.1)
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
        public long  NowMs;
        public bool  CommitRequested;
        public bool  EligibleNow;
        /// <summary>True if the attacker confirmed a TRIANGLE this tick (§B.7).</summary>
        public bool  AttackerTriangleConfirmedThisTick;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum PassEventKind { PassStarted, PassFailed, PassSucceeded }

    public struct PassTickEvent { public PassEventKind Kind; }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class PassAttemptOps
    {
        public static readonly PassAttemptState Initial = new PassAttemptState
        {
            Kind      = PassSlotKind.Idle,
            StartedMs = 0,
        };

        /// <summary>§B.7.1 — eligibility check. All conditions must hold for a pass to start.</summary>
        public static bool IsPassEligible(PassEligibilityParams p)
        {
            if (p.Guard != GuardState.Closed) return false;

            bool oneFootUnlocked =
                p.Bottom.LeftFoot.State  == FootState.Unlocked ||
                p.Bottom.RightFoot.State == FootState.Unlocked;
            if (!oneFootUnlocked) return false;

            if (p.DefenderStamina < 0.2f) return false;

            bool controlZone(BaseZone z) =>
                z == BaseZone.BicepL || z == BaseZone.BicepR ||
                z == BaseZone.KneeL  || z == BaseZone.KneeR;

            if (!controlZone(p.LeftBaseZone)  || p.LeftBasePressure  < 0.5f) return false;
            if (!controlZone(p.RightBaseZone) || p.RightBasePressure < 0.5f) return false;

            if (p.RsY > -0.3f) return false;

            return true;
        }

        public static PassAttemptState Tick(
            PassAttemptState prev,
            PassTickInput inp,
            List<PassTickEvent> events,
            int windowMs = PassTiming.WindowMs)
        {
            if (prev.Kind == PassSlotKind.Idle)
            {
                if (inp.CommitRequested && inp.EligibleNow)
                {
                    events.Add(new PassTickEvent { Kind = PassEventKind.PassStarted });
                    return new PassAttemptState { Kind = PassSlotKind.InProgress, StartedMs = inp.NowMs };
                }
                return prev;
            }

            // IN_PROGRESS.
            if (inp.AttackerTriangleConfirmedThisTick)
            {
                events.Add(new PassTickEvent { Kind = PassEventKind.PassFailed });
                return Initial;
            }

            if (inp.NowMs - prev.StartedMs >= windowMs)
            {
                events.Add(new PassTickEvent { Kind = PassEventKind.PassSucceeded });
                return Initial;
            }

            return prev;
        }
    }
}
