# Stage 2 Unity 移植計画 v1.1

**作成日**: 2026-04-24
**改訂日**: 2026-04-25 (実態調査により §3 を最新化)
**対象エンジン**: Unity 6 (6000.0 LTS)
**前提**:
- [architecture_overview_v1.md §4](./architecture_overview_v1.md) — 型と層の対応表
- [stage2_port_plan_ue5_deprecated_v1.md](./stage2_port_plan_ue5_deprecated_v1.md) — UE5 版(廃止済み。設計意図の参照用)
- Stage 1 の TypeScript 実装 (`src/prototype/web/src/`)

**目的**: Stage 1 のロジック層を **1ファイル = 1 C# ソース** に直訳する手順を確定する。

**非目的**: Unity プロジェクト内部の全詳細設計 (Animator Controller、Shader Graph、
URP PostProcess 等)。それらはコアロジック port 完了後に個別設計。

---

## 1. ファイル対応表

### 1.1 基礎 & 足場

| 項目 | Stage 2 出力 | 状態 |
|---|---|---|
| `Packages/manifest.json` | `com.unity.inputsystem` + `com.unity.test-framework` | 🟢 |
| `ProjectSettings/ProjectSettings.asset` | 最小 YAML (Unity 6 LTS) | 🟢 |
| `Assets/BJJSimulator/Runtime/BJJSimulator.asmdef` | メイン assembly def | 🟢 |
| `Assets/BJJSimulator/Tests/BJJSimulatorTests.asmdef` | テスト assembly def | 🟢 |
| `src/prototype/unity/.gitignore` | Library/ Temp/ 等を除外 | 🟢 |

### 1.2 Core types

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `input/types.ts` | `Runtime/Core/BJJCoreTypes.cs` (`ButtonBit`) | 🟢 |
| `state/hand_fsm.ts` (側・状態 enum) | 同上 (`HandSide` / `HandState`) | 🟢 |
| `state/foot_fsm.ts` (側・状態 enum) | 同上 (`FootSide` / `FootState`) | 🟢 |
| `input/intent.ts` (`GripZone`) | 同上 (`GripZone` — None ordinal 0) | 🟢 |
| sentinel pattern | 同上 (`BJJConst.SentinelTimeMs = long.MinValue`) | 🟢 |

### 1.3 状態機械

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `state/hand_fsm.ts` | `Runtime/State/HandFSM.cs` (`HandFSM` struct + `HandFSMOps`) | 🟢 |
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

### 1.4 入力層

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `input/types.ts` | `Runtime/Input/BJJInputFrame.cs` (`InputFrame` struct) | 🟢 |
| `input/intent.ts` | `Runtime/Input/BJJIntent.cs` | 🟢 |
| `input/intent_defense.ts` | `Runtime/Input/BJJDefenseIntent.cs` | 🟢 |
| `input/layerA.ts` | `Runtime/Input/LayerA.cs` (pure assembler + RawHardwareSnapshot) | 🟢 |
| `input/layerB.ts` | `Runtime/Input/LayerB.cs` | 🟢 |
| `input/layerB_defense.ts` | `Runtime/Input/LayerBDefense.cs` | 🟢 |
| `input/layerD.ts` | `Runtime/Input/LayerD.cs` | 🟢 |
| `input/layerD_defense.ts` | `Runtime/Input/LayerDDefense.cs` | 🟢 |
| `input/transform.ts` | `Runtime/Input/InputTransform.cs` | 🟢 |
| `input/keyboard.ts` / `gamepad.ts` | — (実装差替: New Input System) | 🚫 |

### 1.5 Sim / AI

| Stage 1 ソース | Stage 2 出力 | 状態 |
|---|---|---|
| `sim/fixed_step.ts` | `Runtime/Sim/FixedStep.cs` | 🟢 |
| `ai/opponent_ai.ts` | `Runtime/AI/OpponentAI.cs` | 🟢 |
| `main.ts` | — (`GameManager` MonoBehaviour に分解) | 🚫 |

