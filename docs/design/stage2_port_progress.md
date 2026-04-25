# Stage 2 移植 進捗トラッカ

**最終更新**: 2026-04-25
**エンジン**: Unity 6 (6000.0 LTS) — UE5 から変更 (2026-04-23)
**前提**: [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) §1 のファイル対応表

各行は Stage 1 のソースファイル 1 本 = Stage 2 の 1 C# ファイル。
`🟢 done` / `🟡 in-progress` / `⚪ pending` / `🚫 intentionally discarded`。

> **注意**: UE5 版の進捗は [stage2_port_plan_ue5_deprecated_v1.md](./stage2_port_plan_ue5_deprecated_v1.md) に移動・廃止済み。

---

## サマリ (2026-04-25)

- **Pure コアロジック (Core / State / Input / Sim / AI)**: 全 🟢
- **EditMode 単体テスト**: 全 🟢 (Stage 1 の `tests/unit/*` 1 対 1)
- **EditMode シナリオ結合テスト**: 全 🟢 (Stage 1 の `tests/scenario/*` 6 ファイル 1 対 1)
- **Platform 層 (MonoBehaviour)**: 全 🟢 (`BJJGameManager` / `BJJInputProvider` / `BJJSessionLifecycle` / `BJJDebugHud`)
- **New Input System (`BJJInputActions.inputactions`)**: 🟢 (Digit1-7 binding は未追加 — §残課題参照)
- **Editor 自動化 (`BJJSceneSetup.cs`)**: 🟢 (`BJJ → Setup Scene` メニュー)
- **Unity MCP 統合**: 🟢 (`com.coplaydev.unity-mcp` + `.mcp.json`)
- **レンダ / 演出 (mesh / URP / UI Toolkit)**: 全 ⚪ (Editor 上で対話設計)

---

## 基礎 & 足場 (Unity)

| 項目 | 状態 | 備考 |
|---|---|---|
| `Packages/manifest.json` | 🟢 | `com.unity.inputsystem` + `com.unity.test-framework` + `com.coplaydev.unity-mcp` |
| `ProjectSettings/ProjectSettings.asset` | 🟢 | Unity 6 LTS 最小 YAML |
| `Assets/BJJSimulator/Runtime/BJJSimulator.asmdef` | 🟢 | メイン assembly def |
| `Assets/BJJSimulator/Editor/BJJSimulator.Editor.asmdef` | 🟢 | Editor 専用(MCP / scene-build) |
| `Assets/BJJSimulator/Tests/BJJSimulatorTests.asmdef` | 🟢 | テスト assembly def (Editor-only) |
| `src/prototype/unity/.gitignore` | 🟢 | Library/ Temp/ 等を除外 |
| `.gitattributes` | 🟢 | Unity text assets + `*.asmdef` を LF テキストとして追加 |
| repo ルート `.mcp.json` | 🟢 | Claude Code → unityMCP HTTP `localhost:8080/mcp` |

## Core types

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
| `input/types.ts` | `Runtime/Input/BJJInputFrame.cs` | 🟢 |
| `input/intent.ts` | `Runtime/Input/BJJIntent.cs` | 🟢 |
| `input/intent_defense.ts` | `Runtime/Input/BJJDefenseIntent.cs` | 🟢 |
| `input/layerA.ts` | `Runtime/Input/LayerA.cs` (+ `KbRecentMs` 仲裁) | 🟢 |
| `input/layerB.ts` | `Runtime/Input/LayerB.cs` | 🟢 |
| `input/layerB_defense.ts` | `Runtime/Input/LayerBDefense.cs` | 🟢 |
| `input/layerD.ts` | `Runtime/Input/LayerD.cs` | 🟢 |
| `input/layerD_defense.ts` | `Runtime/Input/LayerDDefense.cs` | 🟢 |
| `input/transform.ts` | `Runtime/Input/InputTransform.cs` | 🟢 |
| `input/keyboard.ts` / `gamepad.ts` | — | 🚫 New Input System で代替(`BJJInputProvider` がブリッジ) |
| `BJJInputActions.inputactions` | 🟢 | Digit1-7 binding は未追加(§残課題参照) |

