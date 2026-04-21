# Stage 2 UE5 移植計画 v1.1

**前提**:
- [architecture_overview_v1.md §4](./architecture_overview_v1.md) — 型と層の対応表
- [Visual Pillar Document v1.0](../visual/Visual_Pillar_Document_v1.docx) — 最終的な見た目の要件
- Stage 1 の TypeScript 実装(`src/prototype/web/src/`)

**目的**: Stage 1 のロジック層を **1ファイル = 1 C++ ヘッダ/ソース ペア** に直訳する手順を確定する。Stage 2 着手時の「どこから手をつけるか」のブロッカーを解消する。

**非目的**: UE5 プロジェクト内部の全詳細設計(Blueprint 構造、Animation Graph、Material グラフなど)。それらはプロジェクト生成後に個別設計。

**v1.0 → v1.1 の主な改訂**(2026-04-21 セルフレビュー反映):
- 入力層の「破棄」を「実装差替(インタフェース保持)」に修正
- `SimEvent` を `TVariant` から enum + payload struct 方針へ変更(UPROPERTY / Blueprint 露出 / リプレイ可能性)
- シナリオの compile guard を `!UE_BUILD_SHIPPING` + console command に変更
- Vec2 精度、sentinel 比較、TimeScale の 3 パターンを §2 に追加
- §2.3 を「純ロジック = namespace / Blueprint 露出 = Library」の二段に
- §3 ステップ 7 を `7a PostProcess` / `7b Character` に分離
- モジュール名を `BJJSimulator` に統一
- §5 checklist に audio / input latency / Visual Logger を追加、視覚 3 系統を分離

**表記規則**: 以下の `Public/...` / `Private/...` パスはすべて `Source/BJJSimulator/` 配下。API マクロは `BJJSIMULATOR_API`、クラス接頭辞は `FBJJ*` / `UBJJ*` / `EBJJ*` / `ABJJ*`。

---

## 0. 移植の基本ルール

1. **ロジック層は直訳**: `src/prototype/web/src/state/*.ts` と `src/prototype/web/src/input/layerB*.ts` は、意味を変えずに C++ 純関数へ 1対1 で置き換える。
2. **テストも同伴移植**: 対応する `.test.ts` は UE5 の Automation Testing で同じアサーションを書き直す。Stage 1 の 268 テストが Stage 2 でも緑なら、ロジックは壊れていない。
3. **プラットフォーム層は破棄**: `main.ts` / `scene/*` / `sim/fixed_step.ts` は UE5 側で新規実装。Three.js 固有のものは捨てる。
4. **型名は接頭辞を付ける**: UE5 慣習に従い、struct は `F`、クラスは `U`/`A`、enum は `E`。例: `HandFSM` → `FBJJHandFSM`。

---

## 1. ファイル対応表

### 1.1 入力層

**Layer A の契約は維持**: `sample(nowMs) → FBJJInputFrame`。EnhancedInput は Layer A の**実装詳細**に隠し、Layer B 以降のロジック/テストは Stage 1 と同形で残す。これにより 268 テストが Stage 2 Automation でも 1 対 1 で緑化できる。

| Stage 1 TS | Stage 2 C++ | 変換 |
|---|---|---|
| `input/types.ts` | `Public/Input/BJJInputFrame.h` | `InputFrame` → `USTRUCT FBJJInputFrame`。`ButtonBit` → `UENUM bit flags` |
| `input/keyboard.ts` | **実装差替** | EnhancedInput の `InputAction` に置き換え。Layer A の内部実装、**Layer B から見えない** |
| `input/gamepad.ts` | **実装差替** | 同上。`InputMappingContext` を XInput / DualSense 毎に定義 |
| `input/layerA.ts` | `Public/Input/BJJLayerA.h` + `Private/Input/BJJLayerA.cpp` | `ABJJPlayerController` が EnhancedInput のハンドラで内部 bit/軸を蓄積、独自 60Hz タイマで `sample()` して `FBJJInputFrame` を返す(固定ポーリングの semantics を死守) |
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
| `state/posture_break.ts` | `Public/State/BJJPostureBreak.h` + `.cpp` | `Vec2` → `FVector2d`(double 版)。§2.5 参照 |
| `state/stamina.ts` | `Public/State/BJJStamina.h` + `.cpp` | スカラ float のみ、純関数 |
| `state/arm_extracted.ts` | `Public/State/BJJArmExtracted.h` + `.cpp` | 同型 |
| `state/judgment_window.ts` | `Public/State/BJJJudgmentWindow.h` + `.cpp` | `Technique` → `UENUM EBJJTechnique`; timing 定数は `constexpr` |
| `state/counter_window.ts` | `Public/State/BJJCounterWindow.h` + `.cpp` | 同型 |
| `state/pass_attempt.ts` | `Public/State/BJJPassAttempt.h` + `.cpp` | `PassAttemptState` タグ付きユニオンは `TVariant<FIdle, FInProgress>` |
| `state/cut_attempt.ts` | `Public/State/BJJCutAttempt.h` + `.cpp` | 同型 |
| `state/control_layer.ts` | `Public/State/BJJControlLayer.h` + `.cpp` | `Initiative` → `UENUM EBJJInitiative` |
| `state/game_state.ts` | `Public/State/BJJGameState.h` + `.cpp` | `stepSimulation` → `UBJJGameStateLibrary::Step`; `SimEvent` は **enum + payload USTRUCT** (§2.2)。`TVariant` は UPROPERTY 化不可のためリプレイ/Blueprint/シリアライズと両立しない |
| `state/scenarios.ts` | `Public/State/BJJScenarios.h` + `.cpp` | **`#if !UE_BUILD_SHIPPING` ガード**で Development / Test ビルドに含め、Shipping で strip。`Exec` マークしたコンソールコマンド `BJJ.LoadScenario <Name>` から呼べる形に |

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
struct BJJSIMULATOR_API FBJJHandFSM
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

