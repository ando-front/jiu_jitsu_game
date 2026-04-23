# Stage 2 移植 進捗トラッカ

**最終更新**: 2026-04-21
**前提**: [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) §1 のファイル対応表

各行は Stage 1 のソースファイル 1 本 = Stage 2 の 1 ペア。
`🟢 done` / `🟡 in-progress` / `⚪ pending` / `🚫 intentionally discarded`。

---

## 基礎 & 足場

| 項目 | 状態 | 備考 |
|---|---|---|
| `BJJSimulator.uproject` | 🟢 | Runtime module 1 本、EnhancedInput plugin on |
| `BJJSimulator.Target.cs` / `BJJSimulatorEditor.Target.cs` | 🟢 | UE5.5, BuildSettingsVersion.V5 |
| `BJJSimulator.Build.cs` | 🟢 | `Core` / `CoreUObject` / `Engine` / `InputCore` / `EnhancedInput` |
| モジュール entry (`BJJSimulator.h/.cpp`) | 🟢 | `IMPLEMENT_PRIMARY_GAME_MODULE` |
| `Config/DefaultEngine.ini` | 🟢 | `r.OneFrameThreadLag=0` + Lumen 有効(§5.3 入力遅延対策) |
| `Config/DefaultGame.ini` | 🟢 | Project name / version |
| `.gitattributes` | 🟢 | `*.uasset` / `*.umap` / `*.fbx` / `*.wav` 他を LFS track |

## Core types (§1.2 の先頭)

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `input/types.ts` | `Public/Core/BJJCoreTypes.h` (`EBJJButtonBit`) | 🟢 |
| `state/hand_fsm.ts` (側・状態 enum) | 同上 (`EBJJHandSide` / `EBJJHandState`) | 🟢 |
| `state/foot_fsm.ts` (側・状態 enum) | 同上 (`EBJJFootSide` / `EBJJFootState`) | 🟢 |
| `input/intent.ts` (`GripZone`) | 同上 (`EBJJGripZone` — None ordinal 0) | 🟢 |

## 入力層 (§1.1)

| Stage 1 ソース | Stage 2 出力 | 状態 | 備考 |
|---|---|---|---|
| `input/types.ts` | `Public/Input/BJJInputFrame.h` | ⚪ | `FBJJInputFrame` USTRUCT |
| `input/intent.ts` | `Public/Input/BJJIntent.h` | ⚪ | `FBJJHipIntent` / `FBJJGripIntent` / `FBJJIntent` |
| `input/intent_defense.ts` | `Public/Input/BJJDefenseIntent.h` | ⚪ | 同型 |
| `input/layerA.ts` | `Public/Input/BJJLayerA.h`+`.cpp` | ⚪ | EnhancedInput ラッパ |
| `input/layerB.ts` | `Public/Input/BJJLayerB.h`+`.cpp` | ⚪ | |
| `input/layerB_defense.ts` | `Public/Input/BJJLayerBDefense.h`+`.cpp` | ⚪ | |
| `input/layerD.ts` | `Public/Input/BJJLayerD.h`+`.cpp` | ⚪ | |
| `input/layerD_defense.ts` | `Public/Input/BJJLayerDDefense.h`+`.cpp` | ⚪ | |
| `input/transform.ts` | `Public/Input/BJJInputTransform.h`+`.cpp` | ⚪ | |
| `input/keyboard.ts` / `gamepad.ts` | — | 🚫 | 実装差替(EnhancedInput)。Layer A 契約は保持 |

## 状態機械 (§1.2)

