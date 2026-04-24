# Stage 2 移植 進捗トラッカ

**最終更新**: 2026-04-24
**エンジン**: Unity 6 (6000.0 LTS) — UE5 から変更 (2026-04-23)
**前提**: [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) §1 のファイル対応表

各行は Stage 1 のソースファイル 1 本 = Stage 2 の 1 C# ファイル。
`🟢 done` / `🟡 in-progress` / `⚪ pending` / `🚫 intentionally discarded`。

> **注意**: UE5 版の進捗は [stage2_port_plan_ue5_deprecated_v1.md](./stage2_port_plan_ue5_deprecated_v1.md) に移動・廃止済み。

---

## 基礎 & 足場 (Unity)

| 項目 | 状態 | 備考 |
|---|---|---|
| `Packages/manifest.json` | 🟢 | `com.unity.inputsystem` + `com.unity.test-framework` |
| `ProjectSettings/ProjectSettings.asset` | 🟢 | Unity 6 LTS 最小 YAML |
| `Assets/BJJSimulator/Runtime/BJJSimulator.asmdef` | 🟢 | メイン assembly def |
| `Assets/BJJSimulator/Tests/BJJSimulatorTests.asmdef` | 🟢 | テスト assembly def (Editor-only) |
| `src/prototype/unity/.gitignore` | 🟢 | Library/ Temp/ 等を除外 |
| `.gitattributes` | 🟢 | Unity text assets (`*.unity` / `*.prefab` / `*.asset` / `*.meta`) + `*.asmdef` を LF テキストとして追加 |

## Core types (§1.2 の先頭)

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `input/types.ts` | `Runtime/Core/BJJCoreTypes.cs` (`ButtonBit`) | 🟢 |
| `state/hand_fsm.ts` (側・状態 enum) | 同上 (`HandSide` / `HandState`) | 🟢 |
| `state/foot_fsm.ts` (側・状態 enum) | 同上 (`FootSide` / `FootState`) | 🟢 |
| `input/intent.ts` (`GripZone`) | 同上 (`GripZone` — None ordinal 0) | 🟢 |
| sentinel pattern | 同上 (`BJJConst.SentinelTimeMs = long.MinValue`) | 🟢 |

## 入力層

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `input/types.ts` | `Runtime/Input/BJJInputFrame.cs` | ⚪ |
| `input/intent.ts` | `Runtime/Input/BJJIntent.cs` | ⚪ |
| `input/intent_defense.ts` | `Runtime/Input/BJJDefenseIntent.cs` | ⚪ |
| `input/layerA.ts` | `Runtime/Input/LayerA.cs` (New Input System wrapper) | ⚪ |
| `input/layerB.ts` | `Runtime/Input/LayerB.cs` | ⚪ |
| `input/layerB_defense.ts` | `Runtime/Input/LayerBDefense.cs` | ⚪ |
| `input/layerD.ts` | `Runtime/Input/LayerD.cs` | ⚪ |
| `input/layerD_defense.ts` | `Runtime/Input/LayerDDefense.cs` | ⚪ |
| `input/transform.ts` | `Runtime/Input/InputTransform.cs` | ⚪ |
| `input/keyboard.ts` / `gamepad.ts` | — | 🚫 実装差替(New Input System)。Layer A 契約は保持 |

## 状態機械

| Stage 1 ソース | Stage 2 出力 | 状態 | 備考 |
|---|---|---|---|
| `state/hand_fsm.ts` | `Runtime/State/HandFSM.cs` | 🟢 | 全遷移 port 済 + sentinel guard(§2.5) |
| `state/foot_fsm.ts` | `Runtime/State/FootFSM.cs` | ⚪ | 次の候補 |
| `state/posture_break.ts` | `Runtime/State/PostureBreak.cs` | ⚪ | `Vector2` + decay |
| `state/stamina.ts` | `Runtime/State/Stamina.cs` | ⚪ | |
| `state/arm_extracted.ts` | `Runtime/State/ArmExtracted.cs` | ⚪ | |
| `state/judgment_window.ts` | `Runtime/State/JudgmentWindow.cs` | ⚪ | 技 enum + timing const |
| `state/counter_window.ts` | `Runtime/State/CounterWindow.cs` | ⚪ | |
| `state/pass_attempt.ts` | `Runtime/State/PassAttempt.cs` | ⚪ | fat struct パターン |
| `state/cut_attempt.ts` | `Runtime/State/CutAttempt.cs` | ⚪ | 同上 |
| `state/control_layer.ts` | `Runtime/State/ControlLayer.cs` | ⚪ | |
| `state/game_state.ts` | `Runtime/State/GameState.cs` | ⚪ | 集約 + `Step` |
| `state/scenarios.ts` | `Runtime/State/Scenarios.cs` | ⚪ | `#if UNITY_EDITOR` ガード |

## Sim / AI

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `sim/fixed_step.ts` | `Runtime/Sim/FixedStep.cs` | ⚪ |
| `ai/opponent_ai.ts` | `Runtime/AI/OpponentAI.cs` | ⚪ |
| `main.ts` | — | 🚫 `GameManager` MonoBehaviour に分解 |

## レンダ / 演出

| Stage 1 相当 | Stage 2 出力 | 状態 |
|---|---|---|
| `scene/blockman.ts` | Skinned mesh + Animator Controller | ⚪ |
| stamina color grading | URP PostProcess Volume | ⚪ |
| `setWindowTint` / `pulseFlash` | PostProcess + Camera Shake | ⚪ |
| HUD / event log / tutorial / pause | UI Toolkit | ⚪ |

## テスト

| Stage 1 suite | Stage 2 EditMode Test | 状態 | 備考 |
|---|---|---|---|
| `tests/unit/hand_fsm.test.ts` (14 ケース) | `Tests/EditMode/HandFSMTest.cs` | 🟢 | 全ケース port 済 |
| 他 `tests/unit/*.test.ts` (13 ファイル) | — | ⚪ | 該当 FSM の port 時にペアで追加 |
| `tests/scenario/*.test.ts` (6 ファイル) | — | ⚪ | GameState port 後 |

---

## 次の作業単位(小さい順)

1. **FootFSM** port + テスト (`Runtime/State/FootFSM.cs` + `Tests/EditMode/FootFSMTest.cs`)
2. **PostureBreak** port (`Runtime/State/PostureBreak.cs` — `Vector2` + decay、純関数)
3. **Stamina / ArmExtracted** port
4. **InputFrame / Intent struct 定義** (`Runtime/Input/BJJInputFrame.cs`, `BJJIntent.cs`)
5. **GameState** 集約 + `Step` (FSM 群依存; 他が揃ってから)

---

## 既知の技術債

- `BJJSimulator.asmdef` の `autoReferenced: true` は将来大きくなったら `false` に変える
- テストアセンブリは現状 Editor 専用。Runtime でも実行したい場合は Play Mode Tests に移行する
