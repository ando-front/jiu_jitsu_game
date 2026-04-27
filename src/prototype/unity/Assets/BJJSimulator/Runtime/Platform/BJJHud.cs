// PLATFORM — UI Toolkit HUD driver.  Replaces the IMGUI BJJDebugHud.
// See docs/design/stage2_port_progress.md item 3.
//
// Requires a UIDocument component on the same GameObject with
// BJJHud.uxml assigned as its Source Asset in the Inspector.
// BJJGameManager must also be on the same GameObject.
//
// Panel layout mirrors the four IMGUI panels from BJJDebugHud:
//   top-left:     phase / Layer-A input / sim state
//   top-right:    static controls hint
//   bottom-left:  coach checklist (hideable via showCoach)
//   bottom-right: rolling 12-event log + lifecycle overlay

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJGameManager))]
    [RequireComponent(typeof(UIDocument))]
    public class BJJHud : MonoBehaviour
    {
        [SerializeField] private bool showLayerA = true;
        [SerializeField] private bool showCoach  = true;

        private BJJGameManager _mgr;
        private UIDocument     _doc;

        // Cached element references — queried once in Start.
        private Label         _lblPhase, _lblRound;
        private VisualElement _sectionLayerA, _sectionIntent;
        private Label         _lblLsRs, _lblTriggers, _lblButtons;
        private Label         _lblHip, _lblGrip;
        private Label         _lblLhand, _lblRhand, _lblFeet, _lblStamina, _lblBreak, _lblWindow, _lblCounter;
        private Label         _lblEventLog;
        private VisualElement _panelCoach;
        private Label         _lblFeetLocked, _lblHandsGripped, _lblBreakMag, _lblHipPush;
        private VisualElement _overlay;
        private Label         _lblOverlay;

        private const int EventLogLimit = 12;
        private readonly List<(long ms, SimEvent ev)> _eventLog = new(EventLogLimit + 1);

        void Awake()
        {
            _mgr = GetComponent<BJJGameManager>();
            _doc = GetComponent<UIDocument>();
        }

        void Start()
        {
            var root = _doc.rootVisualElement;

            _lblPhase      = root.Q<Label>("lbl-phase");
            _lblRound      = root.Q<Label>("lbl-round");
            _sectionLayerA = root.Q("section-layer-a");
            _sectionIntent = root.Q("section-intent");
            _lblLsRs       = root.Q<Label>("lbl-ls-rs");
            _lblTriggers   = root.Q<Label>("lbl-triggers");
            _lblButtons    = root.Q<Label>("lbl-buttons");
            _lblHip        = root.Q<Label>("lbl-hip");
            _lblGrip       = root.Q<Label>("lbl-grip");
            _lblLhand      = root.Q<Label>("lbl-lhand");
            _lblRhand      = root.Q<Label>("lbl-rhand");
            _lblFeet       = root.Q<Label>("lbl-feet");
            _lblStamina    = root.Q<Label>("lbl-stamina");
            _lblBreak      = root.Q<Label>("lbl-break");
            _lblWindow     = root.Q<Label>("lbl-window");
            _lblCounter    = root.Q<Label>("lbl-counter");
            _lblEventLog   = root.Q<Label>("lbl-event-log");
            _panelCoach    = root.Q("panel-bottom-left");
            _lblFeetLocked   = root.Q<Label>("lbl-feet-locked");
            _lblHandsGripped = root.Q<Label>("lbl-hands-gripped");
            _lblBreakMag     = root.Q<Label>("lbl-break-mag");
            _lblHipPush      = root.Q<Label>("lbl-hip-push");
            _overlay    = root.Q("overlay");
            _lblOverlay = root.Q<Label>("lbl-overlay");
        }

        void Update()
        {
            foreach (var ev in _mgr.LastStepEvents)
            {
                if (IsSuppressed(ev.Kind)) continue;
                _eventLog.Insert(0, (_mgr.CurrentGameState.NowMs, ev));
                if (_eventLog.Count > EventLogLimit) _eventLog.RemoveAt(_eventLog.Count - 1);
            }

            UpdateTopLeft();
            UpdateBottomLeft();
            UpdateBottomRight();
        }

        // --------------------------------------------------------------------
        // Panels
        // --------------------------------------------------------------------

        private void UpdateTopLeft()
        {
            var l = _mgr.Lifecycle;
            var p = _mgr.Provider;
            var g = _mgr.CurrentGameState;

            _lblPhase.text = $"phase {l.CurrentPhase}  role {l.SelectedRole}  scenario {l.ActiveScenario?.ToString() ?? "·"}";
            _lblRound.text = $"round {FormatRound(_mgr.RoundRemainingMs)}  steps/upd {_mgr.LastStepsRun}";

            bool hasFrame = showLayerA && p.LastFrame.HasValue;
            _sectionLayerA.EnableInClassList("hidden", !hasFrame);
            if (hasFrame)
            {
                var f = p.LastFrame.Value;
                _lblLsRs.text    = $"ls ({f.Ls.X:0.00}, {f.Ls.Y:0.00})  rs ({f.Rs.X:0.00}, {f.Rs.Y:0.00})";
                _lblTriggers.text = $"triggers L={f.LTrigger:0.00}  R={f.RTrigger:0.00}  device={f.DeviceKind}";
                _lblButtons.text  = $"buttons {f.Buttons}  edges {f.ButtonEdges}";
            }

            bool hasIntent = p.LastIntent.HasValue;
            _sectionIntent.EnableInClassList("hidden", !hasIntent);
            if (hasIntent)
            {
                var i = p.LastIntent.Value;
                _lblHip.text  = $"hip θ={i.Hip.HipAngleTarget:0.00}  push={i.Hip.HipPush:0.00}  lat={i.Hip.HipLateral:0.00}";
                _lblGrip.text = $"L {i.Grip.LHandTarget} ({i.Grip.LGripStrength:0.00})  R {i.Grip.RHandTarget} ({i.Grip.RGripStrength:0.00})";
            }

            _lblLhand.text   = $"L-hand {g.Bottom.LeftHand.State} → {g.Bottom.LeftHand.Target}";
            _lblRhand.text   = $"R-hand {g.Bottom.RightHand.State} → {g.Bottom.RightHand.Target}";
            _lblFeet.text    = $"feet L={g.Bottom.LeftFoot.State} R={g.Bottom.RightFoot.State}";
            _lblStamina.text = $"stamina B={g.Bottom.Stamina:0.00} T={g.Top.Stamina:0.00}";
            _lblBreak.text   = $"break ({g.Top.PostureBreak.X:0.00}, {g.Top.PostureBreak.Y:0.00})";
            _lblWindow.text  = $"window {g.JudgmentWindow.State}  initiative {g.Control.Initiative}";
            _lblCounter.text = $"counter {g.CounterWindow.State}";
        }

        private void UpdateBottomLeft()
        {
            _panelCoach.EnableInClassList("hidden", !showCoach);
            if (!showCoach) return;

            var g = _mgr.CurrentGameState;
            _lblFeetLocked.text   = $"feet locked: L={g.Bottom.LeftFoot.State == FootState.Locked} R={g.Bottom.RightFoot.State == FootState.Locked}";
            _lblHandsGripped.text = $"hands gripped: L={g.Bottom.LeftHand.State == HandState.Gripped} R={g.Bottom.RightHand.State == HandState.Gripped}";
            _lblBreakMag.text     = $"break magnitude: {Mathf.Sqrt(g.Top.PostureBreak.X * g.Top.PostureBreak.X + g.Top.PostureBreak.Y * g.Top.PostureBreak.Y):0.00}";
            _lblHipPush.text      = $"sustainedHipPush: {g.Sustained.HipPushMs:F0}ms / 300";
        }

        private void UpdateBottomRight()
        {
            var g   = _mgr.CurrentGameState;
            long now = g.NowMs;
            var sb   = new StringBuilder(512);
            foreach (var (ms, ev) in _eventLog)
            {
                float ageS = Mathf.Max(0f, (now - ms) / 1000f);
                sb.AppendLine($"-{ageS:0.0}s  {ev.Kind}");
            }
            _lblEventLog.text = sb.ToString();

            var phase = _mgr.Lifecycle.CurrentPhase;
            bool overlayVisible =
                phase == LifecyclePhase.Prompt      ||
                phase == LifecyclePhase.Paused      ||
                phase == LifecyclePhase.Tutorial    ||
                phase == LifecyclePhase.SessionEnded;

            _overlay.EnableInClassList("hidden", !overlayVisible);
            if (overlayVisible)
            {
                _lblOverlay.text = phase switch
                {
                    LifecyclePhase.Prompt       => $"ROLE: {_mgr.Lifecycle.SelectedRole}\n\nLS左右で切替 / Spaceで決定",
                    LifecyclePhase.Paused       => "PAUSED\n\nEscで再開",
                    LifecyclePhase.Tutorial     => "TUTORIAL\n\n(see docs)\n\nHで戻る",
                    LifecyclePhase.SessionEnded => $"SESSION END: {_mgr.Lifecycle.EndReason}\n\nSpaceでリスタート",
                    _                           => "",
                };
            }
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static bool IsSuppressed(SimEventKind k) =>
            k == SimEventKind.WindowOpen        || k == SimEventKind.WindowClosed        ||
            k == SimEventKind.CounterWindowOpen || k == SimEventKind.CounterWindowClosed ||
            k == SimEventKind.FootLockingStarted;

        private static string FormatRound(float remainMs)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(remainMs / 1000f));
            return $"{total / 60}:{(total % 60):D2}";
        }
    }
}
