// PURE — opponent AI intent generator per docs/design/opponent_ai_v1.md.
// No DOM / no rAF; same GameState + same role always produces the same Intent.
//
// Stage 1 scope: rule-based priority table. The AI observes GameState
// directly and returns one of Intent (if playing as Bottom) or
// DefenseIntent (if playing as Top). No Layer A input emulation.

import { ButtonBit } from "../input/types.js";
import type { Intent } from "../input/intent.js";
import type { DefenseIntent } from "../input/intent_defense.js";
import {
  ZERO_DEFENSE_INTENT,
} from "../input/intent_defense.js";
import type { GameState } from "../state/game_state.js";
import type { Technique } from "../state/judgment_window.js";
import type { CounterTechnique } from "../state/counter_window.js";

// Empty attacker intent placeholder.
const NEUTRAL_INTENT: Intent = Object.freeze({
  hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
  grip: { l_hand_target: null, l_grip_strength: 0, r_hand_target: null, r_grip_strength: 0 },
  discrete: [],
});

export type AIOutput =
  | { role: "Bottom"; intent: Intent }
  | { role: "Top"; defense: DefenseIntent };

// Main entry.
export function opponentIntent(game: GameState, role: "Bottom" | "Top"): AIOutput {
  if (role === "Top") return { role: "Top", defense: topDecide(game) };
  return { role: "Bottom", intent: bottomDecide(game) };
}

// -- TOP side (defender) -----------------------------------------------------

function topDecide(g: GameState): DefenseIntent {
  // Priority 1: counter window is OPEN → commit the first candidate.
  if (g.counterWindow.state === "OPEN" && g.counterWindow.candidates.length > 0) {
    return counterCommitIntent(g.counterWindow.candidates[0]!, g.attackerSweepLateralSign);
  }
  // Priority 2: attacker TRIANGLE is lit in judgment window → feed stack now
  // so the pattern accumulates before commit window.
  if (
    g.judgmentWindow.state === "OPEN" &&
    g.judgmentWindow.candidates.includes("TRIANGLE")
  ) {
    return counterCommitIntent("TRIANGLE_EARLY_STACK", 0);
  }
  // Priority 3: recover from sagittal break.
  if (g.top.postureBreak.y >= 0.5) {
    return Object.freeze({
      hip: { weight_forward: 1, weight_lateral: 0 },
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [{ kind: "RECOVERY_HOLD" }],
    });
  }
  // Priority 4: attacker has a GRIPPED hand and defender cut slot is free.
  const cutIdleLeft = g.cutAttempts.left.kind === "IDLE";
  const cutIdleRight = g.cutAttempts.right.kind === "IDLE";
  const targetHand = pickAttackerGripped(g);
  if (targetHand !== null && (cutIdleLeft || cutIdleRight)) {
    const defenderSide = cutIdleLeft ? "L" : "R";
    return Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [{ kind: "CUT_ATTEMPT", side: defenderSide, rs: targetHand.rs }],
    });
  }
  // Priority 5: arm_extracted → apply bicep base on that side.
  if (g.top.armExtractedLeft || g.top.armExtractedRight) {
    const side = g.top.armExtractedLeft ? "BICEP_L" : "BICEP_R";
    return Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: {
        l_hand_target: side,
        l_base_pressure: 0.8,
        r_hand_target: null,
        r_grip_strength: 0, // ignored by type; TopBaseIntent uses r_base_pressure
        r_base_pressure: 0,
      } as DefenseIntent["base"],
      discrete: [],
    });
  }
  // Priority 6: pass-preparation base setup.
  const bottomGripsCount =
    (g.bottom.leftHand.state === "GRIPPED" ? 1 : 0) +
    (g.bottom.rightHand.state === "GRIPPED" ? 1 : 0);
  const bothFeetLocked =
    g.bottom.leftFoot.state === "LOCKED" && g.bottom.rightFoot.state === "LOCKED";
  if (bothFeetLocked && bottomGripsCount < 2 && g.top.stamina >= 0.5) {
    return Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: {
        l_hand_target: "BICEP_L",
        l_base_pressure: 0.7,
        r_hand_target: "KNEE_R",
        r_base_pressure: 0.7,
      },
      discrete: [],
    });
  }
  // Priority 7: breathe below threshold.
  if (g.top.stamina < 0.3) {
    return Object.freeze({
      hip: { weight_forward: 0, weight_lateral: 0 },
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [{ kind: "BREATH_START" }],
    });
  }
  // Priority 8: idle.
  return ZERO_DEFENSE_INTENT;
}

