// Ported from src/prototype/web/tests/unit/scenarios.test.ts.
// Scenarios must arrive with technique preconditions already met so that
// one input frame (or zero, for purely posture-based techniques) is enough
// to fire the corresponding judgment window.
//
// Reference: src/prototype/web/src/state/scenarios.ts

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class ScenariosTest
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        const float MaxTrigger = 1.0f;

        static System.Collections.Generic.List<Technique> EvalTechniques(ScenarioName name)
        {
            var g = Scenarios.Build(name, 1000L);
            float hipYaw = name == ScenarioName.OmoplataReady
                ? -(float)(System.Math.PI / 2)
                : (float)(System.Math.PI / 2);
            float hipPush = (name == ScenarioName.FlowerReady || name == ScenarioName.HipBumpReady)
                ? 0.6f : 0f;

            var ctx = new JudgmentContext
            {
                Bottom             = g.Bottom,
                Top                = g.Top,
                BottomHipYaw       = hipYaw,
                BottomHipPush      = hipPush,
                SustainedHipPushMs = g.Sustained.HipPushMs,
            };
            var all = JudgmentWindowOps.EvaluateAllTechniques(ctx, MaxTrigger, MaxTrigger);
            return new System.Collections.Generic.List<Technique>(all);
        }

        // -----------------------------------------------------------------------
        // Preconditions satisfied
        // -----------------------------------------------------------------------

        [Test] public void ScissorReadySatisfiesScissorSweep()
            => Assert.Contains(Technique.ScissorSweep, EvalTechniques(ScenarioName.ScissorReady));

        [Test] public void FlowerReadySatisfiesFlowerSweep()
            => Assert.Contains(Technique.FlowerSweep, EvalTechniques(ScenarioName.FlowerReady));

        [Test] public void TriangleReadySatisfiesTriangle()
            => Assert.Contains(Technique.Triangle, EvalTechniques(ScenarioName.TriangleReady));

        [Test] public void OmoplataReadySatisfiesOmoplata()
            => Assert.Contains(Technique.Omoplata, EvalTechniques(ScenarioName.OmoplataReady));

        [Test] public void HipBumpReadySatisfiesHipBump()
            => Assert.Contains(Technique.HipBump, EvalTechniques(ScenarioName.HipBumpReady));

        [Test] public void CrossCollarReadySatisfiesCrossCollar()
            => Assert.Contains(Technique.CrossCollar, EvalTechniques(ScenarioName.CrossCollarReady));

        [Test] public void PassDefenseNoAttackerTechnique()
            => Assert.AreEqual(0, EvalTechniques(ScenarioName.PassDefense).Count);

        // -----------------------------------------------------------------------
        // Structural invariants
        // -----------------------------------------------------------------------

        [Test]
        public void AllScenariosHaveCorrectInitialState()
        {
            foreach (ScenarioName name in System.Enum.GetValues(typeof(ScenarioName)))
            {
                var g = Scenarios.Build(name, 0L);
                Assert.AreEqual(0, g.FrameIndex, $"{name}: frameIndex");
                Assert.IsFalse(g.SessionEnded, $"{name}: sessionEnded");
                Assert.AreEqual(GuardState.Closed, g.Guard, $"{name}: guard must start CLOSED");
            }
        }

        [Test]
        public void TriangleReadySeedsArmExtracted()
        {
            var g = Scenarios.Build(ScenarioName.TriangleReady, 1000L);
            Assert.IsTrue(g.TopArmExtracted.Right,   "TopArmExtracted.Right");
            Assert.IsTrue(g.Top.ArmExtractedRight,   "Top.ArmExtractedRight");
        }
    }
}
