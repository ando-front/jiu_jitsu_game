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
import { KeyboardSource } from "./input/keyboard.js";
import { LayerA } from "./input/layerA.js";
import { ButtonBit, type InputFrame } from "./input/types.js";
import type { DiscreteIntent, Intent } from "./input/intent.js";
import { ZERO_DEFENSE_INTENT, type DefenseIntent } from "./input/intent_defense.js";
import { initialGameState, type GameState } from "./state/game_state.js";
import { breakBucket } from "./state/posture_break.js";
import { advance, FIXED_STEP_MS, type FixedStepState } from "./sim/fixed_step.js";
import { createScene } from "./scene/blockman.js";

type Role = "Bottom" | "Top";

const canvas = document.getElementById("three-canvas") as HTMLCanvasElement;
const hud = document.getElementById("debug-hud") as HTMLPreElement;

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
  <div style="font-size: 13px; opacity: 0.65; max-width: 520px; line-height: 1.6;">
    You are <b>[BOTTOM]</b> — attacker holding closed guard.<br>
    Press <b>LS left/right</b> or <b>A/D</b> to switch to TOP (defender).<br>
    Press <b>[A] / Space</b> to start.
  </div>
`;
document.getElementById("app")?.appendChild(promptEl);
const choiceEl = promptEl.querySelector("#role-choice") as HTMLElement;

let role: Role = "Bottom";
let promptActive = true;

function updatePrompt() {
  choiceEl.textContent = role === "Bottom" ? "BOTTOM" : "TOP";
}

// -- Inputs -------------------------------------------------------------------

const keyboard = new KeyboardSource();
keyboard.attach(window);
const gamepad = new GamepadSource();
const layerA = new LayerA(gamepad, keyboard);

let bState: LayerBState = INITIAL_LAYER_B_STATE;
let bDefState: LayerBDefenseState = INITIAL_LAYER_B_DEFENSE_STATE;
let dState: LayerDState = INITIAL_LAYER_D_STATE;

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

function frame(now: number) {
  const realDt = now - lastRafMs;
  lastRafMs = now;

  if (promptActive) {
    runPromptTick();
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
        lastDefense = ZERO_DEFENSE_INTENT;
        return { frame: inputFrame, intent: b.intent, defense: ZERO_DEFENSE_INTENT };
      } else {
        const d = transformLayerBDefense(inputFrame, bDefState);
        bDefState = d.nextState;
        lastDefense = d.intent;
        // Top-side driver: attacker FSMs stay IDLE by feeding them a
        // neutral Intent. The defender intent flows into stepSimulation
        // via StepOptions.defenseIntent and drives recovery / cuts.
        lastIntent = NEUTRAL_INTENT;
        return { frame: inputFrame, intent: NEUTRAL_INTENT, defense: d.intent };
      }
    },
    resolveCommit: (f, intent, game, dtMs) => {
      // Commit resolution only fires while playing as attacker in Stage 1.
      // Defender-side counter commits belong to input_system_defense_v1 §D.
      if (role !== "Bottom") return null;
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
  });
  simState = res.next;

  const game = simState.game;
  applyToScene(game);
  if (lastFrame !== null && lastIntent !== null) {
    hud.textContent = renderHud(lastFrame, lastIntent, game, res.stepsRun);
  }
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

function runPromptTick() {
  const f = layerA.sample(performance.now());
  lastFrame = f;
  // Switch role by LS horizontal.
  if (f.ls.x < -0.6) role = "Bottom";
  else if (f.ls.x > 0.6) role = "Top";
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
  if (role === "Bottom" && lastIntent !== null) {
    scene3d.bottom.root.rotation.y = lastIntent.hip.hip_angle_target;
    scene3d.bottom.root.position.z = lastIntent.hip.hip_push * 0.3;
    scene3d.bottom.root.position.x = lastIntent.hip.hip_lateral * 0.2;
  } else if (role === "Top" && lastDefense !== null) {
    // As defender, the player controls the top rig's weight.
    scene3d.top.root.position.x = lastDefense.hip.weight_lateral * 0.25;
    scene3d.top.root.position.z = -1.1 + lastDefense.hip.weight_forward * 0.25;
  }

  const pb = g.top.postureBreak;
  if (role === "Bottom") {
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
}

function renderHud(f: InputFrame, intent: Intent, g: GameState, stepsThisRaf: number): string {
  const fmt = (n: number) => n.toFixed(2).padStart(5);
  const lines: string[] = [];
  lines.push(`role ${role}  device ${f.device_kind}  frame ${g.frameIndex}  steps/raf ${stepsThisRaf}  fixedMs ${FIXED_STEP_MS.toFixed(2)}`);
  lines.push("── Layer A ──");
  lines.push(`ls (${fmt(f.ls.x)}, ${fmt(f.ls.y)})   rs (${fmt(f.rs.x)}, ${fmt(f.rs.y)})`);
  lines.push(`triggers L=${fmt(f.l_trigger)}  R=${fmt(f.r_trigger)}`);
  lines.push(`buttons ${formatButtons(f.buttons)}`);
  lines.push(`edges ${formatButtons(f.button_edges)}`);

  if (role === "Bottom") {
    lines.push("── Layer B (attacker) ──");
    lines.push(`hip θ=${fmt(intent.hip.hip_angle_target)}  push=${fmt(intent.hip.hip_push)}  lat=${fmt(intent.hip.hip_lateral)}`);
    lines.push(`grip L ${intent.grip.l_hand_target ?? "·"} (${fmt(intent.grip.l_grip_strength)})`);
    lines.push(`grip R ${intent.grip.r_hand_target ?? "·"} (${fmt(intent.grip.r_grip_strength)})`);
    lines.push(`discrete ${formatDiscrete(intent.discrete)}`);
  } else if (lastDefense !== null) {
    lines.push("── Layer B (defender) ──");
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