v1.0 では `TVariant` を推奨していたが、UE5 で `SimEvent` をリプレイ/シリアライズ/Blueprint に露出したいことが判明したため、**enum + payload struct** を標準パターンとする。`TVariant` は純 C++ 内部ロジック限定。

**C++**(v1.1 標準: enum + payload):
```cpp
UENUM(BlueprintType)
enum class EBJJCutSlotKind : uint8 { Idle, InProgress };

USTRUCT(BlueprintType)
struct FBJJCutAttemptSlot
{
  GENERATED_BODY()

  UPROPERTY(BlueprintReadOnly) EBJJCutSlotKind Kind = EBJJCutSlotKind::Idle;

  // Payload: Kind == InProgress のときのみ意味を持つ。Idle のとき
  // StartedMs は INT64_MIN で埋めておく(sentinel、§2.5)。
  UPROPERTY(BlueprintReadOnly) int64 StartedMs = INT64_MIN;
  UPROPERTY(BlueprintReadOnly) EBJJHandSide TargetAttackerSide = EBJJHandSide::Left;
  UPROPERTY(BlueprintReadOnly) EBJJGripZone TargetZone = EBJJGripZone::None;
};
```

網羅性は `switch(Slot.Kind)` + コンパイラ警告で担保。`SimEvent` のように variant 数が多い場合は、全 payload フィールドを一つの struct に並べる「fat struct」パターンが最も素直(UPROPERTY + 直列化が動く、Visual Logger に出しやすい)。

**C++**(純 C++ 内部限定の alt: `TVariant`):
```cpp
struct FCutSlotIdle {};
struct FCutSlotInProgress { int64 StartedMs; /*…*/ };
using FCutAttemptSlotInternal = TVariant<FCutSlotIdle, FCutSlotInProgress>;
```

`TVariant::Visit` で網羅性が型レベルで得られる代わりに UPROPERTY 化不可。**FSM の public state には使わない。**内部計算の一時変数用途のみ。

### 2.3 純関数の tick

**TS**:
```ts
export function tickHand(prev: HandFSM, input: HandTickInput): {
  next: HandFSM;
  events: readonly HandTickEvent[];
}
```

純ロジックは **Blueprint 露出が必要か** で 2 つに分岐する。

**C++ デフォルト: namespace + free `static`**(Blueprint 露出不要、コンパイル依存最小):
```cpp
// Public/State/BJJHandFSM.h
namespace BJJ::HandFSM
{
  void Tick(
      const FBJJHandFSM& Prev,
      const FBJJHandTickInput& Input,
      FBJJHandFSM& OutNext,
      TArray<FBJJHandTickEvent>& OutEvents);
}
```

**C++ Blueprint 露出版: `UBlueprintFunctionLibrary`**(デザイナが調整/可視化したい場合):
```cpp
UCLASS()
class BJJSIMULATOR_API UBJJHandFSMLibrary : public UBlueprintFunctionLibrary
{
  GENERATED_BODY()
public:
  UFUNCTION(BlueprintCallable, Category = "BJJ|HandFSM")
  static void Tick(
      const FBJJHandFSM& Prev,
      const FBJJHandTickInput& Input,
      FBJJHandFSM& OutNext,
      TArray<FBJJHandTickEvent>& OutEvents);
};
```

基準は **「Blueprint でデバッグ表示 or デザイナ調整をする必要があるか」**。tick 自体を Blueprint から叩くことは普通ないが、Step の入出力を Blueprint で可視化したい場合は Library 側を薄くかぶせる。

