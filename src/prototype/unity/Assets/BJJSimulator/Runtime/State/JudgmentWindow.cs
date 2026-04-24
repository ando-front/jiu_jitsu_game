// Ported from src/prototype/web/src/state/judgment_window.ts.
// See docs/design/state_machines_v1.md §8.
//
// Lifecycle: CLOSED → OPENING (0.2s) → OPEN (≤1.5s) → CLOSING (0.3s) → CLOSED
// with a 400ms cooldown before the next OPENING can begin.

using System.Collections.Generic;

namespace BJJSimulator
{
    // -------------------------------------------------------------------------
    // Technique enum (§8.2 M1 techniques)
    // None at ordinal 0 = "no technique selected"
    // -------------------------------------------------------------------------

    public enum Technique
    {
        None = 0,
        ScissorSweep,
        FlowerSweep,
        Triangle,
        Omoplata,
        HipBump,
        CrossCollar,
    }

    // -------------------------------------------------------------------------
    // Window FSM state
    // -------------------------------------------------------------------------

    public enum JudgmentWindowState { Closed, Opening, Open, Closing }

    // -------------------------------------------------------------------------
    // Timing (§8.1)
    // -------------------------------------------------------------------------

    public struct WindowTiming
    {
        public int OpeningMs;
        public int OpenMaxMs;
        public int ClosingMs;
        public int CooldownMs;

        public static readonly WindowTiming Default = new WindowTiming
        {
            OpeningMs  = 200,
            OpenMaxMs  = 1500,
            ClosingMs  = 300,
            CooldownMs = 400,
        };
    }

    // -------------------------------------------------------------------------
    // Time-scale values (§8.1)
    // -------------------------------------------------------------------------

    public static class WindowTimeScale
    {
        public const float Normal = 1.0f;
        public const float Open   = 0.3f;
    }

    // -------------------------------------------------------------------------
    // FSM state struct (value type)
    // -------------------------------------------------------------------------

    public struct JudgmentWindow
    {
        public JudgmentWindowState State;
        public long                StateEnteredMs;
        /// <summary>Frozen at OPENING entry (§8.3).</summary>
        public List<Technique>     Candidates;
        public long                CooldownUntilMs;
        /// <summary>Which side fired the window. None (HasFiredBy == false) while CLOSED.</summary>
        public bool                HasFiredBy;
        /// <summary>Meaningful only when HasFiredBy is true.</summary>
        public bool                FiredByBottom; // true = Bottom, false = Top
    }

    // -------------------------------------------------------------------------
    // Judgment context — narrow snapshot for firing predicates
    // -------------------------------------------------------------------------

    public struct JudgmentContext
    {
        public ActorState Bottom;
        public ActorState Top;
        public float      BottomHipYaw;       // from Intent.Hip.HipAngleTarget
        public float      BottomHipPush;      // from Intent.Hip.HipPush
        /// <summary>ms so far of hip_push ≥ 0.5 sustained (§D.1.1).</summary>
        public float      SustainedHipPushMs;
    }

    // -------------------------------------------------------------------------
    // Tick input
    // -------------------------------------------------------------------------

    public struct JudgmentTickInput
    {
        public long      NowMs;
        /// <summary>Null = no commit this frame.</summary>
        public Technique? ConfirmedTechnique;
        public bool      DismissRequested;
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
        None = 0,
        Confirmed,
        Dismissed,
        TimedOut,
        Disrupted,
    }

    public struct JudgmentTickEvent
    {
        public JudgmentEventKind Kind;
        /// <summary>Only meaningful for WindowOpening.</summary>
        public List<Technique>   Candidates;
        /// <summary>Only meaningful for WindowClosing.</summary>
        public WindowCloseReason CloseReason;
        /// <summary>Only meaningful for TechniqueConfirmed.</summary>
        public Technique         Technique;
    }

    // -------------------------------------------------------------------------
    // Pure functions
    // -------------------------------------------------------------------------

    public static class JudgmentWindowOps
    {
        public static readonly JudgmentWindow Initial = new JudgmentWindow
        {
            State           = JudgmentWindowState.Closed,
            StateEnteredMs  = BJJConst.SentinelTimeMs,
            Candidates      = new List<Technique>(),
            CooldownUntilMs = BJJConst.SentinelTimeMs,
            HasFiredBy      = false,
            FiredByBottom   = true,
        };

        // -------------------------------------------------------------------------
        // Firing predicates (§8.2)
        // -------------------------------------------------------------------------

