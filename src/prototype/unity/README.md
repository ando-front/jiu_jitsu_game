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

The EditMode suite (`Assets/BJJSimulator/Tests/EditMode/HandFSMTest.cs`) is the
C# mirror of the Stage 1 Vitest suite. Each test case maps 1:1 to a Vitest
`it(...)` block. A regression on either side produces a named, greppable failure.

## Project Layout

```
Assets/
  BJJSimulator/
    Runtime/
      BJJSimulator.asmdef        # main assembly
      Core/
        BJJCoreTypes.cs          # shared enums (GripZone, HandState, …)
      State/
        HandFSM.cs               # HandFSM struct + HandFSMOps tick function
    Tests/
      BJJSimulatorTests.asmdef   # test assembly (Editor-only)
      EditMode/
        HandFSMTest.cs           # NUnit mirror of hand_fsm.test.ts
Packages/
  manifest.json                  # Unity Package Manager deps
ProjectSettings/
  ProjectSettings.asset          # minimal Unity project settings
```

## Port Progress

See [docs/design/stage2_port_progress.md](../../../docs/design/stage2_port_progress.md).
The current design plan is [docs/design/stage2_port_plan_v1.md](../../../docs/design/stage2_port_plan_v1.md).

## Input System Note

`com.unity.inputsystem` 1.11.2 is included in `Packages/manifest.json`.
Layer A (physical device → `InputFrame`) will be implemented using the New
Input System's `PlayerInput` + Input Action Assets when the input layer is ported.