function counterCommitIntent(
  counter: CounterTechnique,
  attackerSweepSign: number,
): DefenseIntent {
  if (counter === "SCISSOR_COUNTER") {
    // LS opposite to sweep direction at full magnitude.
    const sign = attackerSweepSign >= 0 ? -1 : 1;
    // Represent as a hip intent on the defender side: LS.x lives in the
    // InputFrame, not the intent. Stage 1 workaround: we also emit the
    // hip weight_lateral as the AI's direct proxy so that downstream
    // consumers that read RS get the sign right. The actual Layer D
    // resolution runs off the raw InputFrame; since the AI bypasses the
    // input layer, we cannot (easily) fire SCISSOR_COUNTER via Layer D
    // from pure DefenseIntent. Tests therefore exercise the AI's
    // *decision* but commit-resolution testing stays the responsibility
    // of Layer D_defense's unit tests.
    return Object.freeze({
      hip: { weight_forward: 0, weight_lateral: sign },
      base: ZERO_DEFENSE_INTENT.base,
      discrete: [],
    });
  }
  // TRIANGLE_EARLY_STACK: LS up + BTN_BASE hold. Same workaround applies.
  return Object.freeze({
    hip: { weight_forward: 1, weight_lateral: 0 },
    base: ZERO_DEFENSE_INTENT.base,
    discrete: [{ kind: "RECOVERY_HOLD" }],
  });
}

function pickAttackerGripped(g: GameState): { rs: { x: number; y: number } } | null {
  const l = g.bottom.leftHand;
  if (l.state === "GRIPPED" && l.target !== null) {
    return { rs: zoneDirection(l.target) };
  }
  const r = g.bottom.rightHand;
  if (r.state === "GRIPPED" && r.target !== null) {
    return { rs: zoneDirection(r.target) };
  }
  return null;
}

// Mirror of GRIP_ZONE_DIRECTIONS for the defender's RS aim. The defender
// sees the attacker's zones from the opposite perspective; for Stage 1
// we approximate by reusing the same vectors — the cut-attempt target
// picker only cares about the attacker-hand side anyway.
function zoneDirection(zone: string): { x: number; y: number } {
  switch (zone) {
    case "SLEEVE_L": return { x: -0.7, y: -0.7 };
    case "SLEEVE_R": return { x:  0.7, y: -0.7 };
    case "COLLAR_L": return { x: -0.7, y:  0.7 };
    case "COLLAR_R": return { x:  0.7, y:  0.7 };
    case "WRIST_L":  return { x: -1,   y:  0 };
    case "WRIST_R":  return { x:  1,   y:  0 };
    case "BELT":     return { x:  0,   y: -1 };
    case "POSTURE_BREAK": return { x: 0, y: 1 };
    default: return { x: 0, y: 0 };
  }
}

// -- BOTTOM side (attacker) --------------------------------------------------

