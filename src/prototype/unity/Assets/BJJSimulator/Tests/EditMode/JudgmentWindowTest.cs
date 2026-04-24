// NUnit EditMode mirror of src/prototype/web/tests/unit/judgment_window.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class JudgmentWindowTest
    {
        // ------------------------------------------------------------------
        // Builder helpers
        // ------------------------------------------------------------------

        private static HandFSM GrippedHand(HandSide side, GripZone zone) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = zone,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static HandFSM IdleHand(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Idle,
            Target          = GripZone.None,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static FootFSM Foot(FootSide side, FootState state) =>
            new FootFSM { Side = side, State = state, StateEnteredMs = 0 };

        private static ActorState Actor(
            HandFSM? leftHand  = null, HandFSM? rightHand = null,
            FootFSM? leftFoot  = null, FootFSM? rightFoot = null,
            Vec2?    postureBreak = null,
            bool armExtractedLeft = false, bool armExtractedRight = false)
        {
            var a = ActorState.Initial();
            if (leftHand.HasValue)         a.LeftHand          = leftHand.Value;
            if (rightHand.HasValue)        a.RightHand         = rightHand.Value;
            if (leftFoot.HasValue)         a.LeftFoot          = leftFoot.Value;
            if (rightFoot.HasValue)        a.RightFoot         = rightFoot.Value;
            if (postureBreak.HasValue)     a.PostureBreak      = postureBreak.Value;
            a.ArmExtractedLeft  = armExtractedLeft;
            a.ArmExtractedRight = armExtractedRight;
            return a;
        }

        private static JudgmentContext Ctx(
            ActorState? bottom = null, ActorState? top = null,
            float hipYaw = 0f, float hipPush = 0f, float sustainedMs = 0f)
        {
            return new JudgmentContext
            {
                Bottom             = bottom ?? Actor(),
                Top                = top    ?? Actor(),
                BottomHipYaw       = hipYaw,
                BottomHipPush      = hipPush,
                SustainedHipPushMs = sustainedMs,
            };
        }

        // Shorthand: drive the FSM through a series of ticks with the same
        // candidate set, returning the final window.
        private static (JudgmentWindow W, List<JudgmentTickEvent> LastEvents) DriveOpen(
            JudgmentWindow start, List<Technique> cands, long openMs)
        {
            var evList = new List<JudgmentTickEvent>();
            var w = start;
            // Tick to OPENING at t=0
            (w, _) = JudgmentWindowOps.Tick(w, cands,
                new JudgmentTickInput { NowMs = 0 }, evList);
            // Tick to OPEN at t=openMs
            evList.Clear();
            (w, _) = JudgmentWindowOps.Tick(w, cands,
                new JudgmentTickInput { NowMs = openMs }, evList);
            return (w, evList);
        }

        // ------------------------------------------------------------------
        // Technique predicates
        // ------------------------------------------------------------------

        [Test]
        public void ScissorSweep_FiresWithBothFeetLockedSleeveGrip06AndBreak04()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(postureBreak: new Vec2(0.5f, 0f)));
            Assert.IsTrue(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void ScissorSweep_FailsIfGripStrengthBelow06()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(postureBreak: new Vec2(0.5f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.4f, 0f));
        }

        [Test]
        public void ScissorSweep_FailsIfAFootIsUnlocked()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Unlocked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(postureBreak: new Vec2(0.5f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void ScissorSweep_FailsIfPostureBreakMagBelow04()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.SleeveR)),
                top: Actor(postureBreak: new Vec2(0.2f, 0f)));
            Assert.IsFalse(JudgmentWindowOps.ScissorSweepConditions(c, 0.8f, 0f));
        }

        [Test]
        public void FlowerSweep_NeedsBothFeetLockedWristGrippedSagittalHalf()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    rightHand: GrippedHand(HandSide.R, GripZone.WristL)),
                top: Actor(postureBreak: new Vec2(0f, 0.6f)));
            Assert.IsTrue(JudgmentWindowOps.FlowerSweepConditions(c, 0f, 0.5f));
        }

        [Test]
        public void Triangle_NeedsOneFootUnlockedArmExtractedCollarGripped()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Unlocked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.CollarR)),
                top: Actor(armExtractedLeft: true));
            Assert.IsTrue(JudgmentWindowOps.TriangleConditions(c));
        }

        [Test]
        public void Triangle_RejectsWhenBothFeetLocked()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.CollarR)),
                top: Actor(armExtractedLeft: true));
            Assert.IsFalse(JudgmentWindowOps.TriangleConditions(c));
        }

        [Test]
        public void Omoplata_RequiresSignMatchSagittalAndYaw()
        {
            var c = Ctx(
                bottom: Actor(leftHand: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top:    Actor(postureBreak: new Vec2(-0.3f, 0.7f)),
                hipYaw: (float)(System.Math.PI / 3.0 + 0.1));
            Assert.IsTrue(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void Omoplata_FailsIfHipYawBelowPiOver3()
        {
            var c = Ctx(
                bottom: Actor(leftHand: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top:    Actor(postureBreak: new Vec2(-0.3f, 0.7f)),
                hipYaw: (float)(System.Math.PI / 4.0));
            Assert.IsFalse(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void Omoplata_FailsIfLateralSignWrong()
        {
            var c = Ctx(
                bottom: Actor(leftHand: GrippedHand(HandSide.L, GripZone.SleeveR)),
                top:    Actor(postureBreak: new Vec2(0.3f, 0.7f)),  // wrong sign
                hipYaw: (float)(System.Math.PI / 3.0 + 0.1));
            Assert.IsFalse(JudgmentWindowOps.OmoplataConditions(c));
        }

        [Test]
        public void HipBump_NeedsSagittalAndSustainedhHipPush()
        {
            var c = Ctx(top: Actor(postureBreak: new Vec2(0f, 0.8f)), sustainedMs: 350f);
            Assert.IsTrue(JudgmentWindowOps.HipBumpConditions(c));
        }

        [Test]
        public void HipBump_RejectsShortPushDuration()
        {
            var c = Ctx(top: Actor(postureBreak: new Vec2(0f, 0.8f)), sustainedMs: 100f);
            Assert.IsFalse(JudgmentWindowOps.HipBumpConditions(c));
        }

        [Test]
        public void CrossCollar_NeedsBothCollarGripped07AndBreak05()
        {
            var c = Ctx(
                bottom: Actor(
                    leftHand:  GrippedHand(HandSide.L, GripZone.CollarL),
                    rightHand: GrippedHand(HandSide.R, GripZone.CollarR)),
                top: Actor(postureBreak: new Vec2(0.3f, 0.5f)));
            Assert.IsTrue(JudgmentWindowOps.CrossCollarConditions(c, 0.8f, 0.8f));
        }

        [Test]
        public void CrossCollar_FailsIfOneHandNotOnCollarZone()
        {
            var c = Ctx(
                bottom: Actor(
                    leftHand:  GrippedHand(HandSide.L, GripZone.CollarL),
                    rightHand: GrippedHand(HandSide.R, GripZone.SleeveL)),
                top: Actor(postureBreak: new Vec2(0.3f, 0.5f)));
            Assert.IsFalse(JudgmentWindowOps.CrossCollarConditions(c, 0.8f, 0.8f));
        }

        [Test]
        public void EvaluateAllTechniques_ReportsEveryCurrentlySatisfied()
        {
            var c = Ctx(
                bottom: Actor(
                    leftFoot:  Foot(FootSide.L, FootState.Locked),
                    rightFoot: Foot(FootSide.R, FootState.Locked),
                    leftHand:  GrippedHand(HandSide.L, GripZone.CollarL),
                    rightHand: GrippedHand(HandSide.R, GripZone.CollarR)),
                top: Actor(postureBreak: new Vec2(0.3f, 0.5f)));
            var list = JudgmentWindowOps.EvaluateAllTechniques(c, 0.9f, 0.9f);
            Assert.IsTrue(list.Contains(Technique.CrossCollar));
        }

        // ------------------------------------------------------------------
        // FSM lifecycle
        // ------------------------------------------------------------------

        [Test]
        public void ClosedToOpeningOnAnySatisfiedTechnique()
        {
            var w  = JudgmentWindowOps.Initial;
            var ev = new List<JudgmentTickEvent>();
            var cands = new List<Technique> { Technique.ScissorSweep };
            (w, _) = JudgmentWindowOps.Tick(w, cands, new JudgmentTickInput { NowMs = 0 }, ev);
            Assert.AreEqual(JudgmentWindowState.Opening, w.State);
            Assert.AreEqual(JudgmentEventKind.WindowOpening, ev[0].Kind);
        }

        [Test]
        public void OpeningToOpenAfter200ms()
        {
            var cands = new List<Technique> { Technique.ScissorSweep };
            var (w, _) = DriveOpen(JudgmentWindowOps.Initial, cands, 200);
            Assert.AreEqual(JudgmentWindowState.Open, w.State);
        }

        [Test]
        public void OpenToClosingOnConfirmedTechnique()
        {
            var cands  = new List<Technique> { Technique.ScissorSweep };
            var (w, _) = DriveOpen(JudgmentWindowOps.Initial, cands, 200);
            Assert.AreEqual(JudgmentWindowState.Open, w.State);

            var ev = new List<JudgmentTickEvent>();
            JudgmentWindow n;
            (n, _) = JudgmentWindowOps.Tick(w, cands, new JudgmentTickInput
            {
                NowMs              = 300,
                ConfirmedTechnique = Technique.ScissorSweep,
            }, ev);
            Assert.AreEqual(JudgmentWindowState.Closing, n.State);
            bool hasConfirm = false;
            foreach (var e in ev) if (e.Kind == JudgmentEventKind.TechniqueConfirmed) hasConfirm = true;
            Assert.IsTrue(hasConfirm, "TECHNIQUE_CONFIRMED event emitted");
        }

        [Test]
        public void OpenToClosingDisruptedWhenAllCandidatesLoseConditions()
        {
            var cands  = new List<Technique> { Technique.ScissorSweep };
            var (w, _) = DriveOpen(JudgmentWindowOps.Initial, cands, 200);
            Assert.AreEqual(JudgmentWindowState.Open, w.State);

            var ev = new List<JudgmentTickEvent>();
            JudgmentWindow n;
            (n, _) = JudgmentWindowOps.Tick(w, new List<Technique>(), // all conditions collapsed
                new JudgmentTickInput { NowMs = 300 }, ev);
            Assert.AreEqual(JudgmentWindowState.Closing, n.State);
            bool hasDisrupt = false;
            foreach (var e in ev)
                if (e.Kind == JudgmentEventKind.WindowClosing && e.CloseReason == WindowCloseReason.Disrupted)
                    hasDisrupt = true;
            Assert.IsTrue(hasDisrupt, "DISRUPTED reason emitted");
        }

        [Test]
        public void OpenToClosingTimedOutAfterOpenMaxMs()
        {
            var cands  = new List<Technique> { Technique.ScissorSweep };
            var (w, _) = DriveOpen(JudgmentWindowOps.Initial, cands, 200);
            var ev = new List<JudgmentTickEvent>();
            JudgmentWindow n;
            (n, _) = JudgmentWindowOps.Tick(w, cands, new JudgmentTickInput
            {
                NowMs = 200 + WindowTiming.Default.OpenMaxMs,
            }, ev);
            Assert.AreEqual(JudgmentWindowState.Closing, n.State);
            bool hasTimeout = false;
            foreach (var e in ev)
                if (e.Kind == JudgmentEventKind.WindowClosing && e.CloseReason == WindowCloseReason.TimedOut)
                    hasTimeout = true;
            Assert.IsTrue(hasTimeout, "TIMED_OUT reason emitted");
        }

        [Test]
        public void ClosingToClosedHonoursCooldown()
        {
            var cands  = new List<Technique> { Technique.ScissorSweep };
            var (w, _) = DriveOpen(JudgmentWindowOps.Initial, cands, 200);

            // Confirm → CLOSING.
            var ev = new List<JudgmentTickEvent>();
            (w, _) = JudgmentWindowOps.Tick(w, cands, new JudgmentTickInput
            {
                NowMs              = 300,
                ConfirmedTechnique = Technique.ScissorSweep,
            }, ev);
            Assert.AreEqual(JudgmentWindowState.Closing, w.State);

            // CLOSING → CLOSED.
            long closedAt = 300 + WindowTiming.Default.ClosingMs;
            ev.Clear();
            (w, _) = JudgmentWindowOps.Tick(w, cands,
                new JudgmentTickInput { NowMs = closedAt }, ev);
            Assert.AreEqual(JudgmentWindowState.Closed, w.State);
            Assert.AreEqual(closedAt + WindowTiming.Default.CooldownMs, w.CooldownUntilMs);

            // Mid-cooldown: should not open.
            ev.Clear();
            JudgmentWindow mid;
            (mid, _) = JudgmentWindowOps.Tick(w, cands,
                new JudgmentTickInput { NowMs = closedAt + 100 }, ev);
            Assert.AreEqual(JudgmentWindowState.Closed, mid.State);

            // After cooldown: should open.
            ev.Clear();
            JudgmentWindow after;
            (after, _) = JudgmentWindowOps.Tick(w, cands,
                new JudgmentTickInput { NowMs = closedAt + WindowTiming.Default.CooldownMs + 1 }, ev);
            Assert.AreEqual(JudgmentWindowState.Opening, after.State);
        }

        [Test]
        public void CandidateSetIsFrozenAtOpeningEntry()
        {
            var cands1 = new List<Technique> { Technique.ScissorSweep };
            var w      = JudgmentWindowOps.Initial;
            var ev     = new List<JudgmentTickEvent>();
            (w, _) = JudgmentWindowOps.Tick(w, cands1, new JudgmentTickInput { NowMs = 0 }, ev);
            Assert.AreEqual(1, w.Candidates.Count);
            Assert.IsTrue(w.Candidates.Contains(Technique.ScissorSweep));

            // Later tick with HipBump added — candidates should not grow.
            var cands2 = new List<Technique> { Technique.ScissorSweep, Technique.HipBump };
            ev.Clear();
            (w, _) = JudgmentWindowOps.Tick(w, cands2, new JudgmentTickInput { NowMs = 100 }, ev);
            Assert.AreEqual(1, w.Candidates.Count,
                "candidate set must not grow after OPENING");
        }
    }
}
