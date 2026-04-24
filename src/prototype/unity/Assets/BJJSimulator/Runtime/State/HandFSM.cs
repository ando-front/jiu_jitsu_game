// Ported 1:1 from src/prototype/web/src/state/hand_fsm.ts.
// See docs/design/state_machines_v1.md §2.1 for the transition table and
// docs/design/stage2_port_plan_v1.md §2 for C# port conventions.
//
// Time handling: the FSM stores absolute timestamps (ms) at which transitions
// began. Each tick receives nowMs from the caller so the same state logic
// is testable without a real clock.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Timing (§2.1.2 of state_machines_v1.md)
    // -------------------------------------------------------------------------

    public struct HandTiming
    {
        public int ReachMinMs;    // §C.1.2 REACHING 200–350ms
        public int ReachMaxMs;
        public int RetractMs;     // §C.1.2 RETRACT 150ms
        public int ShortMemoryMs; // §C.2 parry short-term memory

        public static readonly HandTiming Default = new HandTiming
        {
            ReachMinMs    = 200,
            ReachMaxMs    = 350,
            RetractMs     = 150,
            ShortMemoryMs = 400,
        };
    }

    // -------------------------------------------------------------------------
    // State (value type — sits inside a larger GameState aggregate)
    // -------------------------------------------------------------------------

    public struct HandFSM
    {
        public HandSide Side;
        public HandState State;
        public GripZone  Target;           // where the hand is currently reaching / gripping
        public long      StateEnteredMs;   // timestamp of the most recent state entry
        public int       ReachDurationMs;  // chosen at REACHING entry
        public GripZone  LastParriedZone;
        public long      LastParriedAtMs;  // BJJConst.SentinelTimeMs until a parry occurs
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct HandTickInput
    {
        public long     NowMs;
        public float    TriggerValue;             // [0, 1]
        public GripZone TargetZone;               // currently intended zone (from Layer B)
        public bool     ForceReleaseAll;           // BTN_RELEASE edge
        // Contact resolution — supplied by caller; HandFSM alone cannot know
        // the opponent's defensive state.
        public bool     OpponentDefendsThisZone;
        // Opponent cut success against this GRIPPED hand (§4.2 / §2.1.4).
        public bool     OpponentCutSucceeded;
        // §2.1.4 last row: opponent posture moved the target out of reach.
        public bool     TargetOutOfReach;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum GripBrokenReason
    {
        TriggerReleased,
        ForceRelease,
        OpponentCut,
        OutOfReach,
    }

    public enum HandEventKind
    {
        ReachStarted,
        Contact,
        Gripped,
        Parried,
        GripBroken,
    }

    // "Fat struct" event — one struct carries every event variant, with
    // Kind deciding which payload fields are meaningful.
    public struct HandTickEvent
    {
        public HandEventKind    Kind;
        public HandSide         Side;
        public GripZone         Zone;
        // Only meaningful for Kind == GripBroken.
        public GripBrokenReason GripBrokenReason;
    }

    // -------------------------------------------------------------------------
    // Pure transition functions
    // -------------------------------------------------------------------------

    public static class HandFSMOps
    {
        /// <summary>Initialise a hand in IDLE at <paramref name="nowMs"/>.</summary>
        public static HandFSM Initial(HandSide side, long nowMs = 0) => new HandFSM
        {
            Side            = side,
            State           = HandState.Idle,
            Target          = GripZone.None,
            StateEnteredMs  = nowMs,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        /// <summary>
        /// One FSM tick. Returns the next state. Any produced events are
        /// appended to <paramref name="events"/> — the list is never cleared,
        /// so callers can accumulate events across multiple tick sites per frame.
        /// </summary>
        public static HandFSM Tick(
            HandFSM prev,
            HandTickInput input,
            List<HandTickEvent> events,
            HandTiming? timing = null)
        {
            var t = timing ?? HandTiming.Default;
            var next = prev;

            // Global escape: BTN_RELEASE forces any engaged hand back through
            // RETRACT (§B.3 "事故の出口").
            bool wasEngaged =
                prev.State == HandState.Gripped ||
                prev.State == HandState.Contact ||
                prev.State == HandState.Reaching;

            if (input.ForceReleaseAll && wasEngaged)
            {
                if (prev.State == HandState.Gripped && prev.Target != GripZone.None)
                    events.Add(new HandTickEvent
                    {
                        Kind             = HandEventKind.GripBroken,
                        Side             = prev.Side,
                        Zone             = prev.Target,
                        GripBrokenReason = GripBrokenReason.ForceRelease,
                    });
                return EnterRetract(prev, input.NowMs);
            }

            switch (prev.State)
            {
                case HandState.Idle:
                {
                    // §2.1.2 — trigger press + a target zone kicks off REACHING.
                    // No zone = no action; a player pressing the trigger without
                    // aiming does nothing (§B.2.2 gating).
                    if (input.TriggerValue > 0f && input.TargetZone != GripZone.None)
                    {
                        next.State           = HandState.Reaching;
                        next.Target          = input.TargetZone;
                        next.StateEnteredMs  = input.NowMs;
                        next.ReachDurationMs = ChooseReachDuration(t);
                        events.Add(new HandTickEvent
                        {
                            Kind = HandEventKind.ReachStarted,
                            Side = prev.Side,
                            Zone = input.TargetZone,
                        });
                    }
                    break;
                }

                case HandState.Reaching:
                {
                    // Abort back to IDLE if the player releases the trigger
                    // mid-reach. Nothing was ever contacted, so we skip RETRACT
                    // (quiet cancel).
                    if (input.TriggerValue == 0f)
                    {
                        next.State          = HandState.Idle;
                        next.Target         = GripZone.None;
                        next.StateEnteredMs = input.NowMs;
                        break;
                    }
                    // Zone re-aim: if the player swings RS to a new zone
                    // mid-reach, restart the reach toward the new target.
                    if (input.TargetZone != GripZone.None && input.TargetZone != prev.Target)
                    {
                        next.Target          = input.TargetZone;
                        next.StateEnteredMs  = input.NowMs;
                        next.ReachDurationMs = ChooseReachDuration(t);
                        events.Add(new HandTickEvent
                        {
                            Kind = HandEventKind.ReachStarted,
                            Side = prev.Side,
                            Zone = input.TargetZone,
                        });
                        break;
                    }
                    // Reach timer expired → CONTACT (1 frame).
                    if (input.NowMs - prev.StateEnteredMs >= prev.ReachDurationMs)
                    {
                        next.State          = HandState.Contact;
                        next.StateEnteredMs = input.NowMs;
                        if (prev.Target != GripZone.None)
                            events.Add(new HandTickEvent
                            {
                                Kind = HandEventKind.Contact,
                                Side = prev.Side,
                                Zone = prev.Target,
                            });
                    }
                    break;
                }

                case HandState.Contact:
                {
                    // §2.1.3 — resolution happens on the frame after CONTACT
                    // entry. Priority order (strict): opponent-defends >
                    // short-memory > grip.
                    if (prev.Target == GripZone.None)
                        return EnterIdle(prev, input.NowMs);

                    // §2.5 sentinel guard: skip the time delta when
                    // LastParriedAtMs is the sentinel, otherwise long.MinValue
                    // subtraction overflows.
                    bool hasParryMemory  = prev.LastParriedAtMs != BJJConst.SentinelTimeMs;
                    bool recentlyParried =
                        hasParryMemory &&
                        prev.LastParriedZone == prev.Target &&
                        (input.NowMs - prev.LastParriedAtMs) < t.ShortMemoryMs;

                    if (input.OpponentDefendsThisZone || recentlyParried)
                    {
                        next.State           = HandState.Parried;
                        next.StateEnteredMs  = input.NowMs;
                        next.LastParriedZone = prev.Target;
                        next.LastParriedAtMs = input.NowMs;
                        events.Add(new HandTickEvent
                        {
                            Kind = HandEventKind.Parried,
                            Side = prev.Side,
                            Zone = prev.Target,
                        });
                    }
                    else
                    {
                        next.State          = HandState.Gripped;
                        next.StateEnteredMs = input.NowMs;
                        events.Add(new HandTickEvent
                        {
                            Kind = HandEventKind.Gripped,
                            Side = prev.Side,
                            Zone = prev.Target,
                        });
                    }
                    break;
                }

                case HandState.Parried:
                    // §2.1.2 — PARRIED is instantaneous; next tick → RETRACT.
                    return EnterRetract(prev, input.NowMs);

                case HandState.Gripped:
                {
                    // §2.1.4 — any break condition routes back through RETRACT.
                    if (input.OpponentCutSucceeded && prev.Target != GripZone.None)
                    {
                        events.Add(GripBrokenEvent(prev.Side, prev.Target, GripBrokenReason.OpponentCut));
                        return EnterRetract(prev, input.NowMs);
                    }
                    if (input.TargetOutOfReach && prev.Target != GripZone.None)
                    {
                        events.Add(GripBrokenEvent(prev.Side, prev.Target, GripBrokenReason.OutOfReach));
                        return EnterRetract(prev, input.NowMs);
                    }
                    if (input.TriggerValue == 0f && prev.Target != GripZone.None)
                    {
                        events.Add(GripBrokenEvent(prev.Side, prev.Target, GripBrokenReason.TriggerReleased));
                        return EnterRetract(prev, input.NowMs);
                    }
                    // GRIPPED persists — grip strength is read directly from
                    // the live trigger by downstream layers (Layer C). We don't
                    // store it here because it's a pure function of the current
                    // frame's trigger.
                    break;
                }

                case HandState.Retract:
                {
                    // §2.1.2 — retract timer; no new REACH allowed during this window.
                    if (input.NowMs - prev.StateEnteredMs >= t.RetractMs)
                        return EnterIdle(prev, input.NowMs);
                    break;
                }
            }

            return next;
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static HandFSM EnterIdle(HandFSM prev, long nowMs)
        {
            var n = prev;
            n.State           = HandState.Idle;
            n.Target          = GripZone.None;
            n.StateEnteredMs  = nowMs;
            n.ReachDurationMs = 0;
            return n;
        }

        private static HandFSM EnterRetract(HandFSM prev, long nowMs)
        {
            var n = prev;
            n.State           = HandState.Retract;
            n.Target          = GripZone.None;
            n.StateEnteredMs  = nowMs;
            n.ReachDurationMs = 0;
            return n;
        }

        private static int ChooseReachDuration(HandTiming t)
        {
            // §C.1.2 — "200–350ms, distance-dependent, linear". Stage 1 has no
            // world positions; midpoint is deterministic and matches TS.
            return (t.ReachMinMs + t.ReachMaxMs) / 2;
        }

        private static HandTickEvent GripBrokenEvent(HandSide side, GripZone zone, GripBrokenReason reason)
            => new HandTickEvent
            {
                Kind             = HandEventKind.GripBroken,
                Side             = side,
                Zone             = zone,
                GripBrokenReason = reason,
            };
    }
}
