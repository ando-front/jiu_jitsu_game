// PLATFORM — entry point. rAF-driven fixed-timestep loop:
// Layer A → (Layer B attacker ∪ Layer B defender) → Layer D → stepSimulation
// → Three.js + HUD.
//
// Role selection (§F of input_system_defense_v1.md): a one-shot prompt at
// startup lets the player pick Bottom or Top. The role gates which Layer B
// branch runs; the other side is driven by ZERO_*_INTENT so its FSMs tick
// without influence.

import { GamepadSource } from "./input/gamepad.js";
import { INITIAL_LAYER_B_STATE, transformLayerB, type LayerBState } from "./input/layerB.js";
import { INITIAL_LAYER_B_DEFENSE_STATE, transformLayerBDefense, type LayerBDefenseState } from "./input/layerB_defense.js";
import { INITIAL_LAYER_D_STATE, resolveLayerD, type LayerDState } from "./input/layerD.js";
import { INITIAL_LAYER_D_DEFENSE_STATE, resolveLayerDDefense, type LayerDDefenseState } from "./input/layerD_defense.js";
import { opponentIntent, type AIOutput } from "./ai/opponent_ai.js";
import { KeyboardSource } from "./input/keyboard.js";
import { LayerA } from "./input/layerA.js";
import { ButtonBit, type InputFrame } from "./input/types.js";
import type { DiscreteIntent, Intent } from "./input/intent.js";
import { ZERO_DEFENSE_INTENT, type DefenseIntent } from "./input/intent_defense.js";
import { initialGameState, type GameState, type SimEvent } from "./state/game_state.js";
import {
  SCENARIO_ORDER,
  buildScenario,
  type ScenarioName,
} from "./state/scenarios.js";
import { breakBucket } from "./state/posture_break.js";
import { advance, FIXED_STEP_MS, type FixedStepState } from "./sim/fixed_step.js";
import { createScene } from "./scene/blockman.js";

type Role = "Bottom" | "Top" | "Spectate";
const ROLE_CYCLE: readonly Role[] = ["Bottom", "Top", "Spectate"] as const;

const canvas = document.getElementById("three-canvas") as HTMLCanvasElement;
const hud = document.getElementById("debug-hud") as HTMLPreElement;
const eventLogListEl = document.getElementById("event-log-list") as HTMLUListElement;

// -- Role prompt --------------------------------------------------------------
// Per defense doc §F: one-shot full-screen text on boot, dismissed with A (BTN_BASE).
// Until dismissed, the sim is paused and inputs only drive the prompt.

const promptEl = document.createElement("div");
promptEl.style.cssText = `
  position: absolute; inset: 0; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 18px;
  background: rgba(8, 8, 12, 0.92); color: #e6e6ea; z-index: 50;
  font-family: ui-monospace, Menlo, Consolas, monospace;
  text-align: center; padding: 24px;
`;
promptEl.innerHTML = `
  <div style="font-size: 22px; letter-spacing: 0.1em;">ROLE</div>
  <div id="role-choice" style="font-size: 36px; font-weight: 600;">BOTTOM</div>
  <div id="role-description" style="font-size: 13px; opacity: 0.65; max-width: 520px; line-height: 1.6;">
    You are <b>[BOTTOM]</b> — attacker holding closed guard.<br>
    Nudge <b>LS left/right</b> or tap <b>A/D</b> to cycle BOTTOM → TOP → SPECTATE.<br>
    Press <b>[A] / Space</b> to start.
  </div>
  <div style="font-size: 12px; opacity: 0.8; margin-top: 12px;
              padding: 6px 14px; border: 1px solid #4e52a4; border-radius: 5px;
              background: rgba(32, 34, 56, 0.6);">
    遊び方がわからない場合は <b>[H]</b> キー、または画面右下の
    <b>? チュートリアル</b> ボタンで日本語ガイドを表示できます。
  </div>
`;
document.getElementById("app")?.appendChild(promptEl);
const choiceEl = promptEl.querySelector("#role-choice") as HTMLElement;
const descEl = promptEl.querySelector("#role-description") as HTMLElement;

let role: Role = "Bottom";
let promptActive = true;

