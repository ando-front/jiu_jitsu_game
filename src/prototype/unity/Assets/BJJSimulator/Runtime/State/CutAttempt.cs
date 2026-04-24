// Ported from src/prototype/web/src/state/cut_attempt.ts.
// See docs/design/state_machines_v1.md §4.2.
//
// Each defender hand maintains its own CutAttempt slot. On commit, we pick
// the attacker's GRIPPED hand whose zone best matches the RS aim, start a
// 1500ms timer, and at expiry evaluate the current grip strength:
//   strength < 0.5  → cut SUCCEEDS (attacker hand forced into RETRACT)
//   strength ≥ 0.5  → cut FAILS

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Per-hand slot (tagged union via Kind enum)
    // -------------------------------------------------------------------------

    public enum CutSlotKind { Idle, InProgress }

    public struct CutAttemptSlot
    {
        public CutSlotKind Kind;
        public long        StartedMs;
        /// <summary>Which attacker hand the cut targets. Valid only when Kind == InProgress.</summary>
        public HandSide    TargetAttackerSide;
        public GripZone    TargetZone;
    }

    public struct CutAttempts
    {
        public CutAttemptSlot Left;   // defender's left hand
        public CutAttemptSlot Right;  // defender's right hand
    }

    // -------------------------------------------------------------------------
    // Timing
    // -------------------------------------------------------------------------

    public static class CutTiming
    {
        public const int AttemptMs = 1500;
        public const float SuccessStrengthThreshold = 0.5f;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct CutTickInput
    {
        public long    NowMs;
        /// <summary>RS direction for the left defender hand commit (null = no commit).</summary>
        public Vec2?   LeftCommit;
        public Vec2?   RightCommit;
        public HandFSM AttackerLeft;
        public HandFSM AttackerRight;
        public float   AttackerTriggerL;
        public float   AttackerTriggerR;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum CutEventKind { CutStarted, CutSucceeded, CutFailed }

    public struct CutTickEvent
    {
        public CutEventKind Kind;
        public HandSide     DefenderSide;
        public HandSide     AttackerSide;  // for Started / Succeeded
        public GripZone     Zone;          // for Started
    }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class CutAttemptOps
    {
        public static readonly CutAttempts Initial = new CutAttempts
        {
            Left  = new CutAttemptSlot { Kind = CutSlotKind.Idle },
            Right = new CutAttemptSlot { Kind = CutSlotKind.Idle },
        };

        public static CutAttempts Tick(
            CutAttempts prev,
            CutTickInput input,
            List<CutTickEvent> events,
            int attemptMs = CutTiming.AttemptMs)
        {
            var nextLeft  = TickSlot(HandSide.L, prev.Left,  input.LeftCommit,  input, events, attemptMs);
            var nextRight = TickSlot(HandSide.R, prev.Right, input.RightCommit, input, events, attemptMs);
            return new CutAttempts { Left = nextLeft, Right = nextRight };
        }

        /// <summary>
        /// Decides which attacker hand to target given the defender RS vector and
        /// the attacker's current GRIPPED hands.
        /// Returns false if no attacker hand is GRIPPED (§B.4.1 "候補が0なら不発").
        /// </summary>
        public static bool PickCutTarget(
            Vec2 rs,
            HandFSM attackerLeft,
            HandFSM attackerRight,
            out HandSide side,
            out GripZone zone)
        {
            float mag = rs.Magnitude;
            if (mag < 1e-6f)
            {
                // No RS direction — fall back to first GRIPPED hand.
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

            bool preferLeft = rs.X < 0f;
            HandFSM? l = (attackerLeft.State  == HandState.Gripped && attackerLeft.Target  != GripZone.None) ? attackerLeft  : (HandFSM?)null;
            HandFSM? r = (attackerRight.State == HandState.Gripped && attackerRight.Target != GripZone.None) ? attackerRight : (HandFSM?)null;

            if (l.HasValue && (!r.HasValue || preferLeft))
            {
                side = HandSide.L; zone = l.Value.Target; return true;
            }
            if (r.HasValue)
            {
                side = HandSide.R; zone = r.Value.Target; return true;
            }

            side = HandSide.L; zone = GripZone.None; return false;
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static CutAttemptSlot TickSlot(
            HandSide defenderSide,
            CutAttemptSlot prev,
            Vec2? commit,
            CutTickInput input,
            List<CutTickEvent> events,
            int attemptMs)
        {
            if (prev.Kind == CutSlotKind.InProgress)
            {
                if (input.NowMs - prev.StartedMs >= attemptMs)
                {
                    float strength = prev.TargetAttackerSide == HandSide.L
                        ? input.AttackerTriggerL
                        : input.AttackerTriggerR;

                    if (strength < CutTiming.SuccessStrengthThreshold)
                        events.Add(new CutTickEvent
                        {
                            Kind         = CutEventKind.CutSucceeded,
                            DefenderSide = defenderSide,
                            AttackerSide = prev.TargetAttackerSide,
                        });
                    else
                        events.Add(new CutTickEvent
                        {
                            Kind         = CutEventKind.CutFailed,
                            DefenderSide = defenderSide,
                        });

                    return new CutAttemptSlot { Kind = CutSlotKind.Idle };
                }
                // Still running; a second commit request is ignored.
                return prev;
            }

            // IDLE: honour a commit if one is provided and a target exists.
            if (commit.HasValue)
            {
                if (PickCutTarget(commit.Value, input.AttackerLeft, input.AttackerRight,
                    out var side, out var zone))
                {
                    events.Add(new CutTickEvent
                    {
                        Kind         = CutEventKind.CutStarted,
                        DefenderSide = defenderSide,
                        AttackerSide = side,
                        Zone         = zone,
                    });
                    return new CutAttemptSlot
                    {
                        Kind               = CutSlotKind.InProgress,
                        StartedMs          = input.NowMs,
                        TargetAttackerSide = side,
                        TargetZone         = zone,
                    };
                }
                // No target → silent drop (§B.4.1 "音声フィードバックのみ").
            }

            return prev;
        }
    }
}
