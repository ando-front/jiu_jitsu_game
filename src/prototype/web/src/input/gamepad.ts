// Gamepad source using the browser Gamepad API.
// Maps Xbox / DualSense standard mapping onto our logical layout (§A.2.1).

import { ButtonBit, type DeviceKind } from "./types.js";

// Standard Gamepad API button indices (W3C "standard" mapping).
// https://www.w3.org/TR/gamepad/#remapping
const GP_BTN = {
  A: 0,
  B: 1,
  X: 2,
  Y: 3,
  LB: 4,
  RB: 5,
  LT: 6,
  RT: 7,
  BACK: 8,
  START: 9,
} as const;

export type GamepadSnapshot = Readonly<{
  connected: boolean;
  device_kind: DeviceKind;
  ls_x: number;
  ls_y: number;
  rs_x: number;
  rs_y: number;
  l_trigger: number;
  r_trigger: number;
  buttons: number;
}>;

function detectDeviceKind(pad: Gamepad): DeviceKind {
  const id = pad.id.toLowerCase();
  if (id.includes("054c") || id.includes("dualsense") || id.includes("playstation")) {
    return "DualSense";
  }
  return "Xbox";
}

export class GamepadSource {
  /** Which navigator.getGamepads() slot we read from. Null = auto-pick first connected. */
  preferredIndex: number | null = null;

  snapshot(): GamepadSnapshot {
    const pads = typeof navigator !== "undefined" ? navigator.getGamepads?.() : null;
    const pad = this.pickPad(pads);
    if (!pad) {
      return {
        connected: false,
        device_kind: "Xbox",
        ls_x: 0, ls_y: 0, rs_x: 0, rs_y: 0,
        l_trigger: 0, r_trigger: 0,
        buttons: 0,
      };
    }

    // Invert Y so "up on stick" is positive — matches §B.1 sign convention
    // (ls.y > 0 means hip pushes forward away from camera / opponent).
    const ls_x = pad.axes[0] ?? 0;
    const ls_y = -(pad.axes[1] ?? 0);
    const rs_x = pad.axes[2] ?? 0;
    const rs_y = -(pad.axes[3] ?? 0);

    const lt = pad.buttons[GP_BTN.LT]?.value ?? 0;
    const rt = pad.buttons[GP_BTN.RT]?.value ?? 0;

    let buttons = 0;
    const held = (i: number) => pad.buttons[i]?.pressed ?? false;
    if (held(GP_BTN.LB)) buttons |= ButtonBit.L_BUMPER;
    if (held(GP_BTN.RB)) buttons |= ButtonBit.R_BUMPER;
    if (held(GP_BTN.A)) buttons |= ButtonBit.BTN_BASE;
    if (held(GP_BTN.B)) buttons |= ButtonBit.BTN_RELEASE;
    if (held(GP_BTN.Y)) buttons |= ButtonBit.BTN_BREATH;
    if (held(GP_BTN.X)) buttons |= ButtonBit.BTN_RESERVED;
    if (held(GP_BTN.START)) buttons |= ButtonBit.BTN_PAUSE;

    return {
      connected: true,
      device_kind: detectDeviceKind(pad),
      ls_x, ls_y, rs_x, rs_y,
      l_trigger: lt, r_trigger: rt,
      buttons,
    };
  }

  private pickPad(pads: (Gamepad | null)[] | null | undefined): Gamepad | null {
    if (!pads) return null;
    if (this.preferredIndex !== null) {
      return pads[this.preferredIndex] ?? null;
    }
    for (const p of pads) {
      if (p && p.connected) return p;
    }
    return null;
  }
}
