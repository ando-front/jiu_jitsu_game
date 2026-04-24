// NUnit EditMode mirror of src/prototype/web/tests/unit/hand_fsm.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest
// suite so a regression on either side produces a named, greppable failure.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using System.Collections.Generic;
using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public static class HandFSMTestHelpers
    {
        // Midpoint of §C.1.2's 200–350ms reach window; matches the FSM default.
        public const int ReachMid = (200 + 350) / 2; // 275

        public static HandTickInput BaseInput() => new HandTickInput
        {
            NowMs                  = 0,
            TriggerValue           = 0f,
            TargetZone             = GripZone.None,
            ForceReleaseAll        = false,
            OpponentDefendsThisZone = false,
            OpponentCutSucceeded   = false,
            TargetOutOfReach       = false,
        };

        // Drive a hand through a sequence of inputs, returning (finalState, allEventKinds).
        public static (HandFSM Final, List<HandEventKind> EventKinds) Run(
            HandFSM start,
            params HandTickInput[] steps)
        {
            var h      = start;
            var kinds  = new List<HandEventKind>();
            var events = new List<HandTickEvent>();
            foreach (var step in steps)
            {
                events.Clear();
                h = HandFSMOps.Tick(h, step, events);
                foreach (var ev in events) kinds.Add(ev.Kind);
            }
            return (h, kinds);
        }

        public static HandFSM BuildGripped()
        {
            var h = HandFSMOps.Initial(HandSide.R);
            var events = new List<HandTickEvent>();
            h = HandFSMOps.Tick(h, new HandTickInput { NowMs = 0,                TriggerValue = 1f, TargetZone = GripZone.Belt }, events);
            h = HandFSMOps.Tick(h, new HandTickInput { NowMs = ReachMid,          TriggerValue = 1f, TargetZone = GripZone.Belt }, events);
            h = HandFSMOps.Tick(h, new HandTickInput { NowMs = ReachMid + 16,     TriggerValue = 1f, TargetZone = GripZone.Belt }, events);
            Assert.AreEqual(HandState.Gripped, h.State, "BuildGripped helper: expected GRIPPED");
            return h;
        }
    }

    // -------------------------------------------------------------------------
    // IDLE → REACHING
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HandFSMIdleTests
    {
        [Test]
        public void TriggerAloneDoesNothing()
        {
            var h      = HandFSMOps.Initial(HandSide.L);
            var events = new List<HandTickEvent>();
            var next   = HandFSMOps.Tick(h, new HandTickInput { NowMs = 0, TriggerValue = 1f, TargetZone = GripZone.None }, events);

            Assert.AreEqual(HandState.Idle, next.State);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void TriggerPlusTargetStartsReach()
        {
            var h      = HandFSMOps.Initial(HandSide.L);
            var events = new List<HandTickEvent>();
            var next   = HandFSMOps.Tick(h, new HandTickInput { NowMs = 0, TriggerValue = 1f, TargetZone = GripZone.SleeveR }, events);

            Assert.AreEqual(HandState.Reaching, next.State);
            Assert.AreEqual(GripZone.SleeveR,   next.Target);
            Assert.AreEqual(1,                  events.Count);
            Assert.AreEqual(HandEventKind.ReachStarted, events[0].Kind);
        }
    }

    // -------------------------------------------------------------------------
    // REACHING → CONTACT → GRIPPED
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HandFSMReachingTests
    {
        [Test]
        public void ReachesAndGrips()
        {
            var h = HandFSMOps.Initial(HandSide.L);
            var (final, kinds) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,                                TriggerValue = 1f, TargetZone = GripZone.CollarL },
                new HandTickInput { NowMs = HandFSMTestHelpers.ReachMid,      TriggerValue = 1f, TargetZone = GripZone.CollarL },
                new HandTickInput { NowMs = HandFSMTestHelpers.ReachMid + 16, TriggerValue = 1f, TargetZone = GripZone.CollarL }
            );

            Assert.AreEqual(HandState.Gripped, final.State);
            CollectionAssert.AreEqual(
                new[] { HandEventKind.ReachStarted, HandEventKind.Contact, HandEventKind.Gripped },
                kinds);
        }

        [Test]
        public void AbortToIdleOnTriggerReleasesMidReach()
        {
            var h = HandFSMOps.Initial(HandSide.L);
            var (final, kinds) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,   TriggerValue = 1f, TargetZone = GripZone.CollarL },
                new HandTickInput { NowMs = 100, TriggerValue = 0f, TargetZone = GripZone.None    }
            );

            Assert.AreEqual(HandState.Idle, final.State);
            CollectionAssert.AreEqual(new[] { HandEventKind.ReachStarted }, kinds);
        }

        [Test]
        public void ReAimMidReachRestartsTimer()
        {
            int rm = HandFSMTestHelpers.ReachMid;
            var h  = HandFSMOps.Initial(HandSide.L);
            var (final, kinds) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,            TriggerValue = 1f, TargetZone = GripZone.CollarL  },
                new HandTickInput { NowMs = 100,          TriggerValue = 1f, TargetZone = GripZone.SleeveR  }, // re-aim
                new HandTickInput { NowMs = 100 + rm,     TriggerValue = 1f, TargetZone = GripZone.SleeveR  }, // CONTACT
                new HandTickInput { NowMs = 100 + rm + 16,TriggerValue = 1f, TargetZone = GripZone.SleeveR  }  // GRIPPED
            );

            Assert.AreEqual(HandState.Gripped, final.State);
            Assert.AreEqual(GripZone.SleeveR,  final.Target);
            CollectionAssert.AreEqual(
                new[] { HandEventKind.ReachStarted, HandEventKind.ReachStarted, HandEventKind.Contact, HandEventKind.Gripped },
                kinds);
        }
    }

    // -------------------------------------------------------------------------
    // CONTACT resolution (§2.1.3)
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HandFSMContactTests
    {
        [Test]
        public void OpponentDefendingParriesAndRetracts()
        {
            int rm = HandFSMTestHelpers.ReachMid;
            var h  = HandFSMOps.Initial(HandSide.L);
            var (final, kinds) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,          TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm,         TriggerValue = 1f, TargetZone = GripZone.SleeveR },                            // CONTACT
                new HandTickInput { NowMs = rm + 16,    TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = true }, // PARRIED
                new HandTickInput { NowMs = rm + 32,    TriggerValue = 0f, TargetZone = GripZone.None   },                             // → RETRACT
                new HandTickInput { NowMs = rm + 32 + HandTiming.Default.RetractMs, TriggerValue = 0f, TargetZone = GripZone.None }    // → IDLE
            );

            Assert.AreEqual(HandState.Idle, final.State);
            CollectionAssert.Contains(kinds, HandEventKind.Parried);
            Assert.AreEqual(GripZone.SleeveR, final.LastParriedZone);
        }

        [Test]
        public void ShortMemoryReparriesSameZoneWithin400ms()
        {
            // Fabricate a REACHING hand with a recent parry baked in, then
            // advance to CONTACT before the 400ms window elapses.
            var primed = new HandFSM
            {
                Side            = HandSide.L,
                State           = HandState.Reaching,
                Target          = GripZone.SleeveR,
                StateEnteredMs  = 100,
                ReachDurationMs = 100,
                LastParriedZone = GripZone.SleeveR,
                LastParriedAtMs = 50, // 150ms before the CONTACT frame below
            };

            var events = new List<HandTickEvent>();
            // Advance to CONTACT (reach timer fires).
            var contact = HandFSMOps.Tick(primed,
                new HandTickInput { NowMs = 200, TriggerValue = 1f, TargetZone = GripZone.SleeveR }, events);
            Assert.AreEqual(HandState.Contact, contact.State);

            // CONTACT frame: 150ms since the last parry → still inside the 400ms window.
            events.Clear();
            var resolve = HandFSMOps.Tick(contact,
                new HandTickInput { NowMs = 200, TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = false }, events);

            Assert.AreEqual(HandState.Parried, resolve.State);
            Assert.AreEqual(1, events.FindAll(e => e.Kind == HandEventKind.Parried).Count);
        }

        [Test]
        public void ShortMemoryExpiredDoesNotReparry()
        {
            var primed = new HandFSM
            {
                Side            = HandSide.L,
                State           = HandState.Contact,
                Target          = GripZone.SleeveR,
                StateEnteredMs  = 500,
                ReachDurationMs = 0,
                LastParriedZone = GripZone.SleeveR,
                LastParriedAtMs = 50, // 450ms earlier → outside 400ms window
            };

            var events = new List<HandTickEvent>();
            var resolve = HandFSMOps.Tick(primed,
                new HandTickInput { NowMs = 500, TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = false }, events);

            Assert.AreEqual(HandState.Gripped, resolve.State);
        }

        [Test]
        public void ShortMemoryDoesNotApplyToDifferentZone()
        {
            int rm = HandFSMTestHelpers.ReachMid;
            var h  = HandFSMOps.Initial(HandSide.L);
            // First: reach SleeveR and get parried.
            var (s1Final, _) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,                                 TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm,                                TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm + 16,                           TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = true },
                new HandTickInput { NowMs = rm + 32,                           TriggerValue = 0f, TargetZone = GripZone.None   },
                new HandTickInput { NowMs = rm + 32 + HandTiming.Default.RetractMs, TriggerValue = 0f, TargetZone = GripZone.None }
            );

            // Immediately retarget a *different* zone — should succeed without re-parry.
            int t2 = rm + 32 + HandTiming.Default.RetractMs;
            var (s2Final, s2Kinds) = HandFSMTestHelpers.Run(s1Final,
                new HandTickInput { NowMs = t2,             TriggerValue = 1f, TargetZone = GripZone.CollarL },
                new HandTickInput { NowMs = t2 + rm,        TriggerValue = 1f, TargetZone = GripZone.CollarL },
                new HandTickInput { NowMs = t2 + rm + 16,   TriggerValue = 1f, TargetZone = GripZone.CollarL }
            );

            Assert.AreEqual(HandState.Gripped, s2Final.State);
            CollectionAssert.Contains(s2Kinds, HandEventKind.Gripped);
        }
    }

    // -------------------------------------------------------------------------
    // GRIPPED break conditions (§2.1.4)
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HandFSMGrippedTests
    {
        [Test]
        public void TriggerReleasedEmitsGripBrokenAndEntersRetract()
        {
            var gripped = HandFSMTestHelpers.BuildGripped();
            var events  = new List<HandTickEvent>();
            var next    = HandFSMOps.Tick(gripped,
                new HandTickInput { NowMs = 1000, TriggerValue = 0f, TargetZone = GripZone.None }, events);

            Assert.AreEqual(HandState.Retract, next.State);
            var broken = events.Find(e => e.Kind == HandEventKind.GripBroken);
            Assert.IsNotNull(broken, "GRIP_BROKEN event expected");
            Assert.AreEqual(GripBrokenReason.TriggerReleased, broken.GripBrokenReason);
        }

        [Test]
        public void OpponentCutSucceedsEmitsGripBrokenOpponentCut()
        {
            var gripped = HandFSMTestHelpers.BuildGripped();
            var events  = new List<HandTickEvent>();
            HandFSMOps.Tick(gripped,
                new HandTickInput { NowMs = 1000, TriggerValue = 1f, TargetZone = GripZone.Belt, OpponentCutSucceeded = true }, events);

            Assert.IsTrue(events.Exists(e =>
                e.Kind == HandEventKind.GripBroken && e.GripBrokenReason == GripBrokenReason.OpponentCut));
        }

        [Test]
        public void ForceReleaseFromGrippedEmitsGripBrokenForceRelease()
        {
            var gripped = HandFSMTestHelpers.BuildGripped();
            var events  = new List<HandTickEvent>();
            var next    = HandFSMOps.Tick(gripped,
                new HandTickInput { NowMs = 1000, TriggerValue = 1f, TargetZone = GripZone.Belt, ForceReleaseAll = true }, events);

            Assert.AreEqual(HandState.Retract, next.State);
            Assert.IsTrue(events.Exists(e =>
                e.Kind == HandEventKind.GripBroken && e.GripBrokenReason == GripBrokenReason.ForceRelease));
        }
    }

    // -------------------------------------------------------------------------
    // RETRACT blocks new REACHING (§2.1.2)
    // -------------------------------------------------------------------------

    [TestFixture]
    public class HandFSMRetractTests
    {
        [Test]
        public void CannotStartReachDuringRetract()
        {
            int rm = HandFSMTestHelpers.ReachMid;
            var h  = HandFSMOps.Initial(HandSide.L);
            var (afterRetract, _) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,          TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm,         TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm + 16,    TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = true },
                new HandTickInput { NowMs = rm + 32,    TriggerValue = 1f, TargetZone = GripZone.SleeveR } // PARRIED → RETRACT
            );
            Assert.AreEqual(HandState.Retract, afterRetract.State);

            // Try to start a new reach while still in RETRACT.
            long midRetract = afterRetract.StateEnteredMs + HandTiming.Default.RetractMs / 2;
            var events = new List<HandTickEvent>();
            var next = HandFSMOps.Tick(afterRetract,
                new HandTickInput { NowMs = midRetract, TriggerValue = 1f, TargetZone = GripZone.CollarL }, events);

            Assert.AreEqual(HandState.Retract, next.State);
        }

        [Test]
        public void AfterRetractExpiresHandIsIdleAndCanReach()
        {
            int rm = HandFSMTestHelpers.ReachMid;
            var h  = HandFSMOps.Initial(HandSide.L);
            var (afterRetract, _) = HandFSMTestHelpers.Run(h,
                new HandTickInput { NowMs = 0,          TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm,         TriggerValue = 1f, TargetZone = GripZone.SleeveR },
                new HandTickInput { NowMs = rm + 16,    TriggerValue = 1f, TargetZone = GripZone.SleeveR, OpponentDefendsThisZone = true },
                new HandTickInput { NowMs = rm + 32,    TriggerValue = 1f, TargetZone = GripZone.SleeveR }
            );

            long idleAt = afterRetract.StateEnteredMs + HandTiming.Default.RetractMs;
            var events = new List<HandTickEvent>();
            var nowIdle = HandFSMOps.Tick(afterRetract,
                new HandTickInput { NowMs = idleAt, TriggerValue = 0f, TargetZone = GripZone.None }, events);
            Assert.AreEqual(HandState.Idle, nowIdle.State);

            events.Clear();
            var reaching = HandFSMOps.Tick(nowIdle,
                new HandTickInput { NowMs = idleAt + 1, TriggerValue = 1f, TargetZone = GripZone.WristL }, events);
            Assert.AreEqual(HandState.Reaching, reaching.State);
        }
    }
}
