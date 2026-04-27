// Keyboard source — see docs/design/input_system_v1.md §A.2.2.
// Digital only. All analogue magnitudes clamp to 1.0 as documented.

import { ButtonBit } from "./types.js";

// Physical keys we listen to. Everything else is ignored.
const TRACKED_KEYS = new Set([
  "KeyW", "KeyA", "KeyS", "KeyD",
  "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
  "KeyF", "KeyJ",
  "KeyR", "KeyU",
  "Space", "KeyX", "KeyC", "KeyV",
  "Escape",
]);

export type KeyboardSnapshot = Readonly<{
  ls_up: boolean;
  ls_down: boolean;
  ls_left: boolean;
  ls_right: boolean;
  rs_up: boolean;
  rs_down: boolean;
  rs_left: boolean;
  rs_right: boolean;
  l_trigger: boolean;
  r_trigger: boolean;
  buttons: number;
}>;

export class KeyboardSource {
  private readonly pressed = new Set<string>();
  // Wall-clock (performance.now) of the most recent tracked-key event.
  // LayerA reads this to break ties against noisy gamepads that report
  // ghost axis activity while the human is actually typing.
  private lastEventMs: number = Number.NEGATIVE_INFINITY;

  attach(target: Window = window): () => void {
    const down = (e: KeyboardEvent) => {
      if (!TRACKED_KEYS.has(e.code)) return;
      this.pressed.add(e.code);
      this.lastEventMs = performance.now();
      e.preventDefault();
    };
    const up = (e: KeyboardEvent) => {
      if (!TRACKED_KEYS.has(e.code)) return;
      this.pressed.delete(e.code);
      this.lastEventMs = performance.now();
      e.preventDefault();
    };
    target.addEventListener("keydown", down);
    target.addEventListener("keyup", up);
    return () => {
      target.removeEventListener("keydown", down);
      target.removeEventListener("keyup", up);
    };
  }

  // Explicit test hook — unit tests drive state without real DOM events.
  setKeyForTest(code: string, down: boolean): void {
    if (down) this.pressed.add(code);
    else this.pressed.delete(code);
    this.lastEventMs = performance.now();
  }

  // Most-recent tracked-key event timestamp (any key in TRACKED_KEYS,
  // down OR up). Returns -Infinity if no key has ever been touched.
  lastEventTimestampMs(): number {
    return this.lastEventMs;
  }

  // Any tracked key currently held — used by LayerA for tie-breaking.
  anyKeyHeld(): boolean {
    return this.pressed.size > 0;
  }

  snapshot(): KeyboardSnapshot {
    const is = (c: string) => this.pressed.has(c);
    let buttons = 0;
    if (is("KeyR")) buttons |= ButtonBit.L_BUMPER;
    if (is("KeyU")) buttons |= ButtonBit.R_BUMPER;
    if (is("Space")) buttons |= ButtonBit.BTN_BASE;
    if (is("KeyX")) buttons |= ButtonBit.BTN_RELEASE;
    if (is("KeyC")) buttons |= ButtonBit.BTN_BREATH;
    if (is("KeyV")) buttons |= ButtonBit.BTN_RESERVED;
    if (is("Escape")) buttons |= ButtonBit.BTN_PAUSE;

    return {
      ls_up: is("KeyW"),
      ls_down: is("KeyS"),
      ls_left: is("KeyA"),
      ls_right: is("KeyD"),
      rs_up: is("ArrowUp"),
      rs_down: is("ArrowDown"),
      rs_left: is("ArrowLeft"),
      rs_right: is("ArrowRight"),
      l_trigger: is("KeyF"),
      r_trigger: is("KeyJ"),
      buttons,
    };
  }
}