function updatePrompt() {
  choiceEl.textContent = role.toUpperCase();
  const body =
    role === "Bottom"
      ? "You are <b>[BOTTOM]</b> — attacker holding closed guard."
      : role === "Top"
      ? "You are <b>[TOP]</b> — defender trying to pass."
      : "<b>[SPECTATE]</b> — both sides run by AI; you observe.";
  descEl.innerHTML = `${body}<br>
    Nudge <b>LS left/right</b> or tap <b>A/D</b> to cycle BOTTOM → TOP → SPECTATE.<br>
    Press <b>[A] / Space</b> to start.`;
}

// -- Tutorial overlay (日本語ガイド) ------------------------------------------
// Toggled with H key or the "? チュートリアル" button. While open, the
// fixed-step sim loop is paused so the player can read without the game
// advancing. Separate from the role prompt and the session-end overlay.

const tutorialEl = document.getElementById("tutorial") as HTMLElement;
const tutorialToggleBtn = document.getElementById("tutorial-toggle") as HTMLButtonElement;

function setTutorial(open: boolean): void {
  tutorialEl.classList.toggle("open", open);
  tutorialEl.scrollTop = 0;
}
function tutorialIsOpen(): boolean {
  return tutorialEl.classList.contains("open");
}
function toggleTutorial(): void {
  setTutorial(!tutorialIsOpen());
}

tutorialToggleBtn.addEventListener("click", toggleTutorial);
// H / Esc / 1-5 on a global listener — deliberately outside the
// game-input keyboard source so these meta-keys work even while the
// role prompt or the end-overlay owns game input. Digits load practice
// scenarios (§ docs/design/state_machines_v1.md §8.2 conditions).
window.addEventListener("keydown", (e) => {
  if (e.code === "KeyH") {
    toggleTutorial();
    e.preventDefault();
  } else if (e.code === "Escape" && tutorialIsOpen()) {
    setTutorial(false);
    e.preventDefault();
  } else if (!tutorialIsOpen() && !promptActive) {
    // Digit1..Digit5 → scenario 0..4. Digit0 → clear back to neutral.
    const digitMatch = /^Digit([0-9])$/.exec(e.code);
    if (digitMatch !== null) {
      const n = Number(digitMatch[1]);
      if (n === 0) {
        restartSession();
        e.preventDefault();
      } else {
        const idx = n - 1;
        if (idx < SCENARIO_ORDER.length) {
          loadScenario(SCENARIO_ORDER[idx]!);
          e.preventDefault();
        }
      }
    }
  }
});

// -- Inputs -------------------------------------------------------------------

const keyboard = new KeyboardSource();
keyboard.attach(window);
const gamepad = new GamepadSource();
const layerA = new LayerA(gamepad, keyboard);

let bState: LayerBState = INITIAL_LAYER_B_STATE;
let bDefState: LayerBDefenseState = INITIAL_LAYER_B_DEFENSE_STATE;
let dState: LayerDState = INITIAL_LAYER_D_STATE;
let dDefState: LayerDDefenseState = INITIAL_LAYER_D_DEFENSE_STATE;

// -- Scene & sim --------------------------------------------------------------

const scene3d = createScene(canvas);
scene3d.resize(window.innerWidth, window.innerHeight);
window.addEventListener("resize", () =>
  scene3d.resize(window.innerWidth, window.innerHeight),
);

let simState: FixedStepState = Object.freeze({
  accumulatorMs: 0,
  simClockMs: performance.now(),
  game: initialGameState(performance.now()),
});
let lastRafMs = performance.now();

let lastIntent: Intent | null = null;
let lastDefense: DefenseIntent | null = null;
let lastFrame: InputFrame | null = null;
// Stashed AI decisions for the current fixed step. sample() fills these;
// resolveCommit / resolveCounterCommit read them so we don't recompute
// the AI twice per step. In Spectate mode both are populated; in the
// single-human modes only the AI-owned side is populated and the other
// stays null.
let pendingAiBottom: AIOutput | null = null;
let pendingAiTop: AIOutput | null = null;

// -- Event log ---------------------------------------------------------------
// Rolling buffer of the last N sim events so transient outcomes
// (PARRIED / WINDOW_OPENING / TECHNIQUE_CONFIRMED / CUT_*) stay visible
// after their pulse fades. Not every event is logged — foot FSM pings
// and WINDOW_OPEN/CLOSED get filtered because they're spammy and the
// already-shown WINDOW_OPENING event conveys the interesting edge.

