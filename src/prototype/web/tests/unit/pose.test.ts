// Tests for the pure pose-synthesis layer (scene/pose.ts). The rendering
// itself is platform code, but the FSM-state → joint-target mapping is pure
// and these invariants are what make the motion read correctly: reaches
// extend, grips pull, locks squeeze, posture breaks crumple, fatigue sags.

import { describe, expect, it } from "vitest";
import {
  computeBottomPose,
  computeTopPose,
  type BottomPoseInput,
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
    expect(attacking.torsoPitch).toBeLessThan(idle.torsoPitch);
  });
});

describe("computeBottomPose — legs and hips", () => {
  it("LOCKED squeezes knees in; UNLOCKED opens them", () => {
    const locked = computeBottomPose(bottomInput());
    const open = computeBottomPose(
      bottomInput({ leftFootState: "UNLOCKED", rightFootState: "UNLOCKED" }),
    );
    expect(locked.legL.hipRoll).toBeLessThan(0);
    expect(open.legL.hipRoll).toBeGreaterThan(0);
    expect(locked.legL.kneeBend).toBeGreaterThan(open.legL.kneeBend);
  });

  it("LOCKING wobbles over time (effort animation)", () => {
    const a = computeBottomPose(bottomInput({ leftFootState: "LOCKING", nowMs: 0 }));
    const b = computeBottomPose(bottomInput({ leftFootState: "LOCKING", nowMs: 150 }));
    expect(a.legL.kneeBend).not.toBeCloseTo(b.legL.kneeBend, 5);
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
    expect(spent.headPitch).toBeGreaterThan(fresh.headPitch);
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
    expect(broken.torsoPitch).toBeLessThan(upright.torsoPitch);
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
