// PURE — IBJJF-style position score for the M1 prototype loop.
//
// Two players, two running point totals. The guard passer (top) scores 3 for
// completing a pass into side control; the guard player (bottom) scores 2 for
// a sweep. Pure value transforms so the porting story to Stage 2
// (BJJScore.cs) is a 1:1 mirror — no mutation, no framework idioms.

export interface BJJScore {
  top: number;
  bottom: number;
}

export const INITIAL_SCORE: BJJScore = Object.freeze({ top: 0, bottom: 0 });

// IBJJF point values.
export const PASS_POINTS = 3;
export const SWEEP_POINTS = 2;

export function applyPass(score: BJJScore): BJJScore {
  return { ...score, top: score.top + PASS_POINTS };
}

export function applySweep(score: BJJScore): BJJScore {
  return { ...score, bottom: score.bottom + SWEEP_POINTS };
}
