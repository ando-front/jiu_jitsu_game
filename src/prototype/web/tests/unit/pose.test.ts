// Tests for the pure pose-synthesis layer (scene/pose.ts). The rendering
// itself is platform code, but the FSM-state → joint-target mapping is pure
// and these invariants are what make the motion read correctly: reaches
// extend, grips pull, locks squeeze, posture breaks crumple, fatigue sags.

import { describe, expect, it } from "vitest";
import {
  BOTTOM_PLACEMENT,
  TOP_PLACEMENT,
  balancePost,
  computeBodyFrames,
  computeBottomPose,
  computeFinishPoses,
  computeScenePoses,
  computeTopPose,
  gripZoneAnchor,
  legDirections,
  solveLeg,
  type BottomPoseInput,
  type LegPose,
  type TopPoseInput,
} from "../../src/scene/pose.js";

const IDLE_HAND = { state: "IDLE", target: null } as const;

function bottomInput(overrides: Partial<BottomPoseInput> = {}): BottomPoseInput {
  return {
    nowMs: 0,
    stamina: 1,
    guard: "CLOSED",
    leftHand: IDLE_HAND,
    rightHand: IDLE_HAND,
    leftFootState: "LOCKED",
    rightFootState: "LOCKED",
    hipAngle: 0,
    hipPush: 0,
    hipLateral: 0,
    gripStrengthL: 0,
    gripStrengthR: 0,
    windowOpen: false,
    ...overrides,
  };
}

function topInput(overrides: Partial<TopPoseInput> = {}): TopPoseInput {
  return {
    nowMs: 0,
    stamina: 1,
    leftHand: IDLE_HAND,
    rightHand: IDLE_HAND,
    postureBreakX: 0,
    postureBreakY: 0,
    weightForward: 0,
    weightLateral: 0,
    armExtractedL: false,
    armExtractedR: false,
    passElapsedMs: null,
    cutElapsedLMs: null,
    cutElapsedRMs: null,
    counterWindowOpen: false,
    ...overrides,
  };
}

describe("computeBottomPose — arms", () => {
  it("REACHING extends the elbow versus IDLE", () => {
    const idle = computeBottomPose(bottomInput());
    const reaching = computeBottomPose(
      bottomInput({ leftHand: { state: "REACHING", target: "COLLAR_L" } }),
    );
    expect(reaching.armL.elbowBend).toBeLessThan(idle.armL.elbowBend);
  });

  it("GRIPPED pulls (more elbow bend than CONTACT) and trembles with grip strength", () => {
    const contact = computeBottomPose(
      bottomInput({ leftHand: { state: "CONTACT", target: "SLEEVE_L" } }),
    );
    const gripped = computeBottomPose(
      bottomInput({ leftHand: { state: "GRIPPED", target: "SLEEVE_L" }, gripStrengthL: 1 }),
    );
    expect(gripped.armL.elbowBend).toBeGreaterThan(contact.armL.elbowBend);
    expect(gripped.armL.tremor).toBeGreaterThan(contact.armL.tremor);
  });

  it("PARRIED flings the arm wide (more shoulder roll than any engaged state)", () => {
    const gripped = computeBottomPose(
      bottomInput({ leftHand: { state: "GRIPPED", target: "COLLAR_L" } }),
    );
    const parried = computeBottomPose(
      bottomInput({ leftHand: { state: "PARRIED", target: "COLLAR_L" } }),
    );
    expect(parried.armL.shoulderRoll).toBeGreaterThan(gripped.armL.shoulderRoll);
  });

  it("collar reach raises the arm higher (more negative pitch) than a belt reach", () => {
    const collar = computeBottomPose(
      bottomInput({ rightHand: { state: "REACHING", target: "COLLAR_R" } }),
    );
    const belt = computeBottomPose(
      bottomInput({ rightHand: { state: "REACHING", target: "BELT" } }),
    );
    expect(collar.armR.shoulderPitch).toBeLessThan(belt.armR.shoulderPitch);
  });

  it("attacking the collar curls the torso up (sit-up crunch)", () => {
    const idle = computeBottomPose(bottomInput());
    const attacking = computeBottomPose(
      bottomInput({ leftHand: { state: "GRIPPED", target: "COLLAR_L" } }),
    );
    expect(attacking.torsoPitch).toBeGreaterThan(idle.torsoPitch);
  });
});

