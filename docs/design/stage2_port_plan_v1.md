# Stage 2 UE5 移植計画 v1.0

**前提**:
- [architecture_overview_v1.md §4](./architecture_overview_v1.md) — 型と層の対応表
- [Visual Pillar Document v1.0](../visual/Visual_Pillar_Document_v1.docx) — 最終的な見た目の要件
- Stage 1 の TypeScript 実装(`src/prototype/web/src/`)

**目的**: Stage 1 のロジック層を **1ファイル = 1 C++ ヘッダ/ソース ペア** に直訳する手順を確定する。Stage 2 着手時の「どこから手をつけるか」のブロッカーを解消する。

**非目的**: UE5 プロジェクト内部の全詳細設計(Blueprint 構造、Animation Graph、Material グラフなど)。それらはプロジェクト生成後に個別設計。

---

## 0. 移植の基本ルール

1. **ロジック層は直訳**: `src/prototype/web/src/state/*.ts` と `src/prototype/web/src/input/layerB*.ts` は、意味を変えずに C++ 純関数へ 1対1 で置き換える。
2. **テストも同伴移植**: 対応する `.test.ts` は UE5 の Automation Testing で同じアサーションを書き直す。Stage 1 の 268 テストが Stage 2 でも緑なら、ロジックは壊れていない。
3. **プラットフォーム層は破棄**: `main.ts` / `scene/*` / `sim/fixed_step.ts` は UE5 側で新規実装。Three.js 固有のものは捨てる。
4. **型名は接頭辞を付ける**: UE5 慣習に従い、struct は `F`、クラスは `U`/`A`、enum は `E`。例: `HandFSM` → `FBJJHandFSM`。

---

## 1. ファイル対応表

### 1.1 入力層

| Stage 1 TS | Stage 2 C++ | 変換 |
|---|---|---|
| `input/types.ts` | `Public/Input/BJJInputFrame.h` | `InputFrame` → `USTRUCT FBJJInputFrame`。`ButtonBit` → `UENUM bit flags` |
| `input/keyboard.ts` | **破棄** | UE5 Enhanced Input Subsystem が代替 |
| `input/gamepad.ts` | **破棄** | 同上 |
| `input/layerA.ts` | `Public/Input/BJJLayerA.h` + `Private/Input/BJJLayerA.cpp` | `APlayerController` サブクラスにポーリングを置き、毎フレーム `FBJJInputFrame` を生成 |
| `input/intent.ts` | `Public/Input/BJJIntent.h` | `FBJJIntent` + `FBJJGripIntent` 等の `USTRUCT` 群 |
| `input/intent_defense.ts` | `Public/Input/BJJDefenseIntent.h` | 同上 |
| `input/layerB.ts` | `Public/Input/BJJLayerB.h` + `.cpp` | 純粋関数は `UBJJLayerBLibrary` の `static` メソッドに |
| `input/layerB_defense.ts` | `Public/Input/BJJLayerBDefense.h` + `.cpp` | 同上 |
| `input/layerD.ts` | `Public/Input/BJJLayerD.h` + `.cpp` | 同上 |
| `input/layerD_defense.ts` | `Public/Input/BJJLayerDDefense.h` + `.cpp` | 同上 |
| `input/transform.ts` | `Public/Input/BJJInputTransform.h` + `.cpp` | 小ヘルパ群、`namespace` 関数に |

### 1.2 状態機械

| Stage 1 TS | Stage 2 C++ | 変換 |
|---|---|---|
| `state/hand_fsm.ts` | `Public/State/BJJHandFSM.h` + `.cpp` | `HandState` → `UENUM EHandState`; FSM 構造体は `USTRUCT FHandFSM`; tick 関数は `static` |
| `state/foot_fsm.ts` | `Public/State/BJJFootFSM.h` + `.cpp` | 同型 |
| `state/posture_break.ts` | `Public/State/BJJPostureBreak.h` + `.cpp` | `Vec2` → `FVector2D` |
| `state/stamina.ts` | `Public/State/BJJStamina.h` + `.cpp` | スカラ float のみ、純関数 |
| `state/arm_extracted.ts` | `Public/State/BJJArmExtracted.h` + `.cpp` | 同型 |
| `state/judgment_window.ts` | `Public/State/BJJJudgmentWindow.h` + `.cpp` | `Technique` → `UENUM EBJJTechnique`; timing 定数は `constexpr` |
| `state/counter_window.ts` | `Public/State/BJJCounterWindow.h` + `.cpp` | 同型 |
| `state/pass_attempt.ts` | `Public/State/BJJPassAttempt.h` + `.cpp` | `PassAttemptState` タグ付きユニオンは `TVariant<FIdle, FInProgress>` |
| `state/cut_attempt.ts` | `Public/State/BJJCutAttempt.h` + `.cpp` | 同型 |
| `state/control_layer.ts` | `Public/State/BJJControlLayer.h` + `.cpp` | `Initiative` → `UENUM EBJJInitiative` |
| `state/game_state.ts` | `Public/State/BJJGameState.h` + `.cpp` | `stepSimulation` → `UBJJGameStateLibrary::Step`; `SimEvent` は `TVariant` の配列 |
| `state/scenarios.ts` | `Public/State/BJJScenarios.h` + `.cpp` | デバッグビルド限定、`#if WITH_EDITOR` ガード |

