# Stage 2 (UE5) Prototype Workspace

This directory holds the Unreal Engine 5 side of the project.

## Goal

Port Stage 1 pure logic (A->B->C->D pipeline and FSMs) into UE5 C++ with a one-to-one structure where possible. Stage 1 (`src/prototype/web/`) remains the reference implementation; any divergence is a bug.

## Current Scaffold

- `Source/BJJLogicCore/BJJLogicCore.Build.cs`
- `Source/BJJLogicCore/Public/BJJButtonBit.h` — ButtonBit enum + device kind
- `Source/BJJLogicCore/Public/BJJGripZone.h` — grip zones + unit-vector table
- `Source/BJJLogicCore/Public/BJJInputFrame.h` — Layer A output struct
- `Source/BJJLogicCore/Public/BJJIntent.h` — Layer B output (hip / grip / discrete)
- `Source/BJJLogicCore/Public/BJJLayerB.h` — Layer B config + state + transform
- `Source/BJJLogicCore/Public/BJJGameState.h` — aggregate sim state (threads LayerB state)
- `Source/BJJLogicCore/Public/BJJStepSimulation.h` — per-tick driver
- `Source/BJJLogicCore/Private/BJJLayerB.cpp` — Layer B port
- `Source/BJJLogicCore/Private/BJJLogicCoreModule.cpp` — `IMPLEMENT_MODULE`
- `Source/BJJLogicCore/Private/BJJStepSimulation.cpp` — Layer B wired into Step
- `Source/BJJLogicCore/Private/Tests/BJJLayerBTests.cpp` — UE Automation tests mirroring Stage 1 `layerB.test.ts`

## Attaching this module to a UE5 project

The module is source-only. Follow these steps once per workstation.

1. **Create the host project.** In UE5 Editor:
   *New Project → Games → Blank → C++ → Starter Content off → Target Platform Desktop.*
   Name it e.g. `BJJStage2` and save it to `src/prototype/ue5/BJJStage2/`.
2. **Close the editor** before editing source layout.
3. **Link the module into the host project's `Source/`** (symlink on macOS/Linux, copy on Windows):
   ```sh
   cd src/prototype/ue5/BJJStage2/Source
   ln -s ../../Source/BJJLogicCore ./BJJLogicCore
   ```
4. **Register the module in the `.Target.cs` files.** Edit both `BJJStage2.Target.cs` and `BJJStage2Editor.Target.cs` under `BJJStage2/Source/`:
   ```csharp
   ExtraModuleNames.AddRange(new string[] { "BJJStage2", "BJJLogicCore" });
   ```
5. **Declare the dependency.** Edit `BJJStage2/Source/BJJStage2/BJJStage2.Build.cs` and add `"BJJLogicCore"` to `PublicDependencyModuleNames`.
6. **Declare the module in the `.uproject`.** In `BJJStage2.uproject`, under `"Modules"`, append:
   ```json
   { "Name": "BJJLogicCore", "Type": "Runtime", "LoadingPhase": "Default" }
   ```
7. **Regenerate project files:** right-click `BJJStage2.uproject` → *Generate Visual Studio / Xcode project files*.
8. **Build** from your IDE (Development Editor configuration) and launch the Editor.

### Verifying the build

After the Editor opens with the host project, run the Layer B tests:

- **GUI:** *Tools → Test Automation → Automation → expand `BJJ.Input.LayerB` → Start Tests.*
- **Headless:**
  ```sh
  UnrealEditor-Cmd BJJStage2.uproject \
    -ExecCmds="Automation RunTests BJJ.Input.LayerB; Quit" \
    -unattended -nopause -nullrhi
  ```
  Exit code 0 = all pass.

## Parity with Stage 1

Layer B is expected to be behaviourally equivalent to `src/prototype/web/src/input/layerB.ts`. The automation test suite in `BJJLayerBTests.cpp` replays the same scenarios as `tests/unit/layerB.test.ts`; when a Stage 1 test is added, mirror it here.

Canonical constants (scaling, thresholds, zone directions) are duplicated rather than generated. If they diverge, Stage 1 wins — fix the UE5 side.

## Next Implementation Steps

1. ~~Attach this module to a UE5 project (`.uproject`) as a runtime module.~~ See above.
2. ~~Implement full Layer B intent transform rules from `src/prototype/web/src/input/layerB.ts`.~~ Done.
3. Port Layer A (`src/prototype/web/src/input/layerA.ts`) — gamepad / keyboard → `FBJJInputFrame` adapter. On UE5 this becomes a `UEnhancedInput` integration.
4. Port state modules from `src/prototype/web/src/state/` in this order: `stamina` → `posture_break` → `hand_fsm` → `foot_fsm` → `judgment_window` → `counter_window` → `cut_attempt` → `pass_attempt` → `arm_extracted` → `control_layer`.
5. Port defender-side (`input/layerB_defense.ts`, `input/intent_defense.ts`) after attacker parity holds.
6. Wire `FBJJStepSimulation::Step` to a fixed-step tick owner (GameMode or custom subsystem) once Layer A exists.
7. Add a scenario test harness that replays Stage 1 `tests/scenario/*.test.ts` vectors through `FBJJStepSimulation::Step`.

## Notes

- Keep logic files pure C++ where possible (no Actor dependency in core logic).
- Visual fidelity work (animation, cloth, lighting, post process) should stay outside this module.
- IDE diagnostics will show `CoreMinimal.h` unresolved until the host project is generated — this is expected; the headers resolve via the engine include paths set up by step 7 above.
