// PLATFORM — IMGUI debug HUD. v1 plain-text only.
// See docs/design/stage2_game_manager_v1.md §2.2.4.
//
// Mirrors the four panels Stage 1 main.ts paints:
//   - top-left:    debug HUD text (renderHud)
//   - bottom-left: coach checklist (renderCoach)
//   - bottom-right: event log (renderEventLog)
//   - top-right:   input live (renderInputLive) + tutorial hint
//
// IMGUI is not the production UI — that becomes UI Toolkit in a later phase.

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    public class BJJDebugHud : MonoBehaviour
    {
        [SerializeField] private bool showLayerA = true;
        [SerializeField] private bool showCoach  = true;
        [SerializeField] private int  fontSize   = 12;

        private BJJGameManager _mgr;
        private GUIStyle       _style;

        // Rolling event log (mirrors main.ts §215).
        private const int EventLogLimit = 12;
        private readonly List<(long ms, SimEvent ev)> _eventLog = new(EventLogLimit + 1);

        void Awake()
        {
            _mgr = GetComponent<BJJGameManager>();
        }

        void Update()
        {
            // Drain new events into the rolling log.
            foreach (var ev in _mgr.LastStepEvents)
            {
                if (IsSuppressed(ev.Kind)) continue;
                _eventLog.Insert(0, (_mgr.CurrentGameState.NowMs, ev));
                if (_eventLog.Count > EventLogLimit) _eventLog.RemoveAt(_eventLog.Count - 1);
            }
        }

        void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    normal = { textColor = new Color(0.85f, 0.85f, 0.88f) },
                    wordWrap = false,
                };
            }

            DrawTopLeft();
            DrawTopRight();
            DrawBottomRight();
            if (showCoach) DrawBottomLeft();
        }

        // -- Panels ------------------------------------------------------------

        private void DrawTopLeft()
        {
            var sb = new StringBuilder(2048);
            var l  = _mgr.Lifecycle;
            var p  = _mgr.Provider;
            var g  = _mgr.CurrentGameState;

            var scenarioStr = l.ActiveScenario?.ToString() ?? "·";
            sb.AppendLine($"phase {l.CurrentPhase}  role {l.SelectedRole}  scenario {scenarioStr}");
            sb.AppendLine($"round {FormatRound(_mgr.RoundRemainingMs)}  steps/upd {_mgr.LastStepsRun}");

            if (showLayerA && p.LastFrame.HasValue)
            {
                var f = p.LastFrame.Value;
                sb.AppendLine("── Layer A ──");
                sb.AppendLine($"ls ({f.Ls.X:0.00}, {f.Ls.Y:0.00})  rs ({f.Rs.X:0.00}, {f.Rs.Y:0.00})");
                sb.AppendLine($"triggers L={f.LTrigger:0.00}  R={f.RTrigger:0.00}  device={f.DeviceKind}");
                sb.AppendLine($"buttons {f.Buttons}  edges {f.ButtonEdges}");
            }

            if (p.LastIntent.HasValue)
            {
                var i = p.LastIntent.Value;
                sb.AppendLine("── Intent (attacker) ──");
                sb.AppendLine($"hip θ={i.Hip.HipAngleTarget:0.00}  push={i.Hip.HipPush:0.00}  lat={i.Hip.HipLateral:0.00}");
                sb.AppendLine($"L {i.Grip.LHandTarget} ({i.Grip.LGripStrength:0.00})  R {i.Grip.RHandTarget} ({i.Grip.RGripStrength:0.00})");
            }

            sb.AppendLine("── Sim ──");
            sb.AppendLine($"L-hand {g.Bottom.LeftHand.State} → {g.Bottom.LeftHand.Target}");
            sb.AppendLine($"R-hand {g.Bottom.RightHand.State} → {g.Bottom.RightHand.Target}");
            sb.AppendLine($"feet L={g.Bottom.LeftFoot.State} R={g.Bottom.RightFoot.State}");
            sb.AppendLine($"stamina B={g.Bottom.Stamina:0.00} T={g.Top.Stamina:0.00}");
            sb.AppendLine($"break ({g.Top.PostureBreak.X:0.00}, {g.Top.PostureBreak.Y:0.00})");
            sb.AppendLine($"window {g.JudgmentWindow.State}  initiative {g.Control.Initiative}");
            sb.AppendLine($"counter {g.CounterWindow.State}");

            GUI.Box(new Rect(8, 8, 460, 320), GUIContent.none);
            GUI.Label(new Rect(14, 14, 450, 310), sb.ToString(), _style);
        }

        private void DrawTopRight()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("[H] tutorial   [Esc] pause   [1-7] scenario   [0] reset");
            sb.AppendLine("Bottom: WASD腰  ↑↓←→ザ狙い  F/J トリガー  R/U 足");
            sb.AppendLine("Space=A  X=B  C=Y  V=X");
            float w = 360f, h = 70f, x = Screen.width - w - 8f, y = 8f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUI.Label(new Rect(x + 6, y + 6, w - 12, h - 12), sb.ToString(), _style);
        }

        private void DrawBottomLeft()
        {
            // Coach checklist — minimal v1: just count of currently-satisfied
            // techniques. Stage 1's full per-technique checklist will move
            // here in the UI Toolkit phase.
            var g = _mgr.CurrentGameState;
            var sb = new StringBuilder(256);
            sb.AppendLine("Coach (v1 minimal)");
            sb.AppendLine($"feet locked: L={g.Bottom.LeftFoot.State == FootState.Locked} R={g.Bottom.RightFoot.State == FootState.Locked}");
            sb.AppendLine($"hands gripped: L={g.Bottom.LeftHand.State == HandState.Gripped} R={g.Bottom.RightHand.State == HandState.Gripped}");
            sb.AppendLine($"break magnitude: {Mathf.Sqrt(g.Top.PostureBreak.X * g.Top.PostureBreak.X + g.Top.PostureBreak.Y * g.Top.PostureBreak.Y):0.00}");
            sb.AppendLine($"sustainedHipPush: {g.Sustained.HipPushMs:F0}ms / 300");
            float w = 280f, h = 110f, x = 8f, y = Screen.height - h - 8f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUI.Label(new Rect(x + 6, y + 6, w - 12, h - 12), sb.ToString(), _style);
        }

        private void DrawBottomRight()
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("Event log (latest 12)");
            long now = _mgr.CurrentGameState.NowMs;
            foreach (var (ms, ev) in _eventLog)
            {
                float ageS = Mathf.Max(0f, (now - ms) / 1000f);
                sb.AppendLine($"-{ageS:0.0}s  {ev.Kind}");
            }
            float w = 280f, h = 240f, x = Screen.width - w - 8f, y = Screen.height - h - 8f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            GUI.Label(new Rect(x + 6, y + 6, w - 12, h - 12), sb.ToString(), _style);

            // Lifecycle overlays — drawn over everything else.
            if (_mgr.Lifecycle.CurrentPhase == LifecyclePhase.Prompt)
                DrawCenteredOverlay($"ROLE: {_mgr.Lifecycle.SelectedRole}\n\nLS左右で切替 / Spaceで決定");
            else if (_mgr.Lifecycle.CurrentPhase == LifecyclePhase.Paused)
                DrawCenteredOverlay("PAUSED\n\nEscで再開");
            else if (_mgr.Lifecycle.CurrentPhase == LifecyclePhase.Tutorial)
                DrawCenteredOverlay("TUTORIAL\n\n(see docs)\n\nHで戻る");
            else if (_mgr.Lifecycle.CurrentPhase == LifecyclePhase.SessionEnded)
                DrawCenteredOverlay($"SESSION END: {_mgr.Lifecycle.EndReason}\n\nSpaceでリスタート");
        }

        private void DrawCenteredOverlay(string text)
        {
            float w = 480f, h = 160f;
            float x = (Screen.width  - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            var s = new GUIStyle(_style)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = fontSize + 4,
            };
            GUI.Label(new Rect(x, y, w, h), text, s);
        }

        // -- Helpers -----------------------------------------------------------

        private static bool IsSuppressed(SimEventKind k) =>
            k == SimEventKind.WindowOpen || k == SimEventKind.WindowClosed ||
            k == SimEventKind.CounterWindowOpen || k == SimEventKind.CounterWindowClosed ||
            k == SimEventKind.FootLockingStarted;

        private static string FormatRound(float remainMs)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(remainMs / 1000f));
            return $"{total / 60}:{(total % 60):D2}";
        }
    }
}
