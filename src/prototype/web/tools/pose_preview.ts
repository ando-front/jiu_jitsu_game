// Headless pose preview — renders the articulated blockman rigs to PNG
// without WebGL by projecting every mesh's world-space segment through the
// game camera and rasterising thick lines into a pixel buffer.
//
//   npx vite-node tools/pose_preview.ts
//
// Output: tools/preview/*.png, one image per scripted scene state. Used to
// eyeball procedural poses (supine guard, reaches, posture break) in cloud
// sessions where no browser is available. Not part of the test suite.

import * as fs from "node:fs";
import * as path from "node:path";
import * as THREE from "three";
import { PNG } from "pngjs";
import { buildBlockman, type BlockmanRig } from "../src/scene/blockman.js";
import {
  computeFinishPoses,
  computeScenePoses,
  type BodyPose,
  type BottomPoseInput,
  type FinishKind,
  type TopPoseInput,
} from "../src/scene/pose.js";

const W = 640;
const H = 480;

// Front camera mirrors createScene's; the side camera is diagnostic-only.
const frontCamera = new THREE.PerspectiveCamera(55, W / H, 0.1, 100);
frontCamera.position.set(0, 1.25, 1.9);
frontCamera.lookAt(0, 0.55, -0.7);
frontCamera.updateMatrixWorld(true);

const sideCamera = new THREE.PerspectiveCamera(55, W / H, 0.1, 100);
sideCamera.position.set(2.4, 0.95, -0.5);
sideCamera.lookAt(0, 0.4, -0.5);
sideCamera.updateMatrixWorld(true);

let camera = frontCamera;

type RGB = readonly [number, number, number];

function drawLine(
  buf: Buffer,
  x0: number, y0: number, x1: number, y1: number,
  thickness: number,
  rgb: RGB,
): void {
  const steps = Math.max(1, Math.ceil(Math.hypot(x1 - x0, y1 - y0)));
  const r = Math.max(1, thickness / 2);
  for (let i = 0; i <= steps; i += 1) {
    const t = i / steps;
    const cx = x0 + (x1 - x0) * t;
    const cy = y0 + (y1 - y0) * t;
    for (let dy = -r; dy <= r; dy += 1) {
      for (let dx = -r; dx <= r; dx += 1) {
        if (dx * dx + dy * dy > r * r) continue;
        const px = Math.round(cx + dx);
        const py = Math.round(cy + dy);
        if (px < 0 || px >= W || py < 0 || py >= H) continue;
        const idx = (py * W + px) * 4;
        buf[idx] = rgb[0];
        buf[idx + 1] = rgb[1];
        buf[idx + 2] = rgb[2];
        buf[idx + 3] = 255;
      }
    }
  }
}

function project(v: THREE.Vector3): { x: number; y: number; scale: number } {
  const p = v.clone().project(camera);
  return {
    x: (p.x * 0.5 + 0.5) * W,
    y: (1 - (p.y * 0.5 + 0.5)) * H,
    scale: 1 / Math.max(0.2, v.clone().applyMatrix4(camera.matrixWorldInverse).length()),
  };
}

// Collect each mesh as its world-space principal segment (capsules/boxes
// run along local y; spheres become dots), so intertwined bodies can be
// depth-sorted segment-by-segment before drawing (painter's algorithm per
// limb, not per rig — grappling poses interleave constantly).
type Segment = { a: THREE.Vector3; b: THREE.Vector3; radius: number; rgb: RGB; depth: number };

