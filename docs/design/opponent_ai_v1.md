## 相手 AI 設計書 v1.0

**プロジェクト**: 柔術シミュレータ
**対象スコープ**: Stage 1 プロト — プレイヤーが1側を操作している時に反対側をルールベース AI が自動操作する最小実装
**前提文書**:
- [入力系詳細設計書 v1.0（攻め）](./input_system_v1.md)
- [入力系詳細設計書 v1.0（防御）](./input_system_defense_v1.md)
- [状態機械設計書 v1.0](./state_machines_v1.md)
- [アーキテクチャ概要 v1.0](./architecture_overview_v1.md)

**版**: 2026-04-19

---

## 0. 設計意図

Stage 1 プロトは、プレイヤーが `role=Bottom` で攻めている間、`top` は完全に passive だった。これでは **攻め側の条件成立過程を検証できても、攻防のリズムが発生しない** ため、設計書§0の命題「条件を作る操作感」を体感的に確かめられない。

本書では **最小限のルールベース AI** を定義する。目的は3つ:

1. **攻防のリズムを発生させる**: 攻め側がグリップを取ったらカットが来る、崩されたら戻そうとする等、基本的な反応を生む
2. **設計書の未検証領域を埋める**: posture_break 回復・カット試行・判断窓カウンター等、入力側だけでは起動しない状態遷移を AI が触発する
3. **Stage 2 AI の雛形にする**: 本書の構造（状況評価 → 行動選択 → 意図生成）は UE5 Behavior Tree に自然に移植できる

### 0.1 非スコープ（v1.0 で扱わない）

- **機械学習ベース AI**: 行動選択はすべて決定論的ルール
- **難易度調整**: AI 強度パラメータは単一固定値（playtest 後に拡張）
- **連携プレイ（2 vs 1 等）**: プレイヤー 1 vs AI 1 のみ
- **人格表現**: AI の「癖」や「プレイスタイル」の差別化

---

## 1. 配置と制御フロー

### 1.1 どちらを AI が操作するか

```
  プレイヤーが role = Bottom → AI が TOP 側を操作
  プレイヤーが role = Top    → AI が BOTTOM 側を操作
```

**1.2** Stage 1 の `main.ts` では起動時プロンプトで role を選ばせているので、プロンプト完了時に「選ばれなかった側」に AI を接続する。

### 1.2 どの層に AI が入るか

アーキテクチャ§1.1 の A→B→C→D パイプラインで、AI は **B 層の出力（Intent / DefenseIntent）を生成する** 位置に入る。つまり:

```
  [Player]    →  Layer A → Layer B → Intent     ─┐
                                                 ├→  stepSimulation
  [AI  ]      →  (observe GameState) → Intent   ─┘
```

AI は A 層を持たない（物理入力を模倣しない）。直接 Intent を生成するので、入力系のデッドゾーン・カーブ等を通らない。これにより **AI の意図は「なぜその値か」が説明可能** になる（「LS を 0.8 入れた」ではなく「hip_push = 0.8 を指示した」）。

### 1.3 Stage 2 への移植パス

- Stage 1 の AI Intent 生成関数 → Stage 2 の UE5 Behavior Tree / State Tree
- 判定順序（§3）はそのまま BT のセレクタ優先度に対応
- 行動（§4）はそれぞれが BT の Task ノードに対応

**構造を崩さないこと**が移植性の核心。係数を変えるのは playtest 後で OK。

---

## 2. AI の観測データ（入力）

AI は毎 tick、**`GameState` 全体を読み取り可能**。ただし以下の値を中心に使う:

| 観測項目 | 用途 |
|---|---|
| `bottom.leftHand.state` / `bottom.rightHand.state` | 攻め側が GRIPPED かの判定 |
| `bottom.leftHand.target` / `bottom.rightHand.target` | どのゾーンを掴まれているか |
| `top.postureBreak` | 崩され具合、回復優先度の判断 |
| `top.stamina` | 行動選択の閾値 |
| `judgmentWindow.state` / `counterWindow.state` | 窓中は通常行動を止める |
| `counterWindow.candidates` | カウンター commit 可否 |
| `cutAttempts` | 既にカット中ならさらなる commit を避ける |
| `passAttempt.kind` | パス進行中なら他行動を抑制 |

