// PlayMode integration tests for BJJInputProvider.PollHardware.
//
// WHY PlayMode: PollHardware reads Keyboard.current and Gamepad.current
// directly, which are only available when the InputSystem runtime is live.
// EditMode tests cover LayerAOps (pure) via RawHardwareSnapshot injection;
// these tests verify the Provider-level polling produces the same behaviour
// through the real hardware path.
//
// Pattern: inherit InputTestFixture → AddDevice<Keyboard/Gamepad> to get
// virtual devices, call PollHardware() manually (mirrors BJJGameManager.Update),
// assert on the public DigitEdges / LastFrame properties.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using BJJSimulator;
using BJJSimulator.Platform;

namespace BJJSimulator.PlayModeTests
{
    [TestFixture]
    public class BJJInputProviderPlayModeTests : InputTestFixture
    {
        private GameObject      _providerGo;
        private BJJInputProvider _provider;
        private InputActionAsset _asset;
        private Keyboard         _keyboard;

        // ------------------------------------------------------------------
        // Shared setup / teardown
        // ------------------------------------------------------------------

        [SetUp]
        public override void Setup()
        {
            base.Setup(); // resets InputSystem to clean test state

            _keyboard = InputSystem.AddDevice<Keyboard>();

            // Minimal InputActionAsset so OnEnable can resolve action
            // references without requiring the real .inputactions asset on disk.
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _asset.AddActionMap("Player");
            map.AddAction("LeftStick",    InputActionType.Value,  "<Gamepad>/leftStick");
            map.AddAction("RightStick",   InputActionType.Value,  "<Gamepad>/rightStick");
            map.AddAction("LeftTrigger",  InputActionType.Value,  "<Gamepad>/leftTrigger");
            map.AddAction("RightTrigger", InputActionType.Value,  "<Gamepad>/rightTrigger");
            map.AddAction("LeftBumper",   InputActionType.Button, "<Gamepad>/leftShoulder");
            map.AddAction("RightBumper",  InputActionType.Button, "<Gamepad>/rightShoulder");
            map.AddAction("BtnBase",      InputActionType.Button, "<Gamepad>/buttonSouth");
            map.AddAction("BtnRelease",   InputActionType.Button, "<Gamepad>/buttonEast");
            map.AddAction("BtnBreath",    InputActionType.Button, "<Gamepad>/buttonNorth");
            map.AddAction("BtnReserved",  InputActionType.Button, "<Gamepad>/buttonWest");
            map.AddAction("BtnPause",     InputActionType.Button, "<Gamepad>/startButton");

            // Create the provider on an initially-inactive GO so OnEnable is
            // deferred until after actionsAsset is injected via reflection.
            _providerGo = new GameObject("TestProvider");
            _providerGo.SetActive(false);
            _provider   = _providerGo.AddComponent<BJJInputProvider>();

            typeof(BJJInputProvider)
                .GetField("actionsAsset",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_provider, _asset);

            _providerGo.SetActive(true); // OnEnable now runs with asset assigned
        }

        [TearDown]
        public override void TearDown()
        {
            if (_providerGo != null) Object.DestroyImmediate(_providerGo);
            if (_asset      != null) Object.DestroyImmediate(_asset);
            base.TearDown();
        }

        // ------------------------------------------------------------------
        // Case 1 — edge fires exactly once on the first down-stroke
        // Purpose: verify _digitPrev tracking produces up→down edges only
        // Expected: bit 1 set on press, 0 on release, set again on re-press
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DigitEdgeFiresOncePerPress()
        {
            long t = 1000;

            // First press — _digitPrev=0, digitNow=0b10 → edge=0b10
            Press(_keyboard.digit1Key);
            _provider.PollHardware(t);
            Assert.AreNotEqual(0, _provider.DigitEdges & (1 << 1),
                "first down-stroke: bit 1 must be set");

            // Release — digitNow=0, edges contain no up→down transitions
            Release(_keyboard.digit1Key);
            _provider.PollHardware(t += 16);
            Assert.AreEqual(0, _provider.DigitEdges & (1 << 1),
                "after release: bit 1 must be 0 (no up→down edge)");

            yield return null; // one game frame passes

            // Second press — _digitPrev now 0 again → fresh edge
            Press(_keyboard.digit1Key);
            _provider.PollHardware(t += 16);
            Assert.AreNotEqual(0, _provider.DigitEdges & (1 << 1),
                "second down-stroke: bit 1 must fire again");

            Release(_keyboard.digit1Key);
        }

