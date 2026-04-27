// PLATFORM — gate / overlay state machine for the Unity host.
// See docs/design/stage2_game_manager_v1.md §2.2.3.
//
// Holds the exclusive phase (Prompt / Tutorial / Active / Paused / SessionEnded)
// and side data (selected role, active scenario, end reason). BJJGameManager
// reads CurrentPhase each Update and only calls FixedStepOps.Advance when
// CurrentPhase == Active. Mutations all go through the explicit method API so
// regression diff stays small.

using System;
using UnityEngine;

namespace BJJSimulator.Platform
{
    public enum LifecyclePhase
    {
        Prompt,
        Tutorial,
        Active,
        Paused,
        SessionEnded,
    }

    public enum PlayerRole { Bottom, Top, Spectate }

    // Superset of GameState.SessionEndReason. RoundTimeUp lives only on the
    // host side — Stage 1 main.ts §285 routes the round timer through the
    // session-end overlay path but the Pure layer never emits it.
    public enum LifecycleEndReason
    {
        PassSuccess,
        TechniqueFinished,
        GuardOpened,
        RoundTimeUp,
    }

    public class BJJSessionLifecycle : MonoBehaviour
    {
        // ---------------------------------------------------------------------
        // Read-only state
        // ---------------------------------------------------------------------

        public LifecyclePhase     CurrentPhase     { get; private set; } = LifecyclePhase.Prompt;
        public PlayerRole         SelectedRole     { get; private set; } = PlayerRole.Bottom;
        public ScenarioName?      ActiveScenario   { get; private set; } = null;
        public LifecycleEndReason? EndReason       { get; private set; } = null;

        // Wall-clock ms since the SessionEnded overlay appeared. Used by
        // Spectate auto-restart (main.ts §304).
        public float SessionEndElapsedMs { get; private set; } = 0f;
        public const float SpectateAutoRestartMs = 2000f;

        // ---------------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------------
        // GameManager subscribes to these to actually rebuild _simState.
        // Lifecycle itself never touches the Pure layer.

        public event Action OnRestartRequested;
        public event Action<ScenarioName> OnScenarioLoadRequested;
        public event Action OnPromptDismissed;

        // ---------------------------------------------------------------------
        // Phase transitions
        // ---------------------------------------------------------------------

        public void DismissPrompt()
        {
            if (CurrentPhase != LifecyclePhase.Prompt) return;
            CurrentPhase = LifecyclePhase.Active;
            OnPromptDismissed?.Invoke();
        }

        public void ToggleTutorial()
        {
            if (CurrentPhase == LifecyclePhase.Tutorial)
            {
                // Returning from tutorial drops back to whatever was running
                // before. v1 always returns to Active — Pause is not preserved
                // across tutorial open/close because Stage 1 also clobbers it.
                CurrentPhase = LifecyclePhase.Active;
            }
            else if (CurrentPhase == LifecyclePhase.Active || CurrentPhase == LifecyclePhase.Paused)
            {
                CurrentPhase = LifecyclePhase.Tutorial;
            }
            // Prompt / SessionEnded: ignore — tutorial is meaningless there.
        }

        public void SetPaused(bool paused)
        {
            if (CurrentPhase == LifecyclePhase.Active && paused)
            {
                CurrentPhase = LifecyclePhase.Paused;
            }
            else if (CurrentPhase == LifecyclePhase.Paused && !paused)
            {
                CurrentPhase = LifecyclePhase.Active;
            }
        }

        public void EndSession(LifecycleEndReason reason)
        {
            if (CurrentPhase == LifecyclePhase.SessionEnded) return;
            CurrentPhase = LifecyclePhase.SessionEnded;
            EndReason = reason;
            SessionEndElapsedMs = 0f;
        }

        // GameState.SessionEndReason → LifecycleEndReason adapter.
        public void EndSessionFromSim(SessionEndReason reason)
        {
            var mapped = reason switch
            {
                SessionEndReason.PassSuccess        => LifecycleEndReason.PassSuccess,
                SessionEndReason.TechniqueFinished  => LifecycleEndReason.TechniqueFinished,
                SessionEndReason.GuardOpened        => LifecycleEndReason.GuardOpened,
                _                                   => LifecycleEndReason.GuardOpened,
            };
            EndSession(mapped);
        }

        public void RestartSession()
        {
            CurrentPhase = LifecyclePhase.Active;
            ActiveScenario = null;
            EndReason = null;
            SessionEndElapsedMs = 0f;
            OnRestartRequested?.Invoke();
        }

        public void LoadScenario(ScenarioName name)
        {
            CurrentPhase = LifecyclePhase.Active;
            ActiveScenario = name;
            EndReason = null;
            SessionEndElapsedMs = 0f;
            OnScenarioLoadRequested?.Invoke(name);
        }

        public void CycleRole(int direction)
        {
            if (CurrentPhase != LifecyclePhase.Prompt) return;
            int n = 3; // PlayerRole has 3 values
            int idx = ((int)SelectedRole + direction % n + n) % n;
            SelectedRole = (PlayerRole)idx;
        }

        // GameManager calls this each Update while in SessionEnded phase so
        // Spectate can auto-restart without a button press.
        public void TickSessionEnd(float realDtMs)
        {
            if (CurrentPhase != LifecyclePhase.SessionEnded) return;
            SessionEndElapsedMs += realDtMs;
            if (SelectedRole == PlayerRole.Spectate &&
                SessionEndElapsedMs >= SpectateAutoRestartMs)
            {
                RestartSession();
            }
        }
    }
}