---

## 3. 行動選択: 優先度テーブル

AI は毎 tick、以下を **上から評価** し、最初に合致した行動を実行する。合致しない場合は「待機」行動。

### 3.1 TOP側 AI（プレイヤーが Bottom の時）

| 優先度 | 条件 | 行動 | 詳細 |
|---|---|---|---|
| 1 | `counterWindow.state == OPEN` 且つ候補あり | §4.1 カウンター commit | 該当技の入力パターン |
| 2 | `judgmentWindow.state == OPEN` 且つ TRIANGLE 候補 | §4.1 カウンター commit（早期スタック） | 三角絞め阻止 |
| 3 | `top.postureBreak.sagittal ≥ 0.5` | §4.2 姿勢戻し | RECOVERY_HOLD + weight_forward=1 |
| 4 | 攻め側の任意の手が `GRIPPED` 0.5秒以上 且つ `cutAttempts` 空き | §4.3 カット試行 | その手の zone に向けて CUT_ATTEMPT |
| 5 | 攻め側の `arm_extracted` が true | §4.4 bicep ベース | 該当側の BICEP_* に強ベース |
| 6 | 攻め側 両足 LOCKED 且つ 攻め側 2 グリップ未満 且つ `top.stamina ≥ 0.5` | §4.5 パス試行準備 | KNEE_L/R + BICEP_L にベース（commit はしない） |
| 7 | `top.stamina < 0.3` | §4.6 呼吸 | BREATH_START + 静止 |
| 8 | 上記いずれも不成立 | §4.7 待機 | ZERO_DEFENSE_INTENT |

### 3.2 BOTTOM側 AI（プレイヤーが Top の時）

| 優先度 | 条件 | 行動 | 詳細 |
|---|---|---|---|
| 1 | `judgmentWindow.state == OPEN` 且つ候補あり | §4.8 技 commit | 最初の候補を選択 |
| 2 | `bottom.stamina ≥ 0.5` 且つ 攻め側グリップ 0 本 | §4.9 グリップ取り | SLEEVE_R を左手で狙う |
| 3 | 攻め側 片手 GRIPPED（非 COLLAR） | §4.10 もう片手もグリップ | 未使用側で COLLAR を狙う |
| 4 | 攻め側 両手 GRIPPED 且つ `top.postureBreak` < 0.4 | §4.11 崩し作り | hip_push ≥ 0.5 維持 |
| 5 | `bottom.stamina < 0.3` | §4.6 呼吸（攻め側版） | BTN_BREATH |
| 6 | 上記いずれも不成立 | §4.7 待機（攻め側版） | 全グリップ維持 |

---

## 4. 行動定義

### 4.1 カウンター commit（TOP）

`counterWindow.candidates` の先頭を取り、対応する入力パターンを返す:

- `SCISSOR_COUNTER` → `LS.x = -sign(attackerSweepLateralSign)`、他 0
- `TRIANGLE_EARLY_STACK` → `LS.y = 1`、`BTN_BASE` ホールド

Layer D_defense の判定条件に直接マッピングする。AI は判定パターンを**知っている**前提（プレイヤーが取説で知るのと同等）。

### 4.2 姿勢戻し（TOP）

```
DefenseIntent {
  hip: { weight_forward: 1, weight_lateral: 0 },
  base: ZERO_TOP_BASE,
  discrete: [{ kind: "RECOVERY_HOLD" }]
}
```

これにより `computeDefenderRecovery` が起動し、`posture_break.sagittal` が原点方向に押し戻される。

### 4.3 カット試行（TOP）

攻め側の GRIPPED な手を選び、その **zone の方向に向けて** RS を指定し、CUT_ATTEMPT を発火。

- 左右どちらの defender hand を使うか: 最初に空いている方（`cutAttempts.left.kind == "IDLE"` を優先）
- RS 方向: 攻め側 zone の物理位置に対応（SLEEVE_R は defender から見て左前なので `(−0.7, −0.7)` 等）
- 攻め側が 2 手 GRIPPED でも 1 手ずつカットする（両手同時は不自然）

### 4.4 Bicep ベース（TOP）

