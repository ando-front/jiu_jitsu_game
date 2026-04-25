// PLATFORM — Update hub. See docs/design/stage2_game_manager_v1.md §2.2.1.
//
// Each Unity Update tick:
//   1. Read BJJSessionLifecycle phase. If not Active, drain input edges that
//      drive the lifecycle (Pause toggle, Tutorial open) and return.
//   2. Otherwise, advance the round timer. If it expires, end the session.
//   3. Hand the gamestate to BJJInputProvider, poll hardware, and call
//      FixedStepOps.Advance with provider as the IStepProvider.
//   4. Walk SimEvents looking for SessionEnded; route to lifecycle.
//   5. Stash results so BJJDebugHud can read them next render.

using UnityEngine;

namespace BJJSimulator.Platform
{
    [RequireComponent(typeof(BJJSessionLifecycle))]
    [RequireComponent(typeof(BJJInputProvider))]
    public class BJJGameManager : MonoBehaviour
    {
        [SerializeField] private BJJDebugHud hud;
        [SerializeField] private float roundLengthSeconds = 5f * 60f;

        public BJJSessionLifecycle Lifecycle { get; private set; }
        public BJJInputProvider    Provider  { get; private set; }

        // Read-only snapshots for HUD.
        public GameState   CurrentGameState   { get; private set; }
        public SimEvent[]  LastStepEvents     { get; private set; } = System.Array.Empty<SimEvent>();
        public int         LastStepsRun       { get; private set; }
        public float       RoundElapsedMs     { get; private set; }
        public float       RoundRemainingMs   => Mathf.Max(0f, roundLengthSeconds * 1000f - RoundElapsedMs);

        private FixedStepState _simState;
        private double _lastUpdateTimeMs;

        // ---------------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------------

        void Awake()
        {
            Lifecycle = GetComponent<BJJSessionLifecycle>();
            Provider  = GetComponent<BJJInputProvider>();

            Lifecycle.OnRestartRequested       += HandleRestart;
            Lifecycle.OnScenarioLoadRequested  += HandleScenarioLoad;
            Lifecycle.OnPromptDismissed        += HandlePromptDismissed;

            ResetSim(GameStateOps.InitialGameState(NowMs()));
        }

        void OnDestroy()
        {
            if (Lifecycle != null)
            {
                Lifecycle.OnRestartRequested      -= HandleRestart;
                Lifecycle.OnScenarioLoadRequested -= HandleScenarioLoad;
                Lifecycle.OnPromptDismissed       -= HandlePromptDismissed;
            }
        }

