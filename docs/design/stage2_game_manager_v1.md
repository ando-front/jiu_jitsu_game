# Stage 2 GameManager 設計ノート v1.0

**作成日**: 2026-04-25
**対象**: Stage 2 Unity 6 プロジェクト (`src/prototype/unity/`)
**前提**:
- [stage2_port_plan_v1.md](./stage2_port_plan_v1.md) — ファイル単位 port 計画
- [architecture_overview_v1.md](./architecture_overview_v1.md) — Pure / Platform 境界、`stepSimulation` 単方向データフロー
- [input_system_v1.md §A](./input_system_v1.md) — Layer A の物理デバイス契約
- [input_system_defense_v1.md §F](./input_system_defense_v1.md) — Role 選択フロー
- Stage 1 リファレンス: `src/prototype/web/src/main.ts` (930 行)
- Pure 層 entrypoint: `src/prototype/unity/Assets/BJJSimulator/Runtime/Sim/FixedStep.cs` の `IStepProvider`

---

## 0. 本ノートの位置付け

Stage 2 における **MonoBehaviour 配線層**（= Stage 1 `main.ts` の Unity 側ホスト）の
責務分解を確定する。Pure ロジック層 (`Runtime/State/`, `Runtime/Input/`,
`Runtime/Sim/`, `Runtime/AI/`) は **既に C# port 済み・NUnit 234 ケース緑**で、
本ノートはそれらを Unity の Update / MonoBehaviour 構造から **どう叩くか**
だけを扱う。

**ロックインする決定**:
1. `main.ts` の 9 責務をどの MonoBehaviour に割るか (§2)
2. `KeyboardSource` / `GamepadSource` を New Input System の Action Asset に
   置換える際の Action ↔ `RawHardwareSnapshot` フィールド mapping (§3)
3. `Update()` 内の処理順序と Pause / Tutorial / SessionEnd の gate 順序 (§4)
4. v1 シーンに含めるもの / 別フェーズに送るもの (§5)
5. EditMode テストでカバーできる範囲と Play Mode を v1 に含めない理由 (§6)

**非目的**:
- ビジュアル (Skinned mesh / Animator / URP / 後処理) の具体設計 — 別ノート
- Coach HUD / Event Log / Tutorial の UI Toolkit レイアウト — 別ノート
- ロール選択 UI のビジュアル仕様 — 別ノート（本ノートはロジック上の gate のみ規定）
- 数値チューニング (デッドゾーン / dt / SPECTATE_AUTO_RESTART_MS の Stage 2 値) —
  Pure 層に既に固定値が入っているため変更しない

---

## 1. Stage 1 `main.ts` の責務棚卸し

`src/prototype/web/src/main.ts` を読み解いた結果、以下 9 責務が rAF ループ
1 本に集約されている (行番号は 2026-04-25 時点)。Stage 2 ではこれを分解する。

| # | 責務 | Stage 1 該当箇所 | 主な状態 |
|---|---|---|---|
| 1 | Role 選択プロンプト (Bottom/Top/Spectate) | `runPromptTick()` 769–796 | `role`, `promptActive`, `lastPromptLsX` |
| 2 | チュートリアル overlay (H / Esc) | `setTutorial()` 124–168 | `tutorialEl.classList` |
| 3 | シナリオ選択 (Digit1–7 / 0) | `keydown` 144–168, `loadScenario()` 397–420 | `activeScenario` |
| 4 | Pause (Esc edge / BTN_PAUSE) | `setPaused()` 313–316, 563–572 | `paused` |
| 5 | ラウンドタイマー (5 分) | `roundElapsedMs` 614–620 | `roundElapsedMs` |
| 6 | Session 終了 overlay + restart | `showEndOverlay()` 351–389 | `sessionEndReason`, `sessionEndElapsedMs` |
| 7 | 入力サンプリング → Layer A→B→D | `frame()` 622–697 内 `provider.sample` | `bState`, `bDefState`, `dState`, `dDefState`, `lastFrame`, `lastIntent`, `lastDefense` |
| 8 | `FixedStep.advance` + AI 注入 | `frame()` 622–697 + `pendingAi*` | `simState`, `pendingAiBottom`, `pendingAiTop` |
| 9 | 描画 (scene + HUD + EventLog + Coach + InputLive) | `applyToScene()`, `renderHud()`, `renderEventLog()`, `renderCoach()`, `renderInputLive()` 700–913 | `eventLog`, `coachTarget` |

