// PLATFORM — blockman avatar data pump.
// See docs/design/stage2_port_progress.md item 1 (Skinned mesh + Animator).
//
// Reads CurrentGameState from BJJGameManager each LateUpdate and drives
// serialised Transform references (wired in the Inspector) to match.  All
// references are optional — null ones are silently skipped so the scene runs
// before a full rig is assembled in the Editor.
//
// Bottom = guard-side player (attacker intent).
// Top    = passer-side player (defender intent).
//
// Coordinate convention: local space of each root Transform.  Rig authors
// should keep bottomRoot/topRoot parented under a scene "Characters" node
// at world origin (Z forward, Y up) so the offsets serialised here stay
// readable.

using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJAvatarBinder : MonoBehaviour
    {
        // --------------------------------------------------------------------
        // Bottom (guard) — Inspector refs
        // --------------------------------------------------------------------

        [Header("Bottom (Guard) rig")]
        [SerializeField] private Transform bottomRoot;
        [SerializeField] private Transform bottomLeftHand;
        [SerializeField] private Transform bottomRightHand;
        [SerializeField] private Transform bottomLeftFoot;
        [SerializeField] private Transform bottomRightFoot;

        // Optional per-foot Renderer for locked/unlocked colour feedback.
        [SerializeField] private Renderer bottomLeftFootRenderer;
        [SerializeField] private Renderer bottomRightFootRenderer;

        // --------------------------------------------------------------------
        // Top (passer) — Inspector refs
        // --------------------------------------------------------------------

        [Header("Top (Passer) rig")]
        [SerializeField] private Transform topRoot;
        [SerializeField] private Transform topSpine; // driven by PostureBreak Vec2

        // --------------------------------------------------------------------
        // Visual tuning
        // --------------------------------------------------------------------

        [Header("Blockman world-space base positions")]
        [SerializeField] private Vector3 bottomBase = new Vector3(-0.4f, 0f, 0f);
        [SerializeField] private Vector3 topBase    = new Vector3( 0.4f, 0f, 0f);

        [Header("Posture-break lean (degrees at full break = 1.0)")]
        [SerializeField, Range(0f, 60f)] private float postureYawMaxDeg   = 30f; // X → Y-axis lean
        [SerializeField, Range(0f, 60f)] private float posturePitchMaxDeg = 25f; // Y → X-axis lean

        [Header("Hand reach target (local offset from hand pivot)")]
        [SerializeField] private Vector3 handReachOffset = new Vector3(0f, 0.1f, 0.35f);
        [SerializeField, Range(0.05f, 1f)] private float handLerpSpeed = 0.25f;

        [Header("Foot lift when Unlocked (world Y, local space)")]
        [SerializeField, Range(0f, 0.5f)] private float footUnlockedLiftY = 0.15f;
        [SerializeField, Range(0.05f, 1f)] private float footLerpSpeed    = 0.25f;

        [Header("Foot state colours (Renderer.material.color)")]
        [SerializeField] private Color footLockedColor   = new Color(0.15f, 0.80f, 0.15f);
        [SerializeField] private Color footLockingColor  = new Color(0.90f, 0.70f, 0.10f);
        [SerializeField] private Color footUnlockedColor = new Color(0.90f, 0.20f, 0.20f);

        // --------------------------------------------------------------------
        // Runtime
        // --------------------------------------------------------------------

        private BJJGameManager _manager;

        void Awake() => _manager = GetComponent<BJJGameManager>();

        // LateUpdate runs after BJJGameManager.Update, so CurrentGameState
        // already reflects the latest FixedStepOps.Advance result.
        void LateUpdate()
        {
            var g = _manager.CurrentGameState;
            DriveBottom(g.Bottom);
            DriveTop(g.Top);
        }

        // --------------------------------------------------------------------
        // Bottom drive
        // --------------------------------------------------------------------

        private void DriveBottom(ActorState s)
        {
            if (bottomRoot != null)
                bottomRoot.localPosition = bottomBase;

            DriveHand(bottomLeftHand,  s.LeftHand);
            DriveHand(bottomRightHand, s.RightHand);
            DriveFoot(bottomLeftFoot,  bottomLeftFootRenderer,  s.LeftFoot);
            DriveFoot(bottomRightFoot, bottomRightFootRenderer, s.RightFoot);
        }

        // --------------------------------------------------------------------
        // Top drive — PostureBreak → spine lean
        // --------------------------------------------------------------------

        private void DriveTop(ActorState s)
        {
            if (topRoot != null)
                topRoot.localPosition = topBase;

            if (topSpine == null) return;

            // PostureBreak.X = lateral (signed, left negative), .Y = forward.
            // Euler: pitch = forward lean, yaw = lateral lean.
            topSpine.localRotation = Quaternion.Euler(
                s.PostureBreak.Y * posturePitchMaxDeg,
                s.PostureBreak.X * postureYawMaxDeg,
                0f);
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void DriveHand(Transform hand, HandFSM fsm)
        {
            if (hand == null) return;

            Vector3 target = fsm.State switch
            {
                HandState.Reaching or HandState.Contact or HandState.Gripped
                    => handReachOffset,
                _ => Vector3.zero,
            };
            hand.localPosition = Vector3.Lerp(hand.localPosition, target, handLerpSpeed);
        }

        private void DriveFoot(Transform foot, Renderer rend, FootFSM fsm)
        {
            if (foot == null) return;

            float targetY = fsm.State == FootState.Unlocked ? footUnlockedLiftY : 0f;
            var p = foot.localPosition;
            foot.localPosition = new Vector3(
                p.x,
                Mathf.Lerp(p.y, targetY, footLerpSpeed),
                p.z);

            if (rend == null) return;
            rend.material.color = fsm.State switch
            {
                FootState.Locked  => footLockedColor,
                FootState.Locking => footLockingColor,
                _                 => footUnlockedColor,
            };
        }
    }
}
