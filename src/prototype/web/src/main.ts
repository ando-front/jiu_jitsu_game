// PLATFORM — entry point. rAF-driven fixed-timestep loop:
// Layer A → Layer B → Layer D → stepSimulation → Three.js + HUD.

import { GamepadSource } from "./input/gamepad.js";
import { INITIAL_LAYER_B_STATE, transformLayerB, type LayerBState } from "./input/layerB.js";
import { INITIAL_LAYER_D_STATE, resolveLayerD, type LayerDState } from "./input/layerD.js";
import { KeyboardSource } from "./input/keyboard.js";
import { LayerA } from "./input/layerA.js";
import { ButtonBit, type InputFrame } from "./input/types.js";
import type { DiscreteIntent, Intent } from "./input/intent.js";
import { initialGameState, type GameState } from "./state/game_state.js";
import { breakBucket } from "./state/posture_break.js";
import { advance, FIXED_STEP_MS, type FixedStepState } from "./sim/fixed_step.js";
import { createScene } from "./scene/blockman.js";

const canvas = document.getElementById("three-canvas") as HTMLCanvasElement;
const hud = document.getElementById("debug-hud") as HTMLPreElement;

const scene3d = createScene(canvas);
scene3d.resize(window.innerWidth, window.innerHeight);
window.addEventListener("resize", () =>
  scene3d.resize(window.innerWidth, window.innerHeight),
);

const keyboard = new KeyboardSource();
keyboard.attach(window);
const gamepad = new GamepadSource();
const layerA = new LayerA(gamepad, keyboard);

let bState: LayerBState = INITIAL_LAYER_B_STATE;
let dState: LayerDState = INITIAL_LAYER_D_STATE;
let simState: FixedStepState = Object.freeze({
  accumulatorMs: 0,
  simClockMs: performance.now(),
  game: initialGameState(performance.now()),
});
let lastRafMs = performance.now();

let lastIntent: Intent | null = null;
let lastFrame: InputFrame | null = null;

function frame(now: number) {
  const realDt = now - lastRafMs;
  lastRafMs = now;

  const res = advance(simState, realDt, {
    sample: (stepNowMs: number) => {
      const inputFrame = layerA.sample(stepNowMs);
      const b = transformLayerB(inputFrame, bState);
      bState = b.nextState;
      lastFrame = inputFrame;
      lastIntent = b.intent;
      return { frame: inputFrame, intent: b.intent };
    },
    resolveCommit: (f, intent, game, dtMs) => {
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
  if (lastIntent !== null) {
    scene3d.blockman.rotation.y = lastIntent.hip.hip_angle_target;
    scene3d.blockman.position.z = lastIntent.hip.hip_push * 0.3;
    scene3d.blockman.position.x = lastIntent.hip.hip_lateral * 0.2;
  }
  if (lastFrame !== null && lastIntent !== null) {
    hud.textContent = renderHud(lastFrame, lastIntent, game, res.stepsRun);
  }

  scene3d.render();
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

function renderHud(f: InputFrame, intent: Intent, g: GameState, stepsThisRaf: number): string {
  const fmt = (n: number) => n.toFixed(2).padStart(5);
  const lines: string[] = [];
  lines.push(`device ${f.device_kind}  frame ${g.frameIndex}  steps/raf ${stepsThisRaf}  fixedMs ${FIXED_STEP_MS.toFixed(2)}`);
  lines.push("── Layer A ──");
  lines.push(`ls (${fmt(f.ls.x)}, ${fmt(f.ls.y)})   rs (${fmt(f.rs.x)}, ${fmt(f.rs.y)})`);
  lines.push(`triggers L=${fmt(f.l_trigger)}  R=${fmt(f.r_trigger)}`);
  lines.push(`buttons ${formatButtons(f.buttons)}`);
  lines.push(`edges ${formatButtons(f.button_edges)}`);
  lines.push("── Layer B ──");
  lines.push(`hip θ=${fmt(intent.hip.hip_angle_target)}  push=${fmt(intent.hip.hip_push)}  lat=${fmt(intent.hip.hip_lateral)}`);
  lines.push(`grip L ${intent.grip.l_hand_target ?? "·"} (${fmt(intent.grip.l_grip_strength)})`);
  lines.push(`grip R ${intent.grip.r_hand_target ?? "·"} (${fmt(intent.grip.r_grip_strength)})`);
  lines.push(`discrete ${formatDiscrete(intent.discrete)}`);
  lines.push("── Actor (bottom) ──");
  lines.push(`L-hand ${g.bottom.leftHand.state} → ${g.bottom.leftHand.target ?? "·"}`);
  lines.push(`R-hand ${g.bottom.rightHand.state} → ${g.bottom.rightHand.target ?? "·"}`);
  lines.push(`feet L=${g.bottom.leftFoot.state} R=${g.bottom.rightFoot.state}`);
  lines.push(`stamina ${fmt(g.bottom.stamina)}`);
  lines.push("── Actor (top) ──");
  lines.push(`postureBreak (${fmt(g.top.postureBreak.x)}, ${fmt(g.top.postureBreak.y)}) bucket=${breakBucket(g.top.postureBreak)}`);
  lines.push(`armExtracted L=${g.topArmExtracted.left} R=${g.topArmExtracted.right}`);
  lines.push(`sustain L=${g.topArmExtracted.leftSustainMs.toFixed(0)}ms R=${g.topArmExtracted.rightSustainMs.toFixed(0)}ms`);
  lines.push("── Session ──");
  lines.push(`guard ${g.guard}`);
  lines.push(`initiative ${g.control.initiative}${g.control.lockedByWindow ? " (locked)" : ""}`);
  lines.push(`sustained hipPushMs ${g.sustained.hipPushMs.toFixed(0)}`);
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
