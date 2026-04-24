// NUnit EditMode mirror of src/prototype/web/tests/unit/control_layer.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public class ControlLayerTest
    {
        private static HandFSM GrippedHand(HandSide side) => new HandFSM
        {
            Side            = side,
            State           = HandState.Gripped,
            Target          = GripZone.SleeveR,
            StateEnteredMs  = 0,
            ReachDurationMs = 0,
            LastParriedZone = GripZone.None,
            LastParriedAtMs = BJJConst.SentinelTimeMs,
        };

        private static ActorState Actor(
            bool armExtractedLeft = false, bool armExtractedRight = false,
            HandFSM? leftHand = null, HandFSM? rightHand = null)
        {
            var a = ActorState.Initial();
            a.ArmExtractedLeft  = armExtractedLeft;
            a.ArmExtractedRight = armExtractedRight;
            if (leftHand.HasValue)  a.LeftHand  = leftHand.Value;
            if (rightHand.HasValue) a.RightHand = rightHand.Value;
            return a;
        }

        private static ControlLayerInputs MakeInputs(
            JudgmentWindow? win         = null,
            ActorState?     bottom      = null,
            ActorState?     top         = null,
            bool            cutInProgress = false)
        {
            return new ControlLayerInputs
            {
                JudgmentWindow        = win    ?? JudgmentWindowOps.Initial,
                Bottom                = bottom ?? Actor(),
                Top                   = top    ?? Actor(),
                DefenderCutInProgress = cutInProgress,
            };
        }

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        [Test]
        public void WindowOpen_LocksInitiativeToFiringSide()
        {
            var win = new JudgmentWindow
            {
                State           = JudgmentWindowState.Open,
                StateEnteredMs  = 0,
                Candidates      = new List<Technique> { Technique.ScissorSweep },
                CooldownUntilMs = BJJConst.SentinelTimeMs,
                HasFiredBy      = true,
                FiredByBottom   = true,
            };
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial, MakeInputs(win: win));
            Assert.AreEqual(Initiative.Bottom, out_.Initiative);
            Assert.IsTrue(out_.LockedByWindow);
        }

        [Test]
        public void ArmExtractedOnBottom_YieldsBottom()
        {
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial,
                MakeInputs(bottom: Actor(armExtractedLeft: true)));
            Assert.AreEqual(Initiative.Bottom, out_.Initiative);
        }

        [Test]
        public void ArmExtractedOnTop_YieldsTop()
        {
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial,
                MakeInputs(top: Actor(armExtractedRight: true)));
            Assert.AreEqual(Initiative.Top, out_.Initiative);
        }

        [Test]
        public void BottomWith2GrippedHands_YieldsBottom()
        {
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial,
                MakeInputs(bottom: Actor(
                    leftHand:  GrippedHand(HandSide.L),
                    rightHand: GrippedHand(HandSide.R))));
            Assert.AreEqual(Initiative.Bottom, out_.Initiative);
        }

        [Test]
        public void DefenderCutInProgress_YieldsTop()
        {
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial,
                MakeInputs(cutInProgress: true));
            Assert.AreEqual(Initiative.Top, out_.Initiative);
        }

        [Test]
        public void NothingApplies_YieldsNeutral()
        {
            var out_ = ControlLayerOps.Update(ControlLayerOps.Initial, MakeInputs());
            Assert.AreEqual(Initiative.Neutral, out_.Initiative);
            Assert.IsFalse(out_.LockedByWindow);
        }

        [Test]
        public void WindowLockReleasesWhenWindowReturnsToClosed()
        {
            var prev = new ControlLayer { Initiative = Initiative.Bottom, LockedByWindow = true };
            var out_ = ControlLayerOps.Update(prev, MakeInputs()); // window CLOSED (default)
            Assert.IsFalse(out_.LockedByWindow);
            Assert.AreEqual(Initiative.Neutral, out_.Initiative);
        }
    }
}