// Body-frame ankle position for the (right-leg-authored) solver output:
// hip joint + thigh + shin, using the rig's segment lengths.
function anklePos(leg: LegPose): readonly [number, number, number] {
  const { thigh, shin } = legDirections(leg);
  const hip = [0.11, -0.05, 0] as const;
  return [
    hip[0] + thigh[0] * 0.38 + shin[0] * 0.36,
    hip[1] + thigh[1] * 0.38 + shin[1] * 0.36,
    hip[2] + thigh[2] * 0.38 + shin[2] * 0.36,
  ];
}

describe("solveLeg / legDirections roundtrip", () => {
  it("reconstructs the authored thigh and shin directions", () => {
    const d1 = [0.31, -0.76, 0.56] as const;
    const d2 = [-0.84, -0.53, -0.16] as const;
    const { thigh, shin } = legDirections(solveLeg(d1, d2));
    const n = (v: readonly number[]) => {
      const len = Math.hypot(v[0]!, v[1]!, v[2]!);
      return [v[0]! / len, v[1]! / len, v[2]! / len];
    };
    const [e1, e2] = [n(d1), n(d2)];
    expect(thigh[0]).toBeCloseTo(e1[0]!, 5);
    expect(thigh[1]).toBeCloseTo(e1[1]!, 5);
    expect(thigh[2]).toBeCloseTo(e1[2]!, 5);
    expect(shin[0]).toBeCloseTo(e2[0]!, 5);
    expect(shin[1]).toBeCloseTo(e2[1]!, 5);
    expect(shin[2]).toBeCloseTo(e2[2]!, 5);
  });
});

describe("computeBottomPose — legs and hips", () => {
  it("LOCKED wraps: thigh runs up toward the opponent, ankle crosses the midline", () => {
    const locked = computeBottomPose(bottomInput());
    const { thigh, shin } = legDirections(locked.legR);
    expect(thigh[1]).toBeLessThan(0); // toward the opponent (−y body frame)
    expect(thigh[2]).toBeGreaterThan(0); // lifted off the mat
    expect(shin[0]).toBeLessThan(0); // shin sweeps across the centre line
    expect(anklePos(locked.legR)[0]).toBeLessThan(0); // ankle past midline → crossable
  });

  it("UNLOCKED frames instead of wrapping: ankle stays on its own side", () => {
    const open = computeBottomPose(
      bottomInput({ leftFootState: "UNLOCKED", rightFootState: "UNLOCKED" }),
    );
    const lockedAnkle = anklePos(computeBottomPose(bottomInput()).legR);
    const openAnkle = anklePos(open.legR);
    expect(openAnkle[0]).toBeGreaterThan(0);
    expect(openAnkle[0]).toBeGreaterThan(lockedAnkle[0]);
  });

  it("LOCKING wobbles over time (effort animation)", () => {
    const a = computeBottomPose(bottomInput({ leftFootState: "LOCKING", nowMs: 0 }));
    const b = computeBottomPose(bottomInput({ leftFootState: "LOCKING", nowMs: 150 }));
    expect(a.legL).not.toEqual(b.legL);
  });

  it("hip intent moves the pelvis (push → z, lateral → x + roll)", () => {
    const pose = computeBottomPose(bottomInput({ hipPush: 1, hipLateral: -1 }));
    expect(pose.pelvisZ).toBeGreaterThan(0);
    expect(pose.pelvisX).toBeLessThan(0);
    expect(pose.pelvisRoll).toBeLessThan(0);
  });
});

