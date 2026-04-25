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

**Window → Package Manager** → verify `Input System` (1.11.2) is present and
`MCP for Unity` (com.coplaydev.unity-mcp) is present (manifest.json pulls it
in automatically; first import takes ~30s for the git fetch).

If Unity prompts to enable the New Input System backend, click **Yes** and
let the editor restart.

### 3. Verify EditMode tests are green

**Window → General → Test Runner → EditMode → Run All**. Should report
**~270 passed / 0 failed** in under 10 seconds.

### 4. Build the BJJ scene (one click)

From the Unity menu bar choose **BJJ → Setup Scene**. The editor script at
`Assets/BJJSimulator/Editor/BJJSceneSetup.cs` will:

- Create `Assets/Scenes/BJJ.unity` (overwriting any prior copy)
- Spawn `BJJ_GameManager` with `BJJSessionLifecycle` → `BJJInputProvider` →
  `BJJGameManager` → `BJJDebugHud` attached in the order `[RequireComponent]`
  expects
- Wire `BJJInputProvider.actionsAsset` to `BJJInputActions.inputactions`
- Wire `BJJGameManager.hud` to the same GameObject's `BJJDebugHud`
- Save the scene and register it as Build Settings index 0

A confirmation dialog appears when the build is complete. Re-run the menu at
any time to reset the scene to a known-good state. **BJJ → Open Scene** is
also exposed for quick navigation.

### 5. Press Play

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

### 6. Run a practice scenario

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

## Unity MCP integration (Editor automation from Claude Code)

This repo ships pre-wired for [`CoplayDev/unity-mcp`](https://github.com/CoplayDev/unity-mcp),
which exposes the running Unity Editor to Claude Code (and Cursor / Claude
Desktop / Windsurf) as an MCP server. Once both halves are running, a Claude
Code session on your local machine gains `mcp__unity__*` tools that can read
the project, run EditMode tests, build scenes, manipulate GameObjects, and
trigger Play mode without manual menu clicks.

### Architecture (why two pieces)

```
┌──────────────────┐  HTTP :8080   ┌────────────────────┐
│  Claude Code     │  ──────────►  │  MCP for Unity     │
│  (your terminal) │  ◄──────────  │  bridge (in Editor)│
└──────────────────┘               └────────────────────┘
        ▲                                    │
        │ reads .mcp.json                    │ drives
        ▼                                    ▼
  /jiu_jitsu_game/.mcp.json          your open Unity 6 project
```

- `.mcp.json` at the repo root tells Claude Code to expect an MCP server at
  `http://localhost:8080/mcp`.
- `Packages/manifest.json` contains the `com.coplaydev.unity-mcp` git
  dependency, so the bridge installs automatically on first project import.
- The bridge runs **inside Unity** — it only listens while the Editor is open
  and the server has been started.

### One-time local setup (5 minutes)

1. **Open the Unity project** as in the §1 steps above. Wait for the package
   manager to finish — `MCP for Unity` should appear in the package list.
2. **Open the bridge window**: **Window → MCP for Unity**. The first time you
   open it Unity will compile the bridge's editor scripts (~10s).
3. **Click `Start Server`**. The status indicator turns green and the
   bridge begins listening on `127.0.0.1:8080` (loopback only by default —
   LAN binding is opt-in under Advanced).
4. **(Optional) Tick `Auto-start with Editor`** so the server resumes the next
   time you open the project.
5. **Launch Claude Code** from the repo root:
   ```sh
   cd /path/to/jiu_jitsu_game
   claude
   ```
   On first connect, Claude Code will prompt to approve the `unityMCP` server
   from `.mcp.json`. Approve project-scope.
6. **Verify the connection** by typing `/mcp` inside Claude Code. You should
   see `unityMCP` listed as `connected` with a tool count > 0.

### Daily workflow

- **Start of session**: open Unity → it auto-starts the server (if you ticked
  the box) → run `claude` from the repo root.
- **Sanity check**: ask Claude `list the GameObjects in the BJJ scene` — if
  the bridge is up, Claude will use `mcp__unity__*` tools and answer with
  the live hierarchy.
- **End of session**: closing Unity stops the server. Claude Code will report
  `unityMCP` as disconnected on its next `/mcp` check; that's expected.

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `/mcp` shows `unityMCP: failed` | Unity not running, or `Start Server` not clicked | Open Unity → Window → MCP for Unity → Start Server |
| `/mcp` shows `unityMCP: connected` but no tools | Bridge crashed or wrong Unity version | Console window → check for `MCPForUnity` errors. The bridge supports Unity 2021.3 LTS+ |
| Claude says it can't reach `localhost:8080` from within a remote/cloud sandbox | The bridge is loopback-only by design | Run Claude Code on the same machine as Unity. Cloud sandboxes (e.g. claude.ai/code) cannot see your local Unity Editor |
| `manifest.json` git fetch fails on first import | Offline / corp proxy blocking github.com | Either remove the `com.coplaydev.unity-mcp` line and install via Package Manager → "Add package from git URL", or configure git to use your proxy |
| Want a different fork (e.g. `justinpbarnett/unity-mcp`) | — | Replace the git URL in `Packages/manifest.json` and the URL/transport block in `.mcp.json` according to that fork's README |

### Security note

The bridge has access to your Unity project and can mutate the scene, run
arbitrary edit-time C#, and trigger Play mode. Treat it the same as giving
Claude Code shell access to the project. The default loopback-only binding
prevents anyone on your LAN from connecting; do not change this unless you
understand the implications.

## Port Progress

See [docs/design/stage2_port_progress.md](../../../docs/design/stage2_port_progress.md).
The current design plan is [docs/design/stage2_port_plan_v1.md](../../../docs/design/stage2_port_plan_v1.md).
The host-layer (MonoBehaviour) design is
[docs/design/stage2_game_manager_v1.md](../../../docs/design/stage2_game_manager_v1.md).
