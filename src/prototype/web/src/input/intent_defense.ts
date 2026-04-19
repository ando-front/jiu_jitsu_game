// PURE — defender-side Intent types. Mirrors docs/design/input_system_defense_v1.md §B.

export type BaseZone =
  | "CHEST"
  | "HIP"
  | "KNEE_L"
  | "KNEE_R"
  | "BICEP_L"
  | "BICEP_R";

export const BASE_ZONE_DIRECTIONS: ReadonlyArray<
  Readonly<{ zone: BaseZone; dir: Readonly<{ x: number; y: number }> }>
> = Object.freeze([
  { zone: "CHEST",    dir: Object.freeze({ x: 0, y: 1 }) },
  { zone: "HIP",      dir: Object.freeze({ x: 0, y: -1 }) },
  { zone: "KNEE_L",   dir: Object.freeze({ x: -Math.SQRT1_2, y: -Math.SQRT1_2 }) },
  { zone: "KNEE_R",   dir: Object.freeze({ x:  Math.SQRT1_2, y: -Math.SQRT1_2 }) },
  { zone: "BICEP_L",  dir: Object.freeze({ x: -Math.SQRT1_2, y:  Math.SQRT1_2 }) },
  { zone: "BICEP_R",  dir: Object.freeze({ x:  Math.SQRT1_2, y:  Math.SQRT1_2 }) },
]);

export type TopHipIntent = Readonly<{
  weight_forward: number;  // [-1, 1]
  weight_lateral: number;  // [-1, 1]
}>;

export type TopBaseIntent = Readonly<{
  l_hand_target: BaseZone | null;
  l_base_pressure: number;
  r_hand_target: BaseZone | null;
  r_base_pressure: number;
}>;

// Cut attempts and pass commits are time-sensitive — see §4.2 / §B.7.
// They carry their RS-derived targets/directions at the moment of the edge.
export type DefenseDiscreteIntent =
  | { kind: "CUT_ATTEMPT"; side: "L" | "R"; rs: Readonly<{ x: number; y: number }> }
  | { kind: "RECOVERY_HOLD" }   // §B.6.1 — BTN_BASE hold (posture recovery)
  | { kind: "BASE_RELEASE_ALL" } // §B.6 BTN_RELEASE
  | { kind: "BREATH_START" }
  | { kind: "PASS_COMMIT"; rs: Readonly<{ x: number; y: number }> } // §B.7
  | { kind: "PAUSE" };

export type DefenseIntent = Readonly<{
  hip: TopHipIntent;
  base: TopBaseIntent;
  discrete: ReadonlyArray<DefenseDiscreteIntent>;
}>;

export const ZERO_TOP_HIP: TopHipIntent = Object.freeze({
  weight_forward: 0,
  weight_lateral: 0,
});

export const ZERO_TOP_BASE: TopBaseIntent = Object.freeze({
  l_hand_target: null,
  l_base_pressure: 0,
  r_hand_target: null,
  r_base_pressure: 0,
});

export const ZERO_DEFENSE_INTENT: DefenseIntent = Object.freeze({
  hip: ZERO_TOP_HIP,
  base: ZERO_TOP_BASE,
  discrete: Object.freeze([]),
});
