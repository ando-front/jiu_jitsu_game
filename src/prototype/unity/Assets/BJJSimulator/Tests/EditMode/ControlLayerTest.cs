// NUnit EditMode mirror of src/prototype/web/tests/unit/control_layer.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class ControlLayerTest
    {
        static HandFSM GrippedHand(HandSide side) => new HandFSM
        {
            Side = side, State = HandState.Gripped, Target = GripZone.SleeveR,
            StateEnteredMs = 0, ReachDurationMs = 0,
            LastParriedZone = GripZone.None, LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        static ActorState Actor(
            bool armExtL = false, bool armExtR = false,
            HandFSM? lh = null, HandFSM? rh = null)
        {
            var a = GameStateOps.InitialActorState();
            return new ActorState
            {
                LeftHand          = lh ?? a.LeftHand,
                RightHand         = rh ?? a.RightHand,
                LeftFoot          = a.LeftFoot,
                RightFoot         = a.RightFoot,
                PostureBreak      = Vec2.Zero,
                Stamina           = 1f,
                ArmExtractedLeft  = armExtL,
                ArmExtractedRight = armExtR,
            };
        }

        static ControlLayerInputs Inputs(
            JudgmentWindow? win = null,
            ActorState? bottom = null, ActorState? top = null,
            bool defenderCut = false)
        {
            return new ControlLayerInputs
            {
                JudgmentWindow        = win    ?? JudgmentWindow.Initial,
                Bottom                = bottom ?? GameStateOps.InitialActorState(),
                Top                   = top    ?? GameStateOps.InitialActorState(),
                DefenderCutInProgress = defenderCut,
            };
        }

        // -----------------------------------------------------------------------

        [Test]
        public void JudgmentWindow_OPEN_Locks_Initiative_To_FiredSide_Bottom()
        {
            var win = new JudgmentWindow
            {
                State           = JudgmentWindowState.Open,
                StateEnteredMs  = 0,
                Candidates      = new[] { Technique.ScissorSweep },
                CooldownUntilMs = BJJConst.SentinelTimeMs,
                FiredBy         = WindowSide.Bottom,
            };
            var result = ControlLayerOps.Update(ControlLayer.Initial, Inputs(win: win));
            Assert.AreEqual(Initiative.Bottom, result.Initiative);
            Assert.IsTrue(result.LockedByWindow);
        }

        [Test]
        public void ArmExtracted_On_Bottom_Gives_Bottom_Initiative()
        {
            var result = ControlLayerOps.Update(ControlLayer.Initial,
                Inputs(bottom: Actor(armExtL: true)));
            Assert.AreEqual(Initiative.Bottom, result.Initiative);
        }

        [Test]
        public void ArmExtracted_On_Top_Gives_Top_Initiative()
        {
            var result = ControlLayerOps.Update(ControlLayer.Initial,
                Inputs(top: Actor(armExtR: true)));
            Assert.AreEqual(Initiative.Top, result.Initiative);
        }

        [Test]
        public void Bottom_With_TwoGrips_Gives_Bottom_Initiative()
        {
            var result = ControlLayerOps.Update(ControlLayer.Initial, Inputs(
                bottom: Actor(lh: GrippedHand(HandSide.L), rh: GrippedHand(HandSide.R))));
            Assert.AreEqual(Initiative.Bottom, result.Initiative);
        }

        [Test]
        public void DefenderCutInProgress_Gives_Top_Initiative()
        {
            var result = ControlLayerOps.Update(ControlLayer.Initial, Inputs(defenderCut: true));
            Assert.AreEqual(Initiative.Top, result.Initiative);
        }

        [Test]
        public void Nothing_Applies_Gives_Neutral()
        {
            var result = ControlLayerOps.Update(ControlLayer.Initial, Inputs());
            Assert.AreEqual(Initiative.Neutral, result.Initiative);
            Assert.IsFalse(result.LockedByWindow);
        }

        [Test]
        public void WindowLock_Releases_When_Window_Returns_To_Closed()
        {
            var locked = new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = true };
            var result = ControlLayerOps.Update(locked, Inputs());
            Assert.IsFalse(result.LockedByWindow);
            Assert.AreEqual(Initiative.Neutral, result.Initiative);
        }
    }
}