## 状態機械

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `state/hand_fsm.ts` | `Runtime/State/HandFSM.cs` | 🟢 |
| `state/foot_fsm.ts` | `Runtime/State/FootFSM.cs` | 🟢 |
| `state/posture_break.ts` | `Runtime/State/PostureBreak.cs` | 🟢 |
| `state/stamina.ts` | `Runtime/State/Stamina.cs` | 🟢 |
| `state/arm_extracted.ts` | `Runtime/State/ArmExtracted.cs` | 🟢 |
| `state/judgment_window.ts` | `Runtime/State/JudgmentWindow.cs` | 🟢 |
| `state/counter_window.ts` | `Runtime/State/CounterWindow.cs` | 🟢 |
| `state/pass_attempt.ts` | `Runtime/State/PassAttempt.cs` | 🟢 |
| `state/cut_attempt.ts` | `Runtime/State/CutAttempt.cs` | 🟢 |
| `state/control_layer.ts` | `Runtime/State/ControlLayer.cs` | 🟢 |
| `state/game_state.ts` | `Runtime/State/GameState.cs` | 🟢 |
| `state/scenarios.ts` | `Runtime/State/Scenarios.cs` | 🟢 |

## Sim / AI / Platform

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `sim/fixed_step.ts` | `Runtime/Sim/FixedStep.cs` | 🟢 |
| `ai/opponent_ai.ts` | `Runtime/AI/OpponentAI.cs` | 🟢 |
| `main.ts` | `Runtime/Platform/BJJGameManager.cs` ほか | 🟢 |
| (新規) | `Runtime/Platform/BJJSessionLifecycle.cs` | 🟢 |
| (新規) | `Runtime/Platform/BJJInputProvider.cs` (IStepProvider) | 🟢 |
| (新規) | `Runtime/Platform/BJJDebugHud.cs` (IMGUI v1) | 🟢 |
| (新規) | `Editor/BJJSceneSetup.cs` (`BJJ → Setup Scene`) | 🟢 |

## レンダ / 演出 (Stage 2 固有)

| Stage 1 相当 | Stage 2 出力 | 状態 |
|---|---|---|
| `scene/blockman.ts` | Skinned mesh + Animator Controller | ⚪ |
| stamina color grading | URP PostProcess Volume | ⚪ |
| `setWindowTint` / `pulseFlash` | PostProcess + Camera Shake | ⚪ |
| HUD / event log / tutorial / pause | UI Toolkit | ⚪ (現 IMGUI HUD は v1 暫定) |

## テスト