### 1.3 シミュレーション/ AI

| Stage 1 TS | Stage 2 C++ | 変換 |
|---|---|---|
| `sim/fixed_step.ts` | `Public/Sim/BJJFixedStep.h` + `.cpp` | `GameMode` 内の固定ステップ駆動。実時間は `FPlatformTime::Seconds()` |
| `ai/opponent_ai.ts` | `Public/AI/BJJOpponentAI.h` + `.cpp` | 優先度ルールは `TArray<FAIRule>` として表現、順序走査 |
| `main.ts` | **破棄** | UE5 側は `AGameModeBase::Tick` + Widget Blueprint (HUD) に分割 |

### 1.4 レンダリング / シーン

| Stage 1 TS | Stage 2 UE5 | 変換 |
|---|---|---|
| `scene/blockman.ts` | **全面新規** | `ACharacter` + Animation Blueprint。ブロックマンではなくフォトリアル道着キャラ |
| overlay shader (`uStaminaFatigue`) | PostProcess Material | 暖色シフトを PostProcessVolume の Global Color Grading / Split-Toning で実装(Visual Pillar 2.4) |
| `setWindowTint` | PostProcess Material パラメータ | 窓中のビネット + スロー用 Radial Blur |
| `pulseFlash` / `pulseShake` | Blueprint イベント + CineCamera Shake | 短時間演出。`UGameplayStatics::PlayWorldCameraShake` で代替 |
| HUD(debug HUD / event log / tutorial / pause / timer) | UMG Widget Blueprint | デバッグHUDは `WITH_EDITOR` 限定、プレイヤー向けHUDはタイマーのみ可視(他はカラーグレーディングで伝える) |

### 1.5 テスト

| Stage 1 TS | Stage 2 C++ |
|---|---|
| `tests/unit/*.test.ts` | `Source/BJJTests/Private/*Test.cpp` + `IMPLEMENT_SIMPLE_AUTOMATION_TEST` |
| `tests/scenario/*.test.ts` | 同上、`EAutomationTestFlags::ProductFilter` でスモーク/シナリオ分離 |

268 テスト全部が Stage 2 Automation Test で緑になるまでロジック移植は完了扱いしない。

---

## 2. 型の変換パターン(具体例)

### 2.1 Readonly 構造体

**TS**:
```ts
export type HandFSM = Readonly<{
  side: HandSide;
  state: HandState;
  target: GripZone | null;
  stateEnteredMs: number;
  reachDurationMs: number;
  lastParriedZone: GripZone | null;
  lastParriedAtMs: number;
}>;
```

**C++**:
```cpp
USTRUCT(BlueprintType)
struct BJJ_API FBJJHandFSM
{
  GENERATED_BODY()

  UPROPERTY(BlueprintReadOnly) EBJJHandSide Side = EBJJHandSide::Left;
  UPROPERTY(BlueprintReadOnly) EBJJHandState State = EBJJHandState::Idle;
  // GripZone は optional — None sentinel を enum 側に持たせるか TOptional を使う
  UPROPERTY(BlueprintReadOnly) EBJJGripZone Target = EBJJGripZone::None;
  UPROPERTY(BlueprintReadOnly) int64 StateEnteredMs = 0;
  UPROPERTY(BlueprintReadOnly) int32 ReachDurationMs = 0;
  UPROPERTY(BlueprintReadOnly) EBJJGripZone LastParriedZone = EBJJGripZone::None;
  UPROPERTY(BlueprintReadOnly) int64 LastParriedAtMs = INT64_MIN;
};
```

**不変性の扱い**: UPROPERTY は本物の `const` ではないが、純関数側で「`const FBJJHandFSM&` を受け取り、新しい `FBJJHandFSM` を返す」規約で運用する。レビューで「fieldを直接代入しているコード」を弾く。

### 2.2 タグ付きユニオン

**TS**:
```ts
export type CutAttemptSlot =
  | { kind: "IDLE" }
  | { kind: "IN_PROGRESS"; startedMs: number; targetAttackerSide: "L" | "R"; targetZone: GripZone };
```

**C++** (推奨: `TVariant`):
```cpp
struct FCutSlotIdle {};

USTRUCT()
struct FCutSlotInProgress
{
  GENERATED_BODY()
  int64 StartedMs = 0;
  EBJJHandSide TargetAttackerSide = EBJJHandSide::Left;
  EBJJGripZone TargetZone = EBJJGripZone::None;
};

using FCutAttemptSlot = TVariant<FCutSlotIdle, FCutSlotInProgress>;
```