戻り値で構造体ペアを返す代わりに out パラメータ。TS の `readonly` array は `TArrayView<const T>` を引数で受け、`TArray<T>&` で返す。

### 2.4 タイムベース

Stage 1 は `nowMs: number`(double millis since epoch)を引き回している。Stage 2 は:
- 物理フレーム時刻: `FPlatformTime::Seconds()` → 内部は `double` で秒
- ゲームロジックの `nowMs`: **`int64` のミリ秒に丸めて保持**。浮動小数点誤差を FSM 遷移から排除する
- 判断窓のスロー中は `DeltaTimeMs = FMath::RoundToInt(RealDeltaSeconds * 1000 * TimeScale)` で離散化

Stage 1 の `FIXED_STEP_MS = 1000/60` は Stage 2 でも維持。`FTickFunction` の `TickInterval` ではなく **独自固定ステップループを `ABJJGameMode` 内に持つ**(アニメブループリントの可変フレームと分離する)。

**TimeScale / スローモの実装手段(明示)**: `AWorldSettings::SetTimeDilation` や `FApp::SetUseFixedTimeStep` は使わない。判断窓中の `scale = 0.3` は **固定ステップループ内で `GameDtMs = FixedStepMs * scale` を自前計算し、Layer A/B のサンプリングは `realDt` のまま動かす**(Stage 1 と同じ分離)。これにより「スローモ中も入力ポーリングは 60 Hz で効く」という M1 critical の感覚が UE5 でも維持される。AnimBP/VFX は `UWorld::DeltaSeconds` をそのまま使うので、スローモをそこに波及させたい場合は PostProcess の Radial Blur / Anim Rate Scaling で別途表現する。

### 2.5 Vec2 精度と sentinel 値

**Vec2 → FVector2d (double)**: `FVector2D` の実体は 5.0 以降 `FVector2f` か `FVector2d` で切り替わる(precision マクロ依存)。Stage 1 は 64-bit float で動いており、`posture_break` の decay 合成で小さな誤差が効く場面がある(例: 閾値 0.4 近傍で技が出る/出ない)。**posture_break と時刻を跨ぐ係数計算は `FVector2d` / `double` で固定**する。`FVector2D` のままでも動くが、数値回帰が起きたとき追跡しづらい。

**Sentinel 値**: Stage 1 は `Number.NEGATIVE_INFINITY` を「まだ記録されていない」印として使う(`lastParriedAtMs` / `armExtracted.leftSetAtMs` 等)。C++ 直訳:

| TS | C++ | 比較パターン |
|---|---|---|
| `Number.NEGATIVE_INFINITY` | `INT64_MIN`(`int64` 時刻用) | `Prev.LastParriedAtMs == INT64_MIN` で「未記録」を判定、`nowMs - Prev.LastParriedAtMs < 400` の式に巻き込まない |
| `null`(optional enum) | `EBJJGripZone::None` のような sentinel 値、または `TOptional<T>` | None 値は enum 定義の先頭に置いて default 初期化を sentinel 扱いにする |

誤ったパターン: `if (nowMs - Prev.LastParriedAtMs < 400)` — `INT64_MIN` を含むと未定義挙動(オーバーフロー)。必ず `if (Prev.LastParriedAtMs != INT64_MIN && nowMs - Prev.LastParriedAtMs < 400)` と書く。

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
   ├── [7a] PostProcess   ポスト処理マテリアル(暖色シフト + window vignette)
   │         ↑ アート依存なし、art を待たずに進められる
   │
   └── [7b] Character     ACharacter + Animation Blueprint + Control Rig
             ↑ モキャップ / リターゲットが必要(クリティカルパス)
      ↓