function collectSegments(rig: BlockmanRig, rgb: RGB, out: Segment[]): void {
  rig.root.updateMatrixWorld(true);
  rig.root.traverse((obj) => {
    if (!(obj instanceof THREE.Mesh)) return;
    const geo = obj.geometry as THREE.BufferGeometry & { parameters?: Record<string, number> };
    const params = geo.parameters ?? {};
    let halfLen = 0;
    let radius = 0.05;
    let axis = new THREE.Vector3(0, 1, 0);
    if ("length" in params) {
      // CapsuleGeometry
      halfLen = (params["length"]! / 2) + (params["radius"] ?? 0);
      radius = params["radius"] ?? 0.05;
    } else if ("height" in params) {
      // BoxGeometry — use depth as the long axis for feet.
      halfLen = (params["depth"] ?? 0.1) / 2;
      radius = (params["height"] ?? 0.06) / 2;
      axis = new THREE.Vector3(0, 0, 1);
    } else if ("radius" in params) {
      // SphereGeometry
      radius = params["radius"]!;
    }
    const a = obj.localToWorld(axis.clone().multiplyScalar(halfLen));
    const b = obj.localToWorld(axis.clone().multiplyScalar(-halfLen));
    const mid = a.clone().add(b).multiplyScalar(0.5);
    const depth = mid.distanceTo(camera.position);
    out.push({ a, b, radius, rgb, depth });
  });
}

function drawSegments(buf: Buffer, segments: Segment[]): void {
  segments.sort((s1, s2) => s2.depth - s1.depth); // back to front
  for (const s of segments) {
    const pa = project(s.a);
    const pb = project(s.b);
    drawLine(buf, pa.x, pa.y, pb.x, pb.y, s.radius * 700 * pa.scale, s.rgb);
  }
}

function drawGroundGrid(buf: Buffer): void {
  for (let z = -2; z <= 2; z += 0.5) {
    const a = project(new THREE.Vector3(-2, 0, z));
    const b = project(new THREE.Vector3(2, 0, z));
    drawLine(buf, a.x, a.y, b.x, b.y, 1, [60, 60, 68]);
  }
  for (let x = -2; x <= 2; x += 0.5) {
    const a = project(new THREE.Vector3(x, 0, -2));
    const b = project(new THREE.Vector3(x, 0, 2));
    drawLine(buf, a.x, a.y, b.x, b.y, 1, [60, 60, 68]);
  }
}

const IDLE = { state: "IDLE", target: null } as const;

function bottomBase(overrides: Partial<BottomPoseInput>): BottomPoseInput {
  return {
    nowMs: 0, stamina: 1, guard: "CLOSED",
    leftHand: IDLE, rightHand: IDLE,
    leftFootState: "LOCKED", rightFootState: "LOCKED",
    hipAngle: 0, hipPush: 0, hipLateral: 0,
    gripStrengthL: 0, gripStrengthR: 0,
    windowOpen: false,
    ...overrides,
  };
}

function topBase(overrides: Partial<TopPoseInput>): TopPoseInput {
  return {
    nowMs: 0, stamina: 1,
    leftHand: IDLE, rightHand: IDLE,
    postureBreakX: 0, postureBreakY: 0,
    weightForward: 0, weightLateral: 0,
    armExtractedL: false, armExtractedR: false,
    passElapsedMs: null,
    cutElapsedLMs: null,
    cutElapsedRMs: null,
    counterWindowOpen: false,
    ...overrides,
  };
}

type SceneSpec = {
  name: string;
  bottom: Partial<BottomPoseInput>;
  top: Partial<TopPoseInput>;
  // When set, the scene renders computeFinishPoses(kind, tMs) instead of
  // the live FSM-driven poses.
  finish?: { kind: FinishKind; tMs: number };
};