`TVariant::Visit` を使えば TS の `switch (slot.kind)` と同じ網羅性が型レベルで確保できる。Blueprint 露出が必要な場合は enum + optional payload に退化させる(例: `ECutSlotKind` + `FCutSlotInProgress` が `TOptional`)。

### 2.3 純関数の tick

**TS**:
```ts
export function tickHand(prev: HandFSM, input: HandTickInput): {
  next: HandFSM;
  events: readonly HandTickEvent[];
}
```

**C++** (推奨: 静的メソッド):
```cpp
UCLASS()
class BJJ_API UBJJHandFSMLibrary : public UBlueprintFunctionLibrary
{
  GENERATED_BODY()
public:
  static void Tick(
      const FBJJHandFSM& Prev,
      const FBJJHandTickInput& Input,
      FBJJHandFSM& OutNext,
      TArray<FBJJHandTickEvent>& OutEvents);
};
```

戻り値で構造体ペアを返す代わりに out パラメータ。TS の `readonly` array は `TArrayView<const T>` を引数で受け、`TArray<T>&` で返す。

### 2.4 タイムベース

Stage 1 は `nowMs: number`(double millis since epoch)を引き回している。Stage 2 は:
- 物理フレーム時刻: `FPlatformTime::Seconds()` → 内部は `double` で秒
- ゲームロジックの `nowMs`: `int64` のミリ秒に丸めて保持。浮動小数点誤差をFSM遷移から排除する
- 判断窓のスロー中は `DeltaTimeMs = FMath::RoundToInt(RealDeltaSeconds * 1000 * TimeScale)` で離散化

Stage 1 の `FIXED_STEP_MS = 1000/60` は Stage 2 でも維持。`FTickFunction` の `TickInterval` ではなく独自固定ステップループを `GameMode` 内に持つ(アニメブループリントの可変フレームと分離する)。

---

## 3. 実装順序

Stage 1 と同じ順序で並行的に進める:

```
[1] 基礎型         Core types (FBJJInputFrame / FBJJIntent / FBJJGripZone / ...)
      ↓
[2] 純FSM         FBJJHandFSM / FBJJFootFSM / FBJJPostureBreak / FBJJStamina / FBJJArmExtracted
      ↓
[3] 集約          FBJJGameState + stepSimulation 相当
      ↓
[4] 制御系        FBJJGuardFSM / FBJJControlLayer / FBJJJudgmentWindow / FBJJCounterWindow
      ↓
[5] 入力層        ABJJPlayerController → FBJJInputFrame → BJJLayerB
      ↓
[6] 固定ステップ    ABJJGameMode 内の固定タイムステップループ
      ↓
[7] Blueprint     ACharacter rigs + Animation Blueprint + PostProcess Material
      ↓
[8] テスト合流    Automation Test で 268 ケース緑化
```

**[1]〜[4] は UE5 エディタなしでビルドできるユニット**。CI で `UE5 Commandlet` 経由の自動テストを回せる。[5] 以降はエディタと Blueprint が要る。

---

## 4. 明示的にやらないこと(Stage 2 v1)

- **リプレイ / ロールバック** — Stage 1 で `GameState` を immutable にしたのは将来のため。Stage 2 v1 では録画だけ、巻き戻しはしない
- **ネット対戦** — オフライン 1P vs AI のみ。Rollback netcode は v2
- **フルボディ IK(グリップファイト)** — UE5 Control Rig + Motion Matching で近似。完全物理はしない(M1 gate 15/20 を狙うにはこれで十分と判断)
- **多部位ダメージ** — ラグドール関節別は v2
- **視覚化の完全移植** — Stage 1 の debug HUD は Stage 2 では出さない。色調整のみでプレイヤーに状態を伝える

---

## 5. 検証チェックリスト(Stage 2 着手者向け)

- [ ] UE5.5 以上でプロジェクト雛形を作成、C++ モード
- [ ] `Source/BJJ/Public/Core` / `Input` / `State` / `Sim` / `AI` のサブフォルダを作成
- [ ] `BJJ.Build.cs` に `Core`, `CoreUObject`, `Engine`, `EnhancedInput` を入れる
- [ ] `src/prototype/web/src/state/hand_fsm.ts` と同一のテストを Automation Test で写経 → 緑を確認
- [ ] §1 のテーブル上から順に、各ペアの移植 + テストを繰り返す
- [ ] §3.8 で 268 ケース合流時に diff を取って意味的に同じことを確認
- [ ] Visual Pillar Document を読んで Stage 2 専用の視覚要件(SSS、Gi cloth、Lumen)を PostProcess Material + Material Graph に落とす

---

## 6. 未決事項(v1.1)

- **ビルド構成**: Development vs Shipping での scenarios のガード方法詳細
- **Blueprint 露出範囲**: どこまで `UPROPERTY(BlueprintReadOnly)` を付けるか(デザイナの調整範囲)
- **リプレイデータ形式**: Stage 1 の `SimEvent` 配列をそのままシリアライズするか、デルタ保存か
- **テスト並列化**: 268 Automation Test が逐次だと秒数が辛い場合の対応
