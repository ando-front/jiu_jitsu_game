// Ported 1:1 from src/prototype/web/src/state/foot_fsm.ts.
// See docs/design/state_machines_v1.md §2.2 for the transition table.
//
// FootSide and FootState enums are declared in BJJCoreTypes.cs.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Timing (§2.2 of state_machines_v1.md)
    // -------------------------------------------------------------------------

    public struct FootTiming
    {
        /// <summary>§2.2 — re-lock attempt takes this long (ms).</summary>
        public int LockingMs;

        public static readonly FootTiming Default = new FootTiming { LockingMs = 300 };
    }

    // -------------------------------------------------------------------------
    // Locking posture threshold
    // -------------------------------------------------------------------------

    /// <summary>
    /// §2.2 — LOCKING succeeds only when the opponent's sagittal posture break
    /// is at or above this value at timer expiry.
    /// </summary>
    public static class FootConst
    {
        public const float LockingPostureThreshold = 0.3f;
    }

    // -------------------------------------------------------------------------
    // State (value type)
    // -------------------------------------------------------------------------

    public struct FootFSM
    {
        public FootSide  Side;
        public FootState State;
        public long      StateEnteredMs;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct FootTickInput
    {
        public long  NowMs;
        /// <summary>True on the frame the bumper button edge fires for this side.</summary>
        public bool  BumperEdge;
        /// <summary>
        /// Opponent's sagittal posture-break component, read at LOCKING expiry
        /// to decide LOCK_SUCCEEDED vs LOCK_FAILED.
        /// </summary>
        public float OpponentPostureSagittal;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum FootEventKind
    {
        Unlocked,
        LockingStarted,
        LockSucceeded,
        LockFailed,
    }

    public struct FootTickEvent
    {
        public FootEventKind Kind;
        public FootSide      Side;
    }

    // -------------------------------------------------------------------------
    // Pure transition functions
    // -------------------------------------------------------------------------

    public static class FootFSMOps
    {
        /// <summary>Initialise a foot in LOCKED state (closed guard default).</summary>
        public static FootFSM Initial(FootSide side, long nowMs = 0) => new FootFSM
        {
            Side           = side,
            State          = FootState.Locked,
            StateEnteredMs = nowMs,
        };

        /// <summary>
        /// One FSM tick. Returns the next state. Any produced events are
        /// appended to <paramref name="events"/>; the list is never cleared.
        /// </summary>
        public static FootFSM Tick(
            FootFSM prev,
            FootTickInput input,
            List<FootTickEvent> events,
            FootTiming? timing = null)
        {
            var t    = timing ?? FootTiming.Default;
            var next = prev;

            switch (prev.State)
            {
                case FootState.Locked:
                {
                    if (input.BumperEdge)
                    {
                        next.State          = FootState.Unlocked;
                        next.StateEnteredMs = input.NowMs;
                        events.Add(new FootTickEvent { Kind = FootEventKind.Unlocked, Side = prev.Side });
                    }
                    break;
                }

                case FootState.Unlocked:
                {
                    if (input.BumperEdge)
                    {
                        next.State          = FootState.Locking;
                        next.StateEnteredMs = input.NowMs;
                        events.Add(new FootTickEvent { Kind = FootEventKind.LockingStarted, Side = prev.Side });
                    }
                    break;
                }

                case FootState.Locking:
                {
                    // Pressing bumper again cancels the locking attempt.
                    if (input.BumperEdge)
                    {
                        next.State          = FootState.Unlocked;
                        next.StateEnteredMs = input.NowMs;
                        break;
                    }
                    if (input.NowMs - prev.StateEnteredMs >= t.LockingMs)
                    {
                        if (input.OpponentPostureSagittal >= FootConst.LockingPostureThreshold)
                        {
                            next.State          = FootState.Locked;
                            next.StateEnteredMs = input.NowMs;
                            events.Add(new FootTickEvent { Kind = FootEventKind.LockSucceeded, Side = prev.Side });
                        }
                        else
                        {
                            next.State          = FootState.Unlocked;
                            next.StateEnteredMs = input.NowMs;
                            events.Add(new FootTickEvent { Kind = FootEventKind.LockFailed, Side = prev.Side });
                        }
                    }
                    break;
                }
            }

            return next;
        }
    }
}
