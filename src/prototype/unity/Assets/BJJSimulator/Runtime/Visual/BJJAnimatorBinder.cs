// VISUAL — Alternative GameState bridge for Mixamo Humanoid rigs.
//
// This is the Animator-parameter path. The primary visual path on this
// project is the Transform-driven `BJJSimulator.Platform.BJJAvatarBinder`
// which directly poses primitive blockman joints. Use this binder instead
// when the project moves to a Mixamo Humanoid (or any other Animator
// Controller-driven rig) — see src/prototype/unity/README.md "Visual layer
// (Mixamo Animator path, optional)".
//
// Animator parameter contract (must match BJJAvatar.controller). Order is
// not significant; missing names log a Unity console warning rather than
// silently no-op.
//
//   IsBottom    bool       per-Avatar role tag
//   LHandState  int        (int)BJJSimulator.HandState
//   RHandState  int        (int)BJJSimulator.HandState
//   LFootState  int        (int)BJJSimulator.FootState
//   RFootState  int        (int)BJJSimulator.FootState
//   JustParried trigger    fires when any hand transitions into Parried
//
// Either Animator slot may be left null; the binder is no-op for the
// missing side. This lets a single-avatar test scene work without
// wiring the second Animator.

using BJJSimulator;
using BJJSimulator.Platform;
using UnityEngine;

namespace BJJSimulator.Visual
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJAnimatorBinder : MonoBehaviour
    {
        [SerializeField] private Animator bottomAnimator;
        [SerializeField] private Animator topAnimator;

        // Auto-resolved in Awake via [RequireComponent] guarantee. Kept
        // private (not [SerializeField]) so misconfiguration is impossible
        // — adding the component to BJJ_GameManager "just works".
        private BJJGameManager _manager;

        // Animator parameter names — single source of truth at code-review
        // time. The Animator Controller asset must declare matching
        // parameters of the listed types.
        private const string PIsBottom    = "IsBottom";
        private const string PLHandState  = "LHandState";
        private const string PRHandState  = "RHandState";
        private const string PLFootState  = "LFootState";
        private const string PRFootState  = "RFootState";
        private const string PJustParried = "JustParried";

        // Previous-frame snapshot for parry-edge detection. _hasPrev guards
        // both the very first Update *and* the first Update after a
        // Lifecycle restart / scenario load — so a fresh GameState that
        // legitimately starts with hands already in Parried does not
        // re-trigger the JustParried clip.
        private HandState _prevBottomL;
        private HandState _prevBottomR;
        private HandState _prevTopL;
        private HandState _prevTopR;
        private bool      _hasPrev;

        void Awake()
        {
            _manager = GetComponent<BJJGameManager>();
        }

        void Start()
        {
            // Subscribed in Start (not Awake) so BJJGameManager.Awake has
            // already populated _manager.Lifecycle. Same-GameObject Awake
            // ordering is not contractual, but Start runs after every Awake.
            if (_manager?.Lifecycle == null) return;
            _manager.Lifecycle.OnRestartRequested      += HandleRestart;
            _manager.Lifecycle.OnScenarioLoadRequested += HandleScenarioLoad;
        }

        void OnDestroy()
        {
            if (_manager?.Lifecycle == null) return;
            _manager.Lifecycle.OnRestartRequested      -= HandleRestart;
            _manager.Lifecycle.OnScenarioLoadRequested -= HandleScenarioLoad;
        }

        void Update()
        {
            if (_manager == null) return;

            var g = _manager.CurrentGameState;

            ApplyActor(bottomAnimator, isBottom: true,
                       ref _prevBottomL, ref _prevBottomR, in g.Bottom);
            ApplyActor(topAnimator,    isBottom: false,
                       ref _prevTopL,    ref _prevTopR,    in g.Top);

            _hasPrev = true;
        }

        private void ApplyActor(
            Animator anim,
            bool isBottom,
            ref HandState prevL,
            ref HandState prevR,
            in ActorState actor)
        {
            if (anim == null)
            {
                // Still advance prev state so we don't burst-fire JustParried
                // the moment an Animator is assigned mid-session.
                prevL = actor.LeftHand.State;
                prevR = actor.RightHand.State;
                return;
            }

            anim.SetBool   (PIsBottom,   isBottom);
            anim.SetInteger(PLHandState, (int)actor.LeftHand.State);
            anim.SetInteger(PRHandState, (int)actor.RightHand.State);
            anim.SetInteger(PLFootState, (int)actor.LeftFoot.State);
            anim.SetInteger(PRFootState, (int)actor.RightFoot.State);

            if (_hasPrev)
            {
                if (prevL != HandState.Parried && actor.LeftHand.State  == HandState.Parried)
                    anim.SetTrigger(PJustParried);
                if (prevR != HandState.Parried && actor.RightHand.State == HandState.Parried)
                    anim.SetTrigger(PJustParried);
            }

            prevL = actor.LeftHand.State;
            prevR = actor.RightHand.State;
        }

        // Lifecycle event handlers — suppress one frame of parry-edge
        // detection so a freshly loaded GameState that starts with hands in
        // Parried does not fire JustParried on the first post-reset Update.
        private void HandleRestart()                    => _hasPrev = false;
        private void HandleScenarioLoad(ScenarioName _) => _hasPrev = false;
    }
}
