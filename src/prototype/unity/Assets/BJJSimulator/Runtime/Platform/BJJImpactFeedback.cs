// PLATFORM — event-driven flash, window aberration, and camera shake.
// See docs/design/stage2_port_progress.md (setWindowTint / pulseFlash item).
//
// Maps main.ts §700-742 scene effects to three URP/camera overrides:
//
//   pulseFlash(color, durationMs)
//     → ColorAdjustments.colorFilter tint that decays back to white.
//
//   setWindowTint(strength)
//     → ChromaticAberration.intensity driven by JudgmentWindow / CounterWindow
//       state each frame (complements the Vignette in BJJVolumeController).
//
//   pulseShake(amplitude, durationMs)
//     → Additive camera localPosition offset with exponential decay.
//       Last frame's offset is reversed before the new one is applied so
//       the camera's base position is always preserved.
//
// Stage 1 flash palette (hex → float):
//   TechniqueConfirmed  0xffd98c  amber         (1.00, 0.85, 0.55)
//   CounterConfirmed    0x9ec9ff  cool blue      (0.62, 0.79, 1.00)
//   WindowOpening       0xfff2d0  soft warm      (1.00, 0.95, 0.82)
//   GuardOpened         0xff7070  red            (1.00, 0.44, 0.44)
//   PassStarted         0x9ec9ff  cool blue      (0.62, 0.79, 1.00)
//   PassSucceeded       0x9ec9ff  cool blue      (0.62, 0.79, 1.00)
//   PassFailed          0xffb080  warm orange    (1.00, 0.69, 0.50)
//
// Setup in the Inspector:
//   1. Assign the Main Camera to [mainCamera].
//   2. Assign the same Global Volume used by BJJVolumeController to [globalVolume].
//      The Volume Profile must include ColorAdjustments and ChromaticAberration
//      override entries (Create Override → Color Adjustments / Chromatic Aberration).
//   Null references silently disable the corresponding effect.

#if BJJ_URP
using UnityEngine.Rendering.Universal;
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJImpactFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Volume globalVolume;

        [Header("Flash (ColorAdjustments.colorFilter)")]
        [Tooltip("Lerp speed from flash color back to white each LateUpdate.")]
        [SerializeField, Range(0.05f, 1f)] private float flashDecaySpeed = 0.18f;

        [Header("Window tint (ChromaticAberration.intensity)")]
        [Tooltip("Aberration intensity when judgment window is fully open.")]
        [SerializeField, Range(0f, 1f)]   private float caAtWindowOpen  = 0.45f;
        [Tooltip("Lerp speed toward target aberration per LateUpdate.")]
        [SerializeField, Range(0.01f, 1f)] private float caLerpSpeed    = 0.10f;

        [Header("Camera shake")]
        [Tooltip("Fraction of shake amplitude removed each second (higher = sharper).")]
        [SerializeField, Range(1f, 30f)] private float shakeDecayRate = 10f;

        // ------------------------------------------------------------------

        private BJJGameManager _mgr;

#if BJJ_URP
        private ColorAdjustments     _ca;
        private ChromaticAberration  _chromatic;
