// NUnit EditMode mirror of src/prototype/web/tests/unit/judgment_window.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class JudgmentWindowTest
    {
        // --- helpers ----------------------------------------------------------

        static HandFSM GrippedHand(HandSide side, GripZone zone, long atMs = 0) => new HandFSM
        {
            Side = side, State = HandState.Gripped, Target = zone,
            StateEnteredMs = atMs, ReachDurationMs = 0,
            LastParriedZone = GripZone.None, LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        static HandFSM IdleHand(HandSide side) => new HandFSM
        {
            Side = side, State = HandState.Idle, Target = GripZone.None,
            StateEnteredMs = 0, ReachDurationMs = 0,
            LastParriedZone = GripZone.None, LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        static FootFSM Foot(FootSide side, FootState state) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = 0 };

        static ActorState Actor(
            HandFSM? lh = null, HandFSM? rh = null,
            FootFSM? lf = null, FootFSM? rf = null,
            Vec2 pb = default,
            bool armExtL = false, bool armExtR = false)
        {
            var init = GameStateOps.InitialActorState();
            return new ActorState
            {
                LeftHand          = lh ?? init.LeftHand,
                RightHand         = rh ?? init.RightHand,
                LeftFoot          = lf ?? init.LeftFoot,
                RightFoot         = rf ?? init.RightFoot,
                PostureBreak      = pb,
                Stamina           = 1f,
                ArmExtractedLeft  = armExtL,
                ArmExtractedRight = armExtR,
            };
        }

        static JudgmentContext Ctx(
            ActorState? bottom = null, ActorState? top = null,
            float hipYaw = 0f, float hipPush = 0f, float sustainedPushMs = 0f)
        {
            var a = GameStateOps.InitialActorState();
            return new JudgmentContext
            {
                Bottom             = bottom ?? a,
                Top                = top    ?? a,
                BottomHipYaw       = hipYaw,
                BottomHipPush      = hipPush,
                SustainedHipPushMs = sustainedPushMs,
            };
        }

        static (JudgmentWindow Next, List<JudgmentTickEvent> Events) Tick(
            JudgmentWindow w, Technique[] cand, long nowMs,
            Technique? confirm = null, bool dismiss = false)
        {
            var events = new List<JudgmentTickEvent>();
            float ts;
            var next = JudgmentWindowOps.Tick(w, cand, new JudgmentTickInput
            {
                NowMs = nowMs, ConfirmedTechnique = confirm, DismissRequested = dismiss,
            }, events, out ts);
            return (next, events);
        }

        static readonly Technique[] ScissorOnly = { Technique.ScissorSweep };
        static readonly Technique[] None        = System.Array.Empty<Technique>();

        // --- per-technique predicates -----------------------------------------

        [Test]
        public void ScissorSweep_Fires_BothFeetLocked_SleeveGripped_BreakAbove04()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(0.5f, 0f)));
            Assert.IsTrue(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void ScissorSweep_Fails_LowGripStrength()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(0.5f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.4f, 0f));
        }

        [Test]
        public void ScissorSweep_Fails_FootUnlocked()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Unlocked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(0.5f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void ScissorSweep_Fails_LowPostureBreak()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(0.2f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void FlowerSweep_Fires_BothFeetLocked_WristGripped_SagittalAbove05()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    rh: GrippedHand(HandSide.R, GripZone.WristL)),
                top: Actor(pb: new Vec2(0f, 0.6f)));
            Assert.IsTrue(JudgmentWindowOps.FlowerSweepConditions(c, 0f, 0.5f));
        }

        [Test]
        public void Triangle_Fires_OneFootUnlocked_ArmExtracted_CollarGripped()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Unlocked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.CollarR)),
                top: Actor(armExtL: true));
            Assert.IsTrue(JudgmentWindowOps.TriangleConditions(c));
        }

        [Test]
        public void Triangle_Fails_BothFeetLocked()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.CollarR)),
                top: Actor(armExtL: true));
            Assert.IsFalse(JudgmentWindowOps.TriangleConditions(c));
        }

        [Test]
        public void Omoplata_Fires_SleeveHand_SagittalAbove06_CorrectSign_YawAbovePiOver3()
        {
            var c = Ctx(
                bottom: Actor(lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(-0.3f, 0.7f)),
                hipYaw: (float)(System.Math.PI / 3.0) + 0.1f);
            Assert.IsTrue(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void Omoplata_Fails_LowYaw()
        {
            var c = Ctx(
                bottom: Actor(lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(-0.3f, 0.7f)),
                hipYaw: (float)(System.Math.PI / 4.0));
            Assert.IsFalse(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void Omoplata_Fails_WrongLateralSign()
        {
            var c = Ctx(
                bottom: Actor(lh: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(pb: new Vec2(0.3f, 0.7f)), // wrong sign
                hipYaw: (float)(System.Math.PI / 3.0) + 0.1f);
            Assert.IsFalse(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void HipBump_Fires_SagittalAbove07_Sustained300ms()
        {
            var c = Ctx(top: Actor(pb: new Vec2(0f, 0.8f)), sustainedPushMs: 350f);
            Assert.IsTrue(JudgmentWindowOps.HipBumpConditions(c));
        }

        [Test]
        public void HipBump_Fails_ShortPush()
        {
            var c = Ctx(top: Actor(pb: new Vec2(0f, 0.8f)), sustainedPushMs: 100f);
            Assert.IsFalse(JudgmentWindowOps.HipBumpConditions(c));
        }

        [Test]
        public void CrossCollar_Fires_BothCollarGripped_BreakAbove05()
        {
            var c = Ctx(
                bottom: Actor(
                    lh: GrippedHand(HandSide.L, GripZone.CollarL),
                    rh: GrippedHand(HandSide.R, GripZone.CollarR)),
                top: Actor(pb: new Vec2(0.3f, 0.5f)));
            Assert.IsTrue(JudgmentWindowOps.CrossCollarConditions(c, 0.8f, 0.8f));
        }

        [Test]
        public void CrossCollar_Fails_OneHandNotOnCollar()
        {
            var c = Ctx(
                bottom: Actor(
                    lh: GrippedHand(HandSide.L, GripZone.CollarL),
                    rh: GrippedHand(HandSide.R, GripZone.SleeveL)),
                top: Actor(pb: new Vec2(0.3f, 0.5f)));
            Assert.IsFalse(JudgmentWindowOps.CrossCollarConditions(c, 0.8f, 0.8f));
        }

        [Test]
        public void EvaluateAllTechniques_ReportsSatisfiedTechniques()
        {
            var c = Ctx(
                bottom: Actor(
                    lf: Foot(FootSide.L, FootState.Locked),
                    rf: Foot(FootSide.R, FootState.Locked),
                    lh: GrippedHand(HandSide.L, GripZone.CollarL),
                    rh: GrippedHand(HandSide.R, GripZone.CollarR)),
                top: Actor(pb: new Vec2(0.3f, 0.5f)));
            var list = JudgmentWindowOps.EvaluateAllTechniques(c, 0.9f, 0.9f);
            bool hasCrossCollar = false;
            foreach (var t in list) if (t == Technique.CrossCollar) { hasCrossCollar = true; break; }
            Assert.IsTrue(hasCrossCollar);
        }

        // --- FSM lifecycle ---------------------------------------------------

        [Test]
        public void Closed_To_Opening_OnSatisfiedTechnique_OutsideCooldown()
        {
            var (next, events) = Tick(JudgmentWindow.Initial, ScissorOnly, 0L);
            Assert.AreEqual(JudgmentWindowState.Opening, next.State);
            bool hasOpening = false;
            foreach (var e in events) if (e.Kind == JudgmentEventKind.WindowOpening) { hasOpening = true; break; }
            Assert.IsTrue(hasOpening);
        }

        [Test]
        public void Opening_To_Open_After200ms_TimeScaleAt03()
        {
            var w = JudgmentWindow.Initial;
            var events = new List<JudgmentTickEvent>();
            float ts;
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 0 }, events, out ts);
            events.Clear();
            JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 100 }, events, out ts);
            Assert.Greater(ts, JudgmentWindowTimeScale.Open);
            Assert.Less(ts,    JudgmentWindowTimeScale.Normal);
            events.Clear();
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 200 }, events, out ts);
            Assert.AreEqual(JudgmentWindowState.Open, w.State);
        }

        [Test]
        public void Open_To_Closing_OnConfirmedTechnique()
        {
            var w = JudgmentWindow.Initial;
            var events = new List<JudgmentTickEvent>();
            float ts;
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 0 }, events, out ts);
            events.Clear();
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 200 }, events, out ts);
            events.Clear();
            var res = new List<JudgmentTickEvent>();
            var next = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput
            {
                NowMs = 300, ConfirmedTechnique = Technique.ScissorSweep,
            }, res, out ts);
            Assert.AreEqual(JudgmentWindowState.Closing, next.State);
            bool hasConfirmed = false;
            foreach (var e in res) if (e.Kind == JudgmentEventKind.TechniqueConfirmed) { hasConfirmed = true; break; }
            Assert.IsTrue(hasConfirmed);
        }

        [Test]
        public void Open_To_Closing_Disrupted_WhenAllCandidatesCollapse()
        {
            var w = JudgmentWindow.Initial;
            var events = new List<JudgmentTickEvent>();
            float ts;
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 0   }, events, out ts);
            events.Clear();
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 200 }, events, out ts);
            events.Clear();
            var res  = new List<JudgmentTickEvent>();
            var next = JudgmentWindowOps.Tick(w, None, new JudgmentTickInput { NowMs = 300 }, res, out ts);
            Assert.AreEqual(JudgmentWindowState.Closing, next.State);
            bool hasDisrupted = false;
            foreach (var e in res)
                if (e.Kind == JudgmentEventKind.WindowClosing && e.CloseReason == WindowCloseReason.Disrupted)
                    { hasDisrupted = true; break; }
            Assert.IsTrue(hasDisrupted);
        }

        [Test]
        public void Open_To_Closing_TimedOut_AfterOpenMaxMs()
        {
            var w = JudgmentWindow.Initial;
            var events = new List<JudgmentTickEvent>();
            float ts;
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 0 },   events, out ts);
            events.Clear();
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 200 }, events, out ts);
            events.Clear();
            int t = 200 + JudgmentWindowTiming.Default.OpenMaxMs;
            var res  = new List<JudgmentTickEvent>();
            var next = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = t }, res, out ts);
            Assert.AreEqual(JudgmentWindowState.Closing, next.State);
            bool hasTimedOut = false;
            foreach (var e in res)
                if (e.Kind == JudgmentEventKind.WindowClosing && e.CloseReason == WindowCloseReason.TimedOut)
                    { hasTimedOut = true; break; }
            Assert.IsTrue(hasTimedOut);
        }

        [Test]
        public void Closing_To_Closed_HonoursCooldown_400ms()
        {
            var w = JudgmentWindow.Initial;
            var events = new List<JudgmentTickEvent>();
            float ts;
            // Drive to OPEN.
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 0 },   events, out ts);
            events.Clear();
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = 200 }, events, out ts);
            events.Clear();
            // Confirm → CLOSING.
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput
            {
                NowMs = 300, ConfirmedTechnique = Technique.ScissorSweep,
            }, events, out ts);
            events.Clear();
            Assert.AreEqual(JudgmentWindowState.Closing, w.State);

            // CLOSING → CLOSED.
            long closedAt = 300 + JudgmentWindowTiming.Default.ClosingMs;
            w = JudgmentWindowOps.Tick(w, ScissorOnly, new JudgmentTickInput { NowMs = closedAt }, events, out ts);
            events.Clear();
            Assert.AreEqual(JudgmentWindowState.Closed, w.State);
            Assert.AreEqual(closedAt + JudgmentWindowTiming.Default.CooldownMs, w.CooldownUntilMs);

            // Mid-cooldown: technique satisfied but should not re-OPEN.
            var (midNext, _) = Tick(w, ScissorOnly, closedAt + 100L);
            Assert.AreEqual(JudgmentWindowState.Closed, midNext.State);

            // After cooldown: should re-OPEN.
            var (afterNext, _) = Tick(w, ScissorOnly, closedAt + JudgmentWindowTiming.Default.CooldownMs + 1);
            Assert.AreEqual(JudgmentWindowState.Opening, afterNext.State);
        }

        [Test]
        public void CandidateSet_FrozenAtOpeningEntry()
        {
            var w = JudgmentWindow.Initial;
            var (w1, _) = Tick(w, ScissorOnly, 0L);
            Assert.AreEqual(1, w1.Candidates.Length);
            Assert.AreEqual(Technique.ScissorSweep, w1.Candidates[0]);

            // Second tick with additional candidate should NOT expand the list.
            var (w2, _) = Tick(w1, new[] { Technique.ScissorSweep, Technique.HipBump }, 100L);
            Assert.AreEqual(1, w2.Candidates.Length);
            Assert.AreEqual(Technique.ScissorSweep, w2.Candidates[0]);
        }
    }
}