const EVENT_LOG_LIMIT = 12;
type LoggedEvent = { nowMs: number; ev: SimEvent };
const eventLog: LoggedEvent[] = [];
const SUPPRESSED_EVENT_KINDS: ReadonlySet<string> = new Set([
  "WINDOW_OPEN",
  "WINDOW_CLOSED",
  "COUNTER_WINDOW_OPEN",
  "COUNTER_WINDOW_CLOSED",
  "LOCKING_STARTED",
]);

function pushEvent(ev: SimEvent, nowMs: number): void {
  if (SUPPRESSED_EVENT_KINDS.has(ev.kind)) return;
  eventLog.unshift({ nowMs, ev });
  if (eventLog.length > EVENT_LOG_LIMIT) eventLog.length = EVENT_LOG_LIMIT;
}

function formatEvent(ev: SimEvent): { args: string } {
  // Args are the event-specific extras (side / zone / reason / etc.).
  // Kept as a single short string so the log line stays on one row.
  switch (ev.kind) {
    case "REACH_STARTED":
    case "CONTACT":
    case "GRIPPED":
    case "PARRIED":
      return { args: `${ev.side} ${ev.zone}` };
    case "GRIP_BROKEN":
      return { args: `${ev.side} ${ev.zone} (${ev.reason})` };
    case "UNLOCKED":
    case "LOCK_SUCCEEDED":
    case "LOCK_FAILED":
      return { args: ev.side };
    case "WINDOW_OPENING":
      return { args: `${ev.firedBy} ${ev.candidates.join(",")}` };
    case "WINDOW_CLOSING":
      return { args: ev.reason };
    case "TECHNIQUE_CONFIRMED":
      return { args: ev.technique };
    case "COUNTER_WINDOW_OPENING":
      return { args: ev.candidates.join(",") };
    case "COUNTER_WINDOW_CLOSING":
      return { args: ev.reason };
    case "COUNTER_CONFIRMED":
      return { args: ev.counter };
    case "CUT_STARTED":
      return { args: `def=${ev.defender} → atk=${ev.attackerSide} ${ev.zone}` };
    case "CUT_SUCCEEDED":
      return { args: `def=${ev.defender} → atk=${ev.attackerSide}` };
    case "CUT_FAILED":
      return { args: `def=${ev.defender}` };
    case "SESSION_ENDED":
      return { args: ev.reason };
    default:
      return { args: "" };
  }
}

function renderEventLog(nowMs: number): void {
  const parts: string[] = [];
  for (const entry of eventLog) {
    const ageSec = Math.max(0, (nowMs - entry.nowMs) / 1000);
    const ageTxt = ageSec < 10 ? ageSec.toFixed(1) : ageSec.toFixed(0);
    const { args } = formatEvent(entry.ev);
    parts.push(
      `<li><span class="t">-${ageTxt}s</span>` +
      `<span class="k${entry.ev.kind}">${entry.ev.kind}</span>` +
      (args ? ` <span class="args">${escapeHtml(args)}</span>` : "") +
      `</li>`,
    );
  }
  eventLogListEl.innerHTML = parts.join("");
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}
// Reason last SESSION_ENDED pulse carried, so the restart overlay can
// label the outcome. null when the session is still live.
type SessionEndReason =
  | "PASS_SUCCESS"
  | "TECHNIQUE_FINISHED"
  | "GUARD_OPENED"
  | "ROUND_TIME_UP";
let sessionEndReason: SessionEndReason | null = null;
// Wall-clock accumulator since the end overlay appeared, used to
// auto-restart while spectating. Reset on show/hide.
let sessionEndElapsedMs = 0;
const SPECTATE_AUTO_RESTART_MS = 2000;

// -- Pause state -------------------------------------------------------------
// BTN_PAUSE (Esc / Options) toggles the pause overlay. While paused, the
// fixed-step loop is halted and the Pause DOM overlay is visible.
const pauseEl = document.getElementById("pause-overlay") as HTMLElement;
let paused = false;

function setPaused(next: boolean) {
  paused = next;
  pauseEl.style.display = next ? "flex" : "none";
}