describe("computeBottomPose — vitality", () => {
  it("fatigue drops the head back toward the mat", () => {
    const fresh = computeBottomPose(bottomInput({ stamina: 1 }));
    const spent = computeBottomPose(bottomInput({ stamina: 0 }));
    expect(spent.headPitch).toBeLessThan(fresh.headPitch);
  });

  it("breath oscillates over time and speeds up when spent", () => {
    const t0 = computeBottomPose(bottomInput({ nowMs: 0 }));
    const t1 = computeBottomPose(bottomInput({ nowMs: 400 }));
    expect(t0.breath).not.toBeCloseTo(t1.breath, 5);
    // One full fresh-stamina breath cycle takes ~3.6 s; a spent body must
    // complete more phase in the same window.
    const freshQuarter = computeBottomPose(bottomInput({ nowMs: 500, stamina: 1 })).breath;
    const spentQuarter = computeBottomPose(bottomInput({ nowMs: 500, stamina: 0 })).breath;
    expect(Math.abs(spentQuarter - freshQuarter)).toBeGreaterThan(1e-3);
  });

  it("is deterministic for identical inputs", () => {
    const a = computeBottomPose(bottomInput({ nowMs: 1234 }));
    const b = computeBottomPose(bottomInput({ nowMs: 1234 }));
    expect(a).toEqual(b);
  });
});

describe("computeTopPose", () => {
  it("posture break forward crumples the torso forward", () => {
    const upright = computeTopPose(topInput());
    const broken = computeTopPose(topInput({ postureBreakY: 0.8 }));
    expect(broken.torsoPitch).toBeGreaterThan(upright.torsoPitch);
    expect(broken.pelvisZ).toBeGreaterThan(upright.pelvisZ);
  });

  it("lateral posture break rolls the torso and shifts the pelvis", () => {
    const broken = computeTopPose(topInput({ postureBreakX: -0.7 }));
    expect(broken.torsoRoll).toBeLessThan(0);
    expect(broken.pelvisX).toBeLessThan(0);
  });

  it("deep posture break adds strain tremor; a stable base has none", () => {
    const stable = computeTopPose(topInput({ postureBreakY: 0.2 }));
    const deep = computeTopPose(topInput({ postureBreakY: 0.9 }));
    expect(stable.torsoTremor).toBe(0);
    expect(deep.torsoTremor).toBeGreaterThan(0);
  });

  it("an extracted arm is dragged across the centre line", () => {
    const normal = computeTopPose(topInput());
    const extracted = computeTopPose(topInput({ armExtractedL: true }));
    expect(extracted.armL.shoulderYaw).toBeLessThan(normal.armL.shoulderYaw);
    expect(extracted.armR).toEqual(normal.armR);
  });

  it("defender posting toward CHEST raises the arm versus HIP", () => {
    const chest = computeTopPose(topInput({ leftHand: { state: "CONTACT", target: "CHEST" } }));
    const hip = computeTopPose(topInput({ leftHand: { state: "CONTACT", target: "HIP" } }));
    expect(chest.armL.shoulderPitch).toBeLessThan(hip.armL.shoulderPitch);
  });

  it("fatigue slumps the shoulders and drops the chin", () => {
    const fresh = computeTopPose(topInput({ stamina: 1, nowMs: 0 }));
    const spent = computeTopPose(topInput({ stamina: 0, nowMs: 0 }));
    expect(spent.pelvisY).toBeLessThan(fresh.pelvisY);
    expect(spent.headPitch).toBeGreaterThan(fresh.headPitch);
  });
});