### 1.6 レンダ / 演出

Stage 1 には Three.js 相当ロジックが含まれるが、Stage 2 では Unity URP + Shader Graph
で再実装する。コアロジック port 完了後の作業。

| Stage 1 相当 | Stage 2 出力 | 状態 |
|---|---|---|
| `scene/blockman.ts` | Skinned mesh + Animator Controller | ⚪ |
| stamina color grading | URP PostProcess Volume | ⚪ |
| `setWindowTint` / `pulseFlash` | PostProcess + Camera Shake | ⚪ |
| HUD / event log / tutorial / pause | UI Toolkit Widget | ⚪ |

### 1.7 テスト

| Stage 1 suite | Stage 2 テスト | 状態 |
|---|---|---|
| `tests/unit/hand_fsm.test.ts` (14 ケース) | `Tests/EditMode/HandFSMTest.cs` | 🟢 |
| `tests/unit/foot_fsm.test.ts` | `Tests/EditMode/FootFSMTest.cs` | 🟢 |
| `tests/unit/posture_break.test.ts` | `Tests/EditMode/PostureBreakTest.cs` | 🟢 |
| `tests/unit/stamina.test.ts` | `Tests/EditMode/StaminaTest.cs` | 🟢 |
| `tests/unit/arm_extracted.test.ts` | `Tests/EditMode/ArmExtractedTest.cs` | 🟢 |
| `tests/unit/judgment_window.test.ts` | `Tests/EditMode/JudgmentWindowTest.cs` | 🟢 |
| `tests/unit/counter_window.test.ts` | `Tests/EditMode/CounterWindowTest.cs` | 🟢 |
| `tests/unit/control_layer.test.ts` | `Tests/EditMode/ControlLayerTest.cs` | 🟢 |
| `tests/unit/layerA.test.ts` (InputTransform + LayerA assembler) | `Tests/EditMode/InputTransformTest.cs` | 🟢 |
| `tests/unit/layerB.test.ts` | `Tests/EditMode/LayerBTest.cs` | 🟢 |
| `tests/unit/layerD.test.ts` | `Tests/EditMode/LayerDTest.cs` | 🟢 |
| `tests/unit/layerD_defense.test.ts` | `Tests/EditMode/LayerDDefenseTest.cs` | 🟢 |
| `tests/unit/scenarios.test.ts` | `Tests/EditMode/ScenariosTest.cs` | 🟢 |
| 他 `tests/unit/*.test.ts` (pass/cut — 未テスト) | — | ⚪ |
| `tests/scenario/*.test.ts` (6 ファイル) | — (GameManager phase) | ⚪ |

---

## 2. C# port 規約

### 2.1 struct vs class

FSM の状態スナップショット (HandFSM, FootFSM 等) は **value type (struct)** とする。
理由: Stage 1 の `Readonly<T>` + `Object.freeze` の意図(不変スナップショット)を
C# で表現する最も自然な手段であり、GC 負荷も最小。

- **OK**: `HandFSM next = prev; next.State = ...; return next;`
- **NG**: `class HandFSM` — ヒープ allocation + 参照共有でスナップショット意味論が壊れる

### 2.2 static class for pure functions

状態遷移関数は `static class XxxOps` (または `static class XxxFSM`) に集める。
`MonoBehaviour` / `ScriptableObject` を純粋ロジック層に混ぜない。

```csharp
// 良い例
public static class HandFSMOps
{
    public static HandFSM Tick(HandFSM prev, HandTickInput input, List<HandTickEvent> events) { ... }
}
```

### 2.3 イベント: fat struct パターン

TS の tagged union (`{ kind: "GRIP_BROKEN"; ... } | { kind: "GRIPPED"; ... }`) は
C# では **1 つの struct + Kind enum** にマップする。`Kind` が関係するフィールドを決定。

