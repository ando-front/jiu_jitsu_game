// NUnit EditMode mirror of src/prototype/web/tests/unit/layerB_defense.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest
// suite so a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/input_system_defense_v1.md §B.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public static class LayerBDefenseTestHelpers
    {
        // Mirrors the frame() factory in TS — a zeroed-out InputFrame with optional
        // overrides applied. C# has no spread; pass values explicitly.
        public static InputFrame Frame(
            Vec2      ls          = default,
            Vec2      rs          = default,
            float     lTrigger    = 0f,
            float     rTrigger    = 0f,
            ButtonBit buttons     = ButtonBit.None,
            ButtonBit buttonEdges = ButtonBit.None) => new InputFrame
        {
            Timestamp   = 0L,
            Ls          = ls,
            Rs          = rs,
            LTrigger    = lTrigger,
            RTrigger    = rTrigger,
            Buttons     = buttons,
            ButtonEdges = buttonEdges,
            DeviceKind  = DeviceKind.Keyboard,
        };
    }

    // -------------------------------------------------------------------------
    // describe("TopHipIntent (§B.3)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopHipIntentTests
    {
        // it("LS x/y maps straight to weight_lateral / weight_forward")
        [Test]
        public void LsXYMapsStraightToWeightLateralAndWeightForward()
        {
            var hip = LayerBDefenseOps.ComputeTopHipIntent(
                LayerBDefenseTestHelpers.Frame(ls: new Vec2(-0.4f, 0.8f)));
            Assert.AreEqual( 0.8f, hip.WeightForward, 1e-6f);
            Assert.AreEqual(-0.4f, hip.WeightLateral, 1e-6f);
        }
    }

    // -------------------------------------------------------------------------
    // describe("TopBaseIntent zone selection (§B.4.2)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TopBaseIntentZoneSelectionTests
    {
        // it("RS up + trigger held → CHEST")
        [Test]
        public void RsUpPlusTriggerHeldSelectsChest()
        {
            var (base_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(0f, 1f), lTrigger: 0.5f),
                BaseZone.None);
            Assert.AreEqual(BaseZone.Chest, nextZone);
            Assert.AreEqual(BaseZone.Chest, base_.LHandTarget);
        }

        // it("RS down-right + trigger held → KNEE_R")
        [Test]
        public void RsDownRightPlusTriggerHeldSelectsKneeR()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(0.7f, -0.7f), rTrigger: 0.6f),
                BaseZone.None);
            Assert.AreEqual(BaseZone.KneeR, nextZone);
        }

        // it("no trigger held → base is ZERO_TOP_BASE and zone clears")
        [Test]
        public void NoTriggerHeldClearsBaseAndZone()
        {
            var (base_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(1f, 0f)),
                BaseZone.BicepL);
            Assert.AreEqual(BaseZone.None, base_.LHandTarget);
            Assert.AreEqual(BaseZone.None, base_.RHandTarget);
            Assert.AreEqual(BaseZone.None, nextZone);
        }

        // it("bumper edge suppresses zone selection (§B.4 RS re-routed to cut)")
        [Test]
        public void BumperEdgeSuppressesZoneSelection()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(
                    buttonEdges: ButtonBit.LBumper,
                    buttons:     ButtonBit.LBumper,
                    rs:          new Vec2(0f, 1f),         // would otherwise select CHEST
                    lTrigger:    0.5f),
                BaseZone.BicepL);
            // Held by hysteresis, not overridden.
            Assert.AreEqual(BaseZone.BicepL, nextZone);
        }

        // it("both triggers down → both hands share the zone")
        [Test]
        public void BothTriggersDownShareTheZone()
        {
            var (base_, _) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(
                    rs:       new Vec2(-1f, 0f),
                    lTrigger: 0.8f,
                    rTrigger: 0.6f),
                BaseZone.None);
            // RS pure left has equal dot product with KNEE_L and BICEP_L. Whichever
            // wins, both hands target the same zone.
            Assert.AreNotEqual(BaseZone.None, base_.LHandTarget);
            Assert.AreEqual(base_.LHandTarget, base_.RHandTarget);
        }
    }

    // -------------------------------------------------------------------------
    // describe("Defender discrete intents (§B.6)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class DefenderDiscreteIntentTests
    {
        // it("L_BUMPER edge emits CUT_ATTEMPT(L) with RS snapshot")
        [Test]
        public void LBumperEdgeEmitsCutAttemptLWithRsSnapshot()
        {
            var list = LayerBDefenseOps.ComputeDefenseDiscreteIntents(
                LayerBDefenseTestHelpers.Frame(
                    buttonEdges: ButtonBit.LBumper,
                    buttons:     ButtonBit.LBumper,
                    rs:          new Vec2(-0.8f, 0.4f)));

            DefenseDiscreteIntent? cut = null;
            foreach (var d in list)
                if (d.Kind == DefenseDiscreteIntentKind.CutAttempt) { cut = d; break; }

            Assert.IsTrue(cut.HasValue, "expected a CutAttempt entry");
            Assert.AreEqual(HandSide.L, cut.Value.CutSide);
            Assert.AreEqual(-0.8f,      cut.Value.Rs.X, 1e-6f);
        }

        // it("BTN_BASE held → RECOVERY_HOLD each frame")
        [Test]
        public void BtnBaseHeldEmitsRecoveryHoldEachFrame()
        {
            var list = LayerBDefenseOps.ComputeDefenseDiscreteIntents(
                LayerBDefenseTestHelpers.Frame(buttons: ButtonBit.BtnBase));

            bool found = false;
            foreach (var d in list)
                if (d.Kind == DefenseDiscreteIntentKind.RecoveryHold) { found = true; break; }
            Assert.IsTrue(found, "expected RecoveryHold while BtnBase held");
        }

        // it("BTN_RESERVED edge → PASS_COMMIT with RS direction")
        [Test]
        public void BtnReservedEdgeEmitsPassCommitWithRsDirection()
        {
            var list = LayerBDefenseOps.ComputeDefenseDiscreteIntents(
                LayerBDefenseTestHelpers.Frame(
                    buttonEdges: ButtonBit.BtnReserved,
                    buttons:     ButtonBit.BtnReserved,
                    rs:          new Vec2(0.2f, -0.9f)));

            DefenseDiscreteIntent? commit = null;
            foreach (var d in list)
                if (d.Kind == DefenseDiscreteIntentKind.PassCommit) { commit = d; break; }

            Assert.IsTrue(commit.HasValue, "expected a PassCommit entry");
            Assert.AreEqual(-0.9f, commit.Value.Rs.Y, 1e-6f);
        }
    }

    // -------------------------------------------------------------------------
    // describe("BaseZone full coverage (all six zones selectable)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BaseZoneFullCoverageTests
    {
        private struct ZoneCase
        {
            public Vec2     Rs;
            public BaseZone Expected;
        }

        private static readonly ZoneCase[] Cases = new ZoneCase[]
        {
            new ZoneCase { Rs = new Vec2( 0f,       1f),       Expected = BaseZone.Chest  },
            new ZoneCase { Rs = new Vec2( 0f,      -1f),       Expected = BaseZone.Hip    },
            new ZoneCase { Rs = new Vec2(-0.7071f, -0.7071f),  Expected = BaseZone.KneeL  },
            new ZoneCase { Rs = new Vec2( 0.7071f, -0.7071f),  Expected = BaseZone.KneeR  },
            new ZoneCase { Rs = new Vec2(-0.7071f,  0.7071f),  Expected = BaseZone.BicepL },
            new ZoneCase { Rs = new Vec2( 0.7071f,  0.7071f),  Expected = BaseZone.BicepR },
        };

        // it(`RS ≈ ${...} + trigger → ${expected}`) for each of the six zones.
        // Split into one method per zone so failure diagnostics name the zone.
        [Test] public void RsUpSelectsChest()      => AssertZone(0);
        [Test] public void RsDownSelectsHip()      => AssertZone(1);
        [Test] public void RsDownLeftSelectsKneeL()  => AssertZone(2);
        [Test] public void RsDownRightSelectsKneeR() => AssertZone(3);
        [Test] public void RsUpLeftSelectsBicepL()   => AssertZone(4);
        [Test] public void RsUpRightSelectsBicepR()  => AssertZone(5);

        private static void AssertZone(int idx)
        {
            var c = Cases[idx];
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: c.Rs, lTrigger: 0.5f),
                BaseZone.None);
            Assert.AreEqual(c.Expected, nextZone);
        }
    }

    // -------------------------------------------------------------------------
    // describe("BaseZone hysteresis (§B.4.2 delegates to attacker §B.2.1)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class BaseZoneHysteresisTests
    {
        // it("small nudge from CHEST toward BICEP_L does NOT flip the zone")
        [Test]
        public void SmallNudgeFromChestTowardBicepLDoesNotFlip()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(-0.25f, 0.97f), lTrigger: 0.5f),
                BaseZone.Chest);
            Assert.AreEqual(BaseZone.Chest, nextZone);
        }

        // it("firm move into BICEP_L wedge flips the zone")
        [Test]
        public void FirmMoveIntoBicepLWedgeFlipsTheZone()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(-0.7f, 0.7f), lTrigger: 0.5f),
                BaseZone.Chest);
            Assert.AreEqual(BaseZone.BicepL, nextZone);
        }

        // it("with no previous zone, any aimed RS immediately selects")
        [Test]
        public void WithNoPreviousZoneAnyAimedRsImmediatelySelects()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(-0.25f, 0.97f), lTrigger: 0.5f),
                BaseZone.None);
            // Nearest wedge to (−0.25, 0.97) is CHEST (dot 0.97) over BICEP_L (≈0.86).
            Assert.AreEqual(BaseZone.Chest, nextZone);
        }

        // it("trigger still held but RS centred → zone persists")
        [Test]
        public void TriggerHeldRsCentredZonePersists()
        {
            var (base_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(0f, 0f), lTrigger: 0.7f),
                BaseZone.Hip);
            Assert.AreEqual(BaseZone.Hip, nextZone);
            Assert.AreEqual(BaseZone.Hip, base_.LHandTarget);
        }

        // it("trigger released with RS centred → zone cleared")
        [Test]
        public void TriggerReleasedRsCentredZoneCleared()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(0f, 0f)),
                BaseZone.KneeR);
            Assert.AreEqual(BaseZone.None, nextZone);
        }

        // it("bumper edge preserves previous zone (no flip while RS is repurposed)")
        [Test]
        public void BumperEdgePreservesPreviousZone()
        {
            var (_, nextZone) = LayerBDefenseOps.ComputeTopBaseIntent(
                LayerBDefenseTestHelpers.Frame(
                    buttonEdges: ButtonBit.RBumper,
                    buttons:     ButtonBit.RBumper,
                    rs:          new Vec2(-0.9f, 0.4f), // would otherwise pick BICEP_L
                    rTrigger:    0.4f),
                BaseZone.KneeR);
            Assert.AreEqual(BaseZone.KneeR, nextZone);
        }
    }

    // -------------------------------------------------------------------------
    // describe("transformLayerBDefense threading")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class TransformLayerBDefenseThreadingTests
    {
        // it("returns ZERO base when no trigger is held")
        [Test]
        public void ReturnsZeroBaseWhenNoTriggerHeld()
        {
            var (di, _) = LayerBDefenseOps.Transform(
                LayerBDefenseTestHelpers.Frame(rs: new Vec2(1f, 0f)),
                LayerBDefenseState.Initial);
            Assert.AreEqual(BaseZone.None, di.Base.LHandTarget);
            Assert.AreEqual(BaseZone.None, di.Base.RHandTarget);
        }

        // it("passes hip unchanged while producing a base intent under trigger")
        [Test]
        public void PassesHipUnchangedWhileProducingBaseIntentUnderTrigger()
        {
            var (di, _) = LayerBDefenseOps.Transform(
                LayerBDefenseTestHelpers.Frame(
                    ls:       new Vec2(0.3f, -0.4f),
                    rs:       new Vec2(0f, 1f),
                    lTrigger: 0.7f),
                LayerBDefenseState.Initial);
            Assert.AreEqual(-0.4f,          di.Hip.WeightForward, 1e-6f);
            Assert.AreEqual( 0.3f,          di.Hip.WeightLateral, 1e-6f);
            Assert.AreEqual(BaseZone.Chest, di.Base.LHandTarget);
            Assert.AreEqual( 0.7f,          di.Base.LBasePressure, 1e-6f);
        }
    }
}
