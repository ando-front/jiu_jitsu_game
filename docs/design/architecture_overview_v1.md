## アーキテクチャ概要 v1.0

**プロジェクト**: 柔術シミュレータ
**対象スコープ**: Stage 1（HTML/TypeScript プロト）〜 Stage 2（UE5 本番）への継続設計
**前提文書**:
- [入力系詳細設計書 v1.0（攻め）](./input_system_v1.md)
- [入力系詳細設計書 v1.0（防御）](./input_system_defense_v1.md)
- [状態機械設計書 v1.0](./state_machines_v1.md)
- [CLAUDE.md](../../CLAUDE.md)

**版**: 2026-04-19

---

## 0. 本書の位置付け

入力系・状態機械・防御入力の3本の詳細設計書は、それぞれの担当領域を深く定義している。一方で、これらが**1つの実装体系としてどう組み上がるか**、特に **Stage 1（HTML/TypeScript）から Stage 2（UE5 C++）への移植** を前提とした構造は、どの文書にも書かれていない。

本文書はその不足を埋める **アーキテクチャのハブドキュメント** とする。目的は3つ:

1. **全体像の提示**: どのモジュールがどのモジュールに依存するかの俯瞰
2. **移植可能性の担保**: Stage 1 で書くコードが Stage 2 UE5 C++ にそのまま運べる構造を明示
3. **実装順序のガイド**: 何を先に書き、何を後に回せるかの依存順序

本文書が他設計書と食い違った場合、**個別詳細書（入力系、状態機械）が優先**する。本書はそれらの**接続仕様**を扱う。

---

## 1. 設計原則（Stage 1/2 共通）

### 1.1 単方向データフロー

[入力系v1.0 §0](./input_system_v1.md) で宣言された 4層パイプライン (A→B→C→D) を全システムに拡張する:

```
[Hardware/Input Device]
       ↓
   A. 入力層         (物理デバイス → InputFrame)
       ↓
   B. 意図層         (InputFrame → Intent)
       ↓
   C. アニメ駆動層   (Intent + GameState → IKターゲット + FSM遷移)
       ↓         ↓
   D. 判断窓層    (GameState → JudgmentWindowEvent)
       ↓
[Animation / Rendering / Camera / Audio]
```

**許される後退フロー**は1つだけ: 判断窓が `OPEN` の間、TimeContext がグローバルに時間スケールを変える（[state_machines_v1.md §9](./state_machines_v1.md)）。これ以外にループを作ってはならない。

### 1.2 純粋ロジック層の分離

Stage 2 移植可能性のため、以下のカテゴリの符号を **Stage 1 から厳密に分離** する:

| カテゴリ | Stage 1 での書き方 | Stage 2 での対応 |
|---|---|---|
| **純粋ロジック**（Pure） | TypeScript の純関数 / タグ付きユニオン / イミュータブル struct | C++ の `constexpr` / POD / `std::variant` に機械的変換 |
| **プラットフォーム境界**（Platform） | ブラウザAPI（Gamepad / keyboard / rAF / WebGL） | UE5 API（Enhanced Input / FRunnable / IK Rig） |
| **レンダリング / 演出** | Three.js | UE5（フォトリアル、本番） |

**判定基準**: ファイル単位で「Pure か Platform か」を明記し、純粋層はブラウザ依存APIを一切 import しない。これにより移植時はファイル単位で `.ts→.cpp/.h` 機械変換が可能。

### 1.3 不変データ構造の優先

フレーム間をまたぐ状態（InputFrame、Intent、GameStateスナップショット）は**すべてイミュータブル**に扱う。理由:

- FSM の決定論性担保（[state_machines_v1.md §0.1](./state_machines_v1.md) の原則2）
- 差分比較（前フレームと今フレーム）が容易
- UE5 移植時に `const &` で受け渡す C++ 慣習に自然に対応

Stage 1 では `Readonly<T>` + `Object.freeze` を慣習的に使う。

### 1.4 テスタビリティ優先

純粋ロジック層は、**DOM / Three.js / UE5 を一切使わずにユニットテスト可能**でなければならない。これは [state_machines_v1.md §11](./state_machines_v1.md) のテスト戦略を成立させる前提条件。

---

## 2. モジュール構造（Stage 1）

### 2.1 ディレクトリレイアウト

