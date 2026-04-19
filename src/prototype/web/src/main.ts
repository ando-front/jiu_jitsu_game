// PLATFORM — entry point. Wires Layer A → Layer B → GameState → Three.js + HUD.
// Still runs at rAF cadence; fixed-timestep loop is a later commit.

import { GamepadSource } from "./input/gamepad.js";
import { INITIAL_LAYER_B_STATE, transformLayerB, type LayerBState } from "./input/layerB.js";
import { KeyboardSource } from "./input/keyboard.js";
import { LayerA } from "./input/layerA.js";
import { ButtonBit, type InputFrame } from "./input/types.js";
import type { DiscreteIntent, Intent } from "./input/intent.js";
import { initialGameState, stepSimulation, type GameState } from "./state/game_state.js";
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
let game: GameState = initialGameState(performance.now());

function tick() {
  const now = performance.now();
  const frame = layerA.sample(now);
  const { intent, nextState } = transformLayerB(frame, bState);
  bState = nextState;

  const res = stepSimulation(game, frame, intent);
  game = res.nextState;

  // Scene smoke test — pelvis tracks Layer B hip intent as before. Later
  // commits will drive this from Layer C / animation-driver state instead.
  scene3d.blockman.rotation.y = intent.hip.hip_angle_target;
  scene3d.blockman.position.z = intent.hip.hip_push * 0.3;
  scene3d.blockman.position.x = intent.hip.hip_lateral * 0.2;

  hud.textContent = renderHud(frame, intent, game);
  scene3d.render();
  requestAnimationFrame(tick);
}
requestAnimationFrame(tick);

function renderHud(f: InputFrame, intent: Intent, g: GameState): string {
  const fmt = (n: number) => n.toFixed(2).padStart(5);
  const lines: string[] = [];
  lines.push(`device         ${f.device_kind}       frame ${g.frameIndex}`);
  lines.push("── Layer A ──");
  lines.push(`ls             (${fmt(f.ls.x)}, ${fmt(f.ls.y)})`);
  lines.push(`rs             (${fmt(f.rs.x)}, ${fmt(f.rs.y)})`);
  lines.push(`triggers       L=${fmt(f.l_trigger)}  R=${fmt(f.r_trigger)}`);
  lines.push(`buttons        ${formatButtons(f.buttons)}`);
  lines.push(`edges          ${formatButtons(f.button_edges)}`);
  lines.push("── Layer B ──");
  lines.push(`hip            θ=${fmt(intent.hip.hip_angle_target)}  push=${fmt(intent.hip.hip_push)}  lat=${fmt(intent.hip.hip_lateral)}`);
  lines.push(`grip L         ${intent.grip.l_hand_target ?? "·"} (${fmt(intent.grip.l_grip_strength)})`);
  lines.push(`grip R         ${intent.grip.r_hand_target ?? "·"} (${fmt(intent.grip.r_grip_strength)})`);
  lines.push(`discrete       ${formatDiscrete(intent.discrete)}`);
  lines.push("── GameState ──");
  lines.push(`guard          ${g.guard}`);
  lines.push(`bottom L-hand  ${g.bottom.leftHand.state}  → ${g.bottom.leftHand.target ?? "·"}`);
  lines.push(`bottom R-hand  ${g.bottom.rightHand.state}  → ${g.bottom.rightHand.target ?? "·"}`);
  lines.push(`bottom L-foot  ${g.bottom.leftFoot.state}`);
  lines.push(`bottom R-foot  ${g.bottom.rightFoot.state}`);
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