```
base.l_hand_target = "BICEP_L"  (もしくは R 側)
base.l_base_pressure = 0.8
```

arm_extracted 対応側に圧をかける。`defenderIsBasingBicep` が arm_extracted クリアに寄与する。

### 4.5 パス試行準備（TOP）

まだパスコミットは打たない（成功率が低い段階では不用意にコミットしない）。base hand をコントロールゾーンに置くだけ:

```
base.l_hand_target = "BICEP_L"   pressure 0.7
base.r_hand_target = "KNEE_R"    pressure 0.7
RS = (0.7, -0.7)   // 方向指定、base hysteresis に効く
```

この状態が 2秒以上続いたら §4.5b PASS_COMMIT 発火（v1.1 で定義）。

### 4.6 呼吸（両側）

TOP: `BREATH_START` + 全 base release + 静止
BOTTOM: `BTN_BREATH` 押下エッジ + グリップ解放 + LS 中立

スタミナが閾値を超えたら通常行動に復帰。

### 4.7 待機

ゼロ Intent を返す。現状維持（GRIPPED な手はそのまま）。

### 4.8 技 commit（BOTTOM）

`judgmentWindow.candidates[0]` の commit 入力を返す。Layer D の発火パターンに対応。

### 4.9 グリップ取り（BOTTOM）

左手で `SLEEVE_R` を狙う。入力系 §B.2.1 の通り:

```
Intent {
  hip: 0,
  grip: { l_hand_target: "SLEEVE_R", l_grip_strength: 0.8 },
  discrete: []
}
```

### 4.10 もう片手もグリップ（BOTTOM）

右手が空いていれば `COLLAR_L` を狙う（組み合わせで x コラーを目指す）。

### 4.11 崩し作り（BOTTOM）

```
hip: { hip_push: 0.6, hip_lateral: 0 }
grip: 両手 GRIPPED 維持（強度 0.8）
```

---

## 5. 実装方針（Stage 1）

### 5.1 ファイル配置

- `src/prototype/web/src/ai/opponent_ai.ts` — メインの決定関数
- Pure モジュール（DOM / Three.js 依存なし）
- Stage 2 移植時は `src/ai/opponent_ai.cpp` に 1:1 変換

### 5.2 API

```typescript
function opponentIntent(
  game: GameState,
  role: "Bottom" | "Top",   // the ROLE THE AI PLAYS
): { intent: Intent } | { defense: DefenseIntent };
```

- 純関数: 同じ `game` + 同じ role なら常に同じ結果
- 行動選択に時間状態が要る場合（`cutAttempts` を 0.5 秒待つ等）、それは `GameState` の値から導出する

### 5.3 メインループ統合

main.ts の `sample` コールバックで:

```typescript
if (role === "Bottom") {
  // player drives bottom via Layer B
  // AI drives top via opponentIntent
  const ai = opponentIntent(game, "Top");
  return { frame, intent: playerIntent, defense: ai.defense };
}
```

ただし **AI 側に Layer A の生フレームは流さない**（§1.2）。AI が生成する defense は直接 `defenseIntent` に入る。

---

## 6. 検証指標

Stage 1 での AI 検証基準:

| 指標 | 目標値 | 測定方法 |
|---|---|---|
| プレイヤーが BOTTOM でグリップを 3 秒以上維持できる確率 | ≥ 50% | 10 セッション平均 |
| プレイヤーが TOP で崩しを 0.5 以上作れる確率 | ≥ 60% | 同上 |
| AI が判断窓中に適切なカウンターを選ぶ率 | ≥ 70% | ログから集計 |
| AI が stamina 枯渇で「動けなくなる」現象 | 観測されないこと | 観察 |

未達なら本書は v1.1 に改定（係数チューニング・新規行動追加）。

---

## 7. 未決事項（v1.1 で扱う）

- パスコミットの閾値と成功率チューニング
- 難易度レベル（弱/中/強）のパラメータ化
- プレイヤーの行動に対する **反応遅延** の導入（現状は即時反応で不自然）
- TOP_AI の「プレイヤー BOTTOM が技を仕掛けようとしている瞬間を読む」行動
- 残り 4 技（フラワー/オモプラッタ/ヒップバンプ/十字絞め）のカウンター追加