```
src/prototype/web/
├── index.html                     # エントリHTML（Platform）
├── src/
│   ├── main.ts                    # ブート処理（Platform）
│   ├── input/                     # 入力系 Layer A / B
│   │   ├── types.ts               # InputFrame 型定義（Pure）
│   │   ├── transform.ts           # デッドゾーン・カーブ等（Pure）
│   │   ├── keyboard.ts            # キーボード入力源（Platform）
│   │   ├── gamepad.ts             # ゲームパッド入力源（Platform）
│   │   ├── layerA.ts              # A層アセンブラ（Platform境界）
│   │   └── layerB.ts              # B層（意図変換）← Pure（予定）
│   ├── state/                     # 状態機械 + GameState
│   │   ├── hand_fsm.ts            # HandFSM（Pure）（予定）
│   │   ├── foot_fsm.ts            # FootFSM（Pure）（予定）
│   │   ├── posture_break.ts       # 姿勢崩し更新（Pure）（予定）
│   │   ├── guard_fsm.ts           # GuardFSM（Pure）（予定）
│   │   ├── judgment_window.ts     # 判断窓FSM（Pure）（予定）
│   │   ├── control_layer.ts       # 制御権レイヤー（Pure）（予定）
│   │   └── game_state.ts          # GameState集約型 + tick関数（Pure）（予定）
│   ├── sim/                       # シミュレーション駆動
│   │   └── loop.ts                # 固定タイムステップループ（Platform境界）（予定）
│   └── scene/                     # 描画（すべて Platform）
│       └── blockman.ts            # Three.js ブロックマン
└── tests/                         # Vitest（Pure層のみテスト）
    └── layerA.test.ts
```

### 2.2 Pure / Platform の境界線

各ファイルの冒頭コメントに **`// PURE`** または **`// PLATFORM`** のタグを付ける（本コミット以降の慣例）。Pure ファイルが Platform を import していないかを ESLint ルールで将来機械的に検査する（M1着手時に導入）。

**現時点の区分**:

| ファイル | 区分 | 根拠 |
|---|---|---|
| `input/types.ts` | PURE | 型定義のみ、実行時依存なし |
| `input/transform.ts` | PURE | 純関数のみ |
| `input/keyboard.ts` | PLATFORM | `window.addEventListener` に依存 |
| `input/gamepad.ts` | PLATFORM | `navigator.getGamepads` に依存 |
| `input/layerA.ts` | PLATFORM | keyboard/gamepad を集約する境界 |
| `input/layerB.ts`（予定） | PURE | InputFrame → Intent の純変換 |
| `state/*`（予定） | PURE | 純粋な状態遷移関数 |
| `sim/loop.ts`（予定） | PLATFORM | `requestAnimationFrame` に依存 |
| `scene/*` | PLATFORM | Three.js 依存 |
| `main.ts` | PLATFORM | ブート組立 |

---

## 3. データフロー（フレーム単位）

### 3.1 1フレームの実行順序

```
  1. [rAF tick]                          ← PLATFORM: requestAnimationFrame
       ↓ 固定タイムステップへ正規化
  2. LayerA.sample(nowMs)                ← PLATFORM境界
       ↓ InputFrame (immutable)
  3. LayerB.transform(inputFrame, ctx)   ← PURE
       ↓ Intent (immutable)
  4. GameState.tick(prevState, intent)   ← PURE
     ├─ ActorState 更新 (HandFSM / FootFSM / posture_break / stamina / arm_extracted)
     ├─ GuardFSM 評価
     ├─ ControlLayer 評価（initiative）
     └─ JudgmentWindowFSM 評価
       ↓ GameState (next, immutable)
  5. TimeContext.scale 更新              ← PURE（判断窓のOPEN反映）
  6. Scene.apply(nextState)              ← PLATFORM: Three.js 反映
  7. Scene.render()                      ← PLATFORM: WebGL描画
```

2–5 は **Pure**。3–5 をまとめて `stepSimulation(prev, inputFrame): GameState` 1関数に集約できるのが理想構造（Stage 2 移植時に C++ 側で `UGameplayStatics::Tick` 相当から呼べる）。

### 3.2 タイムステップ戦略

[入力系v1.0 §A.1](./input_system_v1.md) は A層を **固定60Hz** と規定。ブラウザの `rAF` は可変（通常60/120Hz、バッテリ低下時は30Hz）なので、Stage 1 でも固定タイムステップ実装を採る:

```
  accumulator += realDt
  while accumulator >= fixedDt (16.67ms):
      stepSimulation(...)
      accumulator -= fixedDt
  // 残差はレンダー補間用
```

[state_machines_v1.md §9](./state_machines_v1.md) の原則「A層は `real_dt` 基準、B層以降は `game_dt` 基準」を守るため、判断窓の時間スケールは **ステップ内でのみ** 適用。`stepSimulation` の呼び出し回数は `real_dt` で決まる。

