// PLATFORM — entry point. Wires Layer A → Layer B → Three.js scene + debug HUD.
// Still runs at rAF cadence; fixed-timestep loop comes in a later commit.

import { GamepadSource } from "./input/gamepad.js";
import { INITIAL_LAYER_B_STATE, transformLayerB, type LayerBState } from "./input/layerB.js";
import { KeyboardSource } from "./input/keyboard.js";
import { LayerA } from "./input/layerA.js";
import { ButtonBit, type InputFrame } from "./input/types.js";
import type { DiscreteIntent, Intent } from "./input/intent.js";
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

function tick() {
  const frame = layerA.sample(performance.now());
  const { intent, nextState } = transformLayerB(frame, bState);
  bState = nextState;

  // Smoke-test wiring: pelvis yaw follows the computed hip intent, body
  // sway follows hip_push. Still no real Layer C — we're only checking
  // that Layer B produces plausible numbers end-to-end.
  scene3d.blockman.rotation.y = intent.hip.hip_angle_target;
  scene3d.blockman.position.z = intent.hip.hip_push * 0.3;
  scene3d.blockman.position.x = intent.hip.hip_lateral * 0.2;

  hud.textContent = renderHud(frame, intent);
  scene3d.render();
  requestAnimationFrame(tick);
}
requestAnimationFrame(tick);

function renderHud(f: InputFrame, intent: Intent): string {
  const fmt = (n: number) => n.toFixed(2).padStart(5);
  const lines: string[] = [];
  lines.push(`device         ${f.device_kind}`);
  lines.push("── Layer A ──");
  lines.push(`ls             (${fmt(f.ls.x)}, ${fmt(f.ls.y)})`);
  lines.push(`rs             (${fmt(f.rs.x)}, ${fmt(f.rs.y)})`);
  lines.push(`l_trigger      ${fmt(f.l_trigger)}`);
  lines.push(`r_trigger      ${fmt(f.r_trigger)}`);
  lines.push(`buttons        ${formatButtons(f.buttons)}`);
  lines.push(`edges          ${formatButtons(f.button_edges)}`);
  lines.push("── Layer B ──");
  lines.push(`hip_angle      ${fmt(intent.hip.hip_angle_target)} rad`);
  lines.push(`hip_push       ${fmt(intent.hip.hip_push)}`);
  lines.push(`hip_lateral    ${fmt(intent.hip.hip_lateral)}`);
  lines.push(
    `grip L         ${intent.grip.l_hand_target ?? "·"} (${fmt(intent.grip.l_grip_strength)})`,
  );
  lines.push(
    `grip R         ${intent.grip.r_hand_target ?? "·"} (${fmt(intent.grip.r_grip_strength)})`,
  );
  lines.push(`discrete       ${formatDiscrete(intent.discrete)}`);
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
