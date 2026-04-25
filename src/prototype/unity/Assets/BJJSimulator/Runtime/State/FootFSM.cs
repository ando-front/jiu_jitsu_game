// Ported 1:1 from src/prototype/web/src/state/foot_fsm.ts.
// PURE — FootFSM per docs/design/state_machines_v1.md §2.2.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Timing constants (§2.2)
    // -------------------------------------------------------------------------

    public struct FootTiming
    {
        public int LockingMs; // re-lock attempt takes 300 ms

        public static readonly FootTiming Default = new FootTiming { LockingMs = 300 };
    }

    // §2.2 — LOCKING succeeds when the opponent's posture is forward-broken.
    // Threshold read at timer expiry so a briefly-forward posture doesn't win.
    public static partial class FootFSMOps
    {
        public const float LockingPostureThreshold = 0.3f;
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public struct FootFSM
    {
        public FootSide  Side;
        public FootState State;
        public long      StateEnteredMs;
    }

    // -------------------------------------------------------------------------
    // Inputs per tick
    // -------------------------------------------------------------------------

    public struct FootTickInput
    {
        public long  NowMs;
        public bool  BumperEdge;              // L/R bumper edge for this side
        public float OpponentPostureSagittal; // used at LOCKING → LOCKED resolution
    }

    // -------------------------------------------------------------------------
    // Events (fat struct — §2.2 transition table)
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
    // Pure operations
    // -------------------------------------------------------------------------

    public static partial class FootFSMOps
    {
        public static FootFSM Initial(FootSide side, long nowMs = 0) =>
            new FootFSM { Side = side, State = FootState.Locked, StateEnteredMs = nowMs };

        /// <summary>
        /// Advance the FSM by one tick.
        /// <paramref name="events"/> is append-only — events are added, never cleared.
        /// </summary>
        public static FootFSM Tick(
            FootFSM prev,
            FootTickInput input,
            List<FootTickEvent> events,
            FootTiming? timing = null)
        {
            var t = timing ?? FootTiming.Default;

            switch (prev.State)
            {
                case FootState.Locked:
                {
                    if (input.BumperEdge)
                    {
                        events.Add(new FootTickEvent { Kind = FootEventKind.Unlocked, Side = prev.Side });
                        return new FootFSM { Side = prev.Side, State = FootState.Unlocked, StateEnteredMs = input.NowMs };
                    }
                    break;
                }
                case FootState.Unlocked:
                {
                    if (input.BumperEdge)
                    {
                        events.Add(new FootTickEvent { Kind = FootEventKind.LockingStarted, Side = prev.Side });
                        return new FootFSM { Side = prev.Side, State = FootState.Locking, StateEnteredMs = input.NowMs };
                    }
                    break;
                }
                case FootState.Locking:
                {
                    // Player cancelled the re-lock attempt by pressing bumper again.
                    if (input.BumperEdge)
                    {
                        return new FootFSM { Side = prev.Side, State = FootState.Unlocked, StateEnteredMs = input.NowMs };
                    }
                    if (input.NowMs - prev.StateEnteredMs >= t.LockingMs)
                    {
                        if (input.OpponentPostureSagittal >= LockingPostureThreshold)
                        {
                            events.Add(new FootTickEvent { Kind = FootEventKind.LockSucceeded, Side = prev.Side });
                            return new FootFSM { Side = prev.Side, State = FootState.Locked, StateEnteredMs = input.NowMs };
                        }
                        else
                        {
                            events.Add(new FootTickEvent { Kind = FootEventKind.LockFailed, Side = prev.Side });
                            return new FootFSM { Side = prev.Side, State = FootState.Unlocked, StateEnteredMs = input.NowMs };
                        }
                    }
                    break;
                }
            }

            return prev;
        }
    }
}