const SCENES: SceneSpec[] = [
  { name: "01_idle_closed_guard", bottom: {}, top: {} },
  {
    name: "02_reach_collar",
    bottom: { leftHand: { state: "REACHING", target: "COLLAR_R" }, rightHand: { state: "GRIPPED", target: "SLEEVE_L" } },
    top: {},
  },
  {
    name: "03_posture_broken",
    bottom: { leftHand: { state: "GRIPPED", target: "COLLAR_R" }, rightHand: { state: "GRIPPED", target: "SLEEVE_L" }, gripStrengthL: 1, gripStrengthR: 1 },
    top: { postureBreakX: 0.3, postureBreakY: 0.85 },
  },
  {
    name: "04_open_guard_feet_on_hips",
    bottom: { leftFootState: "UNLOCKED", rightFootState: "UNLOCKED", guard: "OPEN" },
    top: { weightForward: 0.5, leftHand: { state: "GRIPPED", target: "CHEST" }, rightHand: { state: "GRIPPED", target: "HIP" } },
  },
  {
    name: "05_exhausted",
    bottom: { stamina: 0.1, nowMs: 900 },
    top: { stamina: 0.1, nowMs: 900, armExtractedL: true },
  },
  {
    name: "06_pass_drive",
    bottom: { leftFootState: "UNLOCKED", rightFootState: "UNLOCKED" },
    top: { passElapsedMs: 800, weightLateral: 0.6, weightForward: 0.6 },
  },
  {
    name: "07_cut_strike",
    bottom: { leftHand: { state: "GRIPPED", target: "COLLAR_R" }, gripStrengthL: 1 },
    top: { cutElapsedRMs: 450 },
  },
  {
    name: "12_window_triangle_entry",
    bottom: { windowOpen: true, windowTechnique: "TRIANGLE" },
    top: { postureBreakY: 0.5 },
  },
  {
    name: "13_window_hipbump_entry",
    bottom: { windowOpen: true, windowTechnique: "HIP_BUMP" },
    top: {},
  },
  { name: "08_finish_triangle", bottom: {}, top: {}, finish: { kind: "TRIANGLE", tMs: 400 } },
  { name: "09_finish_scissor_sweep", bottom: {}, top: {}, finish: { kind: "SCISSOR_SWEEP", tMs: 400 } },
  { name: "10_finish_hip_bump", bottom: {}, top: {}, finish: { kind: "HIP_BUMP", tMs: 400 } },
  { name: "11_finish_pass", bottom: {}, top: {}, finish: { kind: "PASS", tMs: 400 } },
];

const outDir = path.join(import.meta.dirname, "preview");
fs.mkdirSync(outDir, { recursive: true });

for (const spec of SCENES) {
  // Must match createScene's rig setup.
  const bottomRig = buildBlockman("bottom", true);
  bottomRig.root.position.set(0, 0, 0);
  bottomRig.root.rotation.y = Math.PI;
  const topRig = buildBlockman("top", false);
  topRig.root.position.set(0, 0, -0.5);

  // Settle the springs: step the pose for 2 simulated seconds.
  const bIn = bottomBase(spec.bottom);
  const tIn = topBase(spec.top);
  for (let ms = 0; ms <= 2000; ms += 16) {
    const t = (bIn.nowMs ?? 0) + ms;
    let bPose: BodyPose;
    let tPose: BodyPose;
    if (spec.finish !== undefined) {
      const fp = computeFinishPoses(spec.finish.kind, spec.finish.tMs);
      bPose = fp.bottom;
      tPose = fp.top;
    } else {
      const sp = computeScenePoses({ ...bIn, nowMs: t }, { ...tIn, nowMs: t });
      bPose = sp.bottom;
      tPose = sp.top;
    }
    bottomRig.applyPose(bPose, t);
    topRig.applyPose(tPose, t);
  }

  for (const [viewName, cam] of [["front", frontCamera], ["side", sideCamera]] as const) {
    camera = cam;
    const png = new PNG({ width: W, height: H });
    for (let i = 0; i < W * H; i += 1) {
      png.data[i * 4] = 21; png.data[i * 4 + 1] = 22; png.data[i * 4 + 2] = 26; png.data[i * 4 + 3] = 255;
    }
    drawGroundGrid(png.data);
    const segments: Segment[] = [];
    collectSegments(topRig, [201, 180, 138], segments);
    collectSegments(bottomRig, [90, 140, 255], segments);
    drawSegments(png.data, segments);

    const file = path.join(outDir, `${spec.name}_${viewName}.png`);
    fs.writeFileSync(file, PNG.sync.write(png));
    console.log(`wrote ${file}`);
  }
}