describe("computeTopPose — pass and cut animation", () => {
  it("a pass drive drops the torso, surges the hips forward, and steps the lead knee up", () => {
    const idle = computeTopPose(topInput());
    const driving = computeTopPose(topInput({ passElapsedMs: 800, weightLateral: 0.5 }));
    expect(driving.torsoPitch).toBeGreaterThan(idle.torsoPitch);
    expect(driving.pelvisZ).toBeGreaterThan(idle.pelvisZ);
    expect(driving.pelvisY).toBeLessThan(idle.pelvisY);
    // weightLateral ≥ 0 → right leg leads (steps up = more hip flexion).
    expect(driving.legR.hipPitch).toBeLessThan(driving.legL.hipPitch);
  });

  it("a grip cut animates only the cutting arm through windup and strike", () => {
    const idle = computeTopPose(topInput());
    const windup = computeTopPose(topInput({ cutElapsedRMs: 200 }));
    const strike = computeTopPose(topInput({ cutElapsedRMs: 600 }));
    expect(windup.armL).toEqual(idle.armL);
    // Windup raises the arm out; the strike then swats across (yaw flips in).
    expect(windup.armR.shoulderRoll).toBeGreaterThan(idle.armR.shoulderRoll);
    expect(strike.armR.shoulderYaw).toBeLessThan(windup.armR.shoulderYaw);
  });
});

describe("window anticipation", () => {
  it("an open judgment window coils the attacker (hips load, torso curls)", () => {
    const calm = computeBottomPose(bottomInput());
    const coiled = computeBottomPose(bottomInput({ windowOpen: true }));
    expect(coiled.pelvisY).toBeGreaterThan(calm.pelvisY);
    expect(coiled.torsoPitch).toBeGreaterThan(calm.torsoPitch);
  });

  it("an open counter window braces the defender upright", () => {
    const calm = computeTopPose(topInput());
    const braced = computeTopPose(topInput({ counterWindowOpen: true }));
    expect(braced.torsoPitch).toBeLessThan(calm.torsoPitch);
  });
});

describe("computeFinishPoses", () => {
  it("TRIANGLE locks both legs high with the shins crossing the neck line", () => {
    const { bottom, top } = computeFinishPoses("TRIANGLE", 0);
    const { thigh, shin } = legDirections(bottom.legR);
    expect(thigh[2]).toBeGreaterThan(0.6); // thigh steeply up
    expect(shin[0]).toBeLessThan(-0.5); // shin hard across
    expect(top.torsoPitch).toBeGreaterThan(0.6); // defender folded
  });

  it("SCISSOR_SWEEP topples the defender onto his side", () => {
    const { top } = computeFinishPoses("SCISSOR_SWEEP", 600); // settled
    expect(Math.abs(top.pelvisRoll)).toBeGreaterThan(1);
    expect(top.pelvisY).toBeLessThan(0.35);
  });

  it("FLOWER_SWEEP mirrors the topple direction", () => {
    const scissor = computeFinishPoses("SCISSOR_SWEEP", 0).top;
    const flower = computeFinishPoses("FLOWER_SWEEP", 0).top;
    expect(Math.sign(flower.pelvisRoll)).toBe(-Math.sign(scissor.pelvisRoll));
    expect(Math.sign(flower.pelvisX)).toBe(-Math.sign(scissor.pelvisX));
  });

  it("HIP_BUMP sits the attacker up and tips the defender backward", () => {
    const { bottom, top } = computeFinishPoses("HIP_BUMP", 600); // settled
    expect(bottom.torsoPitch).toBeGreaterThan(0.9); // big sit-up
    expect(top.torsoPitch).toBeLessThan(-0.3); // knocked back
  });

  it("PASS settles the defender beside the swept legs", () => {
    const { bottom, top } = computeFinishPoses("PASS", 600); // settled
    expect(Math.abs(top.pelvisX)).toBeGreaterThan(0.3); // off to the side
    // Both attacker legs swept the same way: world-side = −authored for
    // the mirrored left leg, so the signs must oppose.
    const right = legDirections(bottom.legR).thigh;
    const left = legDirections(bottom.legL).thigh;
    expect(Math.sign(left[0])).toBe(-Math.sign(right[0]));
  });

  it("motion finishes ramp through an execution phase into the settle", () => {
    const mid = computeFinishPoses("SCISSOR_SWEEP", 60).top;
    const settled = computeFinishPoses("SCISSOR_SWEEP", 1200).top;
    expect(mid.pelvisRoll).toBeLessThan(settled.pelvisRoll);
    expect(mid.pelvisY).toBeGreaterThan(settled.pelvisY); // still falling
  });

  it("SCRAMBLE re-sets both players after the guard opens", () => {
    const { bottom, top } = computeFinishPoses("SCRAMBLE", 600);
    expect(bottom.torsoPitch).toBeGreaterThan(0.5); // sat up
    expect(top.pelvisZ).toBeLessThan(-0.1); // backing off
  });

  it("submission tableaux keep breathing (poses move over time)", () => {
    const a = computeFinishPoses("CROSS_COLLAR", 0);
    const b = computeFinishPoses("CROSS_COLLAR", 700);
    expect(a.bottom.breath).not.toBeCloseTo(b.bottom.breath, 5);
    expect(a.bottom.armL.elbowBend).not.toBeCloseTo(b.bottom.armL.elbowBend, 5);
  });
});