        public static bool ScissorSweepConditions(JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            if (b.LeftFoot.State  != FootState.Locked) return false;
            if (b.RightFoot.State != FootState.Locked) return false;
            var sleeve = AnyHandGrippedAt(b,
                z => z == GripZone.SleeveL || z == GripZone.SleeveR,
                0.6f, strengthL, strengthR);
            if (!sleeve.HasValue) return false;
            return PostureBreakOps.Magnitude(ctx.Top.PostureBreak) >= 0.4f;
        }

        public static bool FlowerSweepConditions(JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            if (b.LeftFoot.State  != FootState.Locked) return false;
            if (b.RightFoot.State != FootState.Locked) return false;
            var wrist = AnyHandGrippedAt(b,
                z => z == GripZone.WristL || z == GripZone.WristR,
                0f, strengthL, strengthR);
            if (!wrist.HasValue) return false;
            return ctx.Top.PostureBreak.Y >= 0.5f;
        }

        public static bool TriangleConditions(JudgmentContext ctx)
        {
            var b = ctx.Bottom;
            bool oneFootUnlocked =
                b.LeftFoot.State  == FootState.Unlocked ||
                b.RightFoot.State == FootState.Unlocked;
            if (!oneFootUnlocked) return false;
            if (!ctx.Top.ArmExtractedLeft && !ctx.Top.ArmExtractedRight) return false;
            bool collarGripped =
                (b.LeftHand.State  == HandState.Gripped && IsCollar(b.LeftHand.Target))  ||
                (b.RightHand.State == HandState.Gripped && IsCollar(b.RightHand.Target));
            return collarGripped;
        }

        public static bool OmoplataConditions(JudgmentContext ctx)
        {
            var b = ctx.Bottom;
            HandSide? sleeveHand = PickSleeveHand(b);
            if (!sleeveHand.HasValue) return false;
            if (ctx.Top.PostureBreak.Y < 0.6f) return false;
            float sideSign = sleeveHand.Value == HandSide.L ? -1f : 1f;
            float xSign    = ctx.Top.PostureBreak.X > 0f ? 1f
                           : ctx.Top.PostureBreak.X < 0f ? -1f : 0f;
            if (xSign != sideSign) return false;
            return System.Math.Abs(ctx.BottomHipYaw) >= System.Math.PI / 3.0;
        }

        public static bool HipBumpConditions(JudgmentContext ctx)
        {
            if (ctx.Top.PostureBreak.Y < 0.7f) return false;
            return ctx.SustainedHipPushMs >= 300f;
        }

        public static bool CrossCollarConditions(JudgmentContext ctx, float strengthL, float strengthR)
        {
            var b = ctx.Bottom;
            bool lOk = b.LeftHand.State  == HandState.Gripped && IsCollar(b.LeftHand.Target)  && strengthL >= 0.7f;
            bool rOk = b.RightHand.State == HandState.Gripped && IsCollar(b.RightHand.Target) && strengthR >= 0.7f;
            if (!(lOk && rOk)) return false;
            return PostureBreakOps.Magnitude(ctx.Top.PostureBreak) >= 0.5f;
        }

        /// <summary>Evaluate all technique predicates; return those currently satisfied.</summary>
        public static List<Technique> EvaluateAllTechniques(
            JudgmentContext ctx, float strengthL, float strengthR)
        {
            var out_ = new List<Technique>();
            if (ScissorSweepConditions(ctx, strengthL, strengthR)) out_.Add(Technique.ScissorSweep);
            if (FlowerSweepConditions(ctx, strengthL, strengthR))  out_.Add(Technique.FlowerSweep);
            if (TriangleConditions(ctx))                           out_.Add(Technique.Triangle);
            if (OmoplataConditions(ctx))                           out_.Add(Technique.Omoplata);
            if (HipBumpConditions(ctx))                            out_.Add(Technique.HipBump);
            if (CrossCollarConditions(ctx, strengthL, strengthR))  out_.Add(Technique.CrossCollar);
            return out_;
        }

        // -------------------------------------------------------------------------
        // FSM tick
        // -------------------------------------------------------------------------

