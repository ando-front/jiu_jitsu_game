// PURE — practice scenarios.
//
// Each builder returns a GameState whose FSMs / continuous values are
// seeded so that the judgment-window firing condition for one specific
// technique is either already met or within a single input away. This
// lets the developer (or a playtester) skip the 15–20 second grip-build
// phase and iterate directly on the interesting logic.
//
// Rationale: during Stage 1 logic iteration we frequently want to answer
// "does my change break SCISSOR_SWEEP firing?". Reaching that state from
// the neutral start requires correctly ordering grip reaches, posture
// pushes and foot locks. Scenarios short-circuit the ramp-up so a bug in
// the fire condition surfaces in one frame instead of five attempts.
//
// Design choices:
//   - Each scenario is a pure function: (nowMs) → GameState. Deterministic.
//   - Scenarios do NOT call stepSimulation — they poke the freshly-built
//     initialGameState and mutate exactly what's needed. This keeps each
//     scenario small and auditable.
//   - Scenarios never seed the attacker's own judgment-window state;
//     they only seed the *preconditions*, so the window opens naturally
//     during the next stepSimulation tick. That way the OPENING event
//     still fires and is observable in the event log.

import { initialGameState, type ActorState, type GameState, type Vec2 } from "./game_state.js";
import type { HandFSM } from "./hand_fsm.js";
import type { FootFSM } from "./foot_fsm.js";
import type { GripZone } from "../input/intent.js";

export type ScenarioName =
  | "SCISSOR_READY"
  | "FLOWER_READY"
  | "TRIANGLE_READY"
  | "CROSS_COLLAR_READY"
  | "PASS_DEFENSE";

export const SCENARIO_ORDER: readonly ScenarioName[] = [
  "SCISSOR_READY",
  "FLOWER_READY",
  "TRIANGLE_READY",
  "CROSS_COLLAR_READY",
  "PASS_DEFENSE",
] as const;

export const SCENARIO_DESCRIPTIONS: Readonly<Record<ScenarioName, string>> = Object.freeze({
  SCISSOR_READY:
    "両足 LOCKED + L-hand GRIPPED(SLEEVE_R)+ 横崩し 0.5。" +
    "腰を横に振るだけで SCISSOR_SWEEP 判断窓が開く。",
  FLOWER_READY:
    "両足 LOCKED + R-hand GRIPPED(WRIST_L)+ 前崩し 0.6。" +
    "次フレームで FLOWER_SWEEP 条件成立。",
  TRIANGLE_READY:
    "L-foot UNLOCKED + L-hand GRIPPED(COLLAR_R)+ top.armExtractedRight=true。" +
    "TRIANGLE 条件成立。",
  CROSS_COLLAR_READY:
    "両 COLLAR GRIPPED 強度0.7+ + 崩し 0.5+。十字絞め判断窓候補に乗る。",
  PASS_DEFENSE:
    "TOP視点の練習。BOTTOMが片足UNLOCKEDでパスが決まる一歩手前。",
});

// Construct a GRIPPED hand at a given zone. stateEnteredMs/reachDurationMs
// are left at sane defaults so follow-up HandFSM ticks don't force a retransit.
function grippedHand(
  side: "L" | "R",
  zone: GripZone,
  nowMs: number,
): HandFSM {
  return Object.freeze({
    side,
    state: "GRIPPED" as const,
    target: zone,
    stateEnteredMs: nowMs,
    reachDurationMs: 250,
    lastParriedZone: null,
    lastParriedAtMs: Number.NEGATIVE_INFINITY,
  });
}

function foot(
  side: "L" | "R",
  state: FootFSM["state"],
  nowMs: number,
): FootFSM {
  return Object.freeze({ side, state, stateEnteredMs: nowMs });
}

function postureBreak(x: number, y: number): Vec2 {
  return Object.freeze({ x, y });
}

// -- Public API --------------------------------------------------------------