// -----------------------------------------------------------------------------
// Contact IK / FK / gaze / sway (computeScenePoses)

const dist = (a: readonly number[], b: readonly number[]): number =>
  Math.hypot(a[0]! - b[0]!, a[1]! - b[1]!, a[2]! - b[2]!);

describe("computeScenePoses — contact IK", () => {
  it("FK sanity: kneeling head is up high, supine head is toward the camera", () => {
    const poses = computeScenePoses(bottomInput(), topInput());
    const tf = computeBodyFrames(poses.top, TOP_PLACEMENT);
    const bf = computeBodyFrames(poses.bottom, BOTTOM_PLACEMENT);
    expect(tf.headPos[1]).toBeGreaterThan(0.85);
    expect(bf.headPos[2]).toBeGreaterThan(0.3);
    expect(bf.headPos[1]).toBeLessThan(0.65); // curled slightly off the mat
  });

  it("a sleeve grip plants the hand on the defender's actual hand", () => {
    const poses = computeScenePoses(
      bottomInput({ rightHand: { state: "CONTACT", target: "SLEEVE_L" } }),
      topInput(),
    );
    const bf = computeBodyFrames(poses.bottom, BOTTOM_PLACEMENT);
    const tf = computeBodyFrames(poses.top, TOP_PLACEMENT);
    expect(dist(bf.handR, tf.handL)).toBeLessThan(0.06);
  });

  it("an out-of-range collar grip strains at full extension, then connects as posture breaks", () => {
    const upright = computeScenePoses(
      bottomInput({ leftHand: { state: "GRIPPED", target: "COLLAR_R" } }),
      topInput(),
    );
    const broken = computeScenePoses(
      bottomInput({ leftHand: { state: "GRIPPED", target: "COLLAR_R" } }),
      topInput({ postureBreakY: 0.95 }),
    );
    const ubf = computeBodyFrames(upright.bottom, BOTTOM_PLACEMENT);
    const utf = computeBodyFrames(upright.top, TOP_PLACEMENT);
    const bbf = computeBodyFrames(broken.bottom, BOTTOM_PLACEMENT);
    const btf = computeBodyFrames(broken.top, TOP_PLACEMENT);
    // Upright: anchor unreachable → arm near max extension (0.55 m chain).
    expect(dist(ubf.shoulderL, ubf.handL)).toBeGreaterThan(0.5);
    // Broken down: the collar comes into range and the gap closes hard.
    const uGap = dist(ubf.handL, gripZoneAnchor("COLLAR_R", utf)!);
    const bGap = dist(bbf.handL, gripZoneAnchor("COLLAR_R", btf)!);
    expect(bGap).toBeLessThan(uGap - 0.15);
  });

  it("a defender knee post lands on the attacker's actual knee", () => {
    const poses = computeScenePoses(
      bottomInput(),
      topInput({ leftHand: { state: "CONTACT", target: "KNEE_R" } }),
    );
    const tf = computeBodyFrames(poses.top, TOP_PLACEMENT);
    const bf = computeBodyFrames(poses.bottom, BOTTOM_PLACEMENT);
    expect(dist(tf.handL, bf.kneeR)).toBeLessThan(0.06);
  });

  it("cut chops keep priority over IK on the chopping arm", () => {
    const withCut = computeScenePoses(
      bottomInput(),
      topInput({ leftHand: { state: "CONTACT", target: "KNEE_R" }, cutElapsedLMs: 300 }),
    );
    const noIk = computeTopPose(
      topInput({ leftHand: { state: "CONTACT", target: "KNEE_R" }, cutElapsedLMs: 300 }),
    );
    expect(withCut.top.armL).toEqual(noIk.armL);
  });

  it("heads track the opponent laterally", () => {
    const left = computeScenePoses(bottomInput(), topInput({ weightLateral: -0.9 }));
    const right = computeScenePoses(bottomInput(), topInput({ weightLateral: 0.9 }));
    expect(left.bottom.headYaw).not.toBeCloseTo(right.bottom.headYaw, 3);
    expect(Math.sign(left.bottom.headYaw)).toBe(-Math.sign(right.bottom.headYaw));
  });
});