// -- Round timer -------------------------------------------------------------
// Drives a visible countdown and triggers SESSION_ENDED("ROUND_TIME_UP") when
// it hits zero. Matches IBJJF adult white/blue-belt match length (5 min).
// Timer runs on GameState.time.gameDtMs (slow-mo-aware) so the judgment
// window doesn't eat extra seconds.
const ROUND_LENGTH_MS = 5 * 60 * 1000;
let roundElapsedMs = 0;

function formatRoundTime(remainMs: number): string {
  const r = Math.max(0, Math.ceil(remainMs / 1000));
  const m = Math.floor(r / 60);
  const s = r % 60;
  return `${m}:${String(s).padStart(2, "0")}`;
}

// -- Session-end overlay -----------------------------------------------------

const endEl = document.createElement("div");
endEl.style.cssText = `
  position: absolute; inset: 0; display: none; flex-direction: column;
  align-items: center; justify-content: center; gap: 14px;
  background: rgba(8, 8, 12, 0.82); color: #e6e6ea; z-index: 40;
  font-family: ui-monospace, Menlo, Consolas, monospace;
  text-align: center; padding: 24px; pointer-events: none;
`;
endEl.innerHTML = `
  <div style="font-size: 15px; letter-spacing: 0.2em; opacity: 0.65;">SESSION ENDED</div>
  <div id="end-reason" style="font-size: 32px; font-weight: 600;">—</div>
  <div style="font-size: 12px; opacity: 0.55;">Press <b>[A] / Space</b> to restart.</div>
`;
document.getElementById("app")?.appendChild(endEl);
const endReasonEl = endEl.querySelector("#end-reason") as HTMLElement;

function showEndOverlay(reason: SessionEndReason) {
  sessionEndReason = reason;
  sessionEndElapsedMs = 0;
  endReasonEl.textContent =
    reason === "PASS_SUCCESS" ? "GUARD PASSED (TOP WINS)" :
    reason === "TECHNIQUE_FINISHED" ? "TECHNIQUE CONFIRMED (BOTTOM WINS)" :
    reason === "ROUND_TIME_UP" ? "TIME UP — ROUND OVER" :
    "GUARD OPENED — SCRAMBLE";
  endEl.style.display = "flex";
}

function hideEndOverlay() {
  sessionEndReason = null;
  sessionEndElapsedMs = 0;
  endEl.style.display = "none";
}

function restartSession() {
  hideEndOverlay();
  setPaused(false);
  bState = INITIAL_LAYER_B_STATE;
  bDefState = INITIAL_LAYER_B_DEFENSE_STATE;
  dState = INITIAL_LAYER_D_STATE;
  dDefState = INITIAL_LAYER_D_DEFENSE_STATE;
  pendingAiBottom = null;
  pendingAiTop = null;
  lastIntent = null;
  lastDefense = null;
  eventLog.length = 0;
  activeScenario = null;
  roundElapsedMs = 0;
  lastRafMs = performance.now();
  simState = Object.freeze({
    accumulatorMs: 0,
    simClockMs: performance.now(),
    game: initialGameState(performance.now()),
  });
}

// -- Practice scenarios ------------------------------------------------------
// Digit 1-5 loads a preset GameState seeded near a specific judgment-window
// firing condition. See src/state/scenarios.ts. Loading clears the event
// log and any in-progress end overlay, but leaves the role/tutorial alone.
let activeScenario: ScenarioName | null = null;

function loadScenario(name: ScenarioName): void {
  hideEndOverlay();
  setPaused(false);
  bState = INITIAL_LAYER_B_STATE;
  bDefState = INITIAL_LAYER_B_DEFENSE_STATE;
  dState = INITIAL_LAYER_D_STATE;
  dDefState = INITIAL_LAYER_D_DEFENSE_STATE;
  pendingAiBottom = null;
  pendingAiTop = null;
  lastIntent = null;
  lastDefense = null;
  eventLog.length = 0;
  activeScenario = name;
  roundElapsedMs = 0;
  const now = performance.now();
  lastRafMs = now;
  simState = Object.freeze({
    accumulatorMs: 0,
    simClockMs: now,
    game: buildScenario(name, now),
  });
}

