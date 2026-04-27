// Scenarios should arrive with technique preconditions already met so
// that one input frame (or zero, for purely posture-based techniques)
// is enough to fire the corresponding judgment window. If a scenario
// *doesn't* satisfy its intended technique, the prototype misleads the
// developer into thinking the fire logic is broken when in fact the
// scenario is.

import { describe, expect, it } from "vitest";
import { evaluateAllTechniques, type Technique } from "../../src/state/judgment_window.js";
import { buildScenario, SCENARIO_ORDER, type ScenarioName } from "../../src/state/scenarios.js";

// The trigger strength the player is expected to be holding while
// playing through the scenario. Mirrors "both triggers pulled" default.
const MAX_TRIGGER = 1.0;

function evalTechniques(name: ScenarioName): ReadonlyArray<Technique> {
  const g = buildScenario(name, 1000);
  // Scenario-specific JudgmentContext knobs that come from *live input*
  // (hip yaw, hip push) rather than GameState. Mirrors the inputs a
  // player would be holding while the scenario is loaded.
  const hipYaw =
    name === "OMOPLATA_READY" ? -Math.PI / 2 : // match OMOPLATA lateral -sign
    name === "CROSS_COLLAR_READY" ? 0 :
    Math.PI / 2;
  const hipPush = name === "FLOWER_READY" || name === "HIP_BUMP_READY" ? 0.6 : 0;
  return evaluateAllTechniques(
    {
      bottom: g.bottom,
      top: g.top,
      bottomHipYaw: hipYaw,
      bottomHipPush: hipPush,
      sustainedHipPushMs: g.sustained.hipPushMs,
    },
    MAX_TRIGGER,
    MAX_TRIGGER,
  );
}

describe("Scenarios — preconditions satisfied", () => {
  it("SCISSOR_READY satisfies SCISSOR_SWEEP", () => {
    expect(evalTechniques("SCISSOR_READY")).toContain("SCISSOR_SWEEP");
  });

  it("FLOWER_READY satisfies FLOWER_SWEEP", () => {
    expect(evalTechniques("FLOWER_READY")).toContain("FLOWER_SWEEP");
  });

  it("TRIANGLE_READY satisfies TRIANGLE", () => {
    expect(evalTechniques("TRIANGLE_READY")).toContain("TRIANGLE");
  });

  it("OMOPLATA_READY satisfies OMOPLATA", () => {
    expect(evalTechniques("OMOPLATA_READY")).toContain("OMOPLATA");
  });

  it("HIP_BUMP_READY satisfies HIP_BUMP", () => {
    expect(evalTechniques("HIP_BUMP_READY")).toContain("HIP_BUMP");
  });

  it("CROSS_COLLAR_READY satisfies CROSS_COLLAR", () => {
    expect(evalTechniques("CROSS_COLLAR_READY")).toContain("CROSS_COLLAR");
  });

  // PASS_DEFENSE is a defender drill and is not expected to fire an
  // attacker technique; assert the negative so a future refactor doesn't
  // accidentally seed attacker preconditions.
  it("PASS_DEFENSE does NOT satisfy any attacker technique", () => {
    expect(evalTechniques("PASS_DEFENSE")).toHaveLength(0);
  });
});

describe("Scenarios — structural invariants", () => {
  it.each(SCENARIO_ORDER)("%s returns a frozen GameState with frameIndex=0", (name) => {
    const g = buildScenario(name, 0);
    expect(Object.isFrozen(g)).toBe(true);
    expect(g.frameIndex).toBe(0);
    expect(g.sessionEnded).toBe(false);
    // Guard must start CLOSED (scenarios are for in-guard play).
    expect(g.guard).toBe("CLOSED");
  });

  it("TRIANGLE_READY seeds topArmExtracted.right=true so the flag doesn't immediately flip back", () => {
    const g = buildScenario("TRIANGLE_READY", 1000);
    expect(g.topArmExtracted.right).toBe(true);
    expect(g.top.armExtractedRight).toBe(true);
  });
});