### 3.3 イベントと通知

判断窓の開閉、技確定、カット成否等は **状態の副産物として** emit される（戻り値の `GameState` に含まれる新規イベント配列）。コールバック・Observer・EventEmitter は**使わない**（純粋性を壊すため）:

```typescript
type StepResult = {
  nextState: GameState;
  events: readonly SimEvent[];  // 今フレームに発生したもの
};
```

Platform 側（Scene / Camera / Audio）はこの `events` を走査して演出をトリガする。

---

## 4. Stage 1 → Stage 2 移植マッピング

### 4.1 型の対応

| Stage 1 (TypeScript) | Stage 2 (UE5 C++) | 備考 |
|---|---|---|
| `Readonly<T>` / `const` | `const T&` / `USTRUCT(BlueprintType)` 値型 | struct は `USTRUCT` |
| タグ付きユニオン (`type X = A \| B`) | `TVariant<A, B>` or `enum class + union struct` | 状態機械に多用 |
| `{ x: number; y: number }` | `FVector2D` | 姿勢崩しベクトル等 |
| `enum` object (`ButtonBit`) | `UENUM(BlueprintType) enum class` bit flags | 入力系 |
| 純関数 `(prev, input) => next` | `static` メンバ関数 / `constexpr` 関数 | FSM遷移 |

### 4.2 層の対応

| Stage 1 | Stage 2 (UE5) |
|---|---|
| `gamepad.ts` / `keyboard.ts` | Enhanced Input Subsystem（InputAction / InputMappingContext） |
| `layerA.ts` | `APlayerController::SetupInputComponent` + カスタム入力プロセッサ |
| `layerB.ts`（予定） | 同形の C++ 純関数（移植直訳） |
| `state/*`（予定） | 同形の C++ 純関数（移植直訳） |
| `sim/loop.ts`（予定） | `GameMode` の Tick / 固定タイムステップは `FRunnable` で実装 |
| `scene/*` | UE5 Blueprint + Character + Animation Blueprint |