describe("idle micro-sway", () => {
  it("an otherwise idle body keeps re-balancing over time", () => {
    const a = computeBottomPose(bottomInput({ nowMs: 0 }));
    const b = computeBottomPose(bottomInput({ nowMs: 800 }));
    expect(a.pelvisX).not.toBeCloseTo(b.pelvisX, 5);
    const ta = computeTopPose(topInput({ nowMs: 0 }));
    const tb = computeTopPose(topInput({ nowMs: 800 }));
    expect(ta.pelvisRoll).not.toBeCloseTo(tb.pelvisRoll, 5);
  });
});

describe("second realism pass — anticipation, reaction, coupling", () => {
  it("a fresh reach winds up before the lunge", () => {
    const windup = computeBottomPose(
      bottomInput({ leftHand: { state: "REACHING", target: "COLLAR_L", sinceMs: 40 } }),
    );
    const lunge = computeBottomPose(
      bottomInput({ leftHand: { state: "REACHING", target: "COLLAR_L", sinceMs: 400 } }),
    );
    expect(windup.armL.elbowBend).toBeGreaterThan(lunge.armL.elbowBend); // coiled
    expect(windup.armL.shoulderPitch).toBeGreaterThan(lunge.armL.shoulderPitch); // pulled back
  });

  it("reaching twists the torso against the punch-out, mirrored per side", () => {
    const idleYaw = computeBottomPose(bottomInput()).torsoYaw;
    const left = computeBottomPose(
      bottomInput({ leftHand: { state: "REACHING", target: "COLLAR_L", sinceMs: 100 } }),
    ).torsoYaw;
    const right = computeBottomPose(
      bottomInput({ rightHand: { state: "REACHING", target: "COLLAR_R", sinceMs: 100 } }),
    ).torsoYaw;
    expect(left).not.toBeCloseTo(idleYaw, 5);
    expect(Math.sign(left - idleYaw)).toBe(-Math.sign(right - idleYaw));
  });

  it("a held sleeve drags the defender's arm — both hands stay connected", () => {
    const poses = computeScenePoses(
      bottomInput({ rightHand: { state: "GRIPPED", target: "SLEEVE_L" } }),
      topInput(),
    );
    const bf = computeBodyFrames(poses.bottom, BOTTOM_PLACEMENT);
    const tf = computeBodyFrames(poses.top, TOP_PLACEMENT);
    expect(dist(tf.handL, bf.handR)).toBeLessThan(0.06); // hand spheres overlap
    // The dragged arm fights the grip — visible strain tremor.
    expect(poses.top.armL.tremor).toBeGreaterThanOrEqual(0.3);
  });

  it("the defender's weight shifts over a posted hand and dips under an extracted arm", () => {
    const idle = computeTopPose(topInput());
    const posted = computeTopPose(topInput({ rightHand: { state: "GRIPPED", target: "CHEST" } }));
    const extracted = computeTopPose(topInput({ armExtractedL: true }));
    expect(posted.pelvisX).toBeGreaterThan(idle.pelvisX);
    expect(extracted.pelvisRoll).toBeLessThan(idle.pelvisRoll);
  });
});

