// PURE — ControlLayer per docs/design/state_machines_v1.md §7.
//
// Scope: affects presentation only (camera, post-process, audio). Never
// changes FSM transitions. Locked to the side that fires a judgment
// window until that window fully closes.

import type { ActorState } from "./game_state.js";
import type { JudgmentWindow } from "./judgment_window.js";

export type Initiative = "Bottom" | "Top" | "Neutral";

export type ControlLayer = Readonly<{
  initiative: Initiative;
  // While a judgment window is any state other than CLOSED the initiative
  // is locked to whichever side fired it. Expressed as an absolute
  // timestamp is tempting but the window doesn't publish its close time
  // in advance, so we use a flag instead and re-evaluate each tick.
  lockedByWindow: boolean;
}>;

export const INITIAL_CONTROL_LAYER: ControlLayer = Object.freeze({
  initiative: "Neutral" as const,
  lockedByWindow: false,
});

export type ControlLayerInputs = Readonly<{
  judgmentWindow: JudgmentWindow;
  bottom: ActorState;
  top: ActorState;
  // True while the top actor is mid-cut-attempt. Stage 1 passes false;
  // the defender cut implementation will fill this in.
  defenderCutInProgress: boolean;
}>;

export function updateControlLayer(
  prev: ControlLayer,
  inp: ControlLayerInputs,
): ControlLayer {
  const win = inp.judgmentWindow;
  const windowActive = win.state !== "CLOSED";

  // §7.2 highest priority: judgment window lock.
  if (windowActive && win.firedBy !== null) {
    return Object.freeze({
      initiative: win.firedBy,
      lockedByWindow: true,
    });
  }

  // Window transitioned from open to closed this tick — drop the lock and
  // re-evaluate from the current world.
  // (We don't need to special-case the transition; if windowActive is
  // false we just fall through to the normal rules below.)

  // §7.2 rule 2: any arm_extracted → that side owns initiative.
  if (inp.bottom.armExtractedLeft || inp.bottom.armExtractedRight) {
    return Object.freeze({ initiative: "Bottom", lockedByWindow: false });
  }
  if (inp.top.armExtractedLeft || inp.top.armExtractedRight) {
    return Object.freeze({ initiative: "Top", lockedByWindow: false });
  }

  // §7.2 rule 3: attacker has ≥2 active grips → Bottom.
  if (countActiveGrips(inp.bottom) >= 2) {
    return Object.freeze({ initiative: "Bottom", lockedByWindow: false });
  }

  // §7.2 rule 4: defender cut in progress → Top.
  if (inp.defenderCutInProgress) {
    return Object.freeze({ initiative: "Top", lockedByWindow: false });
  }

  // §7.2 rule 5: Neutral.
  if (prev.initiative === "Neutral" && !prev.lockedByWindow) return prev;
  return Object.freeze({ initiative: "Neutral", lockedByWindow: false });
}

function countActiveGrips(actor: ActorState): number {
  let n = 0;
  if (actor.leftHand.state === "GRIPPED") n += 1;
  if (actor.rightHand.state === "GRIPPED") n += 1;
  return n;
}
