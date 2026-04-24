// Ported from src/prototype/web/src/state/counter_window.ts.
// See docs/design/input_system_defense_v1.md §D.
//
// The counter window mirrors the attacker's JudgmentWindowFSM lifecycle but
// with its own candidate set (counter techniques only).

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Counter technique enum
    // None at ordinal 0 = "no counter selected"
    // -------------------------------------------------------------------------

    public enum CounterTechnique
    {
        None = 0,
        ScissorCounter,
        TriangleEarlyStack,
    }

    // -------------------------------------------------------------------------
    // Counter table (§D.2)
    // -------------------------------------------------------------------------

    public static class CounterTable
    {
        /// <summary>Returns the counter technique for a given attacker technique, or None.</summary>
        public static CounterTechnique For(Technique t)
        {
            switch (t)
            {
                case Technique.ScissorSweep: return CounterTechnique.ScissorCounter;
                case Technique.Triangle:     return CounterTechnique.TriangleEarlyStack;
                default:                     return CounterTechnique.None;
            }
        }
    }

    // -------------------------------------------------------------------------
    // FSM state
    // -------------------------------------------------------------------------

    public enum CounterWindowState { Closed, Opening, Open, Closing }

    public struct CounterWindow
    {
        public CounterWindowState  State;
        public long                StateEnteredMs;
        public List<CounterTechnique> Candidates;
        public long                CooldownUntilMs;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct CounterTickInput
    {
        public long                    NowMs;
        /// <summary>True while the attacker window is OPENING or OPEN.</summary>
        public bool                    OpenAttackerWindow;
        /// <summary>Non-empty only on the frame we should enter OPENING (one-shot).</summary>
        public List<CounterTechnique>  OpeningSeed;
        public CounterTechnique?       ConfirmedCounter;
        public bool                    DismissRequested;
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum CounterEventKind
    {
        CounterWindowOpening,
        CounterWindowOpen,
        CounterWindowClosing,
        CounterWindowClosed,
        CounterConfirmed,
    }

    public enum CounterCloseReason
    {
        None = 0,
        Confirmed,
        Dismissed,
        TimedOut,
        AttackerClosed,
    }

    public struct CounterTickEvent
    {
        public CounterEventKind       Kind;
        public List<CounterTechnique> Candidates;  // for CounterWindowOpening
        public CounterCloseReason     CloseReason; // for CounterWindowClosing
        public CounterTechnique       Counter;     // for CounterConfirmed
    }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class CounterWindowOps
    {
        public static readonly CounterWindow Initial = new CounterWindow
        {
            State           = CounterWindowState.Closed,
            StateEnteredMs  = BJJConst.SentinelTimeMs,
            Candidates      = new List<CounterTechnique>(),
            CooldownUntilMs = BJJConst.SentinelTimeMs,
        };

        /// <summary>
        /// Build the counter candidate list from the attacker's technique candidates.
        /// Results appended to <paramref name="out_"/>; empty if nothing has a counter.
        /// </summary>
        public static void CounterCandidatesFor(
            List<Technique> attackerCandidates,
            List<CounterTechnique> out_)
        {
            if (attackerCandidates == null) return;
            foreach (var tech in attackerCandidates)
            {
                var c = CounterTable.For(tech);
                if (c != CounterTechnique.None && !out_.Contains(c))
                    out_.Add(c);
            }
        }

        public static (CounterWindow Next, float TimeScale) Tick(
            CounterWindow prev,
            CounterTickInput input,
            List<CounterTickEvent> events,
            WindowTiming? timing = null)
        {
            var t         = timing ?? WindowTiming.Default;
            var next      = prev;
            float timeScale = WindowTimeScale.Normal;

            switch (prev.State)
            {
                case CounterWindowState.Closed:
                {
                    bool hasSeed = input.OpeningSeed != null && input.OpeningSeed.Count > 0;
                    bool cooldownOk = prev.CooldownUntilMs == BJJConst.SentinelTimeMs ||
                                      input.NowMs >= prev.CooldownUntilMs;
                    if (hasSeed && cooldownOk)
                    {
                        next.State          = CounterWindowState.Opening;
                        next.StateEnteredMs = input.NowMs;
                        next.Candidates     = new List<CounterTechnique>(input.OpeningSeed);
                        events.Add(new CounterTickEvent
                        {
                            Kind       = CounterEventKind.CounterWindowOpening,
                            Candidates = next.Candidates,
                        });
                    }
                    break;
                }

                case CounterWindowState.Opening:
                {
                    float ratio = (float)(input.NowMs - prev.StateEnteredMs) / t.OpeningMs;
                    timeScale   = Lerp(WindowTimeScale.Normal, WindowTimeScale.Open, Clamp01(ratio));
                    if (!input.OpenAttackerWindow)
                    {
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind        = CounterEventKind.CounterWindowClosing,
                            CloseReason = CounterCloseReason.AttackerClosed,
                        });
                        break;
                    }
                    if (ratio >= 1f)
                    {
                        next.State          = CounterWindowState.Open;
                        next.StateEnteredMs = input.NowMs;
                        events.Add(new CounterTickEvent { Kind = CounterEventKind.CounterWindowOpen });
                        timeScale = WindowTimeScale.Open;
                    }
                    break;
                }

                case CounterWindowState.Open:
                {
                    timeScale = WindowTimeScale.Open;

                    if (input.ConfirmedCounter.HasValue &&
                        prev.Candidates.Contains(input.ConfirmedCounter.Value))
                    {
                        events.Add(new CounterTickEvent
                        {
                            Kind    = CounterEventKind.CounterConfirmed,
                            Counter = input.ConfirmedCounter.Value,
                        });
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind        = CounterEventKind.CounterWindowClosing,
                            CloseReason = CounterCloseReason.Confirmed,
                        });
                        break;
                    }

                    if (input.DismissRequested)
                    {
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind        = CounterEventKind.CounterWindowClosing,
                            CloseReason = CounterCloseReason.Dismissed,
                        });
                        break;
                    }

                    if (!input.OpenAttackerWindow)
                    {
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind        = CounterEventKind.CounterWindowClosing,
                            CloseReason = CounterCloseReason.AttackerClosed,
                        });
                        break;
                    }

                    if (input.NowMs - prev.StateEnteredMs >= t.OpenMaxMs)
                    {
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind        = CounterEventKind.CounterWindowClosing,
                            CloseReason = CounterCloseReason.TimedOut,
                        });
                    }
                    break;
                }

                case CounterWindowState.Closing:
                {
                    float ratio = (float)(input.NowMs - prev.StateEnteredMs) / t.ClosingMs;
                    timeScale   = Lerp(WindowTimeScale.Open, WindowTimeScale.Normal, Clamp01(ratio));
                    if (ratio >= 1f)
                    {
                        next = new CounterWindow
                        {
                            State           = CounterWindowState.Closed,
                            StateEnteredMs  = input.NowMs,
                            Candidates      = new List<CounterTechnique>(),
                            CooldownUntilMs = input.NowMs + t.CooldownMs,
                        };
                        events.Add(new CounterTickEvent { Kind = CounterEventKind.CounterWindowClosed });
                        timeScale = WindowTimeScale.Normal;
                    }
                    break;
                }
            }

            return (next, timeScale);
        }

        private static CounterWindow EnterClosing(CounterWindow prev, long nowMs)
        {
            var n = prev;
            n.State          = CounterWindowState.Closing;
            n.StateEnteredMs = nowMs;
            return n;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