describe("Tier 2 — hand grip and ankle articulation", () => {
  it("the hand opens to reach and clenches to a fist when gripping", () => {
    const reaching = computeBottomPose(
      bottomInput({ leftHand: { state: "REACHING", target: "COLLAR_L", sinceMs: 400 } }),
    );
    const gripped = computeBottomPose(
      bottomInput({ leftHand: { state: "GRIPPED", target: "COLLAR_L" }, gripStrengthL: 1 }),
    );
    expect(reaching.armL.grip!).toBeLessThan(0.2); // splayed open
    expect(gripped.armL.grip!).toBeGreaterThan(0.9); // clenched
  });

  it("a planted IK grip keeps its clenched hand", () => {
    const poses = computeScenePoses(
      bottomInput({ rightHand: { state: "GRIPPED", target: "SLEEVE_L" }, gripStrengthR: 1 }),
      topInput(),
    );
    expect(poses.bottom.armR.grip!).toBeGreaterThan(0.85);
  });

  it("locked feet plantarflex (hook) while framing feet dorsiflex", () => {
    const locked = computeBottomPose(bottomInput());
    const framing = computeBottomPose(
      bottomInput({ leftFootState: "UNLOCKED", rightFootState: "UNLOCKED" }),
    );
    expect(locked.legR.ankle!).toBeGreaterThan(0.3); // toes pointed, hooking
    expect(framing.legR.ankle!).toBeLessThan(0); // ball of foot, toes up
  });
});

describe("Tier 3 — technique-specific window entry", () => {
  it("a triangle window raises the hips and climbs the near leg vs a plain window", () => {
    const plain = computeBottomPose(bottomInput({ windowOpen: true }));
    const tri = computeBottomPose(bottomInput({ windowOpen: true, windowTechnique: "TRIANGLE" }));
    expect(tri.pelvisY).toBeGreaterThan(plain.pelvisY);
    const triShin = legDirections(tri.legL).shin;
    const plainShin = legDirections(plain.legL).shin;
    expect(triShin[2]).toBeGreaterThan(plainShin[2]); // shin swings higher/over
  });

  it("a hip-bump window sits the attacker up and posts a hand behind", () => {
    const plain = computeBottomPose(bottomInput({ windowOpen: true }));
    const hb = computeBottomPose(bottomInput({ windowOpen: true, windowTechnique: "HIP_BUMP" }));
    expect(hb.torsoPitch).toBeGreaterThan(plain.torsoPitch + 0.3); // big sit-up
    expect(hb.armR.shoulderPitch).toBeGreaterThan(0); // posting arm reaches back
  });

  it("the omoplata window turns the hips out", () => {
    const omo = computeBottomPose(bottomInput({ windowOpen: true, windowTechnique: "OMOPLATA" }));
    expect(Math.abs(omo.pelvisYaw)).toBeGreaterThan(0.3);
  });

  it("the entry only fires while the window is open", () => {
    const closed = computeBottomPose(bottomInput({ windowOpen: false, windowTechnique: "TRIANGLE" }));
    const idle = computeBottomPose(bottomInput());
    expect(closed.pelvisY).toBeCloseTo(idle.pelvisY, 6);
  });

  it("an engaged grip keeps its grip pose instead of the entry's framing arm", () => {
    const pose = computeBottomPose(
      bottomInput({
        windowOpen: true,
        windowTechnique: "CROSS_COLLAR",
        leftHand: { state: "GRIPPED", target: "COLLAR_L" },
        gripStrengthL: 1,
      }),
    );
    expect(pose.armL.grip!).toBeGreaterThan(0.9); // live grip, not the canned 0.85
  });
});

