// PLATFORM — Three.js scene for Stage 1. Renders two articulated blockmen +
// ground, an overlay vignette for judgment windows, and transient event
// pulses (flash / shake / tint) triggered by SimEvents.
//
// Stage 1 stays deliberately low-fi in *materials* (flat capsules, no
// textures), but the rigs are fully articulated: every joint is driven by
// pose targets from [pose.ts] through damped springs, so FSM snaps read as
// fast-but-continuous human motion — reaches lunge, grips pull, posture
// breaks crumple, locked guards squeeze, and both bodies breathe.

import * as THREE from "three";
import { RIG_DIMS, TOP_PLACEMENT } from "./pose.js";
import type { ArmPose, BodyPose, LegPose } from "./pose.js";

export interface Scene3D {
  renderer: THREE.WebGLRenderer;
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  bottom: BlockmanRig;
  top: BlockmanRig;
  setWindowTint(strength: number): void;
  setInitiative(tint: InitiativeTint): void;
  // §5.4 — warm-colour shift proportional to "how spent" the player's
  // stamina is. Takes a normalised 0–1 fatigue value where 1 = fully
  // spent. Scene handles the actual vignette colour interpolation so
  // main.ts only needs to publish the scalar.
  setStaminaFatigue(fatigue: number): void;
  // Drives both rigs toward the given pose targets. `nowMs` is the sim's
  // game clock; spring smoothing uses its deltas, so judgment-window
  // slow-mo automatically slows the bodies.
  updateMotion(bottomPose: BodyPose, topPose: BodyPose, nowMs: number): void;
  // Transient pulses triggered by sim events. The caller fires-and-forgets
  // these; the scene owns the decay animation.
  pulseFlash(color: THREE.ColorRepresentation, durationMs?: number): void;
  pulseShake(rig: "bottom" | "top", amplitude?: number, durationMs?: number): void;
  updatePulses(realDtMs: number): void;
  resize(w: number, h: number): void;
  render(): void;
}

export type InitiativeTint = "Bottom" | "Top" | "Neutral";

// Named impact reactions. Each kicks velocity into specific joint springs,
// so the rig visibly flinches and then settles — far more physical than a
// positional shake. Fire-and-forget from SimEvents.
export type ImpulseKind =
  | "ARM_L_FLUNG"   // parried: arm knocked outward
  | "ARM_R_FLUNG"
  | "ARM_L_RECOIL"  // grip broken / cut: arm snaps back in
  | "ARM_R_RECOIL"
  | "TORSO_JERK"    // yanked toward the opponent (grip established)
  | "TORSO_JOLT";   // hit/staggered (pass pressure, impacts)

export interface BlockmanRig {
  root: THREE.Group;
  body: THREE.Mesh;
  impulse(kind: ImpulseKind): void;
  setBreakBucket(bucket: number): void;
  // §D3 — colour limbs by FSM state so input → state changes are visible
  // on top of the joint animation. State enum strings come from
  // [hand_fsm.ts] / [foot_fsm.ts]; "IDLE" reverts to base body colour.
  setLimbState(limb: "armL" | "armR" | "legL" | "legR", state: string): void;
  applyPose(pose: BodyPose, nowMs: number): void;
  // Shake offsets live on a dedicated group between root and body so the
  // pose application never fights the pulse animation.
  shakeGroup: THREE.Group;
}

// -----------------------------------------------------------------------------
// Damped-spring joint smoothing. Semi-implicit Euler, sub-stepped so the
// stiffest joints stay stable across rAF hitches. Slight underdamping on the
// arms gives reaches a snappy overshoot.

type Spring = { x: number; v: number };

const SPRING_SUBSTEP_S = 0.008;

function stepSpring(s: Spring, target: number, dtS: number, freqHz: number, zeta: number): number {
  const w = 2 * Math.PI * freqHz;
  let remaining = Math.min(dtS, 0.1);
  while (remaining > 0) {
    const h = Math.min(SPRING_SUBSTEP_S, remaining);
    const a = -w * w * (s.x - target) - 2 * zeta * w * s.v;
    s.v += a * h;
    s.x += s.v * h;
    remaining -= h;
  }
  return s.x;
}

