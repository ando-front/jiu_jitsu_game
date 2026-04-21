# Stage 2 UE5 プロジェクト

**状態**: **scaffold 未実施**。本フォルダは将来の UE5 プロジェクトルート予定地。
現時点では README のみ(UE5 プロジェクトを git に入れる際の衝突を避けるため)。

**着手前に必ず読むもの**:
- [docs/design/stage2_port_plan_v1.md](../../../docs/design/stage2_port_plan_v1.md) — TS → C++ のファイル対応表、型変換、実装順序
- [docs/design/architecture_overview_v1.md §4](../../../docs/design/architecture_overview_v1.md) — 型と層の対応
- [docs/visual/Visual_Pillar_Document_v1.docx](../../../docs/visual/Visual_Pillar_Document_v1.docx) — 視覚要件

---

## 0. 前提環境

| 要件 | 最低 | 推奨 |
|---|---|---|
| GPU | RTX 2060 相当 | RTX 4070 以上 (Lumen + Nanite) |
| RAM | 32 GB | 64 GB |
| ディスク空き | 100 GB | 200 GB (アセット + ビルド) |
| UE バージョン | **5.5** | 5.5+(Stage 1 ロジックの port 時に決定) |
| Visual Studio | 2022 Community + C++ Game Dev workload | 同 |

Stage 1 (`src/prototype/web/`) はブラウザさえあれば動くので、本フォルダに手を付けなくても継続開発は可能。

---

## 1. プロジェクト生成手順

### 1.1 Epic Games Launcher

1. Launcher から UE 5.5(以上)をインストール
2. New Project → **Games > Blank**、**C++**、**No Starter Content**、Ray Tracing OFF(後で PostProcess で制御)
3. プロジェクト名: `BJJSimulator`
4. 保存先: **本フォルダ直下** (`src/prototype/ue5/BJJSimulator/`)

### 1.2 初期構造

生成後、以下のサブフォルダを `Source/BJJSimulator/` に作る。モジュール名 `BJJSimulator`、API マクロ `BJJSIMULATOR_API`、クラス接頭辞は `FBJJ*` / `UBJJ*` / `EBJJ*` / `ABJJ*`([stage2_port_plan_v1.md](../../../docs/design/stage2_port_plan_v1.md) 冒頭の表記規則と一致):

```
Source/BJJSimulator/
├── Public/
│   ├── Core/          # 共有型 (EBJJGripZone, EBJJHandSide, etc.)
│   ├── Input/         # Layer A/B/D (FBJJInputFrame, FBJJIntent, ...)
│   ├── State/         # FSM群 (FBJJHandFSM, FBJJGameState, ...)
│   ├── Sim/           # 固定ステップ駆動
│   └── AI/            # OpponentAI
└── Private/
    └── (同じ構造)
```

### 1.3 最小ビルド確認

1. 空の `FBJJInputFrame` USTRUCT を `Public/Input/BJJInputFrame.h` に置く
2. エディタ起動 → C++ クラスがビルドされる
3. Automation Testing ウィンドウを開いて空リストが出る

ここまで来たら port 作業に入れる。

---

## 2. Git との付き合い方

UE5 プロジェクトのリポジトリ運用は本リポジトリの方針と衝突しやすいので事前整備:

### 2.1 .gitignore の追記

`/.gitignore` に以下を追加(プロジェクト生成直後):

```
# UE5
/src/prototype/ue5/BJJSimulator/Binaries/
/src/prototype/ue5/BJJSimulator/Build/
/src/prototype/ue5/BJJSimulator/DerivedDataCache/
/src/prototype/ue5/BJJSimulator/Intermediate/
/src/prototype/ue5/BJJSimulator/Saved/
/src/prototype/ue5/BJJSimulator/*.sln
/src/prototype/ue5/BJJSimulator/.vs/
```

### 2.2 Git LFS の有効化

[CLAUDE.md](../../../CLAUDE.md) の方針どおり、**`.uasset` / `.umap` / `.fbx` は LFS**:

```bash
git lfs install
git lfs track "*.uasset" "*.umap" "*.fbx" "*.wav" "*.exr"
```

既に `.gitignore` 内でコメントアウトされている LFS 向けエントリがあるので、
UE5 プロジェクト追加時に有効化 + 追記。

---

## 3. 実装順序のガイドレール

[stage2_port_plan_v1.md §3](../../../docs/design/stage2_port_plan_v1.md) の段階に従う(v1.1 で [7] を 2 分割):

1. 基礎型(Core)
2. 純 FSM(HandFSM, FootFSM, PostureBreak, Stamina, ArmExtracted)
3. 集約(GameState + step 関数)
4. 制御系(GuardFSM, ControlLayer, JudgmentWindow, CounterWindow)
5. 入力層(APlayerController → FBJJInputFrame → BJJLayerB)
6. 固定ステップ(AGameMode)
7. **7a. PostProcess**(暖色シフト + window vignette、アート非依存)
   **7b. Character**(ACharacter + AnimBP + Chaos Cloth、モキャップ依存)
8. Automation Test で Stage 1 の 268 ケースを緑化

**[1]〜[4] + [7a] はエディタ + art asset なしでビルド可** なので、Character 待ちの間も CI で port の退行を検出できる。

---

## 4. 参考: Stage 1 コードの読み方

UE5 着手者が Stage 1 TS を読む際のガイド:

```
src/prototype/web/src/
├── input/          ← 全部 C++ に直訳する (keyboard.ts / gamepad.ts を除く)
├── state/          ← 全部 C++ に直訳する
├── ai/             ← 全部 C++ に直訳する
├── sim/fixed_step.ts  ← UE5 側で新規 (AGameMode::Tick)
├── scene/          ← 破棄 (UE5 Blueprint で新規)
└── main.ts         ← 破棄 (UE5 Widget BP + AGameMode に分割)
```

各 `.ts` は小さく、1ファイル = 1 責務 を維持している。横に置いて 1対1 で写経するのが早い。

---

## 5. よくある質問

**Q. ブロックマンのまま UE5 に持っていけないの?**
A. 持って行けるが、Stage 2 の gate は M1 (15/20 grip-fight feel) なので、フォトリアル + cloth sim + Motion Matching まで行かないとテスターは評価できない。Stage 2 は "見た目投入" のマイルストーンなので中途半端な視覚は避ける。

**Q. Stage 1 の Three.js コードで書いた shader (stamina grading) は移植できる?**
A. ロジック(fatigue = 1 - saturate((stamina - 0.15) / 0.45))は同じ。配色は UE5 の PostProcessVolume + Split-Toning で表現する。フラグメントシェーダの直訳ではなく Material Graph で書き直す。

**Q. テストはどう書けばいい?**
A. UE5 Automation Testing。`IMPLEMENT_SIMPLE_AUTOMATION_TEST` で書く。Stage 1 の Vitest アサーションを 1対1 で写経できる。

**Q. GitHub Actions で UE5 CI は回せる?**
A. セルフホストランナーなら可。GitHub Hosted ランナーは UE5 非対応。CI 自動化は後回しで良い([stage2_port_plan_v1.md §6](../../../docs/design/stage2_port_plan_v1.md) 未決事項)。