```csharp
public struct HandTickEvent
{
    public HandEventKind    Kind;
    public HandSide         Side;
    public GripZone         Zone;
    public GripBrokenReason GripBrokenReason; // Kind == GripBroken のときのみ有効
}
```

### 2.4 タイムスタンプ: long (ms)

TS の `number` (float64 ms) は C# では `long` にマップする。
`float` や `double` は避ける — 大きな値での精度落ちがある。

### 2.5 sentinel guard (overflow 対策)

`BJJConst.SentinelTimeMs = long.MinValue` を使う初期値パターン。
`long.MinValue` を含む減算は overflow するため、必ず使用前に sentinel チェックを入れる:

```csharp
bool hasParryMemory = prev.LastParriedAtMs != BJJConst.SentinelTimeMs;
bool recentlyParried = hasParryMemory && (nowMs - prev.LastParriedAtMs) < t.ShortMemoryMs;
```

### 2.6 nullable の回避

`GripZone.None` (ordinal 0) を「ターゲットなし」の sentinel として使う。
`GripZone?` nullable は使わない。理由: struct は default 初期化で None に落ちるため
余分な null チェックが不要で、UE5 port との対称性も保てる。

---

## 3. 次の作業単位 (小さい順)

**現状 (2026-04-25)**: コアロジック (state / input / sim / AI) の C# port は
すべて 🟢。**残りは「実装はあるがテストが無い」6 ファイルの NUnit テスト追加**
と、それ以降の Unity 固有作業 (シーン / ビジュアル / GameManager) のみ。

### 3.1 単体テスト負債 (合計 ~80 ケース)

| 優先 | C# 実装 | TS テスト原本 | ケース数 |
|---|---|---|---|
| 1 | `Runtime/State/CutAttempt.cs` | `tests/unit/cut_attempt.test.ts` | 10 |
| 2 | `Runtime/State/PassAttempt.cs` | `tests/unit/pass_attempt.test.ts` | 11 |
| 3 | `Runtime/Input/LayerA.cs` | `tests/unit/layerA.test.ts` (assembler 部分) | 17 |
| 4 | `Runtime/Input/LayerBDefense.cs` | `tests/unit/layerB_defense.test.ts` | 23 |
| 5 | `Runtime/Sim/FixedStep.cs` | `tests/unit/fixed_step.test.ts` | 6 |
| 6 | `Runtime/AI/OpponentAI.cs` | `tests/unit/opponent_ai.test.ts` | 18 |

各ケースは `[Test]` メソッドとして `Tests/EditMode/XxxTest.cs` に追加する。
HandFSMTest.cs と同じ NUnit + AAA スタイルを踏襲。

### 3.2 結合テスト (シナリオ層)

`tests/scenario/*.test.ts` (6 ファイル, ~33 ケース) は GameState を一巡させる
スモークテスト。Unity 側でも `Tests/EditMode/Scenario/*.cs` として追加するが、
3.1 完了後でよい。

### 3.3 Unity 固有 (テスト負債解消後)

1. `GameManager` MonoBehaviour — `main.ts` の rAF ループを `Update` に分解
2. New Input System の `BJJInputActions.inputactions` 定義 + `LayerA` への wire
3. Skinned mesh + Animator (Stage 1 blockman 相当)
4. URP Volume profile — stamina warm shift / judgment-window vignette
5. UI Toolkit でコーチ HUD / イベントログ移植

---

## 4. 既知の技術債

- `BJJSimulator.asmdef` の `autoReferenced: true` は将来大きくなったら `false` に変える
- テストアセンブリは現状 Editor 専用 (`includePlatforms: ["Editor"]`)。
  Runtime でも実行したい場合は Play Mode Tests に移行する
- `Packages/manifest.json` のバージョン固定 (`1.11.2`, `1.4.5`) は
  Unity Hub からプロジェクトを開いたとき自動解決される。バージョンアップは
  `manifest.json` 直接編集で行う
