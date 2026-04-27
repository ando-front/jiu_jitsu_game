// NUnit EditMode mirror of src/prototype/web/tests/unit/layerA.test.ts.
// Each [Test] here corresponds to one it(...) case from the Stage 1 Vitest
// suite so a regression on either side produces a named, greppable failure.
//
// Reference: docs/design/input_system_v1.md §A.4 (assembler), §A.3 (transforms),
//            §A.2.2 (8-way digital normalisation).
//
// Scope note: TS layerA.test.ts mixes (a) pure-transform tests, (b) LayerA
// assembler tests, and (c) tests that exercise KeyboardSource / GamepadSource
// directly (browser-API-bound classes). In Stage 2:
//   - (a) is fully covered by InputTransformTest.cs already → not duplicated here.
//   - (b) is what this file ports — the pure assembler logic (LayerAOps.Assemble),
//         driven via the engine-agnostic RawHardwareSnapshot struct.
//   - (c) is NOT portable: KeyboardSource / GamepadSource are replaced by Unity's
//         New Input System. Coverage migrates to a PlayMode test that wires the
//         New Input System sources, not to this EditMode suite.
//
// Run from Unity Editor: Window → General → Test Runner → EditMode.

using NUnit.Framework;

namespace BJJSimulator.Tests
{
    public static class LayerATestHelpers
    {
        // A zeroed snapshot — fill in only the fields a given test cares about.
        public static RawHardwareSnapshot EmptyKb() => new RawHardwareSnapshot
        {
            GamepadConnected = false,
            DeviceKind       = DeviceKind.Keyboard,
        };

        // Shorthand for a "live" gamepad snapshot with a stick value over the
        // GamepadHasActivity threshold (>0.2 stick magnitude).
        public static RawHardwareSnapshot LiveGamepad(
            Vec2      ls       = default,
            Vec2      rs       = default,
            float     lTrigger = 0f,
            float     rTrigger = 0f,
            ButtonBit buttons  = ButtonBit.None,
            DeviceKind kind    = DeviceKind.Xbox) => new RawHardwareSnapshot
        {
            GamepadConnected = true,
            GamepadLs        = ls,
            GamepadRs        = rs,
            GamepadLTrigger  = lTrigger,
            GamepadRTrigger  = rTrigger,
            GamepadButtons   = buttons,
            DeviceKind       = kind,
        };
    }

    // -------------------------------------------------------------------------
    // describe("LayerA assembler (§A.4)")
    // -------------------------------------------------------------------------

