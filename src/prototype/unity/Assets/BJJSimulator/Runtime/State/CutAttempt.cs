// Ported 1:1 from src/prototype/web/src/state/cut_attempt.ts.
// PURE — defender cut-attempt per docs/design/state_machines_v1.md §4.2.
//
// Each defender hand maintains its own CutAttempt slot. On CUT_ATTEMPT (with an
// RS direction), we pick the attacker's GRIPPED hand whose zone best matches the
// RS aim, start a 1500ms timer, and at expiry evaluate the attacker's current
// grip strength:
//   strength < 0.5  → CUT_SUCCEEDED (attacker hand forced into RETRACT)
//   strength ≥ 0.5  → CUT_FAILED

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public enum CutSlotKind { Idle, InProgress }

    public struct CutAttemptSlot
    {
        public CutSlotKind Kind;
        public long        StartedMs;          // valid when InProgress
        public HandSide    TargetAttackerSide;  // L or R attacker hand
        public GripZone    TargetZone;

        public static readonly CutAttemptSlot Idle = new CutAttemptSlot { Kind = CutSlotKind.Idle };
    }

    public struct CutAttempts
    {
        public CutAttemptSlot Left;  // defender's left hand
        public CutAttemptSlot Right; // defender's right hand

        public static readonly CutAttempts Initial = new CutAttempts
        {
            Left  = CutAttemptSlot.Idle,
            Right = CutAttemptSlot.Idle,
        };
    }

    // -------------------------------------------------------------------------
    // Timing
    // -------------------------------------------------------------------------

    public struct CutTiming
    {
        public int   AttemptMs;
        public float SuccessStrengthThreshold;

        public static readonly CutTiming Default = new CutTiming
        {
            AttemptMs                = 1500,
            SuccessStrengthThreshold = 0.5f,
        };
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct CutCommit
    {
        public Vec2 Rs;
    }

    public struct CutTickInput
    {
        public long          NowMs;
        public CutCommit?    LeftCommit;   // null = no commit this frame
        public CutCommit?    RightCommit;
        public HandFSM       AttackerLeft;
        public HandFSM       AttackerRight;
        public float         AttackerTriggerL;
        public float         AttackerTriggerR;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum CutEventKind { CutStarted, CutSucceeded, CutFailed }

    public struct CutTickEvent
    {
        public CutEventKind Kind;
        public HandSide     DefenderSide;  // L or R defender hand
        public HandSide     AttackerSide;  // CutStarted + CutSucceeded
        public GripZone     Zone;          // CutStarted
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class CutAttemptOps
    {
        /// <summary>
        /// Pick which attacker hand to target based on RS direction.
        /// Returns false when no attacker hand is GRIPPED (§B.4.1 "候補が0なら不発").
        /// </summary>
        public static bool PickCutTarget(
            Vec2 rs,
            HandFSM attackerLeft,
            HandFSM attackerRight,
            out HandSide side,
            out GripZone zone)
        {
            float mag = (float)System.Math.Sqrt(rs.X * rs.X + rs.Y * rs.Y);

            if (mag < 1e-6f)
            {
                // No RS direction — fall back to the first GRIPPED hand.
                if (attackerLeft.State == HandState.Gripped && attackerLeft.Target != GripZone.None)
                {
                    side = HandSide.L; zone = attackerLeft.Target; return true;
                }
                if (attackerRight.State == HandState.Gripped && attackerRight.Target != GripZone.None)
                {
                    side = HandSide.R; zone = attackerRight.Target; return true;
                }
                side = HandSide.L; zone = GripZone.None; return false;
            }

            // With an aim direction, prefer attacker L when rs.x < 0, else R.
            bool preferLeft = rs.X < 0f;
            bool lGripped   = attackerLeft.State  == HandState.Gripped && attackerLeft.Target  != GripZone.None;
            bool rGripped   = attackerRight.State == HandState.Gripped && attackerRight.Target != GripZone.None;

            if (lGripped && (!rGripped || preferLeft))
            {
                side = HandSide.L; zone = attackerLeft.Target; return true;
            }
            if (rGripped)
            {
                side = HandSide.R; zone = attackerRight.Target; return true;
            }

            side = HandSide.L; zone = GripZone.None; return false;
        }

        public static CutAttempts Tick(
            CutAttempts prev,
            CutTickInput input,
            List<CutTickEvent> events,
            CutTiming timing = default)
        {
            if (timing.AttemptMs == 0) timing = CutTiming.Default;

            var nextLeft  = TickSlot(HandSide.L, prev.Left,  input.LeftCommit,  input, events, timing);
            var nextRight = TickSlot(HandSide.R, prev.Right, input.RightCommit, input, events, timing);
            return new CutAttempts { Left = nextLeft, Right = nextRight };
        }

        // -----------------------------------------------------------------------

        static CutAttemptSlot TickSlot(
            HandSide defenderSide,
            CutAttemptSlot prev,
            CutCommit? commit,
            CutTickInput input,
            List<CutTickEvent> events,
            CutTiming timing)
        {
            if (prev.Kind == CutSlotKind.InProgress)
            {
                if (input.NowMs - prev.StartedMs >= timing.AttemptMs)
                {
                    float strength = prev.TargetAttackerSide == HandSide.L
                        ? input.AttackerTriggerL
                        : input.AttackerTriggerR;

                    if (strength < timing.SuccessStrengthThreshold)
                    {
                        events.Add(new CutTickEvent
                        {
                            Kind         = CutEventKind.CutSucceeded,
                            DefenderSide = defenderSide,
                            AttackerSide = prev.TargetAttackerSide,
                        });
                    }
                    else
                    {
                        events.Add(new CutTickEvent
                        {
                            Kind         = CutEventKind.CutFailed,
                            DefenderSide = defenderSide,
                        });
                    }
                    return CutAttemptSlot.Idle;
                }
                // Still running; a second commit is ignored.
                return prev;
            }

            // IDLE: honour a commit if one is provided and a target exists.
            if (commit.HasValue)
            {
                if (PickCutTarget(commit.Value.Rs, input.AttackerLeft, input.AttackerRight,
                                  out var targetSide, out var targetZone))
                {
                    events.Add(new CutTickEvent
                    {
                        Kind         = CutEventKind.CutStarted,
                        DefenderSide = defenderSide,
                        AttackerSide = targetSide,
                        Zone         = targetZone,
                    });
                    return new CutAttemptSlot
                    {
                        Kind               = CutSlotKind.InProgress,
                        StartedMs          = input.NowMs,
                        TargetAttackerSide = targetSide,
                        TargetZone         = targetZone,
                    };
                }
                // No target → silent drop (§B.4.1 "音声フィードバックのみ").
            }

            return prev;
        }
    }
}