#endif

        // Flash state
        private float _flashRemaining;
        private float _flashTotal = 1f;
        private Color _flashColor = Color.white;

        // Shake state
        private struct ActiveShake
        {
            public float RemainingMs;
            public float TotalMs;
            public float Amplitude;
        }
        private readonly List<ActiveShake> _shakes = new(8);
        private Vector3 _appliedShakeOffset;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        void Awake()
        {
            _mgr = GetComponent<BJJGameManager>();
#if BJJ_URP
            if (globalVolume?.profile != null)
            {
                globalVolume.profile.TryGet(out _ca);
                globalVolume.profile.TryGet(out _chromatic);
            }
#endif
        }

        void LateUpdate()
        {
            foreach (var ev in _mgr.LastStepEvents)
                DispatchEvent(ev.Kind);

#if BJJ_URP
            TickWindowChromatic();
            TickFlash();
#endif
            TickShake();
        }

        // ------------------------------------------------------------------
        // Event dispatch
        // ------------------------------------------------------------------

        private void DispatchEvent(SimEventKind kind)
        {
            switch (kind)
            {
                case SimEventKind.TechniqueConfirmed:
                    TriggerFlash(new Color(1.00f, 0.85f, 0.55f), 220f);
                    TriggerShake(0.15f, 320f);
                    break;
                case SimEventKind.CounterConfirmed:
                    TriggerFlash(new Color(0.62f, 0.79f, 1.00f), 220f);
                    TriggerShake(0.15f, 320f);
                    break;
                case SimEventKind.WindowOpening:
                case SimEventKind.CounterWindowOpening:
                    TriggerFlash(new Color(1.00f, 0.95f, 0.82f), 80f);
                    break;
                case SimEventKind.GuardOpened:
                    TriggerFlash(new Color(1.00f, 0.44f, 0.44f), 360f);
                    break;
                case SimEventKind.PassStarted:
                    TriggerFlash(new Color(0.62f, 0.79f, 1.00f), 140f);
                    break;
                case SimEventKind.PassSucceeded:
                    TriggerFlash(new Color(0.62f, 0.79f, 1.00f), 480f);
                    TriggerShake(0.20f, 500f);
                    break;
                case SimEventKind.PassFailed:
                    TriggerFlash(new Color(1.00f, 0.69f, 0.50f), 260f);
                    break;
                case SimEventKind.HandGripped:
                    TriggerShake(0.04f, 120f);
                    break;
                case SimEventKind.HandParried:
                    TriggerShake(0.06f, 160f);
                    break;
                case SimEventKind.HandGripBroken:
                    TriggerShake(0.05f, 140f);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Flash
        // ------------------------------------------------------------------

        private void TriggerFlash(Color color, float durationMs)
        {
            if (durationMs <= _flashRemaining) return;
            _flashColor     = color;
            _flashRemaining = durationMs;
            _flashTotal     = durationMs;
        }

#if BJJ_URP
        private void TickFlash()
        {
            if (_ca == null) return;

            Color target;
            if (_flashRemaining > 0f)
            {
                _flashRemaining -= Time.unscaledDeltaTime * 1000f;
                float t = Mathf.Clamp01(_flashRemaining / Mathf.Max(1f, _flashTotal));
                target = Color.Lerp(Color.white, _flashColor, t);
            }
            else
            {
                target = Color.white;
            }

            _ca.colorFilter.Override(
                Color.Lerp(_ca.colorFilter.value, target, flashDecaySpeed));
        }

        // ------------------------------------------------------------------
        // Window chromatic aberration
        // ------------------------------------------------------------------

        private void TickWindowChromatic()
        {
            if (_chromatic == null) return;

            var g = _mgr.CurrentGameState;
            bool judgeOpen   = g.JudgmentWindow.State  == JudgmentWindowState.Open;
            bool counterOpen = g.CounterWindow.State   == CounterWindowState.Open;
            bool anyOpening  = g.JudgmentWindow.State  == JudgmentWindowState.Opening ||
                               g.CounterWindow.State   == CounterWindowState.Opening;

            float target =
                (judgeOpen || counterOpen) ? caAtWindowOpen :
                anyOpening                 ? caAtWindowOpen * 0.5f :
                                             0f;

            _chromatic.intensity.Override(
                Mathf.Lerp(_chromatic.intensity.value, target, caLerpSpeed));
        }
#endif

        // ------------------------------------------------------------------
        // Camera shake
        // ------------------------------------------------------------------

        private void TriggerShake(float amplitude, float durationMs)
        {
            _shakes.Add(new ActiveShake
            {
                RemainingMs = durationMs,
                TotalMs     = durationMs,
                Amplitude   = amplitude,
            });
        }

        private void TickShake()
        {
            if (mainCamera == null) return;

            // Reverse last frame's offset before recomputing.
            mainCamera.transform.localPosition -= _appliedShakeOffset;

            float dtMs = Time.unscaledDeltaTime * 1000f;
            var   offset = Vector3.zero;

            for (int i = _shakes.Count - 1; i >= 0; i--)
            {
                var s = _shakes[i];
                s.RemainingMs -= dtMs;
                if (s.RemainingMs <= 0f)
                {
                    _shakes.RemoveAt(i);
                    continue;
                }
                _shakes[i] = s;
                float t   = s.RemainingMs / s.TotalMs;
                float amp = s.Amplitude * t;
                offset += new Vector3(
                    (Random.value * 2f - 1f) * amp,
                    (Random.value * 2f - 1f) * amp * 0.5f,
                    0f);
            }

            _appliedShakeOffset = offset;
            mainCamera.transform.localPosition += _appliedShakeOffset;
        }
    }
}
