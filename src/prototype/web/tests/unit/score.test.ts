// Unit tests for the IBJJF position score (src/state/score.ts).

import { describe, expect, it } from "vitest";
import {
  INITIAL_SCORE,
  PASS_POINTS,
  SWEEP_POINTS,
  applyPass,
  applySweep,
} from "../../src/state/score.js";

describe("BJJScore", () => {
  it("starts at 0–0", () => {
    expect(INITIAL_SCORE).toEqual({ top: 0, bottom: 0 });
  });

  it("a pass scores 3 for top, leaving bottom untouched", () => {
    expect(applyPass(INITIAL_SCORE)).toEqual({ top: PASS_POINTS, bottom: 0 });
    expect(applyPass({ top: 3, bottom: 2 })).toEqual({ top: 6, bottom: 2 });
  });

  it("a sweep scores 2 for bottom, leaving top untouched", () => {
    expect(applySweep(INITIAL_SCORE)).toEqual({ top: 0, bottom: SWEEP_POINTS });
    expect(applySweep({ top: 3, bottom: 2 })).toEqual({ top: 3, bottom: 4 });
  });

  it("is pure — the input score is not mutated", () => {
    const s = { top: 1, bottom: 1 };
    applyPass(s);
    applySweep(s);
    expect(s).toEqual({ top: 1, bottom: 1 });
  });
});
