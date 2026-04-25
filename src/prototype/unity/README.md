# src/prototype/unity/ — Stage 2 Unity Project

Stage 2 porting target. Implements the same logic as the Stage 1 TS prototype
in Unity C#, targeting macOS (Apple Silicon) and Windows PC without a
dedicated GPU requirement.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Unity Editor | **6000.0 LTS** (Unity 6) | Apple Silicon native on macOS |
| Git LFS | any | for future binary assets |

## Opening the Project

1. Install **Unity Hub** → install Unity 6000.0 LTS with **Mac** and/or **Windows** build support.
2. In Unity Hub → **Add project from disk** → select this folder (`src/prototype/unity/`).
3. Unity will import packages and generate the `Library/` directory on first open.

## Running Tests

**Window → General → Test Runner → EditMode** → Run All.

The EditMode suite mirrors the Stage 1 Vitest suite case-for-case. As of
2026-04-25 there are **~270 [Test] methods** covering all Pure-layer modules
(state FSMs, input layers, sim driver, AI). A regression on either Stage 1 TS
or Stage 2 C# side produces a named, greppable failure.

## Project Layout

```
Assets/
  BJJSimulator/
    Runtime/
      BJJSimulator.asmdef        # main assembly (refs Unity.InputSystem)
      AssemblyInfo.cs            # InternalsVisibleTo for tests
      Core/                      # shared enums (GripZone, HandState, …)
      State/                     # FSM structs + Ops (Hand/Foot/Posture/etc.)
      Input/                     # Layer A/B/D + Defense + InputTransform
                                 # + BJJInputActions.inputactions
      Sim/                       # FixedStepOps driver
      AI/                        # OpponentAI
      Platform/                  # MonoBehaviours (host layer)
        BJJSessionLifecycle.cs   # phase/role/scenario gate machine
        BJJInputProvider.cs      # IStepProvider, polls New Input System
        BJJGameManager.cs        # Update hub
        BJJDebugHud.cs           # IMGUI text overlay
    Tests/
      BJJSimulatorTests.asmdef   # test assembly (Editor-only)
      EditMode/
        *Test.cs                 # NUnit unit tests
        Scenario/                # NUnit scenario integration tests
Packages/
  manifest.json                  # com.unity.inputsystem 1.11.2 etc.
ProjectSettings/
  ProjectSettings.asset
```

## Running the Game (first-time setup)

The Pure logic + MonoBehaviour wiring is committed, but Unity needs to import
assets and you need to assemble one Scene by hand. Steps below assume a fresh
clone.

### 1. Open the project

Unity Hub → **Add project from disk** → `src/prototype/unity/` → open with
**Unity 6000.0 LTS**. First import takes 1–3 minutes.

### 2. Confirm packages installed

**Window → Package Manager** → verify `Input System` (1.11.2) is present.
If Unity prompts to enable the New Input System backend, click **Yes** and
let the editor restart.

### 3. Verify EditMode tests are green

**Window → General → Test Runner → EditMode → Run All**. Should report
**~270 passed / 0 failed** in under 10 seconds.

### 4. Create the BJJ scene

1. **File → New Scene** → **Basic (Built-in)** template → save as
   `Assets/Scenes/BJJ.unity` (create the `Scenes/` folder if it doesn't exist).
2. In the Hierarchy window: **+ → Create Empty** → rename to **`BJJ_GameManager`**.
3. With `BJJ_GameManager` selected, in the Inspector click **Add Component**
   and add **all four** Platform components in this order:
   - `BJJSessionLifecycle`
   - `BJJInputProvider`
   - `BJJGameManager`
   - `BJJDebugHud`
   (Unity's `[RequireComponent]` chain enforces this order.)
4. On the `BJJInputProvider` component, drag
   `Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions` from the
   Project window into the **Actions Asset** slot.
5. On the `BJJGameManager` component, drag the same `BJJ_GameManager`
   GameObject (the one you're inspecting) into the **Hud** slot — it picks
   up the `BJJDebugHud` automatically.
6. Save the scene (Ctrl+S / Cmd+S).

### 5. Add the scene to Build Settings (optional, for builds)

**File → Build Settings → Scenes In Build → +** → select `BJJ.unity`.

### 6. Press Play

Hit the **▶︎ Play** button. You should see:

- A grey-on-black IMGUI panel top-left with `phase Prompt  role Bottom`
- An overlay box centered: **`ROLE: Bottom    LS左右で切替 / Spaceで決定`**
- Press **Space** → overlay disappears, `phase Active`, round timer starts
  counting down from `5:00`
- Press **F** → top-left HUD shows `L-hand IDLE → REACHING`, then `→ GRIPPED`
  after ~280 ms
- Press **Esc** → `PAUSED` overlay; press Esc again to resume
- Press **R** then **U** → both feet `LOCKED → UNLOCKED → GUARD_OPENED →
  SESSION END: GuardOpened`
- Press **Space** on the end overlay → restart

### 7. Run a practice scenario

**While in `Active` phase**, press a number key (currently keyboard-bound
through Unity's editor focus, not via the Input Action asset — these will be
wired into the Action asset in a later iteration). For now, scenarios are
loadable only via code; v1 of the GameManager doesn't yet have a digit-key
binding (Stage 1 had it). To exercise a scenario right now, edit
`BJJSessionLifecycle.LoadScenario(ScenarioName.TriangleReady)` from a one-off
Editor menu item, or wait for the next iteration.

## Known v1 Limitations

- **Visuals**: no character rig, no URP, no Shader Graph. The screen is empty
  apart from the IMGUI HUD. Stage 1's blockman + camera shake will be ported
  in a follow-up phase per `docs/design/stage2_port_plan_v1.md` §3.3.
- **Coach HUD**: minimal v1 (just feet/hands/break magnitude). The full
  per-technique checklist from Stage 1 will move to UI Toolkit in a follow-up.
- **Tutorial overlay**: placeholder text only. The Stage 1 Japanese tutorial
  HTML page will become a UI Toolkit document later.
- **Scenario picker UI**: keyboard digit binding not yet in the InputActions
  asset. Use the Lifecycle API directly until then.
- **Round timer length**: editable in `BJJGameManager` Inspector (default 5
  min, matches IBJJF adult white/blue belt).
- **Noisy-gamepad keyboard lockout**: Stage 1 has the
  `kbActive`-priority workaround in `LayerA`; Stage 2 currently does not.
  If your environment has a phantom gamepad (e.g. an Amazon IR remote
  reporting axes pinned at -1), keyboard input will be ignored. Fix is to
  port the same arbitration into `Runtime/Input/LayerA.cs` — tracked as a
  follow-up task.

## Port Progress

See [docs/design/stage2_port_progress.md](../../../docs/design/stage2_port_progress.md).
The current design plan is [docs/design/stage2_port_plan_v1.md](../../../docs/design/stage2_port_plan_v1.md).
The host-layer (MonoBehaviour) design is
[docs/design/stage2_game_manager_v1.md](../../../docs/design/stage2_game_manager_v1.md).