// Per-group spring tuning: arms snap (visible overshoot), torso settles.
const TUNE = {
  arm: { freq: 5.5, zeta: 0.65 },
  leg: { freq: 4.0, zeta: 0.80 },
  torso: { freq: 3.0, zeta: 0.90 },
  pelvis: { freq: 4.0, zeta: 1.0 },
} as const;

// -----------------------------------------------------------------------------

export function createScene(canvas: HTMLCanvasElement): Scene3D {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x15161a);

  // Low ground-level framing: supine guard player in the foreground,
  // kneeling opponent behind — approximates the over-the-shoulder pillar.
  const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 100);
  camera.position.set(0, 1.25, 1.9);
  camera.lookAt(0, 0.55, -0.7);

  scene.add(new THREE.HemisphereLight(0xbcd4ff, 0x1a1a20, 0.8));
  const key = new THREE.DirectionalLight(0xffffff, 1.1);
  key.position.set(2, 4, 3);
  scene.add(key);

  const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(20, 20),
    new THREE.MeshStandardMaterial({ color: 0x232328, roughness: 0.9 }),
  );
  ground.rotation.x = -Math.PI / 2;
  scene.add(ground);

  // Bottom rig is yaw-flipped π so that, with the pelvis pitched −π/2 into
  // the supine pose, the head lands toward the camera and the chest faces
  // up; mirrorXZ compensates its world-axis pelvis offsets for the flip.
  const bottom = buildBlockman(new THREE.Color(0x5a8cff), true);
  bottom.root.position.set(0, 0, 0);
  bottom.root.rotation.y = Math.PI;
  scene.add(bottom.root);

  // Top rig keeps yaw 0 — its local +z (chest front) faces the supine
  // player — and kneels close enough that the locked guard legs wrap its
  // waist with the ankles crossing behind its back.
  const top = buildBlockman(new THREE.Color(0xc9b48a), false);
  top.root.position.set(...TOP_PLACEMENT.origin);
  scene.add(top.root);

  // --- Vignette overlay ---
  const overlayScene = new THREE.Scene();
  const overlayCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  const overlayMaterial = new THREE.ShaderMaterial({
    transparent: true,
    depthTest: false,
    depthWrite: false,
    uniforms: {
      uWindowStrength: { value: 0 },
      uFlashStrength: { value: 0 },
      uFlashColor: { value: new THREE.Color(0xffffff) },
      uVignetteColor: { value: new THREE.Color(0xfff2d0) },
      // §5.4 — 0 = no fatigue (neutral), 1 = fully spent (warm shift).
      uStaminaFatigue: { value: 0 },
      uStaminaColor: { value: new THREE.Color(0xd06030) },
    },
    vertexShader: /* glsl */ `
      varying vec2 vUv;
      void main() {
        vUv = uv;
        gl_Position = vec4(position.xy, 0.0, 1.0);
      }
    `,
    fragmentShader: /* glsl */ `
      varying vec2 vUv;
      uniform float uWindowStrength;
      uniform float uFlashStrength;
      uniform vec3  uFlashColor;
      uniform vec3  uVignetteColor;
      uniform float uStaminaFatigue;
      uniform vec3  uStaminaColor;
      void main() {
        vec2 c = vUv - 0.5;
        float r = length(c);
        float vignette = smoothstep(0.35, 0.8, r) * uWindowStrength;
        // §5.4 stamina grading — edges darken & shift warm as fatigue rises.
        float staminaVignette = smoothstep(0.28, 0.72, r) * uStaminaFatigue;
        vec3 rgb = mix(vec3(0.0), uVignetteColor, uWindowStrength * 0.25);
        rgb = mix(rgb, uStaminaColor, staminaVignette * 0.75);
        rgb = mix(rgb, uFlashColor, uFlashStrength);
        float alpha = max(max(vignette, staminaVignette * 0.65), uFlashStrength);
        gl_FragColor = vec4(rgb, alpha);
      }
    `,
  });
  const overlayQuad = new THREE.Mesh(
    new THREE.PlaneGeometry(2, 2),
    overlayMaterial,
  );
  overlayScene.add(overlayQuad);

  const initiativeLight = new THREE.HemisphereLight(0xffffff, 0x0, 0.2);
  scene.add(initiativeLight);

  // --- Transient pulses ---
  // Flash: uniform on the overlay. Decays linearly over `durationMs`.
  type Flash = { remainingMs: number; totalMs: number; color: THREE.Color };
  let activeFlash: Flash | null = null;

  // Shake: randomised per-frame offset on a rig's shake group, decays
  // linearly. The group is zeroed every updatePulses so shakes never drift.
  type Shake = { remainingMs: number; totalMs: number; amplitude: number; rig: "bottom" | "top" };
  const shakes: Shake[] = [];

  // Initiative camera dolly target — smoothed in updateMotion so the cut
  // between initiative states reads as a slow push, not a teleport.
  let cameraDollyTarget = 1.9;

  return {
    renderer,
    scene,
    camera,
    bottom,
    top,
    setWindowTint(strength: number) {
      overlayMaterial.uniforms.uWindowStrength!.value = Math.max(0, Math.min(1, strength));
    },
    setStaminaFatigue(fatigue: number) {
      overlayMaterial.uniforms.uStaminaFatigue!.value = Math.max(0, Math.min(1, fatigue));
    },
    setInitiative(tint: InitiativeTint) {
      cameraDollyTarget = 1.9 + (tint === "Bottom" ? -0.2 : tint === "Top" ? 0.2 : 0);
      const warm = 0xffe8c4;
      const cool = 0xbfd4ff;
      const neutral = 0xffffff;
      const hex = tint === "Bottom" ? warm : tint === "Top" ? cool : neutral;
      initiativeLight.color.setHex(hex);
    },
    updateMotion(bottomPose, topPose, nowMs) {
      bottom.applyPose(bottomPose, nowMs);
      top.applyPose(topPose, nowMs);
      camera.position.z += (cameraDollyTarget - camera.position.z) * 0.05;
    },
    pulseFlash(color, durationMs = 180) {
      activeFlash = {
        remainingMs: durationMs,
        totalMs: durationMs,
        color: new THREE.Color(color),
      };
    },
    pulseShake(rig, amplitude = 0.08, durationMs = 200) {
      shakes.push({ remainingMs: durationMs, totalMs: durationMs, amplitude, rig });
    },
    updatePulses(realDtMs: number) {
      // Flash decay.
      if (activeFlash !== null) {
        activeFlash.remainingMs -= realDtMs;
        if (activeFlash.remainingMs <= 0) {
          overlayMaterial.uniforms.uFlashStrength!.value = 0;
          activeFlash = null;
        } else {
          const t = activeFlash.remainingMs / activeFlash.totalMs;
          overlayMaterial.uniforms.uFlashStrength!.value = t;
          overlayMaterial.uniforms.uFlashColor!.value.copy(activeFlash.color);
        }
      }

      // Shake — rebuild each rig's shake offset from scratch every frame so
      // expiring shakes decay to exactly zero.
      bottom.shakeGroup.position.set(0, 0, 0);
      top.shakeGroup.position.set(0, 0, 0);
      for (let i = shakes.length - 1; i >= 0; i -= 1) {
        const s = shakes[i]!;
        s.remainingMs -= realDtMs;
        if (s.remainingMs <= 0) {
          shakes.splice(i, 1);
          continue;
        }
        const t = s.remainingMs / s.totalMs;
        const rig = s.rig === "bottom" ? bottom : top;
        rig.shakeGroup.position.x += (Math.random() - 0.5) * s.amplitude * t;
        rig.shakeGroup.position.y += (Math.random() - 0.5) * s.amplitude * t * 0.3;
      }
    },
    resize(w, h) {
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
    },
    render() {
      renderer.autoClear = true;
      renderer.render(scene, camera);
      renderer.autoClear = false;
      renderer.render(overlayScene, overlayCam);
    },
  };
}