[8] テスト合流    Automation Test で 268 ケース緑化
```

**[1]〜[4] + [7a] は UE5 エディタなしでビルドできるユニット**(Character を待たずに CI で回せる)。[7b] は art asset ドロップを待つ間ブロックするので、道具立てができるまで [1]–[6] + [7a] を並行で進める。[5] の PlayerController と [7b] の Character は分けて書く(BlueprintBase を噛ませる)。

---

## 4. 明示的にやらないこと(Stage 2 v1)

- **リプレイ / ロールバック** — Stage 1 で `GameState` を immutable にしたのは将来のため。Stage 2 v1 では録画だけ、巻き戻しはしない
- **ネット対戦** — オフライン 1P vs AI のみ。Rollback netcode は v2
- **フルボディ IK(グリップファイト)** — UE5 Control Rig + Motion Matching で近似。完全物理はしない(M1 gate 15/20 を狙うにはこれで十分と判断)
- **多部位ダメージ** — ラグドール関節別は v2
- **視覚化の完全移植** — Stage 1 の debug HUD は Stage 2 では出さない。色調整のみでプレイヤーに状態を伝える

---

## 5. 検証チェックリスト(Stage 2 着手者向け)

### 5.1 セットアップ
- [ ] UE5.5 以上でプロジェクト雛形を作成、C++ モード
- [ ] `Source/BJJSimulator/Public/Core` / `Input` / `State` / `Sim` / `AI` のサブフォルダを作成
- [ ] `BJJSimulator.Build.cs` に `Core`, `CoreUObject`, `Engine`, `EnhancedInput` を入れる

### 5.2 ロジック移植
- [ ] `src/prototype/web/src/state/hand_fsm.ts` と同一のテストを Automation Test で写経 → 緑を確認
- [ ] §1 のテーブル上から順に、各ペアの移植 + テストを繰り返す
- [ ] §3 手順 [8] で 268 ケース合流時に diff を取って意味的に同じことを確認
- [ ] sentinel 比較パターン(§2.5)で `INT64_MIN` ガードを忘れていないかレビュー

### 5.3 入力遅延(M1 critical)
- [ ] `r.OneFrameThreadLag=0` を `DefaultEngine.ini` に追加(GPU queue 最大 1 frame 化)
- [ ] Forward Rendering を検討(Deferred よりレイテンシ短、ただしビジュアル要件次第)
- [ ] Gamepad polling interval を 60 Hz に合わせる(EnhancedInput 経由でも Layer A の実装で保証)
- [ ] **手動測定**: grip 入力 → GRIPPED event → 画面反映までの end-to-end を 60 fps 固定で 50 ms 以内(M1 grip-fight feel の目標)

### 5.4 ビジュアル(3 系統、独立工程)
- [ ] **PostProcess**: Stage 1 の暖色シフト shader を PostProcessVolume の Global Color Grading / Split-Toning で再実装(stamina fatigue 連動)。判断窓ビネット/Radial Blur も同一マテリアル
- [ ] **Character Material (SSS)**: 肌のサブサーフェス散乱を Subsurface Profile で設定(Visual Pillar §2.1)。Character rig のマテリアル側
- [ ] **Chaos Cloth (Gi)**: 道着のクロスシム。`SkeletalMeshComponent` にクロスアセットをバインド。Character [7b] 工程内で実施
- [ ] **Lumen**: Project Settings → Rendering → Global Illumination = Lumen、Reflections = Lumen。エンジン設定、Material Graph 不要

### 5.5 デバッグ/可観測性
- [ ] **Visual Logger**: Stage 1 の event log panel 相当を `FVisualLogger::EventLog` に落とす。各 `SimEvent` を `UE_VLOG_*` で発火
- [ ] **Gameplay Debugger**: `FGameplayDebuggerCategory_BJJ` で GameState の抜粋(FSM 状態、posture_break、stamina)を in-game 表示
- [ ] **Scenarios**: コンソールコマンド `BJJ.LoadScenario SCISSOR_READY` 等を用意(§1.2)

### 5.6 音響(M1 critical だが後付け可)
- [ ] Stage 1 の主要 SimEvent(`GRIPPED` / `PARRIED` / `WINDOW_OPENING` / `TECHNIQUE_CONFIRMED` / `CUT_SUCCEEDED`)に対応する SoundCue プレースホルダを用意
- [ ] `UGameplayStatics::PlaySoundAtLocation` を event dispatch と同じ場所で呼ぶ。配置だけ決めておき、音源差し替えは後工程

---

## 6. 未決事項(v1.2)

- **Blueprint 露出範囲**: どこまで `UPROPERTY(BlueprintReadOnly)` を付けるか(デザイナの調整範囲)
- **リプレイデータ形式**: Stage 1 の `SimEvent` 配列をそのままシリアライズするか、デルタ保存か(enum + payload 形式決定後に確定)
- **テスト並列化**: 268 Automation Test が逐次だと秒数が辛い場合の対応(`EAutomationTestFlags::ApplicationContextMask` の分離)
- **EnhancedInput MappingContext の role 切替**: Bottom / Top / Spectate 切替時に `UEnhancedInputLocalPlayerSubsystem::AddMappingContext` を swap するか、単一 context + role 分岐で動的解釈するか
- **ビルド構成の細部**: Development 固有(Visual Logger, scenarios, debug HUD)と Shipping 固有(最小 HUD、リプレイ保存)の include マトリクス
- **プラットフォーム**: Windows DirectX12 以外の検証(Mac, console)は v1 スコープ外
- **CI**: GitHub Hosted runner は UE5 非対応。セルフホスト or Horde が必要。ロジック専用の CI(Commandlet + Automation)なら GitHub Hosted でも回せる可能性あり、要調査