function bottomDecide(g: GameState): Intent {
  // Priority 1: judgment window OPEN → commit first candidate.
  if (g.judgmentWindow.state === "OPEN" && g.judgmentWindow.candidates.length > 0) {
    return techniqueCommitIntent(g.judgmentWindow.candidates[0]!);
  }
  const lState = g.bottom.leftHand.state;
  const rState = g.bottom.rightHand.state;
  const bottomGripsCount =
    (lState === "GRIPPED" ? 1 : 0) + (rState === "GRIPPED" ? 1 : 0);

  // Priority 2: no grips yet and stamina OK → reach for SLEEVE_R with L.
  if (bottomGripsCount === 0 && g.bottom.stamina >= 0.5 && lState === "IDLE") {
    return Object.freeze({
      hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
      grip: {
        l_hand_target: "SLEEVE_R",
        l_grip_strength: 0.8,
        r_hand_target: null,
        r_grip_strength: 0,
      },
      discrete: [],
    });
  }

  // Priority 3: one hand GRIPPED on a non-COLLAR → reach COLLAR with other.
  // Pick the mirrored collar side: attacker's L gripping SLEEVE_R → free
  // right hand reaches COLLAR_L (cross-face). Mirror for R.
  if (bottomGripsCount === 1) {
    const useLeft = lState !== "GRIPPED";
    const collarZone = useLeft ? "COLLAR_L" : "COLLAR_R" as const;
    return Object.freeze({
      hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
      grip: useLeft
        ? {
            l_hand_target: collarZone,
            l_grip_strength: 0.8,
            r_hand_target: g.bottom.rightHand.target,
            r_grip_strength: g.bottom.rightHand.state === "GRIPPED" ? 0.8 : 0,
          }
        : {
            l_hand_target: g.bottom.leftHand.target,
            l_grip_strength: g.bottom.leftHand.state === "GRIPPED" ? 0.8 : 0,
            r_hand_target: collarZone,
            r_grip_strength: 0.8,
          },
      discrete: [],
    });
  }

  // Priority 4: both GRIPPED and break < 0.4 → push hip forward to build break.
  const breakMag = Math.hypot(g.top.postureBreak.x, g.top.postureBreak.y);
  if (bottomGripsCount === 2 && breakMag < 0.4) {
    return Object.freeze({
      hip: { hip_angle_target: 0, hip_push: 0.6, hip_lateral: 0 },
      grip: {
        l_hand_target: g.bottom.leftHand.target,
        l_grip_strength: 0.8,
        r_hand_target: g.bottom.rightHand.target,
        r_grip_strength: 0.8,
      },
      discrete: [],
    });
  }

  // Priority 5: low stamina → breathe.
  if (g.bottom.stamina < 0.3) {
    return Object.freeze({
      hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
      grip: {
        l_hand_target: null, l_grip_strength: 0,
        r_hand_target: null, r_grip_strength: 0,
      },
      discrete: [{ kind: "BREATH_START" }],
    });
  }

  // Priority 6: idle — hold current grips.
  return Object.freeze({
    hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
    grip: {
      l_hand_target: g.bottom.leftHand.target,
      l_grip_strength: g.bottom.leftHand.state === "GRIPPED" ? 0.8 : 0,
      r_hand_target: g.bottom.rightHand.target,
      r_grip_strength: g.bottom.rightHand.state === "GRIPPED" ? 0.8 : 0,
    },
    discrete: [],
  });
}

function techniqueCommitIntent(technique: Technique): Intent {
  // Match the patterns Layer D will resolve. Emitted as both hip and
  // grip fields + a button hint; Layer D ultimately reads the raw
  // InputFrame, so the AI's best-effort is to leave an Intent that
  // downstream layers interpret reasonably.
  switch (technique) {
    case "SCISSOR_SWEEP":
      return Object.freeze({
        hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 1 },
        grip: NEUTRAL_INTENT.grip,
        discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "L" }],
      });
    case "FLOWER_SWEEP":
      return Object.freeze({
        hip: { hip_angle_target: 0, hip_push: 1, hip_lateral: 0 },
        grip: NEUTRAL_INTENT.grip,
        discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "R" }],
      });
    case "TRIANGLE":
      return Object.freeze({
        hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
        grip: NEUTRAL_INTENT.grip,
        discrete: [{ kind: "BASE_HOLD" }],
      });
    case "OMOPLATA":
      return Object.freeze({
        hip: { hip_angle_target: 0, hip_push: 0, hip_lateral: 0 },
        grip: NEUTRAL_INTENT.grip,
        discrete: [{ kind: "FOOT_HOOK_TOGGLE", side: "L" }],
      });
    default:
      return NEUTRAL_INTENT;
  }
}

// A reference to ButtonBit keeps the import live for future uses.
void ButtonBit;