        // ------------------------------------------------------------------
        // Case 2 — held digit does not repeatedly set the edge bit
        // Purpose: confirm _digitPrev persists across PollHardware calls
        // Expected: edge fires on frame 0 only; frames 1-4 produce 0
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator HeldDigitDoesNotReFire()
        {
            long t = 1000;
            Press(_keyboard.digit1Key);

            for (int i = 0; i < 5; i++)
            {
                yield return null; // advance one game frame while key stays held
                _provider.PollHardware(t += 16);

                if (i == 0)
                    Assert.AreNotEqual(0, _provider.DigitEdges & (1 << 1),
                        $"frame {i}: initial down-stroke must set edge");
                else
                    Assert.AreEqual(0, _provider.DigitEdges & (1 << 1),
                        $"frame {i}: held key must not re-fire (_digitPrev stays 0b10)");
            }

            Release(_keyboard.digit1Key);
        }

        // ------------------------------------------------------------------
        // Case 3 — keyboard beats noisy gamepad via LayerAOps arbitration
        // Purpose: verify KbAnyHeld=true causes LayerAOps to select keyboard
        // Expected: LastFrame.DeviceKind == Keyboard, Ls.Y > 0 (W key = LsUp)
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NoisyGamepadKeyboardWins()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            yield return null;

            // Pin gamepad LS to (-1, 0) — simulates a stuck/noisy pad axis.
            Set(pad.leftStick, new Vector2(-1f, 0f));

            // W key pressed → kbNow.AnyHeld=true → KbAnyHeld=true in snapshot
            // → LayerAOps sets useGamepad=false → DeviceKind=Keyboard
            Press(_keyboard.wKey);
            yield return null;

            const long t = 2000L;
            _provider.SetCurrentGameState(GameStateOps.InitialGameState(t));
            _provider.PollHardware(t);
            _provider.Sample(t); // populates LastFrame

            Assert.IsTrue(_provider.LastFrame.HasValue,
                "LastFrame must be populated after Sample()");
            Assert.AreEqual(DeviceKind.Keyboard, _provider.LastFrame.Value.DeviceKind,
                "KbAnyHeld must beat noisy gamepad (LayerAOps arbitration)");
            Assert.Greater(_provider.LastFrame.Value.Ls.Y, 0f,
                "W maps to LsUp → EightWayFromDigital must produce Ls.Y > 0");

            Release(_keyboard.wKey);
        }

        // ------------------------------------------------------------------
        // Case 4 — digit edge wired to lifecycle mirrors HandleDigitEdges
        // Purpose: verify digit → ScenarioName cast matches SCENARIO_ORDER
        // Expected: Digit1 → ScissorReady (enum index 0), phase stays Active
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ScenarioLoadResetsLifecycle()
        {
            var lifecycleGo = new GameObject("TestLifecycle");
            var lifecycle   = lifecycleGo.AddComponent<BJJSessionLifecycle>();

            try
            {
                // Prompt → Active (HandleDigitEdges is gated to non-Prompt phases)
                lifecycle.DismissPrompt();
                Assert.AreEqual(LifecyclePhase.Active, lifecycle.CurrentPhase,
                    "DismissPrompt must transition Prompt → Active");

                Press(_keyboard.digit1Key);
                yield return null;
                _provider.PollHardware(3000);

                // Replicate BJJGameManager.HandleDigitEdges logic:
                // Digit1 → (ScenarioName)(1 - 1) = ScenarioName.ScissorReady
                if (((_provider.DigitEdges >> 1) & 1) != 0)
                    lifecycle.LoadScenario((ScenarioName)(1 - 1));

                Assert.AreEqual(ScenarioName.ScissorReady, lifecycle.ActiveScenario,
                    "Digit1 must load ScissorReady (enum index 0, matches SCENARIO_ORDER in Scenarios.cs)");
                Assert.AreEqual(LifecyclePhase.Active, lifecycle.CurrentPhase,
                    "LoadScenario must keep phase Active");

                Release(_keyboard.digit1Key);
            }
            finally
            {
                Object.DestroyImmediate(lifecycleGo);
            }
        }
    }
}
