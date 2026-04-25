// Ported 1:1 from src/prototype/web/src/state/counter_window.ts.
// PURE — defensive counter window per docs/design/input_system_defense_v1.md §D.
//
// A counter window opens only when the attacker's JudgmentWindow enters OPENING
// for a technique that has a registered counter.
//
// Success effects (§D.2):
//   SCISSOR_COUNTER       → forces attacker window to CLOSING (DISRUPTED)
//   TRIANGLE_EARLY_STACK  → same + resets top.arm_extracted both sides to false

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Counter techniques (§D.2)
    // -------------------------------------------------------------------------

    public enum CounterTechnique
    {
        ScissorCounter,
        TriangleEarlyStack,
    }

    public enum CounterWindowState { Closed, Opening, Open, Closing }

    public enum CounterCloseReason
    {
        Confirmed,
        Dismissed,
        TimedOut,
        AttackerClosed, // attacker window shut — defender loses the chance
    }

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public struct CounterWindow
    {
        public CounterWindowState  State;
        public long                StateEnteredMs;
        public CounterTechnique[]  Candidates;
        public long                CooldownUntilMs;

        public static readonly CounterWindow Initial = new CounterWindow
        {
            State           = CounterWindowState.Closed,
            StateEnteredMs  = BJJConst.SentinelTimeMs,
            Candidates      = System.Array.Empty<CounterTechnique>(),
            CooldownUntilMs = BJJConst.SentinelTimeMs,
        };
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

    public struct CounterTickEvent
    {
        public CounterEventKind    Kind;
        public CounterTechnique[]  Candidates;     // CounterWindowOpening
        public CounterCloseReason  CloseReason;    // CounterWindowClosing
        public CounterTechnique    ConfirmedCounter; // CounterConfirmed
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct CounterTickInput
    {
        public long               NowMs;
        public bool               OpenAttackerWindow;  // true while attacker window is OPENING or OPEN
        public CounterTechnique[] OpeningSeed;         // set exactly on the frame we should enter OPENING
        public CounterTechnique?  ConfirmedCounter;    // nullable
        public bool               DismissRequested;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class CounterWindowOps
    {
        // §D.2 — counter table. Not every technique has a counter in M1.
        public static CounterTechnique[] CounterCandidatesFor(Technique[] attackerCandidates)
        {
            var result = new List<CounterTechnique>();
            foreach (var t in attackerCandidates)
            {
                if (TryGetCounter(t, out var c) && !result.Contains(c))
                    result.Add(c);
            }
            return result.ToArray();
        }

        static bool TryGetCounter(Technique t, out CounterTechnique c)
        {
            switch (t)
            {
                case Technique.ScissorSweep: c = CounterTechnique.ScissorCounter;      return true;
                case Technique.Triangle:     c = CounterTechnique.TriangleEarlyStack;  return true;
                default:                     c = default;                               return false;
            }
        }

        /// <summary>
        /// Advance the CounterWindow FSM by one tick.
        /// <paramref name="events"/> is append-only.
        /// </summary>
        public static CounterWindow Tick(
            CounterWindow prev,
            CounterTickInput input,
            List<CounterTickEvent> events,
            out float timeScale,
            JudgmentWindowTiming timing = default)
        {
            if (timing.OpeningMs == 0) timing = JudgmentWindowTiming.Default;

            timeScale = JudgmentWindowTimeScale.Normal;
            var next = prev;

            switch (prev.State)
            {
                case CounterWindowState.Closed:
                {
                    if (input.OpeningSeed != null && input.OpeningSeed.Length > 0 &&
                        input.NowMs >= prev.CooldownUntilMs)
                    {
                        next = new CounterWindow
                        {
                            State           = CounterWindowState.Opening,
                            StateEnteredMs  = input.NowMs,
                            Candidates      = CopyArray(input.OpeningSeed),
                            CooldownUntilMs = prev.CooldownUntilMs,
                        };
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
                    float t = (float)(input.NowMs - prev.StateEnteredMs) / timing.OpeningMs;
                    timeScale = Lerp(JudgmentWindowTimeScale.Normal, JudgmentWindowTimeScale.Open, Clamp01(t));

                    if (!input.OpenAttackerWindow)
                    {
                        next = EnterClosing(prev, input.NowMs);
                        events.Add(new CounterTickEvent
                        {
                            Kind         = CounterEventKind.CounterWindowClosing,
                            CloseReason  = CounterCloseReason.AttackerClosed,
                        });
                        break;
                    }
                    if (t >= 1f)
                    {
                        next = new CounterWindow
                        {
                            State           = CounterWindowState.Open,
                            StateEnteredMs  = input.NowMs,
                            Candidates      = prev.Candidates,
                            CooldownUntilMs = prev.CooldownUntilMs,
                        };
                        events.Add(new CounterTickEvent { Kind = CounterEventKind.CounterWindowOpen });
                        timeScale = JudgmentWindowTimeScale.Open;
                    }
                    break;
                }

                case CounterWindowState.Open:
                {
                    timeScale = JudgmentWindowTimeScale.Open;

                    if (input.ConfirmedCounter.HasValue &&
                        ArrayContains(prev.Candidates, input.ConfirmedCounter.Value))
                    {
                        events.Add(new CounterTickEvent
                        {
                            Kind             = CounterEventKind.CounterConfirmed,
                            ConfirmedCounter = input.ConfirmedCounter.Value,
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

                    if (input.NowMs - prev.StateEnteredMs >= timing.OpenMaxMs)
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
                    float t = (float)(input.NowMs - prev.StateEnteredMs) / timing.ClosingMs;
                    timeScale = Lerp(JudgmentWindowTimeScale.Open, JudgmentWindowTimeScale.Normal, Clamp01(t));
                    if (t >= 1f)
                    {
                        next = new CounterWindow
                        {
                            State           = CounterWindowState.Closed,
                            StateEnteredMs  = input.NowMs,
                            Candidates      = System.Array.Empty<CounterTechnique>(),
                            CooldownUntilMs = input.NowMs + timing.CooldownMs,
                        };
                        events.Add(new CounterTickEvent { Kind = CounterEventKind.CounterWindowClosed });
                        timeScale = JudgmentWindowTimeScale.Normal;
                    }
                    break;
                }
            }

            return next;
        }

        // -----------------------------------------------------------------------

        static CounterWindow EnterClosing(CounterWindow prev, long nowMs) =>
            new CounterWindow
            {
                State           = CounterWindowState.Closing,
                StateEnteredMs  = nowMs,
                Candidates      = prev.Candidates,
                CooldownUntilMs = prev.CooldownUntilMs,
            };

        static bool ArrayContains(CounterTechnique[] arr, CounterTechnique t)
        {
            foreach (var x in arr) if (x == t) return true;
            return false;
        }

        static CounterTechnique[] CopyArray(CounterTechnique[] src)
        {
            var dst = new CounterTechnique[src.Length];
            System.Array.Copy(src, dst, src.Length);
            return dst;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