        /// <returns>
        /// The next FSM state and the time scale to apply this frame.
        /// Events are appended to <paramref name="events"/>.
        /// </returns>
        public static (JudgmentWindow Next, float TimeScale) Tick(
            JudgmentWindow prev,
            List<Technique> currentlySatisfied,
            JudgmentTickInput tick,
            List<JudgmentTickEvent> events,
            WindowTiming? timing = null)
        {
            var t         = timing ?? WindowTiming.Default;
            var next      = prev;
            float timeScale = WindowTimeScale.Normal;

            switch (prev.State)
            {
                case JudgmentWindowState.Closed:
                {
                    bool cooldownOk = prev.CooldownUntilMs == BJJConst.SentinelTimeMs ||
                                      tick.NowMs >= prev.CooldownUntilMs;
                    if (cooldownOk && currentlySatisfied.Count > 0)
                    {
                        next.State          = JudgmentWindowState.Opening;
                        next.StateEnteredMs = tick.NowMs;
                        next.Candidates     = new List<Technique>(currentlySatisfied);
                        next.HasFiredBy     = true;
                        next.FiredByBottom  = true; // Stage 1: only bottom fires
                        events.Add(new JudgmentTickEvent
                        {
                            Kind       = JudgmentEventKind.WindowOpening,
                            Candidates = next.Candidates,
                        });
                    }
                    break;
                }

                case JudgmentWindowState.Opening:
                {
                    float ratio = (float)(tick.NowMs - prev.StateEnteredMs) / t.OpeningMs;
                    timeScale   = Lerp(WindowTimeScale.Normal, WindowTimeScale.Open, Clamp01(ratio));
                    if (ratio >= 1f)
                    {
                        next.State          = JudgmentWindowState.Open;
                        next.StateEnteredMs = tick.NowMs;
                        events.Add(new JudgmentTickEvent { Kind = JudgmentEventKind.WindowOpen });
                        timeScale = WindowTimeScale.Open;
                    }
                    break;
                }

                case JudgmentWindowState.Open:
                {
                    timeScale = WindowTimeScale.Open;

                    if (tick.ConfirmedTechnique.HasValue &&
                        prev.Candidates.Contains(tick.ConfirmedTechnique.Value))
                    {
                        events.Add(new JudgmentTickEvent
                        {
                            Kind      = JudgmentEventKind.TechniqueConfirmed,
                            Technique = tick.ConfirmedTechnique.Value,
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

                    // §8.3 — disruption if every candidate lost its conditions.
                    bool anyStillValid = false;
                    foreach (var tech in prev.Candidates)
                        if (currentlySatisfied.Contains(tech)) { anyStillValid = true; break; }

                    if (!anyStillValid)
                    {
                        next = EnterClosing(prev, tick.NowMs);
                        events.Add(new JudgmentTickEvent
                        {
                            Kind        = JudgmentEventKind.WindowClosing,
                            CloseReason = WindowCloseReason.Disrupted,
                        });
                        break;
                    }

                    if (tick.NowMs - prev.StateEnteredMs >= t.OpenMaxMs)
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
                    float ratio = (float)(tick.NowMs - prev.StateEnteredMs) / t.ClosingMs;
                    timeScale   = Lerp(WindowTimeScale.Open, WindowTimeScale.Normal, Clamp01(ratio));
                    if (ratio >= 1f)
                    {
                        next = new JudgmentWindow
                        {
                            State           = JudgmentWindowState.Closed,
                            StateEnteredMs  = tick.NowMs,
                            Candidates      = new List<Technique>(),
                            CooldownUntilMs = tick.NowMs + t.CooldownMs,
                            HasFiredBy      = false,
                            FiredByBottom   = true,
                        };
                        events.Add(new JudgmentTickEvent { Kind = JudgmentEventKind.WindowClosed });
                        timeScale = WindowTimeScale.Normal;
                    }
                    break;
                }
            }

            return (next, timeScale);
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static JudgmentWindow EnterClosing(JudgmentWindow prev, long nowMs)
        {
            var n = prev;
            n.State          = JudgmentWindowState.Closing;
            n.StateEnteredMs = nowMs;
            return n;
        }

        private static HandSide? AnyHandGrippedAt(
            ActorState actor,
            System.Func<GripZone, bool> matchesZone,
            float minStrength,
            float strengthL,
            float strengthR)
        {
            if (actor.LeftHand.State  == HandState.Gripped &&
                actor.LeftHand.Target != GripZone.None     &&
                matchesZone(actor.LeftHand.Target)         &&
                strengthL >= minStrength)
                return HandSide.L;
            if (actor.RightHand.State  == HandState.Gripped &&
                actor.RightHand.Target != GripZone.None     &&
                matchesZone(actor.RightHand.Target)         &&
                strengthR >= minStrength)
                return HandSide.R;
            return null;
        }

        private static HandSide? PickSleeveHand(ActorState actor)
        {
            if (actor.LeftHand.State  == HandState.Gripped && IsSleeve(actor.LeftHand.Target))  return HandSide.L;
            if (actor.RightHand.State == HandState.Gripped && IsSleeve(actor.RightHand.Target)) return HandSide.R;
            return null;
        }

        private static bool IsCollar(GripZone z) => z == GripZone.CollarL || z == GripZone.CollarR;
        private static bool IsSleeve(GripZone z) => z == GripZone.SleeveL || z == GripZone.SleeveR;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