| Stage 1 suite | Stage 2 EditMode Test | 状態 | 備考 |
|---|---|---|---|
| `tests/unit/hand_fsm.test.ts` | `Tests/EditMode/HandFSMTest.cs` | 🟢 | |
| `tests/unit/foot_fsm.test.ts` | `Tests/EditMode/FootFSMTest.cs` | 🟢 | |
| `tests/unit/posture_break.test.ts` | `Tests/EditMode/PostureBreakTest.cs` | 🟢 | |
| `tests/unit/stamina.test.ts` | `Tests/EditMode/StaminaTest.cs` | 🟢 | |
| `tests/unit/arm_extracted.test.ts` | `Tests/EditMode/ArmExtractedTest.cs` | 🟢 | |
| `tests/unit/judgment_window.test.ts` | `Tests/EditMode/JudgmentWindowTest.cs` | 🟢 | |
| `tests/unit/counter_window.test.ts` | `Tests/EditMode/CounterWindowTest.cs` | 🟢 | |
| `tests/unit/control_layer.test.ts` | `Tests/EditMode/ControlLayerTest.cs` | 🟢 | |
| `tests/unit/cut_attempt.test.ts` | `Tests/EditMode/CutAttemptTest.cs` | 🟢 | |
| `tests/unit/pass_attempt.test.ts` | `Tests/EditMode/PassAttemptTest.cs` | 🟢 | |
| `tests/unit/scenarios.test.ts` | `Tests/EditMode/ScenariosTest.cs` | 🟢 | |
| `tests/unit/layerA.test.ts` (transform 部) | `Tests/EditMode/InputTransformTest.cs` | 🟢 | |
| `tests/unit/layerA.test.ts` (assembler 部) | `Tests/EditMode/LayerATest.cs` | 🟢 | + noisy-gamepad 5 ケース (Stage 2 先行) |
| `tests/unit/layerB.test.ts` | `Tests/EditMode/LayerBTest.cs` | 🟢 | |
| `tests/unit/layerB_defense.test.ts` | `Tests/EditMode/LayerBDefenseTest.cs` | 🟢 | |
| `tests/unit/layerD.test.ts` | `Tests/EditMode/LayerDTest.cs` | 🟢 | |
| `tests/unit/layerD_defense.test.ts` | `Tests/EditMode/LayerDDefenseTest.cs` | 🟢 | |
| `tests/unit/fixed_step.test.ts` | `Tests/EditMode/FixedStepTest.cs` | 🟢 | |
| `tests/unit/opponent_ai.test.ts` | `Tests/EditMode/OpponentAITest.cs` | 🟢 | |
| `tests/scenario/counter_integration.test.ts` | `Tests/EditMode/Scenario/CounterIntegrationTest.cs` | 🟢 | |
| `tests/scenario/cut_attempt_integration.test.ts` | `Tests/EditMode/Scenario/CutAttemptIntegrationTest.cs` | 🟢 | |
| `tests/scenario/defense_integration.test.ts` | `Tests/EditMode/Scenario/DefenseIntegrationTest.cs` | 🟢 | |
| `tests/scenario/game_state.test.ts` | `Tests/EditMode/Scenario/GameStateScenarioTest.cs` | 🟢 | |
| `tests/scenario/pass_attempt_integration.test.ts` | `Tests/EditMode/Scenario/PassAttemptIntegrationTest.cs` | 🟢 | |
| `tests/scenario/technique_scenarios.test.ts` | `Tests/EditMode/Scenario/TechniqueScenariosTest.cs` | 🟢 | |

---

## 次の作業単位 (小さい順)

Pure / テスト / Platform / Editor 自動化 / MCP は完了。残るのは Visual Pillar
領域および小さな配線残課題。

1. **Scenario picker UI** — `BJJInputActions.inputactions` に Digit1-7 binding を
   追加し、`BJJGameManager.Update` で edge を `BJJSessionLifecycle.LoadScenario` に
   ルーティング。Pure 側の `Scenarios.Build` は完成しているので Wiring のみ。
2. **Skinned mesh + Animator** — Stage 1 blockman 相当。`Assets/BJJSimulator/Art/`
   配下にプレースホルダ rig を置き、`BJJ_GameManager` に `BJJAvatarBinder` を
   足す方向。Editor 上で対話設計推奨。
3. **URP Volume profile** — stamina warm shift / judgment-window vignette。
   Visual Pillar §5.4 準拠。`BJJDebugHud` の代替として最小プロファイル。
4. **UI Toolkit へのコーチ HUD 移植** — 現 IMGUI HUD を `.uxml` + `.uss` に分解。
5. **PlayMode テスト** — New Input System 経路の生 polling は EditMode 純関数では
   検証できない。`Tests/PlayMode/` を新設し、`BJJInputProvider.PollHardware` 経路の
   integration テストをいくつか書く価値あり。

---

## 既知の技術債

- `BJJSimulator.asmdef` の `autoReferenced: true` は将来大きくなったら `false` に変える
- テストアセンブリは現状 Editor 専用。PlayMode テストを増やす場合は別 asmdef に分割推奨
- `Packages/manifest.json` の `com.coplaydev.unity-mcp` git 参照はバージョン固定無し
  (`#main`)。再現性が問題になったら `#vX.Y.Z` 等タグ参照に切り替える