| Stage 1 ソース | Stage 2 出力 | 状態 | 備考 |
|---|---|---|---|
| `state/hand_fsm.ts` | `Public/State/BJJHandFSM.h` + `Private/State/BJJHandFSM.cpp` | 🟢 | 全遷移 port 済 + sentinel guard(§2.5) |
| `state/foot_fsm.ts` | `Public/State/BJJFootFSM.h` + `.cpp` | ⚪ | 次の候補。LOCKED/UNLOCKED/LOCKING |
| `state/posture_break.ts` | `Public/State/BJJPostureBreak.h` + `.cpp` | ⚪ | `FVector2d` 明示(§2.5) |
| `state/stamina.ts` | `Public/State/BJJStamina.h` + `.cpp` | ⚪ | |
| `state/arm_extracted.ts` | `Public/State/BJJArmExtracted.h` + `.cpp` | ⚪ | |
| `state/judgment_window.ts` | `Public/State/BJJJudgmentWindow.h` + `.cpp` | ⚪ | 技 enum + timing constexpr |
| `state/counter_window.ts` | `Public/State/BJJCounterWindow.h` + `.cpp` | ⚪ | |
| `state/pass_attempt.ts` | `Public/State/BJJPassAttempt.h` + `.cpp` | ⚪ | enum+payload(§2.2) |
| `state/cut_attempt.ts` | `Public/State/BJJCutAttempt.h` + `.cpp` | ⚪ | 同上 |
| `state/control_layer.ts` | `Public/State/BJJControlLayer.h` + `.cpp` | ⚪ | |
| `state/game_state.ts` | `Public/State/BJJGameState.h` + `.cpp` | ⚪ | 集約 + `Step` |
| `state/scenarios.ts` | `Public/State/BJJScenarios.h` + `.cpp` | ⚪ | `!UE_BUILD_SHIPPING` + console command |

## Sim / AI (§1.3)

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `sim/fixed_step.ts` | `Public/Sim/BJJFixedStep.h` + `.cpp`(`ABJJGameMode` 内部) | ⚪ |
| `ai/opponent_ai.ts` | `Public/AI/BJJOpponentAI.h` + `.cpp` | ⚪ |
| `main.ts` | — | 🚫 `ABJJGameMode` + Widget BP に分解 |

## レンダ / シーン (§1.4)

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `scene/blockman.ts` (geometry) | `ACharacter` + AnimBP (art 依存) | ⚪ 7b |
| overlay shader (stamina) | PostProcess Material | ⚪ 7a |
| `setWindowTint` / `pulseFlash` | PostProcess + Cine Camera Shake | ⚪ 7a |
| HUD / event log / tutorial / pause / timer | UMG Widget BP | ⚪ |

## テスト (§1.5)

| Stage 1 suite | Stage 2 Automation Test | 状態 | 備考 |
|---|---|---|---|
| `tests/unit/hand_fsm.test.ts` (14) | `Private/Tests/BJJHandFSMTest.cpp` | 🟡 | 11 ケース port 済。残り 3 は zone memory の細かい edge case、port 優先度中 |
| 他 `tests/unit/*.test.ts` (13 ファイル) | 未 | ⚪ | 該当 FSM の port 時にペアで追加 |
| `tests/scenario/*.test.ts` (6 ファイル) | 未 | ⚪ | GameState port 後 |

---

## 次の作業単位(小さい順)

1. **FootFSM** port + テスト(HandFSM の型をなぞれば 1 時間程度)
2. **PostureBreak** port(`FVector2d` + decay、純関数)
3. **Stamina / ArmExtracted** port
4. **Intent 系 USTRUCT** 定義(Input/BJJIntent.h 〜 BJJInputFrame.h)
5. **GameState** 集約 + `Step`(FSM 群依存、他が揃ってから)

---

## 既知の技術債(v1.2 port 時に扱う)

- `Tick` 関数内のラムダは Core の autocomplete にかかりやすいが UE5 5.5+ でパフォーマンスに影響しない。現状維持
- `FBJJHandFSM::LastParriedAtMs` のデフォルト値は `MIN_int64` リテラルだが、UE5 でのヘッダ order によっては `CoreTypes.h` の include が前提になる。include 順に注意
- Automation Test module は現状 `BJJSimulator` 本体に同居(`WITH_DEV_AUTOMATION_TESTS` ガード)。巨大化したら `BJJSimulatorTests` サブモジュールに切り出す
