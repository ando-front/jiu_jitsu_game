// Ported 1:1 from src/prototype/web/src/state/judgment_window.ts.
// PURE — JudgmentWindowFSM per docs/design/state_machines_v1.md §8.
//
// Lifecycle: CLOSED → OPENING (0.2s) → OPEN (≤1.5s) → CLOSING (0.3s) → CLOSED
// with a 400ms cooldown before the next OPENING can begin.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Techniques (§8.2)
    // -------------------------------------------------------------------------

    public enum Technique
    {
        ScissorSweep,
        FlowerSweep,
        Triangle,
        Omoplata,
        HipBump,
        CrossCollar,
    }

    // -------------------------------------------------------------------------
    // FSM state
    // -------------------------------------------------------------------------

    public enum JudgmentWindowState
    {
        Closed,
        Opening,
        Open,
        Closing,
    }

    // §8.1 timing table (ms).
    public struct JudgmentWindowTiming
    {
        public int OpeningMs;
        public int OpenMaxMs;
        public int ClosingMs;
        public int CooldownMs;

        public static readonly JudgmentWindowTiming Default = new JudgmentWindowTiming
        {
            OpeningMs  = 200,
            OpenMaxMs  = 1500,
            ClosingMs  = 300,
            CooldownMs = 400,
        };
    }

    // §8.1 time-scale values.
    public static class JudgmentWindowTimeScale
    {
        public const float Normal = 1.0f;
        public const float Open   = 0.3f;
    }

    // "Which side fired the window?" — None when CLOSED.
    public enum WindowSide { None = 0, Bottom, Top }

    public struct JudgmentWindow
    {
        public JudgmentWindowState State;
        public long                StateEnteredMs;
        public Technique[]         Candidates;     // frozen at OPENING entry
        public long                CooldownUntilMs;
        public WindowSide          FiredBy;        // None while CLOSED

        public static readonly JudgmentWindow Initial = new JudgmentWindow
        {
            State           = JudgmentWindowState.Closed,
            StateEnteredMs  = BJJConst.SentinelTimeMs,
            Candidates      = System.Array.Empty<Technique>(),
            CooldownUntilMs = BJJConst.SentinelTimeMs,
            FiredBy         = WindowSide.None,
        };
    }

    // -------------------------------------------------------------------------
    // Context for firing predicates (§8.2)
    // Narrow snapshot — predicates are independently unit-testable.
    // -------------------------------------------------------------------------

    public struct JudgmentContext
    {
        public ActorState Bottom;
        public ActorState Top;
        public float      BottomHipYaw;         // from intent.hip.hip_angle_target
        public float      BottomHipPush;         // from intent.hip.hip_push
        public float      SustainedHipPushMs;    // ms so far of hip_push ≥ 0.5 sustained
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public enum JudgmentEventKind
    {
        WindowOpening,
        WindowOpen,
        WindowClosing,
        WindowClosed,
        TechniqueConfirmed,
    }

    public enum WindowCloseReason
    {
        Confirmed,
        Dismissed,
        TimedOut,
        Disrupted, // §8.3 — all candidates collapsed
    }

    public struct JudgmentTickEvent
    {
        public JudgmentEventKind Kind;
        // WindowOpening
        public Technique[]       Candidates;
        public WindowSide        FiredBy;
        // WindowClosing
        public WindowCloseReason CloseReason;
        // TechniqueConfirmed
        public Technique         ConfirmedTechnique;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct JudgmentTickInput
    {
        public long      NowMs;
        public Technique? ConfirmedTechnique; // nullable — null = no confirm this frame
        public bool      DismissRequested;
    }

    // -------------------------------------------------------------------------
    // Pure operations
    // -------------------------------------------------------------------------

    public static class JudgmentWindowOps
    {
        // --- Firing predicates (§8.2) ----------------------------------------

        public static bool ScissorSweepConditions(
            JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            if (b.LeftFoot.State != FootState.Locked || b.RightFoot.State != FootState.Locked)
                return false;

            bool sleeveGripped = false;
            if (b.LeftHand.State == HandState.Gripped &&
                (b.LeftHand.Target == GripZone.SleeveL || b.LeftHand.Target == GripZone.SleeveR) &&
                strengthL >= 0.6f)
                sleeveGripped = true;
            else if (b.RightHand.State == HandState.Gripped &&
                     (b.RightHand.Target == GripZone.SleeveL || b.RightHand.Target == GripZone.SleeveR) &&
                     strengthR >= 0.6f)
                sleeveGripped = true;

            if (!sleeveGripped) return false;
            return PostureBreakOps.BreakMagnitude(ctx.Top.PostureBreak) >= 0.4f;
        }

        public static bool FlowerSweepConditions(
            JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            if (b.LeftFoot.State != FootState.Locked || b.RightFoot.State != FootState.Locked)
                return false;

            bool wristGripped = false;
            if (b.LeftHand.State == HandState.Gripped &&
                (b.LeftHand.Target == GripZone.WristL || b.LeftHand.Target == GripZone.WristR))
                wristGripped = true;
            else if (b.RightHand.State == HandState.Gripped &&
                     (b.RightHand.Target == GripZone.WristL || b.RightHand.Target == GripZone.WristR))
                wristGripped = true;

            if (!wristGripped) return false;
            return ctx.Top.PostureBreak.Y >= 0.5f;
        }

        public static bool TriangleConditions(JudgmentContext ctx)
        {
            var b = ctx.Bottom;
            bool oneFootUnlocked = b.LeftFoot.State == FootState.Unlocked ||
                                   b.RightFoot.State == FootState.Unlocked;
            if (!oneFootUnlocked) return false;

            bool eitherArmExtracted = ctx.Top.ArmExtractedLeft || ctx.Top.ArmExtractedRight;
            if (!eitherArmExtracted) return false;

            bool collarGripped =
                (b.LeftHand.State  == HandState.Gripped && IsCollar(b.LeftHand.Target)) ||
                (b.RightHand.State == HandState.Gripped && IsCollar(b.RightHand.Target));
            return collarGripped;
        }

        public static bool OmoplataConditions(JudgmentContext ctx)
        {
            var b = ctx.Bottom;
            HandSide? sleeveHand = PickSleeveHand(b);
            if (sleeveHand == null) return false;

            if (ctx.Top.PostureBreak.Y < 0.6f) return false;

            int sideSign = sleeveHand == HandSide.L ? -1 : 1;
            if (System.Math.Sign(ctx.Top.PostureBreak.X) != sideSign) return false;

            return System.Math.Abs(ctx.BottomHipYaw) >= (float)(System.Math.PI / 3.0);
        }

        public static bool HipBumpConditions(JudgmentContext ctx)
        {
            if (ctx.Top.PostureBreak.Y < 0.7f) return false;
            return ctx.SustainedHipPushMs >= 300f;
        }

        public static bool CrossCollarConditions(
            JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            bool lOk = b.LeftHand.State  == HandState.Gripped && IsCollar(b.LeftHand.Target)  && strengthL >= 0.7f;
            bool rOk = b.RightHand.State == HandState.Gripped && IsCollar(b.RightHand.Target) && strengthR >= 0.7f;
            if (!(lOk && rOk)) return false;
            return PostureBreakOps.BreakMagnitude(ctx.Top.PostureBreak) >= 0.5f;
        }

        // Evaluate every technique predicate; return the currently-satisfied set.
        public static Technique[] EvaluateAllTechniques(
            JudgmentContext ctx, float strengthL, float strengthR)
        {
            var out_ = new List<Technique>(6);
            if (ScissorSweepConditions(ctx, strengthL, strengthR)) out_.Add(Technique.ScissorSweep);
            if (FlowerSweepConditions(ctx, strengthL, strengthR))  out_.Add(Technique.FlowerSweep);
            if (TriangleConditions(ctx))                            out_.Add(Technique.Triangle);
            if (OmoplataConditions(ctx))                            out_.Add(Technique.Omoplata);
            if (HipBumpConditions(ctx))                             out_.Add(Technique.HipBump);
            if (CrossCollarConditions(ctx, strengthL, strengthR))   out_.Add(Technique.CrossCollar);
            return out_.ToArray();
        }

        // --- FSM tick ---------------------------------------------------------

        /// <summary>
        /// Advance the JudgmentWindow FSM by one tick.
        /// <paramref name="events"/> is append-only.
        /// Returns the next window state; <paramref name="timeScale"/> is the
        /// slowdown factor for this tick.
        /// </summary>
        public static JudgmentWindow Tick(
            JudgmentWindow prev,
            Technique[] currentlySatisfied,
            JudgmentTickInput tick,
            List<JudgmentTickEvent> events,
            out float timeScale,
            JudgmentWindowTiming timing = default)
        {
            if (timing.OpeningMs == 0) timing = JudgmentWindowTiming.Default;

            timeScale = JudgmentWindowTimeScale.Normal;
            var next = prev;

            switch (prev.State)
            {
                case JudgmentWindowState.Closed:
                {
                    if (tick.NowMs >= prev.CooldownUntilMs && currentlySatisfied.Length > 0)
                    {
                        next = new JudgmentWindow
                        {
                            State           = JudgmentWindowState.Opening,
                            StateEnteredMs  = tick.NowMs,
                            Candidates      = CopyArray(currentlySatisfied),
                            CooldownUntilMs = prev.CooldownUntilMs,
                            FiredBy         = WindowSide.Bottom, // Stage 1: only bottom triggers
                        };
                        events.Add(new JudgmentTickEvent
                        {
                            Kind       = JudgmentEventKind.WindowOpening,
                            Candidates = next.Candidates,
                            FiredBy    = WindowSide.Bottom,
                        });
                    }
                    break;
                }

                case JudgmentWindowState.Opening:
                {
                    float t = (float)(tick.NowMs - prev.StateEnteredMs) / timing.OpeningMs;
                    timeScale = Lerp(JudgmentWindowTimeScale.Normal, JudgmentWindowTimeScale.Open, Clamp01(t));
                    if (t >= 1f)
                    {
                        next = new JudgmentWindow
                        {
                            State           = JudgmentWindowState.Open,
                            StateEnteredMs  = tick.NowMs,
                            Candidates      = prev.Candidates,
                            CooldownUntilMs = prev.CooldownUntilMs,
                            FiredBy         = prev.FiredBy,
                        };
                        events.Add(new JudgmentTickEvent { Kind = JudgmentEventKind.WindowOpen });
                        timeScale = JudgmentWindowTimeScale.Open;
                    }
                    break;
                }

                case JudgmentWindowState.Open:
                {
                    timeScale = JudgmentWindowTimeScale.Open;

                    // Confirm has priority.
                    if (tick.ConfirmedTechnique.HasValue && ArrayContains(prev.Candidates, tick.ConfirmedTechnique.Value))
                    {
                        events.Add(new JudgmentTickEvent
                        {
                            Kind               = JudgmentEventKind.TechniqueConfirmed,
                            ConfirmedTechnique = tick.ConfirmedTechnique.Value,
                        });
                        next = EnterClosing(prev, tick.NowMs);
                        events.Add(new JudgmentTickEvent
                        {
                            Kind        = JudgmentEventKind.WindowClosing,
                            CloseReason = WindowCloseReason.Confirmed,
                        });
                        break;
                    }

                    if (tick.DismissRequested)
                    {
                        next = EnterClosing(prev, tick.NowMs);
                        events.Add(new JudgmentTickEvent
                        {
                            Kind        = JudgmentEventKind.WindowClosing,
                            CloseReason = WindowCloseReason.Dismissed,
                        });
                        break;
                    }

                    // §8.3 disruption if every candidate lost its conditions.
                    if (!AnyStillValid(prev.Candidates, currentlySatisfied))
                    {
                        next = EnterClosing(prev, tick.NowMs);
                        events.Add(new JudgmentTickEvent
                        {
                            Kind        = JudgmentEventKind.WindowClosing,
                            CloseReason = WindowCloseReason.Disrupted,
                        });
                        break;
                    }

                    if (tick.NowMs - prev.StateEnteredMs >= timing.OpenMaxMs)
                    {
                        next = EnterClosing(prev, tick.NowMs);
                        events.Add(new JudgmentTickEvent
                        {
                            Kind        = JudgmentEventKind.WindowClosing,
                            CloseReason = WindowCloseReason.TimedOut,
                        });
                    }
                    break;
                }

                case JudgmentWindowState.Closing:
                {
                    float t = (float)(tick.NowMs - prev.StateEnteredMs) / timing.ClosingMs;
                    timeScale = Lerp(JudgmentWindowTimeScale.Open, JudgmentWindowTimeScale.Normal, Clamp01(t));
                    if (t >= 1f)
                    {
                        next = new JudgmentWindow
                        {
                            State           = JudgmentWindowState.Closed,
                            StateEnteredMs  = tick.NowMs,
                            Candidates      = System.Array.Empty<Technique>(),
                            CooldownUntilMs = tick.NowMs + timing.CooldownMs,
                            FiredBy         = WindowSide.None,
                        };
                        events.Add(new JudgmentTickEvent { Kind = JudgmentEventKind.WindowClosed });
                        timeScale = JudgmentWindowTimeScale.Normal;
                    }
                    break;
                }
            }

            return next;
        }

        // -----------------------------------------------------------------------

        static JudgmentWindow EnterClosing(JudgmentWindow prev, long nowMs) =>
            new JudgmentWindow
            {
                State           = JudgmentWindowState.Closing,
                StateEnteredMs  = nowMs,
                Candidates      = prev.Candidates,
                CooldownUntilMs = prev.CooldownUntilMs,
                FiredBy         = prev.FiredBy,
            };

        static bool AnyStillValid(Technique[] candidates, Technique[] currentlySatisfied)
        {
            foreach (var c in candidates)
                foreach (var s in currentlySatisfied)
                    if (c == s) return true;
            return false;
        }

        static bool ArrayContains(Technique[] arr, Technique t)
        {
            foreach (var x in arr) if (x == t) return true;
            return false;
        }

        static Technique[] CopyArray(Technique[] src)
        {
            var dst = new Technique[src.Length];
            System.Array.Copy(src, dst, src.Length);
            return dst;
        }

        static HandSide? PickSleeveHand(ActorState actor)
        {
            if (actor.LeftHand.State  == HandState.Gripped && IsSleeve(actor.LeftHand.Target))  return HandSide.L;
            if (actor.RightHand.State == HandState.Gripped && IsSleeve(actor.RightHand.Target)) return HandSide.R;
            return null;
        }

        static bool IsCollar(GripZone z) => z == GripZone.CollarL || z == GripZone.CollarR;
        static bool IsSleeve(GripZone z) => z == GripZone.SleeveL || z == GripZone.SleeveR;

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
