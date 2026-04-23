# Stage 2 (UE5) Prototype Workspace

This directory holds the Unreal Engine 5 side of the project.

## Goal

Port Stage 1 pure logic (A->B->C->D pipeline and FSMs) into UE5 C++ with a one-to-one structure where possible.

## Current Scaffold

- `Source/BJJLogicCore/BJJLogicCore.Build.cs`
- `Source/BJJLogicCore/Public/BJJInputFrame.h`
- `Source/BJJLogicCore/Public/BJJIntent.h`
- `Source/BJJLogicCore/Public/BJJGameState.h`
- `Source/BJJLogicCore/Public/BJJStepSimulation.h`
- `Source/BJJLogicCore/Private/BJJStepSimulation.cpp`

## Next Implementation Steps

1. Attach this module to a UE5 project (`.uproject`) as a runtime module.
2. Implement full Layer B intent transform rules from `src/prototype/web/src/input/layerB.ts`.
3. Port state modules from `src/prototype/web/src/state/` into `BJJLogicCore`.
4. Add UE Automation tests that replay Stage 1 scenario vectors.
5. Wire `FBJJStepSimulation::Step` to a fixed-step tick owner (GameMode or custom subsystem).

## Notes

- Keep logic files pure C++ where possible (no Actor dependency in core logic).
- Visual fidelity work (animation, cloth, lighting, post process) should stay outside this module.
