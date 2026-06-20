// NUnit EditMode mirror of src/prototype/web/tests/unit/score.test.ts.
// Each [Test] corresponds to one it(...) case from the Stage 1 Vitest suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    [TestFixture]
    public class BJJScoreTest
    {
        [Test]
        public void StartsAtZeroZero()
        {
            Assert.AreEqual(0, BJJScoreOps.Initial.Top);
            Assert.AreEqual(0, BJJScoreOps.Initial.Bottom);
        }

        [Test]
        public void PassScoresThreeForTop()
        {
            var s = BJJScoreOps.ApplyPass(BJJScoreOps.Initial);
            Assert.AreEqual(BJJScoreOps.PassPoints, s.Top);
            Assert.AreEqual(0, s.Bottom);

            var s2 = BJJScoreOps.ApplyPass(new BJJScore { Top = 3, Bottom = 2 });
            Assert.AreEqual(6, s2.Top);
            Assert.AreEqual(2, s2.Bottom);
        }

        [Test]
        public void SweepScoresTwoForBottom()
        {
            var s = BJJScoreOps.ApplySweep(BJJScoreOps.Initial);
            Assert.AreEqual(0, s.Top);
            Assert.AreEqual(BJJScoreOps.SweepPoints, s.Bottom);

            var s2 = BJJScoreOps.ApplySweep(new BJJScore { Top = 3, Bottom = 2 });
            Assert.AreEqual(3, s2.Top);
            Assert.AreEqual(4, s2.Bottom);
        }
    }
}
