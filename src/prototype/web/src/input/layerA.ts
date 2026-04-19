// Layer A assembler — combines keyboard + gamepad snapshots into an InputFrame.
// Reference: docs/design/input_system_v1.md §A.4.
//
// The active-device rule (§A.1 last bullet): whichever source produced the
// most recent activity wins for UI/prompt purposes. Gamepad wins ties since
// Stage 2 playtests require a pad (§A.2.2 note).

import { applyStickDeadzoneAndCurve, applyTriggerDeadzone, computeButtonEdges, eightWayFromDigital } from "./transform.js";
import type { GamepadSource, GamepadSnapshot } from "./gamepad.js";
import type { KeyboardSource, KeyboardSnapshot } from "./keyboard.js";
import type { DeviceKind, InputFrame, Vec2 } from "./types.js";

export class LayerA {
  private prevButtons = 0;

  constructor(
    private readonly gamepad: GamepadSource,
    private readonly keyboard: KeyboardSource,
  ) {}

  /** Build one InputFrame. `nowMs` is injected for test determinism. */
  sample(nowMs: number): InputFrame {
    const pad = this.gamepad.snapshot();
    const kb = this.keyboard.snapshot();

    const useGamepad = pad.connected && this.gamepadHasActivity(pad);
    const device_kind: DeviceKind = useGamepad ? pad.device_kind : "Keyboard";

    const ls = useGamepad
      ? applyStickDeadzoneAndCurve({ x: pad.ls_x, y: pad.ls_y })
      : this.keyboardLs(kb);
    const rs = useGamepad
      ? applyStickDeadzoneAndCurve({ x: pad.rs_x, y: pad.rs_y })
      : this.keyboardRs(kb);

    const l_trigger = useGamepad
      ? applyTriggerDeadzone(pad.l_trigger)
      : (kb.l_trigger ? 1 : 0);
    const r_trigger = useGamepad
      ? applyTriggerDeadzone(pad.r_trigger)
      : (kb.r_trigger ? 1 : 0);

    const buttons = useGamepad ? pad.buttons : kb.buttons;
    const button_edges = computeButtonEdges(this.prevButtons, buttons);
    this.prevButtons = buttons;

    return Object.freeze<InputFrame>({
      timestamp: nowMs,
      ls,
      rs,
      l_trigger,
      r_trigger,
      buttons,
      button_edges,
      device_kind,
    });
  }

  private gamepadHasActivity(pad: GamepadSnapshot): boolean {
    const anyStick = Math.hypot(pad.ls_x, pad.ls_y) > 0.2 || Math.hypot(pad.rs_x, pad.rs_y) > 0.2;
    const anyTrigger = pad.l_trigger > 0.1 || pad.r_trigger > 0.1;
    const anyButton = pad.buttons !== 0;
    return anyStick || anyTrigger || anyButton;
  }

  private keyboardLs(kb: KeyboardSnapshot): Vec2 {
    return eightWayFromDigital(kb.ls_up, kb.ls_down, kb.ls_left, kb.ls_right);
  }

  private keyboardRs(kb: KeyboardSnapshot): Vec2 {
    return eightWayFromDigital(kb.rs_up, kb.rs_down, kb.rs_left, kb.rs_right);
  }
}
