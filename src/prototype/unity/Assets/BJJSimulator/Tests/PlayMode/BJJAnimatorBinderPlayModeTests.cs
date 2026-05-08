// PlayMode integration tests for Visual/BJJAnimatorBinder.
//
// Coverage rationale:
//   1. The binder's previous-frame cache (_prevBottomL etc.) and the
//      _hasPrev guard cannot be exercised purely from EditMode because the
//      MonoBehaviour Awake/Start/Update lifecycle is required to wire
//      _manager via [RequireComponent] and to fire the Lifecycle event
//      subscription. Reflection is used to read these private fields
//      without expanding the public surface.
//   2. The Animator slots are intentionally left null — the binder's
//      `if (anim == null)` early-return still updates the prev cache, so
//      the bookkeeping side of the binder can be verified without an
//      AnimatorController asset. The actual Animator.SetInteger/Trigger
//      path is straightforward and would only re-test Unity's API.
//   3. Lifecycle subscription is the bug-fix surface that landed with PR
//      #25 (Copilot review #5) — it is the most important thing to
//      regress against.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using BJJSimulator;
using BJJSimulator.Platform;
using BJJSimulator.Visual;

namespace BJJSimulator.PlayModeTests
{
    [TestFixture]
    public class BJJAnimatorBinderPlayModeTests
    {
        private GameObject              _go;
        private InputActionAsset        _asset;
        private BJJSessionLifecycle     _lifecycle;
        private BJJGameManager          _manager;
        private BJJAnimatorBinder       _binder;

        // ------------------------------------------------------------------
        // Fixture wiring
        // ------------------------------------------------------------------

        [SetUp]
        public void Setup()
        {
            _asset = MinimalActionAsset();

            _go = new GameObject("BinderTestRig");
            _go.SetActive(false); // defer OnEnable until asset injected

            // [RequireComponent] chain auto-adds Lifecycle + Provider + Manager
            // when we add the Manager (and Manager when we add the Binder).
            // Do it explicitly so Setup is readable.
            _lifecycle = _go.AddComponent<BJJSessionLifecycle>();
            var provider = _go.AddComponent<BJJInputProvider>();
            _manager   = _go.AddComponent<BJJGameManager>();
            _binder    = _go.AddComponent<BJJAnimatorBinder>();

            typeof(BJJInputProvider)
                .GetField("actionsAsset", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(provider, _asset);

            _go.SetActive(true);
        }

        [TearDown]
        public void Teardown()
        {
            if (_go    != null) Object.DestroyImmediate(_go);
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        // ------------------------------------------------------------------
        // Case 1 — Update populates the prev-state cache via the null-animator
        //          early-return branch, and _hasPrev becomes true after one
        //          Update tick. This is the smoke test for the binder's
        //          frame loop.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator UpdatePopulatesPrevHandStateCache()
        {
            // Stay in Prompt phase: BJJGameManager.Update returns early on
            // Prompt without calling FixedStepOps.Advance, so our injected
            // CurrentGameState is not overwritten before the binder reads
            // it. Active phase would advance the sim and clobber our state.
            yield return null; // Awake/Start ran on activate; this yield runs first Update

            // Inject a non-default GameState so we can tell the binder
            // actually read it (default-init would have HandState.Idle = 0
            // which is indistinguishable from "binder never ran").
            var gs = GameStateOps.InitialGameState(0L);
            gs.Bottom.LeftHand.State  = HandState.Reaching;
            gs.Bottom.RightHand.State = HandState.Gripped;
            gs.Top.LeftHand.State     = HandState.Contact;
            SetCurrentGameState(_manager, gs);
            yield return null; // Update runs in Prompt phase — preserves injection

            Assert.AreEqual(HandState.Reaching, GetEnumField<HandState>(_binder, "_prevBottomL"),
                "Binder must cache Bottom.LeftHand.State after Update");
            Assert.AreEqual(HandState.Gripped, GetEnumField<HandState>(_binder, "_prevBottomR"),
                "Binder must cache Bottom.RightHand.State after Update");
            Assert.AreEqual(HandState.Contact, GetEnumField<HandState>(_binder, "_prevTopL"),
                "Binder must cache Top.LeftHand.State after Update");
            Assert.IsTrue(GetBoolField(_binder, "_hasPrev"),
                "_hasPrev must flip to true after the first Update tick");
        }

        // ------------------------------------------------------------------
        // Case 2 — Lifecycle.RestartSession() invokes the binder's
        //          subscription handler and resets _hasPrev to false.
        //          This is the regression target for PR #25 / Copilot #5
        //          (without the subscription, a freshly-loaded GameState
        //          that legitimately starts with hands in Parried would
        //          mis-fire JustParried on the next Update).
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator LifecycleRestartResetsHasPrevGuard()
        {
            // First Update flips _hasPrev to true. Stay in Prompt for the
            // same reason as case 1 — we want the binder's bookkeeping to
            // run without FixedStepOps.Advance churning state in parallel.
            yield return null;
            Assume.That(GetBoolField(_binder, "_hasPrev"), Is.True,
                "preconditions: _hasPrev must be true before the reset");

            _lifecycle.RestartSession();

            Assert.IsFalse(GetBoolField(_binder, "_hasPrev"),
                "OnRestartRequested handler must clear _hasPrev synchronously");
        }

        // ------------------------------------------------------------------
        // Case 3 — Destroying the binder unsubscribes from the Lifecycle
        //          events; subsequent Lifecycle.RestartSession() must not
        //          crash even though the binder is gone. Catches the class
        //          of bugs where OnDestroy forgets to clean up subscriptions.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OnDestroyUnsubscribesCleanly()
        {
            yield return null;

            Object.DestroyImmediate(_binder);
            _binder = null; // Teardown's null-check skips it

            // Should not throw — if OnDestroy left the subscription dangling,
            // invoking the event would dispatch into a destroyed component
            // and Unity would log a MissingReferenceException.
            Assert.DoesNotThrow(() => _lifecycle.RestartSession(),
                "Lifecycle.RestartSession after binder destruction must not crash");
            Assert.DoesNotThrow(() => _lifecycle.LoadScenario(ScenarioName.ScissorReady),
                "Lifecycle.LoadScenario after binder destruction must not crash");
        }

        // ------------------------------------------------------------------
        // Helpers — reflection accessors so we don't expose internals
        // ------------------------------------------------------------------

        private static InputActionAsset MinimalActionAsset()
        {
            // Matches the shape BJJInputProvider.OnEnable expects.
            // Bindings are dummies; PlayMode tests for the binder don't
            // poll hardware.
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap("Player");
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
            return asset;
        }

        private static void SetCurrentGameState(BJJGameManager mgr, GameState gs)
        {
            // The auto-property's backing field is named
            // "<CurrentGameState>k__BackingField" by the C# compiler.
            var fi = typeof(BJJGameManager).GetField(
                "<CurrentGameState>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, "expected CurrentGameState backing field on BJJGameManager");
            fi.SetValue(mgr, gs);
        }

        private static T GetEnumField<T>(object instance, string name) where T : System.Enum
        {
            var fi = instance.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"expected private field {name} on {instance.GetType().Name}");
            return (T)fi.GetValue(instance);
        }

        private static bool GetBoolField(object instance, string name)
        {
            var fi = instance.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"expected private field {name} on {instance.GetType().Name}");
            return (bool)fi.GetValue(instance);
        }
    }
}
