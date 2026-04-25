// PURE — coaching/checklist evaluator.
//
// Given a technique target and a GameState snapshot, produce a list of
// human-readable conditions with met/unmet status. The Stage 1 HUD uses
// this to show "次に技を出すために何が足りないか" so a first-time player
// can see the cause-and-effect between input and the firing predicates
// in [judgment_window.ts].
//
// Conditions here MUST mirror the predicates in judgment_window.ts. If a
// firing predicate changes there, update both. We keep this in its own
// module (not inside judgment_window.ts) because it's a presentation
// concern — Stage 2 will re-implement the checklist UI in Unity but
// reuse the same predicates.

import type { GameState } from "./game_state.js";
import { breakMagnitude } from "./posture_break.js";
import type { Technique } from "./judgment_window.js";

export type ChecklistItem = Readonly<{
  label: string;
  met: boolean;
  // Optional concrete number, e.g. "0.32 / 0.40" for a magnitude target.
  detail?: string;
}>;

export type Checklist = Readonly<{
  technique: Technique;
  title: string;       // 日本語表示名
  items: ReadonlyArray<ChecklistItem>;
  allMet: boolean;
}>;

export const TECHNIQUE_TITLE_JA: Readonly<Record<Technique, string>> = Object.freeze({
  SCISSOR_SWEEP: "ハサミスイープ",
  FLOWER_SWEEP: "フラワースイープ",
  TRIANGLE: "三角絞め",
  OMOPLATA: "オモプラッタ",
  HIP_BUMP: "ヒップバンプ",
  CROSS_COLLAR: "十字絞め",
});

function fmt(n: number, digits = 2): string {
  return n.toFixed(digits);
}

function gripStrengthOf(g: GameState, side: "L" | "R"): number {
  // Layer-D resolves the live trigger value into HandFSM grip strength.
  // We don't have it on GameState directly; the cleanest reuse is the
  // FSM's recorded strength when GRIPPED. Stage 1 doesn't store live
  // trigger value on GameState, so coaching uses a binary "is GRIPPED"
  // proxy + tells the player to hold the trigger in the description.
  // For numeric items that depend on trigger strength, we read whichever
  // is larger (best-case) so the bar moves as the player presses.
  const h = side === "L" ? g.bottom.leftHand : g.bottom.rightHand;
  // Approximate live strength as 0.7 if GRIPPED (above the 0.6/0.7 thresholds
  // SCISSOR/CROSS_COLLAR check). The player should still see a hint to
  // press the trigger; a precise numeric requires plumbing the input
  // frame into GameState which is outside this task's scope.
  return h.state === "GRIPPED" ? 0.7 : 0;
}

export function buildChecklist(g: GameState, target: Technique): Checklist {
  switch (target) {
    case "SCISSOR_SWEEP":
      return scissorChecklist(g);
    case "FLOWER_SWEEP":
      return flowerChecklist(g);
    case "TRIANGLE":
      return triangleChecklist(g);
    case "OMOPLATA":
      return omoplataChecklist(g);
    case "HIP_BUMP":
      return hipBumpChecklist(g);
    case "CROSS_COLLAR":
      return crossCollarChecklist(g);
  }
}

function asChecklist(
  technique: Technique,
  items: ReadonlyArray<ChecklistItem>,
): Checklist {
  return Object.freeze({
    technique,
    title: TECHNIQUE_TITLE_JA[technique],
    items: Object.freeze(items.map((i) => Object.freeze(i))),
    allMet: items.every((i) => i.met),
  });
}

function scissorChecklist(g: GameState): Checklist {
  const b = g.bottom;
  const lockedBoth = b.leftFoot.state === "LOCKED" && b.rightFoot.state === "LOCKED";
  const sleeveGrip =
    (b.leftHand.state === "GRIPPED" && (b.leftHand.target === "SLEEVE_L" || b.leftHand.target === "SLEEVE_R")) ||
    (b.rightHand.state === "GRIPPED" && (b.rightHand.target === "SLEEVE_L" || b.rightHand.target === "SLEEVE_R"));
  const m = breakMagnitude(g.top.postureBreak);
  return asChecklist("SCISSOR_SWEEP", [
    { label: "両足 LOCKED (R / U で足フック)", met: lockedBoth },
    { label: "袖を掴む (R-Stick ← or → → トリガー)", met: sleeveGrip },
    { label: `崩し ‖break‖ ≥ 0.40`, met: m >= 0.4, detail: `${fmt(m)} / 0.40` },
  ]);
}

function flowerChecklist(g: GameState): Checklist {
  const b = g.bottom;
  const lockedBoth = b.leftFoot.state === "LOCKED" && b.rightFoot.state === "LOCKED";
  const wristGrip =
    (b.leftHand.state === "GRIPPED" && (b.leftHand.target === "WRIST_L" || b.leftHand.target === "WRIST_R")) ||
    (b.rightHand.state === "GRIPPED" && (b.rightHand.target === "WRIST_L" || b.rightHand.target === "WRIST_R"));
  const sag = g.top.postureBreak.y;
  return asChecklist("FLOWER_SWEEP", [
    { label: "両足 LOCKED", met: lockedBoth },
    { label: "手首を掴む (R-Stick ↓ → トリガー)", met: wristGrip },
    { label: `前崩し sagittal ≥ 0.50 (W で押す)`, met: sag >= 0.5, detail: `${fmt(sag)} / 0.50` },
  ]);
}

