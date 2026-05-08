# Stage 2 移植 進捗トラッカ

**最終更新**: 2026-05-08 (Mixamo + grip-feel + Avatar tint + scene cleanup + OTS camera + テスト全 green)
**エンジン**: Unity 6 (6000.0 LTS) — UE5 から変更 (2026-04-23)
**前提**: [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) §1 のファイル対応表

各行は Stage 1 のソースファイル 1 本 = Stage 2 の 1 C# ファイル。
`🟢 done` / `🟡 in-progress` / `⚪ pending` / `🚫 intentionally discarded`。

> **注意**: UE5 版の進捗は [stage2_port_plan_ue5_deprecated_v1.md](./stage2_port_plan_ue5_deprecated_v1.md) に移動・廃止済み。

---

## サマリ (2026-05-08)

- **Pure コアロジック (Core / State / Input / Sim / AI)**: 全 🟢
- **EditMode 単体テスト**: **268 / 268 pass** (PR #32 で pre-existing 2 件解消)
- **EditMode シナリオ結合テスト**: 全 🟢 (Stage 1 の `tests/scenario/*` 6 ファイル 1 対 1)
- **PlayMode テスト**: **7 / 7 pass** (PR #32 で digit edge flake 解消)
- **Platform 層 (MonoBehaviour)**: 全 🟢 (`BJJGameManager` / `BJJInputProvider` / `BJJSessionLifecycle` / `BJJDebugHud`)
- **New Input System (`BJJInputActions.inputactions`)**: 🟢 (Digit1-7 binding は未追加 — §残課題参照)
- **Editor 自動化 (`BJJSceneSetup.cs`)**: 🟢 (`BJJ → Setup Scene` メニュー)
- **Unity MCP 統合**: 🟢 (`com.coplaydev.unity-mcp` + `.mcp.json`)
- **レンダ / 演出 (mesh / URP / UI Toolkit)**: 🟢 完了 (`BJJAvatarBinder` + `BJJVolumeController` + `BJJHud` UI Toolkit + `BJJImpactFeedback` — SetupScene 自動配線済み)
- **Mixamo Humanoid 経路** (PR #27 / `72bed94`): 🟢 完了
  - `Y Bot.fbx` → Humanoid (`CreateFromThisModel`) + `Y BotAvatar` 生成
  - 4 アニメ (`Idle` / `Sitting Idle` / `Crouch Idle` / `Reaction`) → `CopyFromOther` で retarget
  - `Assets/BJJSimulator/Art/Animations/BJJAvatar.controller`: 6 parameters / 4 states / 8 transitions
  - `BJJ_GameManager` 子に `Avatar_Bottom (0,0,1)` / `Avatar_Top (0,0,-1, rot 0,180,0)` 配置 + `BJJAnimatorBinder` 配線
  - Play 検証: `Avatar_Top` が `!IsBottom` で `TopPosture` へ即遷移 / `JustParried` で `Reaction` 経由 `Idle` 復帰 / Console error+warning 0 件確認
- **Grip-feel pass 1** (PR #28 / `0e3a93d`): 🟢 完了
  - `BJJImpactFeedback`: `HandGripped` / `HandParried` / `HandGripBroken` イベントに ChromaticAberration pulse (peaks 0.30 / 0.55 / 0.40、150ms 線形減衰) を追加。既存 window 駆動 CA に `Mathf.Max` で重ね、grip 連打時は強い pulse のみが上書きされる
  - shake 振幅を Stage 1 web tuning から増量(Gripped 0.04→0.08、Parried 0.06→0.10、GripBroken 0.05→0.07)
  - `BJJAvatar.controller`: `Gripped` state 追加(Y Bot@Idle / speed 0.5、AnyState 2-parallel `LHandState/RHandState Equals 3` 入口 + 1 AND 出口 `LHandState/RHandState NotEqual 3`、`canTransitionToSelf=false`)
  - 4 件の Inspector 調整 knob(`caPulseGripped/Parried/GripBroken/DurationMs`)で recompile 不要のチューニング可能
- **Avatar 色差別化** (PR #29 / `d730194`): 🟢 完了
  - `Runtime/Visual/BJJAvatarTint.cs` 新規 — `MaterialPropertyBlock` で `_Color` (Standard) / `_BaseColor` (URP/Lit) 両方上書き、`[ExecuteAlways]` で Edit モードでも反映
  - `Avatar_Bottom` 暖色赤 (0.92, 0.30, 0.30) / `Avatar_Top` 寒色青 (0.30, 0.50, 0.92)、tintStrength 0.6
  - 共有マテリアル不変 / 新規 `.mat` アセット無し。M1 playtest でロール識別性が出る
- **Scene cleanup** (PR #30 / `661bf0b`): 🟢 完了
  - `Characters` ルートに残っていた primitive blockman を `SetActive(false)`(Mixamo 統合時の片付け漏れ)
  - `Assets/_DebugCaptures/` / `Assets/_Recovery/` を `.gitignore` に追加(Editor session ephemera)
  - Programmatic verification: Animator state machine 5 states 全て遷移確認 (`Idle` / `GuardIdle` / `TopPosture` / `Reaction` / `Gripped`)
- **Avatar 向き修正 + OTS カメラ** (PR #31 / `ea22374`): 🟢 完了
  - 旧設定では Bottom (rot 0,0,0) と Top (rot 0,180,0) が背中合わせ。Bottom (0,180,0) / Top (0,0,0) に交換し face-to-face に
  - Main Camera を Top 背後 (0, 1.6, -3.5) → Bottom 背後 (0.6, 2.0, 4.0) に移動、rot (15, 0, 0) → (20, 180, 0) に変更し Visual Pillar の "guard-side immersive over-the-shoulder" に整合
- **テスト fail 3 件解消** (PR #32 / `04be976`): 🟢 完了
  - `InputTransform.ApplyStickDeadzoneAndCurve` を double 精度 + scale 1 回算出に書き直し、Mono ARM64 JIT 起因の 2-ULP 非対称性を解消(`StickPreservesDirection`)
  - `LayerATest` の `RawHardwareSnapshot` で `KbLastEventMs = BJJConst.SentinelTimeMs` を明示。helper (`EmptyKb` / `LiveGamepad`) にも同値を伝播し、struct-default 0L が偽の "keyboard recently active" を返す問題を解消(`LiveGamepadWinsOverKeyboardForDeviceKind`、ついでに `GamepadStickIsRoutedThroughDeadzoneAndCurve` の偶然 pass を正しい経路に修正)
  - PlayMode test の `Press` / `Release` 直後に `yield return null;` を挿入し、InputSystem イベントの flush 待機を保証(`DigitEdgeFiresOncePerPress`)

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
| `BJJInputActions.inputactions` | 🟢 | gameplay 用 action map を提供。scenario digit (`Digit0-7`) は `BJJInputProvider` が `Keyboard.current` から直接 polling(meta-key と同じ扱い) |

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
| `scene/blockman.ts` | `Runtime/Platform/BJJAvatarBinder.cs` + `Assets/BJJSimulator/Art/` | 🟢 完了 (2026-04-30、ただし scene 上の `Characters` ルートは PR #30 で `SetActive(false)` 済み — Mixamo 経路がプライマリ) |
| `scene/blockman.ts` (Mixamo Humanoid 代替パス) | `Runtime/Visual/BJJAnimatorBinder.cs` + `Art/Characters/Y Bot.fbx` + `Art/Animations/*.fbx` + `Art/Animations/BJJAvatar.controller` | 🟢 完了 (2026-05-08, PR #27 + Gripped state 追加 PR #28) — Humanoid retarget / Animator Controller (5 states 含む `Gripped`) / 2 Avatar 配置 / binder 配線 / HUD-less Play 検証済み |
| stamina color grading | `Runtime/Platform/BJJVolumeController.cs` | 🟢 完了 (2026-04-30) — SetupScene 自動配線済み |
| `setWindowTint` / `pulseFlash` / grip CA pulse | `Runtime/Platform/BJJImpactFeedback.cs` | 🟢 完了 (2026-04-27 + 2026-05-08 grip pulse PR #28) |
| Avatar role 色差別化 (Visual Pillar) | `Runtime/Visual/BJJAvatarTint.cs` | 🟢 完了 (2026-05-08, PR #29) — `MaterialPropertyBlock` per-instance、Bottom 赤 / Top 青、Edit/Play 両モード反映 |
| OTS カメラ + Avatar 向き (Visual Pillar) | `Assets/Scenes/BJJ.unity` (Main Camera + Avatar Transform) | 🟢 完了 (2026-05-08, PR #31) — Bottom 後方 (0.6, 2.0, 4.0) rot (20, 180, 0)、Avatar 同士は face-to-face |
| HUD / event log / tutorial / pause | UI Toolkit (`BJJHud.cs` + `BJJHud.uxml` + `BJJHud.uss`) | 🟢 完了 (2026-04-27, lazy bind 修正 `cc79ccd` 2026-05-08) |

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

Pure / テスト / Platform / Editor 自動化 / MCP / Mixamo Humanoid 経路まで完了。
残るのは Visual Pillar の上位仕上げと既知 fail の解消。

1. ~~**Skinned mesh + Animator**~~ 🟢 **完了 (2026-04-26)** — `Runtime/Platform/BJJAvatarBinder.cs`
   + `Assets/BJJSimulator/Art/` ディレクトリを追加。`BJJGameManager.CurrentGameState` を
   LateUpdate で読み BlockMan 関節 Transform へ流す。Inspector rig 配線 (Prefab 組み立て)
   は Editor 作業として残る。
2. ~~**URP Volume profile**~~ 🟢 **完了 (2026-04-27)** — `Runtime/Platform/BJJVolumeController.cs`
   を追加。スタミナ → `WhiteBalance.temperature` warm shift、判断窓開放 →
   `Vignette.intensity` パルス (`#if BJJ_URP` ガード付き)。`manifest.json` に
   `com.unity.render-pipelines.universal 17.0.3` を追加。`BJJSimulator.asmdef` に
   URP 参照 + versionDefine `BJJ_URP`。Global Volume + Profile の Editor セットアップは
   Inspector 作業として残る。
3. ~~**UI Toolkit へのコーチ HUD 移植**~~ 🟢 **完了 (2026-04-27 / 2026-05-08 lazy bind 修正 `cc79ccd`)** — `Runtime/Platform/BJJHud.cs`
   + `Runtime/UI/BJJHud.uxml` + `Runtime/UI/BJJHud.uss`。4 パネル構成 + ライフサイクル中央オーバーレイ。
4. ~~**PlayMode テスト**~~ 🟢 **完了 (2026-04-26 / 2026-05-08 拡張)** —
   `Tests/PlayMode/BJJInputProviderPlayModeTests.cs` (4 ケース、PR #19) +
   `Tests/PlayMode/BJJAnimatorBinderPlayModeTests.cs` (3 ケース、binder の
   prev-state cache / lifecycle subscribe / OnDestroy unsubscribe を検証)。
5. ~~**PostProcess + Camera Shake**~~ 🟢 **完了 (2026-04-27)** — `Runtime/Platform/BJJImpactFeedback.cs`
   を追加。
6. ~~**Mixamo Humanoid 代替パス**~~ 🟢 **完了 (2026-05-08, PR #27 / `72bed94`)** —
   Y Bot.fbx の Humanoid retarget、`BJJAvatar.controller`(6 params / 4 states)、
   `BJJ_GameManager` 子への `Avatar_Bottom` / `Avatar_Top` 配置、`BJJAnimatorBinder`
   スロット配線まで完了。Play 検証で `IsBottom` flag、`!IsBottom → TopPosture`
   遷移、`JustParried → Reaction → Idle` パスを確認。Console error/warning 0 件。
7. ~~**Avatar の Top/Bottom 視覚差別化**~~ 🟢 **完了 (2026-05-08, PR #29 / `d730194`)** —
   `Runtime/Visual/BJJAvatarTint.cs` を追加し、`MaterialPropertyBlock` 経由で
   per-instance に `_Color` / `_BaseColor` を上書き。`Avatar_Bottom` を暖色赤、
   `Avatar_Top` を寒色青で識別。共有マテリアル不変 / `.mat` 増殖無し。
8. ~~**Scene cleanup**~~ 🟢 **完了 (2026-05-08, PR #30 / `661bf0b`)** —
   Mixamo 統合時に残留していた primitive blockman ルート (`Characters`) を `SetActive(false)`。
   `Assets/_DebugCaptures/` / `Assets/_Recovery/` を gitignore。
9. ~~**Avatar 向き修正 + OTS カメラ**~~ 🟢 **完了 (2026-05-08, PR #31 / `ea22374`)** —
   旧 Mixamo 配置で Bottom / Top が背中合わせだった。Bottom rot (0,180,0)、Top rot (0,0,0)
   に交換し face-to-face、Main Camera を Bottom 後方 (0.6, 2.0, 4.0) rot (20, 180, 0) に
   再配置して Visual Pillar の "guard-side immersive over-the-shoulder" に整合。
10. ~~**既存テスト fail 3 件の解消**~~ 🟢 **完了 (2026-05-08, PR #32 / `04be976`)** —
    EditMode 268/268 + PlayMode 7/7 全 green。詳細はサマリ §テスト fail 3 件解消 参照。
11. **Gripped state の姿勢ジャンプ解消**(Visual Pillar follow-up、grip-pose anim 入手後が本番)—
    PR #28 で `Gripped` state は Y Bot@Idle (立ちポーズ) 流用のため、`GuardIdle` (座) /
    `TopPosture` (屈) 状態から遷移時にスタンスがジャンプする既知の限界がある。本対応は
    (a) `BottomGripped` / `TopGripped` の 2 ロール特化サブステート追加(8 AnyState 遷移)、
    または (b) Override Layer + 上半身 AvatarMask で grip-pose を上半身のみオーバーレイ、
    のいずれか。後者は専用 grip-pose アニメ入手後にようやく視覚的価値が出る。
12. **M1 playtest 体感確認とフィードバック反映** — PR #28 の 4 件 Inspector knob
    (`caPulseGripped/Parried/GripBroken/DurationMs`) と PR #29 の `tintColor` × `tintStrength`、
    PR #31 の OTS カメラ位置 / FOV を実 playtest で振り直し、テスター反応を本ドキュメントに追記する。
    Gripped state の姿勢ジャンプが実際にどれだけ気になるかも playtest で判定。

---

## 既知の技術債

- `BJJSimulator.asmdef` の `autoReferenced: true` は将来大きくなったら `false` に変える
- テストアセンブリは現状 Editor 専用。PlayMode テストを増やす場合は別 asmdef に分割推奨
- `Packages/manifest.json` の `com.coplaydev.unity-mcp` git 参照はバージョン固定無し
  (`#main`)。再現性が問題になったら `#vX.Y.Z` 等タグ参照に切り替える
- Editor で Play mode 中に MCP plugin の WebSocket session が稀に切断する事象あり
  (PR #27 authoring 中に 1 回観測、`Assets/_Recovery/` 生成)。再現条件は不明、再起動で復旧。
  再発時は `Editor.log` + `_Recovery/` 中身を保全して follow-up issue 化。