export function buildScenario(name: ScenarioName, nowMs: number): GameState {
  const base = initialGameState(nowMs);
  switch (name) {
    case "SCISSOR_READY":
      return scissorReady(base, nowMs);
    case "FLOWER_READY":
      return flowerReady(base, nowMs);
    case "TRIANGLE_READY":
      return triangleReady(base, nowMs);
    case "CROSS_COLLAR_READY":
      return crossCollarReady(base, nowMs);
    case "PASS_DEFENSE":
      return passDefense(base, nowMs);
  }
}

// -- Individual scenarios ----------------------------------------------------

function scissorReady(g: GameState, nowMs: number): GameState {
  const bottom: ActorState = Object.freeze({
    ...g.bottom,
    leftHand: grippedHand("L", "SLEEVE_R", nowMs),
    leftFoot: foot("L", "LOCKED", nowMs),
    rightFoot: foot("R", "LOCKED", nowMs),
  });
  const top: ActorState = Object.freeze({
    ...g.top,
    // §8.2 SCISSOR condition needs ‖break‖ ≥ 0.4; we seed 0.5 lateral.
    postureBreak: postureBreak(0.5, 0.1),
  });
  return Object.freeze({ ...g, bottom, top });
}

function flowerReady(g: GameState, nowMs: number): GameState {
  const bottom: ActorState = Object.freeze({
    ...g.bottom,
    rightHand: grippedHand("R", "WRIST_L", nowMs),
    leftFoot: foot("L", "LOCKED", nowMs),
    rightFoot: foot("R", "LOCKED", nowMs),
  });
  const top: ActorState = Object.freeze({
    ...g.top,
    // §8.2 FLOWER needs sagittal ≥ 0.5; we seed 0.6.
    postureBreak: postureBreak(0.0, 0.6),
  });
  return Object.freeze({ ...g, bottom, top });
}

function triangleReady(g: GameState, nowMs: number): GameState {
  const bottom: ActorState = Object.freeze({
    ...g.bottom,
    leftHand: grippedHand("L", "COLLAR_R", nowMs),
    leftFoot: foot("L", "UNLOCKED", nowMs),
    rightFoot: foot("R", "LOCKED", nowMs),
  });
  const top: ActorState = Object.freeze({
    ...g.top,
    // §8.2 TRIANGLE needs arm_extracted on one side + COLLAR GRIPPED.
    armExtractedRight: true,
    postureBreak: postureBreak(0.0, 0.3),
  });
  // Corresponding arm-extracted sustained counter so the flag doesn't
  // instantly fall back to false on the next tick.
  const topArmExtracted = Object.freeze({
    ...g.topArmExtracted,
    right: true,
    rightSustainMs: 2000,
    rightSetAtMs: nowMs,
  });
  return Object.freeze({ ...g, bottom, top, topArmExtracted });
}

function crossCollarReady(g: GameState, nowMs: number): GameState {
  const bottom: ActorState = Object.freeze({
    ...g.bottom,
    leftHand: grippedHand("L", "COLLAR_R", nowMs),
    rightHand: grippedHand("R", "COLLAR_L", nowMs),
    leftFoot: foot("L", "LOCKED", nowMs),
    rightFoot: foot("R", "LOCKED", nowMs),
  });
  const top: ActorState = Object.freeze({
    ...g.top,
    // §8.2 CROSS_COLLAR needs ‖break‖ ≥ 0.5 + both COLLAR at strength ≥ 0.7.
    // Strength comes from trigger input each frame — this scenario is
    // a "hold both triggers at max" test.
    postureBreak: postureBreak(0.1, 0.5),
  });
  return Object.freeze({ ...g, bottom, top });
}

function passDefense(g: GameState, nowMs: number): GameState {
  const bottom: ActorState = Object.freeze({
    ...g.bottom,
    // One foot UNLOCKED = guard weakened; TOP wants to pass now.
    leftFoot: foot("L", "UNLOCKED", nowMs),
    rightFoot: foot("R", "LOCKED", nowMs),
    stamina: 0.45,
  });
  const top: ActorState = Object.freeze({
    ...g.top,
    stamina: 0.8,
    postureBreak: postureBreak(0.0, 0.1),
  });
  return Object.freeze({ ...g, bottom, top });
}