// -----------------------------------------------------------------------------
// Articulated rig. Hierarchy (pivots at joints):
//
//   root → shakeGroup → pelvis
//     pelvis: torsoGroup (torso mesh, headGroup, shoulderL/R → elbowL/R)
//             hipL/R → kneeL/R
//
// Limb meshes hang along local −y from their joint group, so joint group
// rotations articulate them like bones.

function limbCapsule(
  radius: number,
  length: number,
  material: THREE.Material,
): THREE.Mesh {
  const mesh = new THREE.Mesh(new THREE.CapsuleGeometry(radius, length, 4, 10), material);
  mesh.position.y = -(length / 2 + radius);
  return mesh;
}

// Exported for headless pose previews (tools/pose_preview.ts) — the rig and
// its spring smoothing run fine without a WebGL context. `mirrorXZ` negates
// the world-axis pelvis offsets for rigs whose root is yaw-flipped π.
export function buildBlockman(baseColor: THREE.Color, mirrorXZ: boolean): BlockmanRig {
  const root = new THREE.Group();
  const shakeGroup = new THREE.Group();
  root.add(shakeGroup);
  const pelvis = new THREE.Group();
  shakeGroup.add(pelvis);

  const material = new THREE.MeshStandardMaterial({
    color: baseColor.clone(),
    roughness: 0.6,
  });

  const pelvisMesh = new THREE.Mesh(new THREE.CapsuleGeometry(0.13, 0.10, 4, 10), material);
  pelvisMesh.rotation.z = Math.PI / 2;
  pelvis.add(pelvisMesh);

  const torsoGroup = new THREE.Group();
  torsoGroup.position.set(0, RIG_DIMS.pelvisToTorso, 0);
  pelvis.add(torsoGroup);

  const torsoMesh = new THREE.Mesh(new THREE.CapsuleGeometry(0.17, 0.30, 4, 12), material);
  torsoMesh.position.y = 0.26;
  torsoGroup.add(torsoMesh);

  const headGroup = new THREE.Group();
  headGroup.position.set(0, RIG_DIMS.headY, 0);
  torsoGroup.add(headGroup);
  const headMat = new THREE.MeshStandardMaterial({
    color: baseColor.clone().lerp(new THREE.Color(0xffffff), 0.25),
    roughness: 0.55,
  });
  const headMesh = new THREE.Mesh(new THREE.SphereGeometry(0.125, 16, 12), headMat);
  headMesh.position.y = RIG_DIMS.headCenterY;
  headGroup.add(headMesh);

  // Limbs need their own materials so we can tint them per FSM state
  // without affecting the torso/head. Upper + lower segments share one
  // material per limb.
  const armLMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const armRMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const legLMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const legRMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });

  type ArmJoints = { shoulder: THREE.Group; elbow: THREE.Group };
  function buildArm(sideX: number, mat: THREE.Material): ArmJoints {
    const shoulder = new THREE.Group();
    shoulder.position.set(sideX, RIG_DIMS.shoulderY, 0);
    // YXZ to match pose.ts's arm IK decomposition (yaw aims the bend
    // plane, pitch raises the arm within it).
    shoulder.rotation.order = "YXZ";
    torsoGroup.add(shoulder);
    shoulder.add(limbCapsule(0.055, 0.17, mat));
    const elbow = new THREE.Group();
    elbow.position.set(0, -RIG_DIMS.upperArm, 0);
    shoulder.add(elbow);
    elbow.add(limbCapsule(0.05, 0.15, mat));
    const hand = new THREE.Mesh(new THREE.SphereGeometry(0.06, 10, 8), mat);
    hand.position.y = -RIG_DIMS.foreArm;
    elbow.add(hand);
    return { shoulder, elbow };
  }

  type LegJoints = { hip: THREE.Group; knee: THREE.Group };
  function buildLeg(sideX: number, mat: THREE.Material): LegJoints {
    const hip = new THREE.Group();
    hip.position.set(sideX, RIG_DIMS.hipY, 0);
    // YXZ: yaw turns the knee's fold plane first, then pitch lifts the
    // thigh within it — matches pose.ts's solveLeg decomposition.
    hip.rotation.order = "YXZ";
    pelvis.add(hip);
    hip.add(limbCapsule(0.08, 0.22, mat));
    const knee = new THREE.Group();
    knee.position.set(0, -RIG_DIMS.thigh, 0);
    hip.add(knee);
    knee.add(limbCapsule(0.065, 0.21, mat));
    const foot = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.06, 0.18), mat);
    foot.position.set(0, -0.36, 0.05);
    knee.add(foot);
    return { hip, knee };
  }

  const armL = buildArm(-RIG_DIMS.shoulderX, armLMat);
  const armR = buildArm(RIG_DIMS.shoulderX, armRMat);
  const legL = buildLeg(-RIG_DIMS.hipX, legLMat);
  const legR = buildLeg(RIG_DIMS.hipX, legRMat);

  const breakTints: readonly THREE.Color[] = [
    baseColor.clone(),
    baseColor.clone().lerp(new THREE.Color(0xe9c878), 0.2),
    baseColor.clone().lerp(new THREE.Color(0xe9a85a), 0.4),
    baseColor.clone().lerp(new THREE.Color(0xc87a3a), 0.6),
    baseColor.clone().lerp(new THREE.Color(0x8a3a1a), 0.8),
  ];

  // Per-state colours. Picked so the player can recognise transitions at
  // a glance: REACH=cyan (moving), GRIP=yellow (engaged), PARRY=red,
  // RETRACT=dim, LOCKED=green (foot hook holding), UNLOCKED=base.
  const baseLimb = baseColor.clone();
  const stateColors: Readonly<Record<string, THREE.Color>> = Object.freeze({
    IDLE:      baseLimb.clone(),
    REACHING:  new THREE.Color(0x6fd0ff),
    CONTACT:   new THREE.Color(0xffffff),
    GRIPPED:   new THREE.Color(0xf2cf5c),
    PARRIED:   new THREE.Color(0xff6a4a),
    RETRACT:   baseLimb.clone().multiplyScalar(0.55),
    LOCKED:    new THREE.Color(0x7be0a0),
    UNLOCKED:  baseLimb.clone(),
    LOCKING:   new THREE.Color(0xc8e078),
  });
  const limbMats: Readonly<Record<string, THREE.MeshStandardMaterial>> = Object.freeze({
    armL: armLMat, armR: armRMat, legL: legLMat, legR: legRMat,
  });

  // --- Pose smoothing state ---
  const springs = new Map<string, Spring>();
  let lastPoseMs: number | null = null;

  function drive(
    key: string,
    target: number,
    dtS: number,
    tune: { freq: number; zeta: number },
  ): number {
    let s = springs.get(key);
    if (s === undefined) {
      s = { x: target, v: 0 };
      springs.set(key, s);
    }
    if (dtS <= 0) return s.x;
    return stepSpring(s, target, dtS, tune.freq, tune.zeta);
  }

  // High-frequency strain jitter, added after smoothing (a 5 Hz spring
  // would swallow a 13 Hz tremor). `phase` decorrelates limbs.
  function jitter(nowMs: number, amp: number, phase: number): number {
    if (amp <= 0) return 0;
    return Math.sin((nowMs / 1000) * 2 * Math.PI * 13 + phase) * amp * 0.06;
  }

  function applyArm(
    prefix: string,
    joints: ArmJoints,
    pose: ArmPose,
    sideSign: number,
    dtS: number,
    nowMs: number,
    phase: number,
  ): void {
    const tre = jitter(nowMs, pose.tremor, phase);
    joints.shoulder.rotation.x = drive(`${prefix}.sp`, pose.shoulderPitch, dtS, TUNE.arm) + tre;
    joints.shoulder.rotation.z = drive(`${prefix}.sr`, pose.shoulderRoll, dtS, TUNE.arm) * sideSign;
    joints.shoulder.rotation.y = drive(`${prefix}.sy`, pose.shoulderYaw, dtS, TUNE.arm) * sideSign;
    joints.elbow.rotation.x = -(drive(`${prefix}.eb`, pose.elbowBend, dtS, TUNE.arm) + tre * 1.5);
  }

  function applyLeg(
    prefix: string,
    joints: LegJoints,
    pose: LegPose,
    sideSign: number,
    dtS: number,
  ): void {
    joints.hip.rotation.x = drive(`${prefix}.hp`, pose.hipPitch, dtS, TUNE.leg);
    joints.hip.rotation.y = drive(`${prefix}.hy`, pose.hipYaw, dtS, TUNE.leg) * sideSign;
    joints.hip.rotation.z = drive(`${prefix}.hr`, pose.hipRoll, dtS, TUNE.leg) * sideSign;
    joints.knee.rotation.x = drive(`${prefix}.kb`, pose.kneeBend, dtS, TUNE.leg);
  }

  // Kick velocity into a spring channel (no-op until the channel exists,
  // i.e. before the first applyPose).
  function kick(key: string, dv: number): void {
    const sp = springs.get(key);
    if (sp !== undefined) sp.v += dv;
  }

  return {
    root,
    body: torsoMesh,
    shakeGroup,
    impulse(kind: ImpulseKind) {
      switch (kind) {
        case "ARM_L_FLUNG":
          kick("aL.sr", 9);
          kick("aL.sp", 3);
          kick("aL.eb", -5);
          break;
        case "ARM_R_FLUNG":
          kick("aR.sr", 9);
          kick("aR.sp", 3);
          kick("aR.eb", -5);
          break;
        case "ARM_L_RECOIL":
          kick("aL.sp", -4);
          kick("aL.eb", 6);
          break;
        case "ARM_R_RECOIL":
          kick("aR.sp", -4);
          kick("aR.eb", 6);
          break;
        case "TORSO_JERK":
          kick("to.p", 2.4);
          kick("hd.p", -1.5);
          break;
        case "TORSO_JOLT":
          kick("to.p", -1.6);
          kick("to.r", 2.0);
          break;
      }
    },
    setBreakBucket(bucket: number) {
      const idx = Math.max(0, Math.min(4, Math.floor(bucket)));
      material.color.copy(breakTints[idx]!);
    },
    setLimbState(limb, state) {
      const mat = limbMats[limb];
      const color = stateColors[state];
      if (mat === undefined || color === undefined) return;
      mat.color.copy(color);
    },
    applyPose(pose: BodyPose, nowMs: number) {
      const dtS =
        lastPoseMs === null ? 0 : Math.max(0, Math.min(0.1, (nowMs - lastPoseMs) / 1000));
      lastPoseMs = nowMs;

      const flip = mirrorXZ ? -1 : 1;
      pelvis.position.x = flip * drive("pv.x", pose.pelvisX, dtS, TUNE.pelvis);
      pelvis.position.y = drive("pv.y", pose.pelvisY, dtS, TUNE.pelvis);
      pelvis.position.z = flip * drive("pv.z", pose.pelvisZ, dtS, TUNE.pelvis);
      pelvis.rotation.x = drive("pv.p", pose.pelvisPitch, dtS, TUNE.pelvis);
      pelvis.rotation.y = drive("pv.yw", pose.pelvisYaw, dtS, TUNE.pelvis);
      pelvis.rotation.z = drive("pv.r", pose.pelvisRoll, dtS, TUNE.pelvis);

      const strain = jitter(nowMs, pose.torsoTremor, 1.7);
      torsoGroup.rotation.x = drive("to.p", pose.torsoPitch, dtS, TUNE.torso) + strain;
      torsoGroup.rotation.y = drive("to.y", pose.torsoYaw, dtS, TUNE.torso);
      torsoGroup.rotation.z = drive("to.r", pose.torsoRoll, dtS, TUNE.torso) + strain * 0.7;
      headGroup.rotation.x = drive("hd.p", pose.headPitch, dtS, TUNE.torso);
      headGroup.rotation.y = drive("hd.y", pose.headYaw, dtS, TUNE.torso);

      // Breathing: chest expands; raw oscillator, already smooth.
      const b = pose.breath * 0.5 + 0.5;
      torsoMesh.scale.set(1 + b * 0.04, 1, 1 + b * 0.06);

      applyArm("aL", armL, pose.armL, -1, dtS, nowMs, 0.0);
      applyArm("aR", armR, pose.armR, 1, dtS, nowMs, 2.4);
      applyLeg("lL", legL, pose.legL, -1, dtS);
      applyLeg("lR", legR, pose.legR, 1, dtS);
    },
  };
}
