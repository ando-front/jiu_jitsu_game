// PLATFORM — IBJJF position-score readout. Standalone IMGUI overlay so it
// needs no UXML wiring: drop it on the BJJ_GameManager GameObject alongside
// BJJGameManager and it draws the running score top-left.
//
//   Blue:  N pts   (top / passer — pass = +3)
//   White: N pts   (bottom / guard — sweep = +2)
//
// A scoring event (GuardPassed / SweepCompleted) flashes a "+3!" / "+2!" pop
// next to the player who scored, fading over FlashDurationS.

using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJScoreHud : MonoBehaviour
    {
        private const float FlashDurationS = 1.2f;

        // Gi colours, matching BJJPoseRig: top wears blue, bottom wears white.
        private static readonly Color BlueGi  = new Color(0.30f, 0.45f, 1.0f);
        private static readonly Color WhiteGi = new Color(0.95f, 0.95f, 0.95f);

        private BJJGameManager _mgr;

        // Active flash pops, anchored to the blue / white score rows.
        private float _blueFlashUntil, _whiteFlashUntil;
        private string _blueFlashText = "", _whiteFlashText = "";

        private GUIStyle _label, _flash;

        void Awake() => _mgr = GetComponent<BJJGameManager>();

        void Update()
        {
            // Watch the sim's last-step events for a scoring change and arm the
            // matching flash pop.
            foreach (var ev in _mgr.LastStepEvents)
            {
                if (ev.Kind == SimEventKind.GuardPassed)
                {
                    _blueFlashText = $"+{BJJScoreOps.PassPoints}!";
                    _blueFlashUntil = Time.time + FlashDurationS;
                }
                else if (ev.Kind == SimEventKind.SweepCompleted)
                {
                    _whiteFlashText = $"+{BJJScoreOps.SweepPoints}!";
                    _whiteFlashUntil = Time.time + FlashDurationS;
                }
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            var score = _mgr.CurrentGameState.Score;

            DrawRow(10f, "Blue:",  score.Top,    BlueGi,  ref _blueFlashUntil,  _blueFlashText);
            DrawRow(40f, "White:", score.Bottom, WhiteGi, ref _whiteFlashUntil, _whiteFlashText);
        }

        private void DrawRow(float y, string name, int pts, Color color,
                             ref float flashUntil, string flashText)
        {
            _label.normal.textColor = color;
            GUI.Label(new Rect(12f, y, 200f, 26f), $"{name} {pts} pts", _label);

            float remain = flashUntil - Time.time;
            if (remain > 0f)
            {
                float a = Mathf.Clamp01(remain / FlashDurationS);
                // Pop rises and fades.
                float rise = (1f - a) * 14f;
                _flash.normal.textColor = new Color(1f, 0.9f, 0.3f, a);
                GUI.Label(new Rect(150f, y - rise, 80f, 26f), flashText, _flash);
            }
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold,
            };
            _flash = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 20,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
