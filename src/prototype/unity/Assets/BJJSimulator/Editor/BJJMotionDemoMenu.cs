// EDITOR — motion demo cycler.
//
// Play-mode-only Editor menu that drives an Avatar's Animator through every
// HandState in turn (Idle → Reaching → Contact → Gripped → Retract → Idle…)
// so each clip can be visually verified in a single sitting without scripting
// inputs or wiring scenarios.
//
// Usage:
//   1. Enter Play mode.
//   2. BJJ → Demo Motions → Cycle Bottom HandStates  (or Top)
//   3. The avatar holds each state for SecondsPerState seconds, then advances.
//      The current state is logged to the Console so you know which clip
//      should be playing.
//   4. BJJ → Demo Motions → Stop Cycle  to halt and hand control back to
//      BJJAnimatorBinder.
//
// Implementation notes:
//   - Disables BJJAnimatorBinder on the parent BJJ_GameManager while running
//     so its per-frame parameter writes don't fight the cycler. Re-enables on
//     Stop and on Play-mode exit.
//   - Drives the cycle from EditorApplication.update + a wall-clock timer
//     (EditorApplication.timeSinceStartup) — no coroutine package dep.
//   - Skips HandState.Parried (4) because Reaction is trigger-driven (the
//     Animator's AnyState→Reaction transition fires on JustParried, not on
//     a HandState equality), so setting LHandState=4 wouldn't enter Reaction.

using UnityEditor;
using UnityEngine;
using BJJSimulator.Visual;

namespace BJJSimulator.EditorTools
{
    public static class BJJMotionDemoMenu
    {
        private const string Root = "BJJ/Demo Motions/";

        // Order chosen so the cycle reads as a "lifecycle of one grip":
        // Idle → reach forward → make contact → close grip → retract → Idle.
        private static readonly (int Hand, string Label)[] CYCLE = new[]
        {
            (0, "Idle"),
            (1, "Reaching"),
            (2, "Contact"),
            (3, "Gripped"),
            (5, "Retract"),
        };

        private const float SecondsPerState = 3f;

        private static int       _index;
        private static double    _stateStartTime;
        private static Animator  _animator;
        private static BJJAnimatorBinder _binder;
        private static bool      _isRunning;
        private static string    _activeAvatar;

        // ------------------------------------------------------------------
        // Menu entries — gated to Play mode via the Validate sibling.
        // ------------------------------------------------------------------

        [MenuItem(Root + "Cycle Bottom HandStates", true)]
        private static bool ValidateBottom() => Application.isPlaying;

        [MenuItem(Root + "Cycle Bottom HandStates")]
        public static void CycleBottom() => StartCycle("Avatar_Bottom", isBottom: true);

        [MenuItem(Root + "Cycle Top HandStates", true)]
        private static bool ValidateTop() => Application.isPlaying;

        [MenuItem(Root + "Cycle Top HandStates")]
        public static void CycleTop() => StartCycle("Avatar_Top", isBottom: false);

        [MenuItem(Root + "Stop Cycle", true)]
        private static bool ValidateStop() => _isRunning;

        [MenuItem(Root + "Stop Cycle")]
        public static void Stop() => StopCycle(reason: "manual");

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private static void StartCycle(string avatarName, bool isBottom)
        {
            if (_isRunning) StopCycle(reason: "restart");

            var path = "BJJ_GameManager/" + avatarName;
            var avatar = GameObject.Find(path);
            if (avatar == null)
            {
                Debug.LogError($"[BJJ Motion Demo] Avatar not found at {path}");
                return;
            }
            _animator = avatar.GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogError($"[BJJ Motion Demo] No Animator on {path}");
                return;
            }

            // Pause BJJAnimatorBinder so it doesn't overwrite our parameters
            // every Update. Lives on the parent BJJ_GameManager.
            var mgr = avatar.transform.parent;
            _binder = mgr != null ? mgr.GetComponent<BJJAnimatorBinder>() : null;
            if (_binder != null) _binder.enabled = false;

            // Pin role + foot state once; the cycle only varies LHandState.
            _animator.SetBool("IsBottom",    isBottom);
            _animator.SetInteger("LFootState", 0);
            _animator.SetInteger("RFootState", 0);
            _animator.SetInteger("RHandState", 0);

            _index          = 0;
            _stateStartTime = EditorApplication.timeSinceStartup;
            _activeAvatar   = avatarName;
            ApplyCurrentState();

            EditorApplication.update      += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _isRunning = true;
            Debug.Log($"[BJJ Motion Demo] Started cycle on {avatarName} ({SecondsPerState}s/state). " +
                      "Use BJJ → Demo Motions → Stop Cycle to halt.");
        }

        private static void StopCycle(string reason)
        {
            if (!_isRunning) return;
            EditorApplication.update      -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // Reset hand state so the avatar settles back at Idle and let the
            // binder take over again.
            if (_animator != null)
            {
                _animator.SetInteger("LHandState", 0);
                _animator.SetInteger("RHandState", 0);
            }
            if (_binder != null) _binder.enabled = true;

            _animator   = null;
            _binder     = null;
            _isRunning  = false;
            _activeAvatar = null;
            Debug.Log($"[BJJ Motion Demo] Cycle stopped ({reason}).");
        }

        private static void Tick()
        {
            if (!Application.isPlaying || _animator == null)
            {
                StopCycle(reason: "play-mode left or animator destroyed");
                return;
            }
            double elapsed = EditorApplication.timeSinceStartup - _stateStartTime;
            if (elapsed >= SecondsPerState)
            {
                _index          = (_index + 1) % CYCLE.Length;
                _stateStartTime = EditorApplication.timeSinceStartup;
                ApplyCurrentState();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode ||
                change == PlayModeStateChange.EnteredEditMode)
                StopCycle(reason: "play mode exit");
        }

        private static void ApplyCurrentState()
        {
            var entry = CYCLE[_index];
            _animator.SetInteger("LHandState", entry.Hand);
            Debug.Log($"[BJJ Motion Demo] {_activeAvatar}: {entry.Label} (LHandState={entry.Hand})");
        }
    }
}