    [TestFixture]
    public class LayerAAssemblerTests
    {
        // it("falls back to keyboard when no gamepad is connected")
        [Test]
        public void FallsBackToKeyboardWhenNoGamepadIsConnected()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                LsRight          = true,           // ls right
                KbLTrigger       = true,           // l_trigger
                KbButtons        = ButtonBit.BtnBase,
            };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 1000L);

            Assert.AreEqual(DeviceKind.Keyboard, frame.DeviceKind);
            Assert.AreEqual(1f, frame.Ls.X, 1e-5f);
            Assert.AreEqual(0f, frame.Ls.Y, 1e-5f);
            Assert.AreEqual(1f, frame.LTrigger, 1e-5f);
            Assert.AreNotEqual(ButtonBit.None, frame.Buttons     & ButtonBit.BtnBase);
            Assert.AreNotEqual(ButtonBit.None, frame.ButtonEdges & ButtonBit.BtnBase);
        }

        // it("timestamp passes through the injected now value")
        [Test]
        public void TimestampPassesThroughTheInjectedNowValue()
        {
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, LayerATestHelpers.EmptyKb(), 42L);
            Assert.AreEqual(42L, frame.Timestamp);
        }

        // it("edge bits are computed across successive samples")
        [Test]
        public void EdgeBitsAreComputedAcrossSuccessiveSamples()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                KbButtons        = ButtonBit.BtnBreath,
            };

            // Frame 1: BTN_BREATH first seen → edge fires.
            var (f1, state1) = LayerAOps.Assemble(LayerAState.Initial, hw, 16L);
            Assert.AreNotEqual(ButtonBit.None, f1.ButtonEdges & ButtonBit.BtnBreath);

            // Frame 2: still held → no edge, but the held bit is still set.
            var (f2, _) = LayerAOps.Assemble(state1, hw, 32L);
            Assert.AreEqual(ButtonBit.None,    f2.ButtonEdges & ButtonBit.BtnBreath);
            Assert.AreNotEqual(ButtonBit.None, f2.Buttons     & ButtonBit.BtnBreath);
        }

        // -------------------------------------------------------------------------
        // Assembler-only logic that the TS test exercised through KeyboardSource /
        // GamepadSource. These are NEW EditMode cases that mirror the *behaviour*
        // those browser-bound tests covered, but driven through RawHardwareSnapshot
        // so they don't depend on a runtime input source.
        // -------------------------------------------------------------------------

        // Mirrors the §A.1 device-priority rule: gamepad wins whenever it has
        // recent activity.
        [Test]
        public void LiveGamepadWinsOverKeyboardForDeviceKind()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = true,
                GamepadLs        = new Vec2(1f, 0f),     // > 0.2 activity threshold
                DeviceKind       = DeviceKind.DualSense,
                // Keyboard fields are also set — assembler must ignore them.
                LsLeft           = true,
                KbButtons        = ButtonBit.BtnBase,
            };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 0L);

            Assert.AreEqual(DeviceKind.DualSense, frame.DeviceKind);
            // Stick comes from the gamepad (post deadzone+curve), not the keyboard.
            Assert.Greater(frame.Ls.X, 0f, "Gamepad LS should win over keyboard LsLeft");
            Assert.AreEqual(ButtonBit.None, frame.Buttons & ButtonBit.BtnBase,
                "Keyboard buttons must not bleed through when gamepad is active");
        }

        // Mirrors §A.4: a connected-but-idle gamepad does NOT win — keyboard wins.
        [Test]
        public void IdleGamepadFallsBackToKeyboard()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = true,
                // All gamepad inputs below the activity thresholds (stick<0.2, trig<0.1).
                GamepadLs        = new Vec2(0.1f, 0f),
                GamepadLTrigger  = 0.05f,
                DeviceKind       = DeviceKind.Xbox,
                // Keyboard input is present.
                LsRight          = true,
                KbButtons        = ButtonBit.BtnRelease,
            };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 0L);

            Assert.AreEqual(DeviceKind.Keyboard, frame.DeviceKind);
            Assert.AreEqual(1f, frame.Ls.X, 1e-5f);
            Assert.AreNotEqual(ButtonBit.None, frame.Buttons & ButtonBit.BtnRelease);
        }

        // Stick deadzone+curve must be applied through the assembler when the
        // gamepad path runs — not just by directly calling InputTransform.
        [Test]
        public void GamepadStickIsRoutedThroughDeadzoneAndCurve()
        {
            // Raw value below the inner deadzone (0.15) → assembler should zero it.
            var hw = LayerATestHelpers.LiveGamepad(
                ls:      new Vec2(0.10f, 0.05f),
                buttons: ButtonBit.BtnBase);          // give the gamepad some activity
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 0L);

            Assert.AreEqual(0f, frame.Ls.X, 1e-6f);
            Assert.AreEqual(0f, frame.Ls.Y, 1e-6f);
        }

        // Keyboard digital → 8-way unit vector must be applied at the assembler
        // level, not just by calling InputTransform directly.
        [Test]
        public void KeyboardDiagonalProducesUnitDiagonalOnFrame()
        {
            var hw = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                LsUp             = true,
                LsRight          = true,
            };
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 0L);

            Assert.AreEqual(1f, frame.Ls.Magnitude, 1e-5f);
        }

        // The held-bit / edge-bit relationship must hold even when the held set
        // grows incrementally (release of one button must not erase another's
        // edge if both happen on the same tick).
        [Test]
        public void PartialButtonChurnPreservesNewlyPressedEdges()
        {
            var hw1 = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                KbButtons        = ButtonBit.BtnBase,
            };
            var (_, s1) = LayerAOps.Assemble(LayerAState.Initial, hw1, 0L);

            // Frame 2: BtnBase released, BtnBreath newly pressed.
            var hw2 = new RawHardwareSnapshot
            {
                GamepadConnected = false,
                KbButtons        = ButtonBit.BtnBreath,
            };
            var (f2, _) = LayerAOps.Assemble(s1, hw2, 16L);

            Assert.AreNotEqual(ButtonBit.None, f2.ButtonEdges & ButtonBit.BtnBreath);
            Assert.AreEqual(ButtonBit.None,    f2.ButtonEdges & ButtonBit.BtnBase);
            Assert.AreEqual(ButtonBit.None,    f2.Buttons     & ButtonBit.BtnBase);
        }
    }

    // -------------------------------------------------------------------------
    // Noisy-gamepad arbitration (§A.4 — KbRecentMs tie-breaker).
    //
    // Mirrors the kbActive logic in src/prototype/web/src/input/layerA.ts so
    // that a broken IR remote pinning axes at -1 (or a controller with a
    // dead-zone-broken stick) cannot silently lock keyboard input out.
    // -------------------------------------------------------------------------

    [TestFixture]
    public class LayerANoisyGamepadTests
    {
        private static RawHardwareSnapshot NoisyPad(
            bool      kbAnyHeld     = false,
            long      kbLastEventMs = BJJConst.SentinelTimeMs,
            ButtonBit kbButtons     = ButtonBit.None,
            bool      lsRight       = false) => new RawHardwareSnapshot
        {
            // Pad reports activity continuously (stick pinned, well past 0.2).
            GamepadConnected = true,
            GamepadLs        = new Vec2(-1f, 0f),
            DeviceKind       = DeviceKind.Xbox,
            // Keyboard truth.
            LsRight          = lsRight,
            KbButtons        = kbButtons,
            KbAnyHeld        = kbAnyHeld,
            KbLastEventMs    = kbLastEventMs,
        };

        // While any tracked key is currently held, the noisy gamepad must lose
        // — independent of how recent kbLastEventMs is.
        [Test]
        public void HeldKeyboardWinsOverNoisyGamepad()
        {
            var hw = NoisyPad(kbAnyHeld: true, kbButtons: ButtonBit.BtnBase, lsRight: true);
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 0L);

            Assert.AreEqual(DeviceKind.Keyboard, frame.DeviceKind);
            Assert.AreEqual(1f, frame.Ls.X, 1e-5f, "Keyboard LsRight should win.");
            Assert.AreNotEqual(ButtonBit.None, frame.Buttons & ButtonBit.BtnBase);
        }

        // No key currently held but a tracked event happened within KbRecentMs
        // → keyboard still wins (covers the "user just released a key" frame).
        [Test]
        public void RecentKeyboardEventWinsOverNoisyGamepad()
        {
            // Event at t=500, sample at t=1000 → 500ms ago, well inside 1500.
            var hw = NoisyPad(kbAnyHeld: false, kbLastEventMs: 500L);
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 1000L);

            Assert.AreEqual(DeviceKind.Keyboard, frame.DeviceKind);
            // Pad LS would be -1 after deadzone+curve; keyboard path produces 0.
            Assert.AreEqual(0f, frame.Ls.X, 1e-5f);
        }

        // Once the keyboard has been quiet for >= KbRecentMs, the gamepad
        // wins again — otherwise releasing the keyboard would silence the pad
        // forever.
        [Test]
        public void StaleKeyboardLetsGamepadWinAgain()
        {
            // Last keyboard event at t=0, sample at t=2000 → 2000ms gap > 1500.
            var hw = NoisyPad(kbAnyHeld: false, kbLastEventMs: 0L);
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 2000L);

            Assert.AreEqual(DeviceKind.Xbox, frame.DeviceKind);
            // Pad LS x = -1 raw → after deadzone+curve still negative (x ≈ -1).
            Assert.Less(frame.Ls.X, 0f, "Gamepad LS should drive the frame.");
        }

        // Sentinel kbLastEventMs (no event ever seen) must NOT be treated as
        // "recent" — without the guard a (nowMs - long.MinValue) overflow
        // would yield a tiny positive number and lock the gamepad out.
        [Test]
        public void SentinelKbLastEventDoesNotBlockGamepad()
        {
            var hw = NoisyPad(
                kbAnyHeld:     false,
                kbLastEventMs: BJJConst.SentinelTimeMs);
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, 1_000_000L);

            Assert.AreEqual(DeviceKind.Xbox, frame.DeviceKind);
        }

        // Boundary: exactly KbRecentMs since last event → gamepad wins
        // (the comparison is strict `<`, so equal is "stale").
        [Test]
        public void ExactlyKbRecentMsBoundaryFlipsToGamepad()
        {
            var hw = NoisyPad(kbAnyHeld: false, kbLastEventMs: 0L);
            var (frame, _) = LayerAOps.Assemble(LayerAState.Initial, hw, LayerAOps.KbRecentMs);

            Assert.AreEqual(DeviceKind.Xbox, frame.DeviceKind);
        }
    }
}