function frame(now: number) {
  const realDt = now - lastRafMs;
  lastRafMs = now;

  // Tutorial is modal — halt the sim loop and the role prompt alike so
  // the player can read without state advancing beneath them.
  if (tutorialIsOpen()) {
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  if (promptActive) {
    runPromptTick();
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  // Paused: halt sim + keep polling for BTN_PAUSE edge to unpause.
  if (paused) {
    const f = layerA.sample(performance.now());
    lastFrame = f;
    if ((f.button_edges & ButtonBit.BTN_PAUSE) !== 0) {
      setPaused(false);
    }
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  // Session-ended: stop the fixed-step loop but keep polling input so
  // the player can restart. Scene pulses still drain so the confirm
  // flash doesn't freeze mid-fade, and the event log keeps ticking so
  // the last events' age counters advance instead of freezing.
  if (sessionEndReason !== null) {
    scene3d.updatePulses(realDt);
    const f = layerA.sample(performance.now());
    lastFrame = f;
    // Spectate: auto-restart after a short delay so you can leave the
    // sim running as a stress test / passive observer.
    if (role === "Spectate") {
      sessionEndElapsedMs += realDt;
      if (sessionEndElapsedMs >= SPECTATE_AUTO_RESTART_MS) {
        restartSession();
      }
    }
    if ((f.button_edges & ButtonBit.BTN_BASE) !== 0) {
      restartSession();
    }
    renderEventLog(simState.game.nowMs);
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  scene3d.updatePulses(realDt);

  // BTN_PAUSE edge: sampled once per rAF (not per fixed step) because
  // the pause gate lives in this loop, not in stepSimulation.
  const pauseProbe = layerA.sample(performance.now());
  if ((pauseProbe.button_edges & ButtonBit.BTN_PAUSE) !== 0) {
    setPaused(true);
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  // Round timer: advance by real wall-clock. We don't use game_dt so
  // judgment-window slow-mo doesn't add extra round time. Time-up is
  // handled by funneling through the existing session-end path.
  roundElapsedMs += realDt;
  if (roundElapsedMs >= ROUND_LENGTH_MS && sessionEndReason === null) {
    showEndOverlay("ROUND_TIME_UP");
    scene3d.render();
    requestAnimationFrame(frame);
    return;
  }

  const res = advance(simState, realDt, {
    sample: (stepNowMs: number) => {
      const inputFrame = layerA.sample(stepNowMs);
      lastFrame = inputFrame;

      if (role === "Bottom") {
        const b = transformLayerB(inputFrame, bState);
        bState = b.nextState;
        lastIntent = b.intent;
        const aiTop = opponentIntent(simState.game, "Top");
        pendingAiTop = aiTop;
        pendingAiBottom = null;
        const aiDefense = aiTop.role === "Top" ? aiTop.defense : ZERO_DEFENSE_INTENT;
        lastDefense = aiDefense;
        return { frame: inputFrame, intent: b.intent, defense: aiDefense };
      } else if (role === "Top") {
        const d = transformLayerBDefense(inputFrame, bDefState);
        bDefState = d.nextState;
        lastDefense = d.intent;
        const aiBottom = opponentIntent(simState.game, "Bottom");
        pendingAiBottom = aiBottom;
        pendingAiTop = null;
        const aiIntent = aiBottom.role === "Bottom" ? aiBottom.intent : NEUTRAL_INTENT;
        lastIntent = aiIntent;
        return { frame: inputFrame, intent: aiIntent, defense: d.intent };
      } else {
        // Spectate: both sides from AI. Human input is ignored but still
        // sampled so the HUD reflects the connected controller.
        const aiBottom = opponentIntent(simState.game, "Bottom");
        const aiTop = opponentIntent(simState.game, "Top");
        pendingAiBottom = aiBottom;
        pendingAiTop = aiTop;
        const aiIntent = aiBottom.role === "Bottom" ? aiBottom.intent : NEUTRAL_INTENT;
        const aiDefense = aiTop.role === "Top" ? aiTop.defense : ZERO_DEFENSE_INTENT;
        lastIntent = aiIntent;
        lastDefense = aiDefense;
        return { frame: inputFrame, intent: aiIntent, defense: aiDefense };
      }
    },
    resolveCommit: (f, intent, game, dtMs) => {
      if (role !== "Bottom") {
        // Top or Spectate → Bottom is AI. Use its stashed decision.
        return pendingAiBottom?.role === "Bottom"
          ? pendingAiBottom.confirmedTechnique
          : null;
      }
      const windowIsOpen = game.judgmentWindow.state === "OPEN";
      const r = resolveLayerD(dState, {
        nowMs: f.timestamp,
        dtMs,
        frame: f,
        hip: intent.hip,
        candidates: game.judgmentWindow.candidates,
        windowIsOpen,
      });
      dState = r.next;
      return r.confirmedTechnique;
    },
    resolveCounterCommit: (f, game, dtMs) => {
      if (role !== "Top") {
        // Bottom or Spectate → Top is AI. Use its stashed decision.
        return pendingAiTop?.role === "Top" ? pendingAiTop.confirmedCounter : null;
      }
      const windowIsOpen = game.counterWindow.state === "OPEN";
      const r = resolveLayerDDefense(dDefState, {
        nowMs: f.timestamp,
        dtMs,
        frame: f,
        candidates: game.counterWindow.candidates,
        windowIsOpen,
        attackerSweepLateralSign: game.attackerSweepLateralSign,
      });
      dDefState = r.next;
      return r.confirmedCounter;
    },
  });
  simState = res.next;

  // Map SimEvents to scene pulses and the rolling event log.
  for (const ev of res.events) {
    pushEvent(ev, simState.game.nowMs);
    switch (ev.kind) {
      case "GRIPPED":
        scene3d.pulseShake("top", 0.04, 120);
        break;
      case "PARRIED":
        scene3d.pulseShake("bottom", 0.06, 160);
        break;
      case "GRIP_BROKEN":
        scene3d.pulseShake("bottom", 0.05, 140);
        break;
      case "TECHNIQUE_CONFIRMED":
        scene3d.pulseFlash(0xffd98c, 220);
        scene3d.pulseShake("top", 0.15, 320);
        break;
      case "COUNTER_CONFIRMED":
        scene3d.pulseFlash(0x9ec9ff, 220);
        scene3d.pulseShake("bottom", 0.15, 320);
        break;
      case "WINDOW_OPENING":
      case "COUNTER_WINDOW_OPENING":
        scene3d.pulseFlash(0xfff2d0, 80);
        break;
      case "GUARD_OPENED":
        scene3d.pulseFlash(0xff7070, 360);
        break;
      case "PASS_STARTED":
        scene3d.pulseFlash(0x9ec9ff, 140);
        break;
      case "PASS_SUCCEEDED":
        scene3d.pulseFlash(0x9ec9ff, 480);
        scene3d.pulseShake("bottom", 0.2, 500);
        break;
      case "PASS_FAILED":
        scene3d.pulseFlash(0xffb080, 260);
        break;
      case "SESSION_ENDED":
        showEndOverlay(ev.reason);
        break;
    }
  }

  const game = simState.game;
  applyToScene(game);
  if (lastFrame !== null && lastIntent !== null) {
    hud.textContent = renderHud(lastFrame, lastIntent, game, res.stepsRun);
  }
  renderEventLog(game.nowMs);
  scene3d.render();
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// Attacker intent used while playing as Top — keeps all attacker FSMs idle.
const NEUTRAL_INTENT: Intent = Object.freeze({
  hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
  grip: { l_hand_target: null, l_grip_strength: 0, r_hand_target: null, r_grip_strength: 0 },
  discrete: [],
});

// Edge-detected LS.x so cycling role requires a fresh push (not just
// holding left/right). The previous two-way assignment didn't work for
// three roles because there's no stick position for "Spectate".
let lastPromptLsX = 0;

function runPromptTick() {
  const f = layerA.sample(performance.now());
  lastFrame = f;
  const crossedLeft = lastPromptLsX > -0.6 && f.ls.x <= -0.6;
  const crossedRight = lastPromptLsX < 0.6 && f.ls.x >= 0.6;
  if (crossedLeft) {
    const idx = ROLE_CYCLE.indexOf(role);
    role = ROLE_CYCLE[(idx - 1 + ROLE_CYCLE.length) % ROLE_CYCLE.length]!;
  } else if (crossedRight) {
    const idx = ROLE_CYCLE.indexOf(role);
    role = ROLE_CYCLE[(idx + 1) % ROLE_CYCLE.length]!;
  }
  lastPromptLsX = f.ls.x;
  updatePrompt();
  // Dismiss with BTN_BASE edge.
  if ((f.button_edges & ButtonBit.BTN_BASE) !== 0) {
    promptActive = false;
    promptEl.remove();
    // Reset the rAF time origin so the first post-prompt frame doesn't
    // dump the whole prompt duration into the sim's accumulator.
    lastRafMs = performance.now();
    simState = Object.freeze({
      accumulatorMs: 0,
      simClockMs: performance.now(),
      game: initialGameState(performance.now()),
    });
  }
}

// -- Scene application --------------------------------------------------------

function applyToScene(g: GameState) {
  // Bottom rig is driven by the attacker intent regardless of who's
  // supplying it (human-as-Bottom, AI-as-Bottom during Top/Spectate).
  if (lastIntent !== null && role !== "Top") {
    scene3d.bottom.root.rotation.y = lastIntent.hip.hip_angle_target;
    scene3d.bottom.root.position.z = lastIntent.hip.hip_push * 0.3;
    scene3d.bottom.root.position.x = lastIntent.hip.hip_lateral * 0.2;
  }
  // Top rig's weight signals come from defender intent (human-as-Top or
  // AI-as-Top during Bottom/Spectate). In Spectate specifically, we also
  // want the postureBreak to keep driving the top rig because defense
  // intent alone doesn't encode crumple. The logic below handles both.
  if (lastDefense !== null && role === "Top") {
    scene3d.top.root.position.x = lastDefense.hip.weight_lateral * 0.25;
    scene3d.top.root.position.z = -1.1 + lastDefense.hip.weight_forward * 0.25;
  }

  const pb = g.top.postureBreak;
  if (role === "Bottom" || role === "Spectate") {
    scene3d.top.root.position.x = pb.x * 0.25;
    scene3d.top.root.position.z = -1.1 + pb.y * 0.3;
  }
  scene3d.top.root.rotation.x = -pb.y * 0.4;
  scene3d.top.root.rotation.z = pb.x * 0.3;
  scene3d.top.setBreakBucket(breakBucket(pb));

  const win = g.judgmentWindow;
  const tintStrength =
    win.state === "OPEN" ? 1 :
    win.state === "OPENING" ? 0.5 :
    win.state === "CLOSING" ? 0.3 :
    0;
  scene3d.setWindowTint(tintStrength);
  scene3d.setInitiative(g.control.initiative);

  // §5.4 — stamina color grading. Fatigue = 1 − stamina, clamped to a
  // visible range so a small drain isn't overly orange. Spectate reads
  // the worse of the two to highlight whichever side is struggling.
  const staminaView =
    role === "Top" ? g.top.stamina :
    role === "Spectate" ? Math.min(g.bottom.stamina, g.top.stamina) :
    g.bottom.stamina;
  // Start the grade at stamina ≤ 0.6 and ramp to full at ≤ 0.15 so
  // the mid-game sits neutral and the "呼吸が重い" zone is unmissable.
  const fatigue = 1 - Math.max(0, (staminaView - 0.15) / 0.45);
  scene3d.setStaminaFatigue(Math.max(0, Math.min(1, fatigue)));
}

function renderHud(f: InputFrame, intent: Intent, g: GameState, stepsThisRaf: number): string {
  const fmt = (n: number) => n.toFixed(2).padStart(5);
  const lines: string[] = [];
  lines.push(`role ${role}  device ${f.device_kind}  frame ${g.frameIndex}  steps/raf ${stepsThisRaf}  fixedMs ${FIXED_STEP_MS.toFixed(2)}`);
  lines.push(`round ${formatRoundTime(ROUND_LENGTH_MS - roundElapsedMs)}  (Esc to pause)`);
  if (activeScenario !== null) {
    lines.push(`scenario ${activeScenario}  (1–7 to switch · 0 to reset)`);
  }
  lines.push("── Layer A ──");
  lines.push(`ls (${fmt(f.ls.x)}, ${fmt(f.ls.y)})   rs (${fmt(f.rs.x)}, ${fmt(f.rs.y)})`);
  lines.push(`triggers L=${fmt(f.l_trigger)}  R=${fmt(f.r_trigger)}`);
  lines.push(`buttons ${formatButtons(f.buttons)}`);
  lines.push(`edges ${formatButtons(f.button_edges)}`);

  // Attacker intent panel: shown for Bottom (human) and Spectate (AI).
  if (role === "Bottom" || role === "Spectate") {
    const label = role === "Bottom" ? "attacker" : "attacker (AI)";
    lines.push(`── Layer B (${label}) ──`);
    lines.push(`hip θ=${fmt(intent.hip.hip_angle_target)}  push=${fmt(intent.hip.hip_push)}  lat=${fmt(intent.hip.hip_lateral)}`);
    lines.push(`grip L ${intent.grip.l_hand_target ?? "·"} (${fmt(intent.grip.l_grip_strength)})`);
    lines.push(`grip R ${intent.grip.r_hand_target ?? "·"} (${fmt(intent.grip.r_grip_strength)})`);
    lines.push(`discrete ${formatDiscrete(intent.discrete)}`);
  }
  // Defender intent panel: shown for Top (human) and Spectate (AI).
  if ((role === "Top" || role === "Spectate") && lastDefense !== null) {
    const label = role === "Top" ? "defender" : "defender (AI)";
    lines.push(`── Layer B (${label}) ──`);
    lines.push(`weight fwd=${fmt(lastDefense.hip.weight_forward)}  lat=${fmt(lastDefense.hip.weight_lateral)}`);
    lines.push(`base L ${lastDefense.base.l_hand_target ?? "·"} (${fmt(lastDefense.base.l_base_pressure)})`);
    lines.push(`base R ${lastDefense.base.r_hand_target ?? "·"} (${fmt(lastDefense.base.r_base_pressure)})`);
    lines.push(`discrete ${lastDefense.discrete.map((d) => d.kind).join(" ") || "·"}`);
  }

  lines.push("── Actor (bottom) ──");
  lines.push(`L-hand ${g.bottom.leftHand.state} → ${g.bottom.leftHand.target ?? "·"}`);
  lines.push(`R-hand ${g.bottom.rightHand.state} → ${g.bottom.rightHand.target ?? "·"}`);
  lines.push(`feet L=${g.bottom.leftFoot.state} R=${g.bottom.rightFoot.state}`);
  lines.push(`stamina ${fmt(g.bottom.stamina)}`);
  lines.push("── Actor (top) ──");
  lines.push(`postureBreak (${fmt(g.top.postureBreak.x)}, ${fmt(g.top.postureBreak.y)}) bucket=${breakBucket(g.top.postureBreak)}`);
  lines.push(`armExtracted L=${g.topArmExtracted.left} R=${g.topArmExtracted.right}`);
  lines.push("── Session ──");
  lines.push(`guard ${g.guard}`);
  lines.push(`initiative ${g.control.initiative}${g.control.lockedByWindow ? " (locked)" : ""}`);
  lines.push(`judgmentWindow ${g.judgmentWindow.state}  timeScale ${g.time.scale.toFixed(2)}`);
  lines.push(`candidates ${g.judgmentWindow.candidates.join(" ") || "·"}`);
  lines.push(`counterWindow  ${g.counterWindow.state}  sweepSign=${g.attackerSweepLateralSign}`);
  lines.push(`counter-cands  ${g.counterWindow.candidates.join(" ") || "·"}`);
  lines.push(`top.stamina    ${fmt(g.top.stamina)}`);
  lines.push(`passAttempt    ${g.passAttempt.kind}${g.passAttempt.kind === "IN_PROGRESS" ? ` (t=${(g.nowMs - g.passAttempt.startedMs).toFixed(0)}ms)` : ""}`);
  lines.push(`sessionEnded   ${g.sessionEnded}`);
  return lines.join("\n");
}

function formatButtons(bits: number): string {
  if (bits === 0) return "·";
  const names: string[] = [];
  for (const [name, bit] of Object.entries(ButtonBit)) {
    if ((bits & bit) !== 0) names.push(name);
  }
  return names.join(" ");
}

function formatDiscrete(list: ReadonlyArray<DiscreteIntent>): string {
  if (list.length === 0) return "·";
  return list
    .map((d) => (d.kind === "FOOT_HOOK_TOGGLE" ? `${d.kind}(${d.side})` : d.kind))
    .join(" ");
}
