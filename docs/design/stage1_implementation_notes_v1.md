# Stage 1 実装ノート v1.0

**目的**: Stage 1 (TypeScript プロトタイプ) を実装する過程で、既存の design
ドキュメントだけでは決まっていなかった細部を **実装と同時に確定した** 項目を
まとめる。Stage 2 C++ 移植時に同じ判断を再度迫られないようにすること、
および design docs の v1.1 改訂を書くときの一次ソースとすること、が目的。

**対象**: [state_machines_v1.md](./state_machines_v1.md),
[input_system_v1.md](./input_system_v1.md),
[input_system_defense_v1.md](./input_system_defense_v1.md) の未決だった点。

**版**: 2026-04-21 / Stage 1 268 tests green の時点で凍結。

---

## 1. カット試行(Cut Attempt)のスロット管理

**設計側の記述**: [state_machines_v1.md §4.2](./state_machines_v1.md#42-グリップカット相手側のアクション) が「防御側が攻め側のグリップをカットする試行」の判定ロジックを規定するが、「**どのハンドスロットで管理するか**」は未定義だった。

**Stage 1 の決定**(実装: [src/state/cut_attempt.ts](../../src/prototype/web/src/state/cut_attempt.ts)):

- **防御側の左手・右手それぞれに 1 スロット** を持たせる。すなわち同時に最大2つの cut attempt が進行しうる。
- 各スロットは `IDLE | IN_PROGRESS { startedMs, targetAttackerSide, targetZone }` のタグ付きユニオン。
- `IN_PROGRESS` 中は **同じ防御ハンドからの二重コミットは黙って無視** する。別の防御ハンドなら独立に開始可。

### 1.1 ターゲット選択ルール

「防御側の RS 向きで attacker のどちらの手を狙うか」は design doc では未規定。Stage 1:

1. RS の大きさが `< 1e-6`(ほぼ入力なし) → attacker の L 手が GRIPPED なら L、そうでなければ R。両方なしなら null(不発)。
2. RS が有効 → **`rs.x < 0` なら attacker の L 側を優先** / そうでなければ R 側。対応する attacker 手が GRIPPED かつ `target !== null` のみ採用。
3. 両方 GRIPPED + RS 有効 → 1. の符号ルールで優先側を選び、不在なら反対側にフォールバック。

**根拠**: 攻め側のゾーン配置は対称なので、RS の左右符号で攻め側の L/R を選ぶのが直感的。ADCC/IBJJF の実際のグリップカットもほぼ左右対応で行う。

### 1.2 成否判定

- タイマー `attemptMs = 1500ms` 経過時に判定(design doc と整合)
- 判定は **expiry 時点の攻め側トリガー値を読む** 設計。「試行中に攻め側が握り直して強度 0.7 へ戻した」が有効な防御として機能する。
- 閾値 `strength < 0.5` なら `CUT_SUCCEEDED`、`>= 0.5` なら `CUT_FAILED`。

---

## 2. カウンター窓(Counter Window)の技カタログ

**設計側の記述**: [input_system_defense_v1.md §D](./input_system_defense_v1.md) が「防御側も判断窓を持つ」と述べるが、**具体的にどの attacker 技にどの counter が対応するか** は table 化されていなかった。

**Stage 1 の決定**(実装: [src/state/counter_window.ts](../../src/prototype/web/src/state/counter_window.ts)):

| 攻撃側の技 | 防御側の counter | 決め入力(Layer D_defense) |
|---|---|---|
| `SCISSOR_SWEEP` | `SCISSOR_COUNTER` | **LS を sweep 方向と逆符号** に最大 |
| `TRIANGLE` | `TRIANGLE_EARLY_STACK` | **LS 上 + `BTN_BASE` ホールド** |
| その他 (`FLOWER_SWEEP` / `OMOPLATA` / `HIP_BUMP` / `CROSS_COLLAR`) | なし (M1 scope 外) | — |

Counter 成立時の副作用:
- 攻撃側の判断窓を強制 CLOSING へ遷移
- `TRIANGLE_EARLY_STACK` はさらに `arm_extracted` を両側リセット(§4.1 の意図「相手がベースを取り戻す」の具体化)

M1 で 6 技中 2 技しか counter が無いのは、残り 4 技が「返せる窓が実戦上ほぼ無い」ため。拡張時は [counter_window.ts](../../src/prototype/web/src/state/counter_window.ts) の `COUNTER_FOR` テーブルを伸ばせば良い。

### 2.1 SCISSOR_COUNTER の方向判定(attackerSweepLateralSign)

SCISSOR_COUNTER の成立条件「sweep 方向と逆」を判定するには、**攻撃側が OPENING に入った瞬間の LS 横方向符号** を覚えておく必要がある(OPEN 中に攻撃側が方向を変えても基準は固定)。

Stage 1 は `GameState.attackerSweepLateralSign: number` をこの目的専用のフィールドとして持ち、`WINDOW_OPENING` と同じフレームで `intent.hip.hip_lateral` の符号を snapshot する。Stage 2 では `FBJJGameState::AttackerSweepLateralSign` として int8 で移植予定。

---

## 3. ラウンドタイマー

**設計側の記述**: design docs には **ラウンド時間の規定がなかった**。M1 スコープは「クローズドガードの攻防限定」なので時間制限は必須ではないが、IBJJF/ADCC 整合を目指すには必要。

**Stage 1 の決定**(実装: [main.ts](../../src/prototype/web/src/main.ts)):

- `ROUND_LENGTH_MS = 5 * 60 * 1000`(5分 = IBJJF アダルト 白帯 / 青帯のマッチ長)
- **実時間 `realDt` で減る**。判断窓のスロー(`TimeContext.scale = 0.3`)中も常時減る。「判断窓中にスローになってラウンドが延びる」を防ぐ。
- 0 到達で `SESSION_ENDED` の新 reason `ROUND_TIME_UP` を発火し、`TIME UP — ROUND OVER` オーバーレイを表示。
- これは **`GameState` ではなく main.ts モジュールの状態**。純粋 sim の関心ごとではない(presentation-adjacent)という判断。Stage 2 では `ABJJGameMode` 側のタイマーに移す。

---

## 4. スタミナのカラーグレーディング(§5.4 への具体化)

**設計側の記述**: [state_machines_v1.md §5.4](./state_machines_v1.md#54-m1スコープでの扱い) が「**視覚化はVisual Pillar 2.4 のカラーグレーディング(呼吸の重さに連動した暖色シフト)で行い、HUD表示はしない**」と指示しているが、**どのスタミナ値でどれくらい色が乗るか** は未規定だった。

**Stage 1 の決定**(実装: [main.ts](../../src/prototype/web/src/main.ts) の `applyToScene` + [scene/blockman.ts](../../src/prototype/web/src/scene/blockman.ts)):

```
fatigue = clamp(1 - (stamina - 0.15) / 0.45, 0, 1)
```

- スタミナ **0.6 以上**: `(stamina - 0.15) / 0.45 >= 1` → `1 - ... <= 0` → `clamp` で `0`(完全に無効)。中盤はニュートラル。
- スタミナ **0.15 以下**: `(stamina - 0.15) / 0.45 <= 0` → `1 - ... >= 1` → `clamp` で `1`(最大暖色)。
- 中間は線形補間。

実装 ([main.ts](../../src/prototype/web/src/main.ts) `applyToScene`) では `Math.max(0, ...)` を内側に足して早期 0 化しているが、外側 `clamp(., 0, 1)` と数学的に等価。移植時はどちらの形でも良い。

SPECTATE ロール中は両側のスタミナの min を採用(より消耗しているほうを視覚化)。

実装は overlay quad の fragment shader に `uStaminaFatigue` uniform を追加、vignette が `uStaminaColor = 0xd06030`(暖色)へ寄っていく形。`smoothstep(0.28, 0.72, r)` で画面エッジから中央へ向けて暖色の強度を落とす。

**Stage 2 では** Material Graph と PostProcessVolume の Split-Toning で同じ振る舞いを実装する(shader 直訳ではない)。

---

## 5. 練習シナリオ(Practice Scenarios)

**設計側の記述**: 存在しない。これは **Stage 1 の iteration 支援機能** であり、プロダクトの機能ではない。

**Stage 1 の決定**(実装: [src/state/scenarios.ts](../../src/prototype/web/src/state/scenarios.ts) + [tests/unit/scenarios.test.ts](../../src/prototype/web/tests/unit/scenarios.test.ts)):

| キー | 名前 | 狙い |
|---|---|---|
| 1 | `SCISSOR_READY` | §8.2 SCISSOR_SWEEP の precondition 満了 |
| 2 | `FLOWER_READY` | 同 FLOWER_SWEEP |
| 3 | `TRIANGLE_READY` | 同 TRIANGLE (`arm_extracted` seed 済) |
| 4 | `OMOPLATA_READY` | 同 OMOPLATA (sign 一致済) |
| 5 | `HIP_BUMP_READY` | 同 HIP_BUMP (`sustainedHipPushMs=350` で 300ms 閾値越え) |
| 6 | `CROSS_COLLAR_READY` | 同 CROSS_COLLAR |
| 7 | `PASS_DEFENSE` | TOP 側の練習(パス直前状態) |
| 0 | (reset) | ニュートラル |

設計制約:
- シナリオは pure function `(nowMs) => GameState`、freeze 済を返す。
- **判断窓の state は seed しない** — 次の `stepSimulation` で OPENING が自然発火する。これにより `WINDOW_OPENING` イベントが event log に出て、UI 側の遷移演出も通常どおり走る。
- `TRIANGLE_READY` は `arm_extracted` のサスタインカウンタも seed して、次フレームでフラグが落ちないようにしている。
- Stage 2 では `#if WITH_EDITOR` でガードし、Shipping ビルドからは除外予定。

---

## 6. その他の小決定

### 6.1 Spectate role

UI 面での拡張。Role type を `Bottom | Top | Spectate` の 3 値に。Spectate 中は opponent AI が両側同時に動き、`SESSION_ENDED` 後 2 秒で `restartSession()` が自動発火(ストレステスト用途)。

### 6.2 イベントログの抑制対象

`WINDOW_OPEN` / `WINDOW_CLOSED` / `COUNTER_WINDOW_OPEN` / `COUNTER_WINDOW_CLOSED` / `LOCKING_STARTED` は UI の event log から suppress。理由: 対応する `*_OPENING` イベントが interesting edge を既に伝えるため、同じ遷移に対して log が2行出ると読みづらい。

### 6.3 defenderCutInProgress の configuration layer 連動

[state_machines_v1.md §7.2](./state_machines_v1.md) ルール 4「防御側のカット試行中 → `initiative = Top`」は実装時に `defenderCutInProgress` を `ControlLayerInputs` へ配線。初期実装では hardcoded `false` だったが、cut slot のいずれかが `IN_PROGRESS` なら `true` を渡すよう修正済([PR #2](https://github.com/ando-front/jiu_jitsu_game/pull/2))。

---

## 7. 未解決のまま残した項目

以下は Stage 1 では決め打ちせず、**M1 プレイテスト後** に確定する:

- **`posture_break` の各係数 `K*`** ([state_machines_v1.md §3.3](./state_machines_v1.md)) — Stage 1 は仮値で動いている。腰入力 / グリップ引き / 防御側戻し / 時間減衰の相対比は人間プレイで調整する必要がある
- **LOCKING 再試行クールダウン** ([state_machines_v1.md §12](./state_machines_v1.md)) — Stage 1 は「失敗すれば即座に UNLOCKED に戻る」単純動作。連打抑制が要るかは playtest で判断
- **判断窓 OPEN 中の複数候補提示 UI** ([state_machines_v1.md §12](./state_machines_v1.md)) — Stage 1 は debug HUD の `candidates` 行で済ませているが、Stage 2 の HUD なし制約下での提示は未設計
- **OMOPLATA 不発テスト** — OMOPLATA の `sign 一致` 判定が ±0 の場合どう転ぶか、実テストは `technique_scenarios.test.ts` で sign=+1/−1 のみ確認。`lateral == 0` の境界は今のところ「発火しない」側に倒している

これらは v1.1 design doc で resolve する(と予想される)。

---

## 8. Stage 2 移植時の注意

本ノートの項目は [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) の作業対象に **すべて含まれている**。移植時に本ノートを読めば、「なぜ Stage 1 はこの値 / この構造を選んだか」の根拠がたどれる。

不一致を見つけたら **本ノート → design doc v1.1 → Stage 2 実装** の順で整合させる(implementation → docs の一方向整合ルール)。