        void Update()
        {
            // Sync role once per Update so prompt cycling shows up immediately.
            Provider.CurrentRole = Lifecycle.SelectedRole;

            // Compute real dt independent of Time.deltaTime so we can survive
            // long prompt/tutorial pauses (Stage 1 main.ts does the same with
            // performance.now()). nowMs is also passed to PollHardware so the
            // assembler's noisy-gamepad arbitration can timestamp keyboard
            // activity.
            long  now    = NowMs();
            float realDt = (float)(now - _lastUpdateTimeMs);
            _lastUpdateTimeMs = now;
            // Cap absurd dt (eg first frame after a long alt-tab).
            if (realDt < 0f || realDt > 250f) realDt = 0f;

            // Always poll hardware so meta keys (Pause, Tutorial, role cycle)
            // see fresh edges.
            Provider.PollHardware(now);

            // Sample one input frame so meta-key edges (BTN_PAUSE, BtnBase to
            // dismiss prompt, etc.) can be observed even when the sim isn't
            // advancing. Discard the intent — we only care about ButtonEdges.
            Provider.SetCurrentGameState(_simState.Game);
            var (probe, _, _) = Provider.Sample(now);

            switch (Lifecycle.CurrentPhase)
            {
                case LifecyclePhase.Prompt:
                {
                    // LS.x edge cycles role; BtnBase edge confirms.
                    HandleRoleCycle(probe);
                    if ((probe.ButtonEdges & ButtonBit.BtnBase) != ButtonBit.None)
                    {
                        Lifecycle.DismissPrompt();
                    }
                    return;
                }

                case LifecyclePhase.Tutorial:
                    // Pause sim while tutorial is open. Esc returns to Active
                    // (Stage 1 main.ts §144).
                    if ((probe.ButtonEdges & ButtonBit.BtnPause) != ButtonBit.None)
                        Lifecycle.ToggleTutorial();
                    return;

                case LifecyclePhase.Paused:
                    if ((probe.ButtonEdges & ButtonBit.BtnPause) != ButtonBit.None)
                        Lifecycle.SetPaused(false);
                    return;

                case LifecyclePhase.SessionEnded:
                    Lifecycle.TickSessionEnd(realDt);
                    if ((probe.ButtonEdges & ButtonBit.BtnBase) != ButtonBit.None)
                        Lifecycle.RestartSession();
                    return;

                case LifecyclePhase.Active:
                {
                    if ((probe.ButtonEdges & ButtonBit.BtnPause) != ButtonBit.None)
                    {
                        Lifecycle.SetPaused(true);
                        return;
                    }

                    RoundElapsedMs += realDt;
                    if (RoundElapsedMs >= roundLengthSeconds * 1000f)
                    {
                        Lifecycle.EndSession(LifecycleEndReason.RoundTimeUp);
                        return;
                    }

                    // Advance one (or more) fixed steps. Provider.Sample will
                    // be called inside Advance — it re-samples fresh inputs
                    // each step using the snapshot we already polled, so the
                    // probe above isn't wasted (LayerAState.PrevButtons would
                    // double-count edges otherwise — but Sample recomputes
                    // from the same snapshot which is exactly what we want).
                    var res = FixedStepOps.Advance(_simState, realDt, Provider);
                    _simState        = res.Next;
                    LastStepEvents   = res.Events;
                    LastStepsRun     = res.StepsRun;
                    CurrentGameState = res.Next.Game;

                    // Route SessionEnded events into the lifecycle.
                    foreach (var ev in res.Events)
                    {
                        if (ev.Kind == SimEventKind.SessionEnded)
                        {
                            Lifecycle.EndSessionFromSim(ev.SessionEndReason);
                            break;
                        }
                    }
                    return;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Lifecycle event handlers
        // ---------------------------------------------------------------------

        private void HandlePromptDismissed()
        {
            ResetSim(GameStateOps.InitialGameState(NowMs()));
            _lastUpdateTimeMs = NowMs();
        }

        private void HandleRestart()
        {
            ResetSim(GameStateOps.InitialGameState(NowMs()));
            _lastUpdateTimeMs = NowMs();
        }

        private void HandleScenarioLoad(ScenarioName name)
        {
            ResetSim(Scenarios.Build(name, NowMs()));
            _lastUpdateTimeMs = NowMs();
        }

        private void ResetSim(GameState fresh)
        {
            _simState = FixedStepState.Initial(NowMs(), fresh);
            CurrentGameState = fresh;
            LastStepEvents   = System.Array.Empty<SimEvent>();
            LastStepsRun     = 0;
            RoundElapsedMs   = 0f;
            Provider.ResetForTest(); // resets layer states + AI stash
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private long NowMs() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        private float _lastPromptLsX;
        private void HandleRoleCycle(InputFrame probe)
        {
            float x = probe.Ls.X;
            bool crossedLeft  = _lastPromptLsX > -0.6f && x <= -0.6f;
            bool crossedRight = _lastPromptLsX <  0.6f && x >=  0.6f;
            if (crossedLeft)  Lifecycle.CycleRole(-1);
            if (crossedRight) Lifecycle.CycleRole(+1);
            _lastPromptLsX = x;
        }
    }
}
