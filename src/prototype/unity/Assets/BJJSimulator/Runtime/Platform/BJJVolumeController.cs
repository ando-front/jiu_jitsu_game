// PLATFORM — URP Global Volume driver.
// See docs/design/stage2_port_progress.md item 2 (URP Volume profile).
//
// Reads CurrentGameState from BJJGameManager each LateUpdate and overrides
// two post-process effect parameters so game state is readable through the
// image without a HUD (Visual Pillar §5.4):
//
//   Stamina → WhiteBalance.temperature warm shift
//     Full stamina (1.0) = neutral temperature (0).
//     Zero stamina (0.0) = warm orange tint (+warmShiftMax, default +40).
//
//   Judgment / Counter window open → Vignette intensity pulse
//     Window closed = vignetteBase (default 0).
//     Judgment open = vignetteJudge (default 0.38, amber colour).
//     Counter open  = vignetteCounter (default 0.38, red colour).
//
// Setup in the Inspector:
//   1. Add a Global Volume to the scene (Post-process layer).
//   2. Create a Volume Profile Asset with WhiteBalance and Vignette overrides.
//   3. Assign the Volume to the [globalVolume] slot on this component.
//   Null globalVolume silently disables all overrides.

#if BJJ_URP
using UnityEngine.Rendering.Universal;
#endif

using UnityEngine;
using UnityEngine.Rendering;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJVolumeController : MonoBehaviour
    {
        [Header("Global Volume (assign in Inspector)")]
        [SerializeField] private Volume globalVolume;

        [Header("Stamina → WhiteBalance temperature")]
        [Tooltip("Temperature override when Bottom stamina = 1.0 (neutral = 0).")]
        [SerializeField, Range(-100f, 100f)] private float tempAtFullStamina  =   0f;
        [Tooltip("Temperature override when Bottom stamina = 0.0 (warm orange).")]
        [SerializeField, Range(-100f, 100f)] private float tempAtZeroStamina  =  40f;
        [Tooltip("Lerp speed toward target temperature per LateUpdate.")]
        [SerializeField, Range(0.01f, 1f)]   private float tempLerpSpeed      = 0.08f;

        [Header("Judgment / Counter window → Vignette")]
        [SerializeField, Range(0f, 1f)] private float vignetteBase     = 0.00f;
        [SerializeField, Range(0f, 1f)] private float vignetteJudge    = 0.38f;
        [SerializeField, Range(0f, 1f)] private float vignetteCounter  = 0.38f;
        [SerializeField, Range(0.01f, 1f)] private float vignetteLerpSpeed = 0.12f;
        [SerializeField] private Color judgeColor   = new Color(0.90f, 0.60f, 0.05f);
        [SerializeField] private Color counterColor = new Color(0.85f, 0.10f, 0.10f);

        // --------------------------------------------------------------------
        // Runtime
        // --------------------------------------------------------------------

        private BJJGameManager _manager;

#if BJJ_URP
        private WhiteBalance    _wb;
        private Vignette        _vig;
#endif

        void Awake()
        {
            _manager = GetComponent<BJJGameManager>();
            BindVolumeComponents();
        }

        void LateUpdate()
        {
#if BJJ_URP
            if (_wb == null && _vig == null) return;
            var g = _manager.CurrentGameState;
            DriveStamina(g.Bottom.Stamina);
            DriveWindows(g);
#endif
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private void BindVolumeComponents()
        {
#if BJJ_URP
            if (globalVolume == null || globalVolume.profile == null) return;
            globalVolume.profile.TryGet(out _wb);
            globalVolume.profile.TryGet(out _vig);
#endif
        }

#if BJJ_URP
        private void DriveStamina(float stamina)
        {
            if (_wb == null) return;
            float target = Mathf.Lerp(tempAtZeroStamina, tempAtFullStamina, Mathf.Clamp01(stamina));
            _wb.temperature.Override(
                Mathf.Lerp(_wb.temperature.value, target, tempLerpSpeed));
        }

        private void DriveWindows(GameState g)
        {
            if (_vig == null) return;

            bool judgeOpen   = g.JudgmentWindow.State == JudgmentWindowState.Open;
            bool counterOpen = g.CounterWindow.State  == CounterWindowState.Open;

            float targetIntensity;
            Color targetColor;
            if (counterOpen)
            {
                targetIntensity = vignetteCounter;
                targetColor     = counterColor;
            }
            else if (judgeOpen)
            {
                targetIntensity = vignetteJudge;
                targetColor     = judgeColor;
            }
            else
            {
                targetIntensity = vignetteBase;
                targetColor     = Color.black;
            }

            _vig.intensity.Override(
                Mathf.Lerp(_vig.intensity.value, targetIntensity, vignetteLerpSpeed));
            _vig.color.Override(
                Color.Lerp(_vig.color.value, targetColor, vignetteLerpSpeed));
        }
#endif
    }
}