---

## 2. MonoBehaviour 分解

責務 1–9 を 4 つの MonoBehaviour と 1 つの ScriptableObject に分割する。
**分割の方針**: Pure 層 (`IStepProvider` 実装) を除き、各 MonoBehaviour が
担う rAF/Update 中の責務領域を **重複なく** 割り当てる。`BJJGameManager` が
ハブとなり、他 3 つを GetComponent / SerializeField で参照する。

### 2.1 クラス一覧

| クラス | 種別 | 主責務 (上記#) | 依存先 |
|---|---|---|---|
| `BJJGameManager` | MonoBehaviour | #4 #5 #7 #8 (ハブ) | `BJJInputProvider`, `BJJSessionLifecycle`, `BJJDebugHud` |
| `BJJInputProvider` | MonoBehaviour, `IStepProvider` 実装 | #7 (Layer A 集約 + B/D 駆動) | `InputAction`s, `LayerAOps`, `LayerB*`, `LayerD*`, `OpponentAI` |
| `BJJSessionLifecycle` | MonoBehaviour | #1 #2 #3 #6 (gate / overlay 状態保持) | (なし — 純粋に状態フラグ管理) |
| `BJJDebugHud` | MonoBehaviour | #9 (HUD テキスト描画) | `BJJGameManager` の公開 read-only ref |
| `BJJInputActionsAsset` | ScriptableObject (`*.inputactions`) | New Input System Action 定義 | (Unity asset) |

ビジュアル層 (Three.js `scene/blockman.ts` の Unity 等価) は **別ノート行き**
で本 v1 では一切触らない。`BJJGameManager` は GameState を Pure 層から
受け取って **テキスト HUD のみに流す**。

### 2.2 各クラス詳細

#### 2.2.1 `BJJGameManager`

責務: ループ駆動 (`Update`) のハブ。`BJJSessionLifecycle` が掲げる gate を
評価し、active なら `FixedStepOps.Advance` を 1 回呼ぶ。Pause edge は
ここで検出して `BJJSessionLifecycle.SetPaused(true)` を叩く。

| 公開 API | シグネチャ | 用途 |
|---|---|---|
| `[SerializeField] BJJInputProvider inputProvider` | inspector | Action wire |
| `[SerializeField] BJJSessionLifecycle lifecycle` | inspector | gate 取得 |
| `[SerializeField] BJJDebugHud hud` | inspector | 描画委譲 |
| `void Update()` | Unity message | rAF 相当 — 全責務を駆動 |
| `GameState CurrentGameState { get; }` | read-only | HUD 描画用 |
| `SimEvent[] LastStepEvents { get; }` | read-only | HUD / 将来のビジュアル委譲用 |
| `int LastStepsRun { get; }` | read-only | デバッグ表示 |

private 状態:
- `FixedStepState _simState` — `FixedStep.cs §43-67` の `Initial(...)` で初期化
- `float _lastUpdateTimeMs` — `Time.realtimeSinceStartupAsDouble * 1000.0` から差分計算
- `float _roundElapsedMs` — 責務#5

#### 2.2.2 `BJJInputProvider` (`IStepProvider` 実装)

責務: New Input System の `InputAction` をポーリングして
`RawHardwareSnapshot` を組み立て、`LayerAOps.Assemble` で `InputFrame` を
生成。さらに `IStepProvider.Sample` / `ResolveCommit` /
`ResolveCounterCommit` の 3 メソッドを実装し、role に応じて Layer B
(攻め/防御) と AI を呼び分ける。**`main.ts` の `provider` インライン定義
(622–697 行) を 1 クラスに昇格させたもの**。

| 公開 API | シグネチャ | 用途 |
|---|---|---|
| `[SerializeField] InputActionAsset actionsAsset` | inspector | `BJJInputActions.inputactions` 参照 |
| `Role CurrentRole { get; set; }` | property | `BJJSessionLifecycle` から書き込み |
| `(InputFrame, Intent, DefenseIntent?) Sample(long nowMs)` | `IStepProvider` | 責務#7 (Layer A→B) |
| `Technique? ResolveCommit(...)` | `IStepProvider` | 責務#7 (Layer D 攻め) |
| `CounterTechnique? ResolveCounterCommit(...)` | `IStepProvider` | 責務#7 (Layer D 防御) |
| `InputFrame? LastFrame { get; }` | read-only | HUD 用 |
| `Intent? LastIntent { get; }` | read-only | HUD 用 |
| `DefenseIntent? LastDefense { get; }` | read-only | HUD 用 |

private 状態:
- `LayerAState _layerA` — `LayerA.cs §47` の struct
- `LayerBState _bState`, `LayerBDefenseState _bDefState`
- `LayerDState _dState`, `LayerDDefenseState _dDefState`
- `AIOutput? _pendingAiBottom`, `AIOutput? _pendingAiTop` — `main.ts` 205–206 と同じ責務
- `RawHardwareSnapshot _snapshot` — Update の冒頭で New Input System から組み立て

`Sample` 呼び出しの直前に `BJJGameManager.Update` 側で `PollHardware()`
(後述) を呼び、`_snapshot` を最新化しておく契約。

#### 2.2.3 `BJJSessionLifecycle`

責務: 「Pure ロジックを進めて良いか」の gate を保持する状態機械。
`main.ts` の `promptActive`, `tutorialIsOpen()`, `paused`,
`sessionEndReason`, `activeScenario` を 1 つの enum 風の構造に集約。

| 公開 API | シグネチャ | 用途 |
|---|---|---|
| `enum LifecyclePhase { Prompt, Tutorial, Active, Paused, SessionEnded }` | type | 排他 phase |
| `LifecyclePhase CurrentPhase { get; }` | read | gate 判定 |
| `Role SelectedRole { get; }` | read | `BJJInputProvider` に注入 |
| `ScenarioName? ActiveScenario { get; }` | read | HUD 表示 |
| `SessionEndReason? EndReason { get; }` | read | overlay 表示 |
| `void DismissPrompt()` | method | BTN_BASE edge で `Active` へ |
| `void ToggleTutorial()` | method | H キー |
| `void SetPaused(bool)` | method | BTN_PAUSE edge |
| `void EndSession(SessionEndReason)` | method | `SimEvent.SessionEnded` を受けて |
| `void RestartSession()` | method | restart 経路すべて |
| `void LoadScenario(ScenarioName)` | method | Digit1–7 |
| `void CycleRole(int direction)` | method | LS.x edge / A/D キー |
| `event Action OnRestartRequested` | event | `BJJGameManager` が `_simState` 再初期化 |
| `event Action<ScenarioName> OnScenarioLoadRequested` | event | 同上 (scenario seed 生成) |

`BJJGameManager.Update` は毎フレーム冒頭で `CurrentPhase` を読み、
`Active` 以外なら **Pure 層の Advance を呼ばずに return** する
(`main.ts` 549–620 と同じ早期 return パターン)。

#### 2.2.4 `BJJDebugHud`

責務: `Update` の末尾で呼ばれ、`BJJGameManager` から GameState / Events /
LastFrame / LastIntent / LastDefense を読んで `IMGUI.OnGUI` または
`UnityEngine.UIElements` のラベルに書き出す。**v1 ではビジュアルでなく
プレーンテキスト**。`main.ts` の `renderHud` (861–913) と
`renderEventLog` (272–286) と `renderInputLive` (457–476) と
`renderCoach` (478–498) を 1 ファイルに集約。

| 公開 API | シグネチャ | 用途 |
|---|---|---|
| `void RenderFromManager(BJJGameManager mgr)` | method | 1 フレーム描画 |
| `[SerializeField] bool showLayerA` | toggle | デバッグ可視化分離 |
| `[SerializeField] bool showCoach` | toggle | 同上 |

実装の選択は v1 では **IMGUI (`OnGUI`)** で十分。Coach HUD の Stage 2 完成形
は別ノート (UI Toolkit) で扱う。

### 2.3 依存関係図 (ASCII)

```
                    +-----------------------+
                    |  BJJGameManager       |
                    |  (Update ハブ)         |
                    +-----+----------+------+
                          |          |
            +-------------+          +---------------+
            |                                        |
            v                                        v
  +--------------------+                  +----------------------+
  | BJJInputProvider   |  <-- role --     | BJJSessionLifecycle  |
  | : IStepProvider    |    inject        | (gate / overlay)     |
  +---------+----------+                  +----------+-----------+
            |                                         |
            | uses                                    |
            v                                         |
  +--------------------+                              |
  | LayerAOps          |                              |
  | LayerB / LayerD    |                              |
  | OpponentAI         |                              |
  | (Pure C#)          |                              |
  +--------------------+                              |
                                                      |
                    +-----------------+               |
                    | BJJDebugHud     | <-- read -----+
                    | (OnGUI 描画)     |
                    +-----------------+
```

---

## 3. 入力配線 (New Input System)

### 3.1 Action Asset の場所と構成

ファイル: `Assets/BJJSimulator/Runtime/Input/BJJInputActions.inputactions`

ActionMap 1 つ (`Player`) のみ。複数 player や UI map は v1 では作らない
(Stage 1 が単一 namespace の `pressed` Set で動いているため)。

### 3.2 Action ↔ `RawHardwareSnapshot` フィールド対応

下表は `Runtime/Input/LayerA.cs §24-40` の `RawHardwareSnapshot` 構造体に
対する完全な mapping。Stage 1 の `keyboard.ts §76-100` および
`gamepad.ts §45-85` の対応関係を直訳。

| Action 名 | type | binding (Keyboard) | binding (Gamepad) | → snapshot field |
|---|---|---|---|---|
| `LeftStick` | Value (Vector2) | `<Keyboard>/w,a,s,d` (composite) | `<Gamepad>/leftStick` | `GamepadLs` (Vector2 として), `LsUp`/`LsDown`/`LsLeft`/`LsRight` (digital fallback) |
| `RightStick` | Value (Vector2) | `<Keyboard>/upArrow,leftArrow,downArrow,rightArrow` | `<Gamepad>/rightStick` | `GamepadRs`, `RsUp`/`RsDown`/`RsLeft`/`RsRight` |
| `LeftTrigger` | Value (Axis) | `<Keyboard>/f` | `<Gamepad>/leftTrigger` | `GamepadLTrigger`, `KbLTrigger` (キーボードは bool→1.0) |
| `RightTrigger` | Value (Axis) | `<Keyboard>/j` | `<Gamepad>/rightTrigger` | `GamepadRTrigger`, `KbRTrigger` |
| `LeftBumper` | Button | `<Keyboard>/r` | `<Gamepad>/leftShoulder` | `ButtonBit.LBumper` を OR |
| `RightBumper` | Button | `<Keyboard>/u` | `<Gamepad>/rightShoulder` | `ButtonBit.RBumper` を OR |
| `BtnBase` | Button | `<Keyboard>/space` | `<Gamepad>/buttonSouth` (A / ✕) | `ButtonBit.BtnBase` |
| `BtnRelease` | Button | `<Keyboard>/x` | `<Gamepad>/buttonEast` (B / ◯) | `ButtonBit.BtnRelease` |
| `BtnBreath` | Button | `<Keyboard>/c` | `<Gamepad>/buttonNorth` (Y / △) | `ButtonBit.BtnBreath` |
| `BtnReserved` | Button | `<Keyboard>/v` | `<Gamepad>/buttonWest` (X / □) | `ButtonBit.BtnReserved` |
| `BtnPause` | Button | `<Keyboard>/escape` | `<Gamepad>/start` | `ButtonBit.BtnPause` |

**Y 軸符号**: Stage 1 `gamepad.ts §61-63` で `ls_y = -axes[1]` を行っている
(「上が正」)。Unity の `<Gamepad>/leftStick` は **既に Y up が正** なので
**符号反転は不要**。`BJJInputProvider.PollHardware` 内でこの差分を吸収する
コメントを必ず残す (Stage 1 と挙動を揃える根拠)。

### 3.3 デバイス優先順位

`LayerA.cs §71` の `useGamepad` 判定で
`InputTransform.GamepadHasActivity(...)` が真なら gamepad、偽なら keyboard。
これは Pure 層がそのまま実装済み。`BJJInputProvider` は両方の値を
**毎フレーム両方ポーリングして** `RawHardwareSnapshot` の両系統フィールドを
埋め、Pure 層の判定に委ねる。

### 3.4 メタキー (gameplay 外) のハンドリング

Stage 1 の `window.addEventListener("keydown")` で扱う以下のキーは
gameplay の `InputAction` とは別系統:

| キー | 用途 (`main.ts` 行) | Stage 2 ハンドリング |
|---|---|---|
| `H` | チュートリアル開閉 (144) | 専用 `InputAction Tutorial` を `Player` map 内に追加し、`BJJSessionLifecycle.ToggleTutorial` に直結 |
| `Esc` (within tutorial) | チュートリアル閉 (148) | 同上 — Tutorial action 内で代替 binding |
| `Esc` (gameplay) | Pause toggle (`BTN_PAUSE` 経由) | `BtnPause` action — `BJJGameManager` で edge 検知 |
| `Digit1`–`7` | scenario load (152–166) | 専用 `Scenario1`..`Scenario7` action を 7 個 — `BJJSessionLifecycle.LoadScenario` |
| `Digit0` | restart (156–158) | `ScenarioReset` action |
| `BracketLeft` / `BracketRight` | coach target cycle (531–541) | v1 範囲外 (Coach HUD 別ノート) |

**未解決**: Action 数が増えるため、**`Meta` という別の InputActionMap に
分離するか、`Player` 1 つに集約するか** は v1 では決め切らない。
分離派の根拠は「gate 中も Meta は live にしたい」だが、`Player` map 1 つに
集約しても `BJJSessionLifecycle.CurrentPhase` で gate 判定すれば実害はない。
§9 に未解決として残す。

---

## 4. ループ制御 (`BJJGameManager.Update`)

### 4.1 処理順序

`Update` 内の処理は **Stage 1 `frame()` 543–755 と同じ早期 return チェーン**
を C# で再現する。`Time.deltaTime` を ms に変換して Pure 層に渡すだけ。

```
Update():
  realDtMs = Time.deltaTime * 1000f

  # gate 1: tutorial — sim 完全停止
  if lifecycle.CurrentPhase == Tutorial:
      hud.RenderFromManager(this)   # 静止画でも HUD は更新
      return

  # gate 2: prompt — role 選択中
  if lifecycle.CurrentPhase == Prompt:
      inputProvider.PollHardware()                      # snapshot 更新
      f = inputProvider.SamplePromptOnly(now)           # InputFrame だけ作る
      lifecycle.HandlePromptInput(f)                    # LS.x edge / BTN_BASE
      hud.RenderFromManager(this)
      return

  # gate 3: paused
  if lifecycle.CurrentPhase == Paused:
      inputProvider.PollHardware()
      f = inputProvider.SamplePromptOnly(now)
      if (f.ButtonEdges & BtnPause) != 0:
          lifecycle.SetPaused(false)
      hud.RenderFromManager(this)
      return

  # gate 4: session ended
  if lifecycle.CurrentPhase == SessionEnded:
      inputProvider.PollHardware()
      f = inputProvider.SamplePromptOnly(now)
      if role == Spectate:
          lifecycle.TickSessionEndAutoRestart(realDtMs)  # 2000ms で auto-restart
      if (f.ButtonEdges & BtnBase) != 0:
          lifecycle.RestartSession()
      hud.RenderFromManager(this)
      return

  # gate 5: pause edge — sample once before Advance
  inputProvider.PollHardware()
  pauseProbe = inputProvider.SamplePromptOnly(now)
  if (pauseProbe.ButtonEdges & BtnPause) != 0:
      lifecycle.SetPaused(true)
      hud.RenderFromManager(this)
      return

  # round timer (real wall-clock; main.ts 614)
  _roundElapsedMs += realDtMs
  if _roundElapsedMs >= ROUND_LENGTH_MS:
      lifecycle.EndSession(SessionEndReason.RoundTimeUp)
      hud.RenderFromManager(this)
      return

  # active path: Pure 層を 1 step 以上回す
  result = FixedStepOps.Advance(_simState, realDtMs, inputProvider)
  _simState = result.Next

  # SimEvent → lifecycle 反映 (SESSION_ENDED のみ; その他は HUD/将来のビジュアル委譲)
  foreach ev in result.Events:
      if ev.Kind == SimEventKind.SessionEnded:
          lifecycle.EndSession(ev.Reason)

  hud.RenderFromManager(this)
```

### 4.2 `Time.deltaTime` の扱い

- **wall-clock** で進めるのは round timer (#5) と SessionEnd の auto-restart
  (#6) のみ。`main.ts` 614 の「`game_dt` を使わない」根拠と同じ。
- Pure 層内の `game_dt` (judgment-window slow-mo 反映) は
  `FixedStep.cs §126-127` で `timeScale` を掛け算して内部で計算済みなので、
  MonoBehaviour 側は何もしない。
- `Time.fixedDeltaTime` は **使わない**。`FixedStepOps` 自身が固定ステップ
  ループを回すため、Unity の `FixedUpdate` を使うと二重ループになる。
  本ノートでは **`Update` 1 本で駆動する**。

### 4.3 Pause 中の挙動

- `lifecycle.SetPaused(true)` → `CurrentPhase = Paused`
- Update は gate 3 で早期 return → `FixedStepOps.Advance` 呼ばれず
- `_simState.SimClockMs` は据え置き → 復帰時に飛ばない
- `_lastUpdateTimeMs` は据え置きでよい (`realDtMs` の起点を再計算しないため、
  pause 解除直後に巨大な `realDtMs` が来うるが、Pure 層の
  `MaxStepsPerAdvance = 8` の spiral-of-death guard が吸収する)

### 4.4 Tutorial / Prompt 中の `_lastUpdateTimeMs`

`main.ts` 789 で `lastRafMs = performance.now()` を prompt 終了直後に
リセットしている。Stage 2 でも `lifecycle.DismissPrompt()` を契機に
`BJJGameManager` が `_lastUpdateTimeMs` を `Time.realtimeSinceStartupAsDouble`
で再起点にする。Tutorial も同様。

---

## 5. シーン構成 (v1 最小)

`Assets/BJJSimulator/Scenes/BJJDevHarness.unity` 1 シーンのみ。

| GameObject | Component | 設定 | 根拠 |
|---|---|---|---|
| `Main Camera` | Camera | `Orthographic = false`, `Position = (0, 0, -10)` | 暫定。OTS は別ノート |
| `Directional Light` | Light | デフォルト | URP / SSS は別ノート |
| `EventSystem` | EventSystem + StandaloneInputModule (or InputSystemUIInputModule) | デフォルト | Unity UI 用の最小 |
| `GameManager` | `BJJGameManager` + `BJJInputProvider` + `BJJSessionLifecycle` + `BJJDebugHud` | 4 コンポーネントを 1 GameObject に | inspector 上で SerializeField 配線がしやすい |

**v1 では含めない** (別ノート行き):
- キャラモデル / Skinned mesh / Animator (Stage 1 `blockman.ts` の Unity 等価)
- URP Volume profile (stamina warm shift / window vignette)
- UI Toolkit Coach HUD / Event Log / Tutorial overlay (v1 は IMGUI 仮 HUD)
- ロール選択 UI のビジュアル仕様
- Pause overlay / SessionEnd overlay の UI

これらは「`stage2_visual_layer_v1.md`」「`stage2_ui_v1.md`」(仮称) で扱う。

---

## 6. テスト方針

### 6.1 EditMode で覆う範囲

- Pure 層 (`Runtime/State/`, `Runtime/Input/`, `Runtime/Sim/`, `Runtime/AI/`) は
  既に 234 ケース緑。**追加は不要**。
- `BJJInputProvider` の `Sample` / `ResolveCommit` /
  `ResolveCounterCommit` は **`IStepProvider` 経由でテスト可能** にするため、
  `RawHardwareSnapshot` を直接 SetForTest する public hook
  (`internal void SetSnapshotForTest(RawHardwareSnapshot)`) を 1 つ生やす。
  この hook を使えば New Input System を一切起こさずに role-swap 時の Layer B
  分岐 / AI 注入を NUnit `[Test]` で検証できる。
- `BJJSessionLifecycle` の phase 遷移は MonoBehaviour 内部状態が gate 状態のみ
  なので、`new BJJSessionLifecycle()` を NUnit から直接 new して
  `DismissPrompt() → SetPaused(true) → SetPaused(false)` のシーケンスを
  assert できる (MonoBehaviour だが Unity API を呼んでいないので EditMode
  test 可能)。
- `BJJGameManager` 自体は Unity ループに乗るため EditMode 不可。Play Mode で
  しか走らせられない。**v1 ではテスト対象外**。

### 6.2 Play Mode テスト — v1 範囲外

Play Mode テスト (`Assets/BJJSimulator/Tests/PlayMode/`) は **v1 では
セットアップしない**。理由:

1. `BJJInputProvider` を上記の `SetSnapshotForTest` 経路で覆えば
   ループ全体の挙動は EditMode 内 + Pure 層シナリオテストで再現可能。
2. Play Mode は CI (GitHub Actions) で Unity license 取得が必要になり、
   M1 着手前にコストを払いたくない。
3. Stage 1 にも Play Mode 相当 (Vitest E2E) は無く、scenario test
   (`tests/scenario/*.test.ts`) で代用している。Stage 2 もこの線を踏襲。

### 6.3 必要な追加テスト

| テスト名 | 配置 | 検証内容 |
|---|---|---|
| `BJJInputProviderTest.cs` | `Tests/EditMode/` | Bottom role: `SetSnapshotForTest` で攻め入力 → `Sample` が `Intent` を埋める / Top role: 攻め入力でも `lastIntent = NEUTRAL_INTENT` になる / Spectate: 両方 AI 出力 |
| `BJJSessionLifecycleTest.cs` | `Tests/EditMode/` | Prompt → DismissPrompt → Active / Active → SetPaused(true) → Paused / Active → EndSession → SessionEnded / SessionEnded → RestartSession → Active |

---

## 7. 既知の差分 / 注意点

### 7.1 Stage 1 と挙動が変わる箇所

- **HUD レイアウト**: Stage 1 は HTML/CSS で組まれているが、v1 では IMGUI。
  情報量は同じだが見た目が違う。Stage 2 の最終形 (UI Toolkit) で揃える。
- **scene pulse / shake**: `main.ts` 700–742 の `scene3d.pulseFlash` /
  `pulseShake` 呼び出しは **v1 では何もしない**。`SimEvent` は HUD ログには
  流すが、ビジュアル反応はビジュアルノートで設計。
- **stamina color grading**: `setStaminaFatigue` (855–858) は
  v1 では HUD のテキスト 1 行にダウングレード。

### 7.2 Stage 1 と意図的に挙動を揃える箇所

- ロール選択は LS.x edge (`> 0.6` / `< -0.6`) で cycle (`main.ts` 772–780)。
  Unity の `<Gamepad>/leftStick` の値域は同じく `[-1, 1]` なので閾値そのまま。
- `roundElapsedMs` は **wall-clock dt** で進める (`main.ts` 614)。slow-mo
  中も round time が止まらない仕様。
- Spectate 中の auto-restart は `SPECTATE_AUTO_RESTART_MS = 2000ms`
  (`main.ts` 305)。

---

## 8. 実装順序

1. `BJJSessionLifecycle.cs` (Pure logic 寄りで EditMode test 容易)
2. `BJJInputActions.inputactions` (Action Asset 定義)
3. `BJJInputProvider.cs` (Action 結線 + `IStepProvider` 実装) +
   `BJJInputProviderTest.cs` (EditMode)
4. `BJJDebugHud.cs` (IMGUI 描画)
5. `BJJGameManager.cs` (Update ハブ; ここで初めて 4 コンポーネントが揃う)
6. `BJJDevHarness.unity` シーン作成 + GameObject 構成
7. Editor で「Bottom role でシナリオ#3 (三角絞め) を実行 → 技確定」を目視確認

各段階の完了判定は EditMode test 緑 + (5 以降) Editor Play で 1 シナリオ
通せること。

---

## 9. 未解決事項

- **Action の `Player` / `Meta` map 分離**: Tutorial / Pause / Scenario 系を
  別 map に分けるか単一 map に集約するか (§3.4)。Tutorial 中も Meta 系 action
  だけは生かしたいが、`BJJSessionLifecycle.CurrentPhase` 判定で十分かも。
  実装着手時に `PlayerInput` のセットアップ難易度を見て判定。
- **Coach target の cycle UI**: `BracketLeft` / `BracketRight` の Stage 2 等価
  (§3.4 の最後の行)。Coach HUD ノート側で扱う方が筋が良いので本ノートでは
  保留。
- **Pause / SessionEnd overlay のビジュアル**: v1 では IMGUI で
  「PAUSED」「SESSION ENDED — restart で再開」テキストを画面中央に出すだけ。
  UI Toolkit ノートで再設計予定。
- **`Time.realtimeSinceStartupAsDouble` vs `Time.unscaledTime` vs 自前計測**:
  Pause 復帰直後の `realDtMs` 起点をどうリセットするかの API 選択は
  実装着手時に決定 (3 候補とも機能的には等価だが Unity LTS 各版での
  precision に差があり、どれが安全かはランタイム検証してから確定)。
- **マルチプレイヤー**: `BJJInputProvider` 1 個で 1 player 想定。ローカル 2 人
  プレイは `architecture_overview_v1.md §9` に未決として残っており、本ノート
  でも v1 範囲外。
- **Editor 上での gamepad 検出**: macOS (Apple Silicon) で
  Xbox / DualSense controller の `<Gamepad>/...` パスが両方拾えるか
  Editor Play で要確認。Stage 1 の `gamepad.ts §33-39` の DualSense 検出
  ロジック相当を Unity の `Gamepad.current.device.description` で再実装する
  必要があるかは未調査。