function triangleChecklist(g: GameState): Checklist {
  const b = g.bottom;
  const oneUnlocked = b.leftFoot.state === "UNLOCKED" || b.rightFoot.state === "UNLOCKED";
  const collarGrip =
    (b.leftHand.state === "GRIPPED" && (b.leftHand.target === "COLLAR_L" || b.leftHand.target === "COLLAR_R")) ||
    (b.rightHand.state === "GRIPPED" && (b.rightHand.target === "COLLAR_L" || b.rightHand.target === "COLLAR_R"));
  const armOut = g.top.armExtractedLeft || g.top.armExtractedRight;
  return asChecklist("TRIANGLE", [
    { label: "片足 UNLOCKED (R か U で外す)", met: oneUnlocked },
    { label: "襟を掴む (R-Stick ↑ → トリガー)", met: collarGrip },
    { label: "腕引き出し成立 (袖/手首を1.5秒引き続ける)", met: armOut },
  ]);
}

function omoplataChecklist(g: GameState): Checklist {
  const b = g.bottom;
  const sleeveSide: "L" | "R" | null =
    b.leftHand.state === "GRIPPED" && (b.leftHand.target === "SLEEVE_L" || b.leftHand.target === "SLEEVE_R")
      ? "L"
      : b.rightHand.state === "GRIPPED" && (b.rightHand.target === "SLEEVE_L" || b.rightHand.target === "SLEEVE_R")
      ? "R"
      : null;
  const sag = g.top.postureBreak.y;
  const lat = g.top.postureBreak.x;
  // §8.2 sign 一致: L→lateral<0, R→lateral>0
  const expectedSign = sleeveSide === "L" ? -1 : sleeveSide === "R" ? 1 : 0;
  const signMatch = sleeveSide !== null && Math.sign(lat) === expectedSign && Math.abs(lat) > 0.05;
  // hip_yaw target — coach uses the actor's last-applied hip yaw via the
  // intent isn't on GameState; we only have postureBreak. So the yaw
  // condition is presented as an instruction without a live %.
  return asChecklist("OMOPLATA", [
    { label: "袖を片手で掴む", met: sleeveSide !== null },
    { label: `前崩し ≥ 0.60 (W で押す)`, met: sag >= 0.6, detail: `${fmt(sag)} / 0.60` },
    {
      label: sleeveSide === "L"
        ? "横崩し: 左方向 (A で振る)"
        : sleeveSide === "R"
        ? "横崩し: 右方向 (D で振る)"
        : "横崩しを袖側へ",
      met: signMatch,
      detail: `lateral=${fmt(lat)}`,
    },
    { label: "腰回転 |hip_yaw| ≥ π/3 (L-Stickを横へ大きく)", met: false, detail: "判断窓発動条件" },
  ]);
}

function hipBumpChecklist(g: GameState): Checklist {
  const sag = g.top.postureBreak.y;
  const sus = g.sustained.hipPushMs;
  return asChecklist("HIP_BUMP", [
    { label: `前崩し sagittal ≥ 0.70`, met: sag >= 0.7, detail: `${fmt(sag)} / 0.70` },
    { label: `腰押し 0.5+ を 300ms 維持 (W ホールド)`, met: sus >= 300, detail: `${sus.toFixed(0)} / 300 ms` },
  ]);
}

function crossCollarChecklist(g: GameState): Checklist {
  const b = g.bottom;
  const lCollar = b.leftHand.state === "GRIPPED" && (b.leftHand.target === "COLLAR_L" || b.leftHand.target === "COLLAR_R");
  const rCollar = b.rightHand.state === "GRIPPED" && (b.rightHand.target === "COLLAR_L" || b.rightHand.target === "COLLAR_R");
  const m = breakMagnitude(g.top.postureBreak);
  // CROSS_COLLAR needs both triggers ≥ 0.7. We can't see live triggers
  // here, so we surface an explicit "両トリガー最大" instruction.
  const lStrong = gripStrengthOf(g, "L") >= 0.7;
  const rStrong = gripStrengthOf(g, "R") >= 0.7;
  return asChecklist("CROSS_COLLAR", [
    { label: "左手 COLLAR を掴む (R-Stick ↑ + F)", met: lCollar },
    { label: "右手 COLLAR を掴む (R-Stick ↑ + J)", met: rCollar },
    { label: "両トリガー強度 ≥ 0.70 (F + J を最大まで)", met: lStrong && rStrong },
    { label: `崩し ‖break‖ ≥ 0.50`, met: m >= 0.5, detail: `${fmt(m)} / 0.50` },
  ]);
}

// Default coaching target table per scenario, so the checklist auto-tunes
// when the player loads a scenario.
import type { ScenarioName } from "./scenarios.js";
export const SCENARIO_DEFAULT_TARGET: Readonly<Record<ScenarioName, Technique>> = Object.freeze({
  SCISSOR_READY: "SCISSOR_SWEEP",
  FLOWER_READY: "FLOWER_SWEEP",
  TRIANGLE_READY: "TRIANGLE",
  OMOPLATA_READY: "OMOPLATA",
  HIP_BUMP_READY: "HIP_BUMP",
  CROSS_COLLAR_READY: "CROSS_COLLAR",
  PASS_DEFENSE: "SCISSOR_SWEEP", // PASS_DEFENSE is TOP-side; default to a BOTTOM target for the live HUD.
});