describe("Tier 4 — balance recovery post", () => {
  it("a hard posture break with a free hand throws it out to post", () => {
    const stable = computeTopPose(topInput({ postureBreakX: 0.2 }));
    const broken = computeTopPose(topInput({ postureBreakX: 0.9 }));
    // The posting (right) arm swings out — more shoulder roll than stable.
    expect(broken.armR.shoulderRoll).toBeGreaterThan(stable.armR.shoulderRoll);
  });

  it("the post plants the free hand low on the mat (near y=0)", () => {
    const poses = computeScenePoses(
      bottomInput(),
      topInput({ postureBreakX: 0.95, postureBreakY: 0.3 }),
    );
    const tf = computeBodyFrames(poses.top, TOP_PLACEMENT);
    expect(tf.handR[1]).toBeLessThan(0.55); // dropped toward the mat (arm at full reach)
  });

  it("does not post with an engaged hand", () => {
    const gripping = computeTopPose(
      topInput({ postureBreakX: 0.95, rightHand: { state: "GRIPPED", target: "CHEST" } }),
    );
    const free = computeTopPose(topInput({ postureBreakX: 0.95 }));
    // The gripping right hand keeps its grip pose, not the post swing-out.
    expect(gripping.armR.shoulderRoll).toBeLessThan(free.armR.shoulderRoll);
  });

  it("no post while posture is stable", () => {
    expect(balancePost(topInput({ postureBreakX: 0.2, postureBreakY: 0.2 }))).toBeNull();
    expect(balancePost(topInput({ postureBreakX: 0.9 }))).not.toBeNull();
  });
});

describe("Tier 5 — motion variation (alive idle)", () => {
  it("a locked idle guard keeps shuffling (legs + hips move over time)", () => {
    const a = computeBottomPose(bottomInput({ nowMs: 0 }));
    const b = computeBottomPose(bottomInput({ nowMs: 700 }));
    expect(a.legL.kneeBend).not.toBeCloseTo(b.legL.kneeBend, 4);
    expect(a.pelvisYaw).not.toBeCloseTo(b.pelvisYaw, 4);
    expect(a.headYaw).not.toBeCloseTo(b.headYaw, 4); // head scans
  });

  it("the guard shuffle stops once a hand engages", () => {
    // With a busy hand, two timestamps differ only by the always-on sway,
    // not the larger shuffle — pelvisYaw stays near the hipAngle baseline.
    const busy = { state: "GRIPPED", target: "COLLAR_L" } as const;
    const a = computeBottomPose(bottomInput({ nowMs: 0, leftHand: busy }));
    const b = computeBottomPose(bottomInput({ nowMs: 700, leftHand: busy }));
    expect(Math.abs(a.pelvisYaw - b.pelvisYaw)).toBeLessThan(0.02);
  });

  it("a searching passer weaves weight side-to-side over time", () => {
    const a = computeTopPose(topInput({ nowMs: 0 }));
    const b = computeTopPose(topInput({ nowMs: 900 }));
    expect(a.pelvisX).not.toBeCloseTo(b.pelvisX, 4);
    expect(a.pelvisRoll).not.toBeCloseTo(b.pelvisRoll, 4);
  });

  it("the weave settles once the pass commits", () => {
    const a = computeTopPose(topInput({ nowMs: 0, passElapsedMs: 300, weightLateral: 0.5 }));
    const b = computeTopPose(topInput({ nowMs: 900, passElapsedMs: 300, weightLateral: 0.5 }));
    // Only the small always-on sway remains; no large search weave.
    expect(Math.abs(a.pelvisX - b.pelvisX)).toBeLessThan(0.05);
  });

  it("breathing is two-octave (not a pure single sine — half-period asymmetric)", () => {
    // For a pure sine of freq f, value at t and t+half-period would be exact
    // negatives. The second octave breaks that symmetry.
    const fresh = bottomInput({ stamina: 1 });
    const hz = 0.28;
    const halfMs = (1 / hz) * 500; // half period in ms
    const v0 = computeBottomPose({ ...fresh, nowMs: 300 }).breath;
    const vHalf = computeBottomPose({ ...fresh, nowMs: 300 + halfMs }).breath;
    expect(Math.abs(v0 + vHalf)).toBeGreaterThan(1e-3); // not exact negatives
  });
});