**重要**: Stage 1 の純粋ロジック層（input/transform、layerB、state/*）は**1ファイル=1クラス/関数群**の粒度で書く。移植時は各 `.ts` を対応する `.h/.cpp` ペアに 1 対 1 変換するだけで済む構造を維持する。

### 4.3 ポータブルでないもの（Stage 2 で新規実装）

- **レンダリング全般**: Three.js → UE5（フォトリアル、SSS、Gi Cloth、Lumen）
- **カメラシステム**: OrbitControls 類似 → UE5 SpringArm + カスタム
- **ポスト処理**: Three.js postprocess → UE5 PostProcessVolume（カラーグレーディングでの状態伝達）
- **モキャプ再生**: （Stage 1 では未使用） → UE5 Animation Blueprint + Motion Matching
- **物理ラグドール**: （Stage 1 では未使用） → UE5 Chaos Physics

これらは Stage 1 で**一切書かない**。書いても捨てコードになるため。

---

## 5. 実装の依存順序（ロードマップ）

Stage 1 で何を先に書くかの依存順序:

```
[完了] Layer A (入力層) ─────┐
                             ↓
[次]  Layer B (意図変換層) ──┐
                             ↓
        HandFSM ─────┐       │
        FootFSM ─────┤       │
        posture_break ─┤     │
        stamina ───────┤     │
        arm_extracted ─┤     │
                       ↓     ↓
                    GameState 集約型 + tick 関数
                       ↓
                GuardFSM / ControlLayer / JudgmentWindowFSM
                       ↓
                sim/loop.ts (固定タイムステップ)
                       ↓
                Scene 反映（ブロックマンの姿勢が入力に追従）
```

各段階の完了判定は「**Vitest で該当層のユニットテストが緑**」+「**ブラウザで目視確認**（Platform境界以降）」。

---

## 6. テスト戦略のレイヤ分担

| レイヤ | テスト手段 | 担当 | 例 |
|---|---|---|---|
| PURE ロジック | Vitest（ユニット） | Stage 1 で書く | `layerA.test.ts`（既存） |
| 状態遷移シナリオ | Vitest（入力列 → 期待状態列） | Stage 1 で書く | `hand_fsm_scenario.test.ts`（予定） |
| 判断窓 6 技条件 | Vitest（GameState スナップショット → 発火可否） | Stage 1 で書く | `judgment_window.test.ts`（予定） |
| 目視・体感 | ブラウザ手動 + 自分での試遊 | Stage 1 で実施 | — |
| M1 grip-fight feel | UE5 ビルドでの 20 名プレイテスト | Stage 2 で実施 | — |

**Stage 1 の責務は「ロジックが設計書通り動くこと」の証明**であり、「体感が柔術家に受け入れられること」は Stage 1 の責務外（UE5 必須、[CLAUDE.md](../../CLAUDE.md) 冒頭の警告と整合）。

---

## 7. 状態ストレージモデル

### 7.1 単一ルートの `GameState`

全状態は1つのイミュータブルな `GameState` ルートに集約する:

```typescript
type GameState = Readonly<{
  bottom: ActorState;
  top: ActorState;
  guard: GuardState;
  control: ControlLayer;
  judgmentWindow: JudgmentWindowState;
  time: TimeContext;
  frameIndex: number;   // デバッグ / テスト時の参照用
}>;
```

### 7.2 副作用の受け皿

イベント（技確定、カット成功、判断窓開閉）は `stepSimulation` の戻り値として明示的に返す。**状態に直接書き込まない**:

```typescript
function stepSimulation(
  prev: GameState,
  input: InputFrame,
): { nextState: GameState; events: readonly SimEvent[] };
```

Platform 側（Camera / Audio / Scene）がこの `events` を購読する。

### 7.3 ロールバック可能性（将来）

この構造なら**状態をスナップショット保存して任意フレームから再シミュレート可能**。M2 のリプレイ機能 / AI 学習 / デバッグ時の「前フレームに戻す」が自然に実装できる。現 Stage 1 では活用しないが、構造だけは保つ。

---

## 8. Stage 1 の非スコープ（意図的に含まない）

以下は Stage 1 で**書かない**。書いても Stage 2 では作り直しになるため:

- 音響（判断窓の SE、グリップ音、呼吸音）
- カメラのシネマティック挙動（OTS の揺らぎ、判断窓中のズーム）
- カラーグレーディング連動（Stage 2 でポストプロセス実装）
- AI（相手 Bot）。Stage 1 では **入力 = プレイヤー手動のみ**。対人/AI は Stage 2 以降
- モーションブレンディング
- 布・毛髪・皮下散乱
- ローカライゼーション / メニューUI

---

## 9. 未決事項（v1.1 で扱う）

- Pure/Platform 境界を検査する ESLint 設定の具体
- Stage 2 への実際の移植時期判断基準（Stage 1 のどこまで作れば移植に着手するか）
- 複数プレイヤー対応（ローカル2人）の `GameState` 拡張
- リプレイ機能の永続化フォーマット
- AI 相手の `Intent` 生成器（攻め側 AI / 防御側 AI）の設計

---

## 10. Stage 2 キックオフ実装（2026-04-23）

UE5 環境準備完了を受け、Stage 2 側に最小の Runtime モジュール骨格を追加した。

- `src/prototype/ue5/Source/BJJLogicCore/BJJLogicCore.Build.cs`
- `src/prototype/ue5/Source/BJJLogicCore/Public/BJJInputFrame.h`
- `src/prototype/ue5/Source/BJJLogicCore/Public/BJJIntent.h`
- `src/prototype/ue5/Source/BJJLogicCore/Public/BJJGameState.h`
- `src/prototype/ue5/Source/BJJLogicCore/Public/BJJStepSimulation.h`
- `src/prototype/ue5/Source/BJJLogicCore/Private/BJJStepSimulation.cpp`

この時点の実装は「移植受け皿の初期化」が目的であり、完全な FSM 群移植は未了。

### 10.1 役割

- `FBJJInputFrame`: Stage 1 の Layer A 出力に対応する入力スナップショット
- `FBJJIntent`: Stage 1 の Layer B 出力に対応する意図構造
- `FBJJGameState`: Stage 1 `GameState` の最小サブセット（暫定）
- `FBJJStepSimulation::Step`: Stage 2 版の `stepSimulation` 入口

### 10.2 直近の移植順（推奨）

1. `layerB.ts` の規則を `FBJJIntent` 生成ロジックへ完全移植
2. `state/hand_fsm.ts`, `state/foot_fsm.ts`, `state/posture_break.ts`, `state/stamina.ts` を C++ 化
3. `judgment_window.ts` と `control_layer.ts` を移植し、`FBJJStepResult` にイベント配列を追加
4. UE Automation Test で Stage 1 シナリオと同一入力列リプレイを実施

### 10.3 境界ルール

- `BJJLogicCore` では Actor / Component 依存を避ける（Pure ロジック維持）
- Enhanced Input, Animation Blueprint, Camera, PostProcess など Platform 実装は別モジュールに分離
- Stage 1 と Stage 2 で同名の状態・イベントを維持し、差分検証を機械化する
