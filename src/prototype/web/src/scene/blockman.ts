// PLATFORM — Three.js scene for Stage 1. Renders two blockmen + ground,
// an overlay vignette for judgment windows, and transient event pulses
// (flash / shake / tint) triggered by SimEvents.
//
// The Stage 1 visual vocabulary is intentionally narrow: placeholder
// geometry, placeholder colours. What matters is that every gameplay
// event has a perceptible cue, so logic regressions are obvious during
// manual testing.

import * as THREE from "three";

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
  // Transient pulses triggered by sim events. The caller fires-and-forgets
  // these; the scene owns the decay animation.
  pulseFlash(color: THREE.ColorRepresentation, durationMs?: number): void;
  pulseShake(rig: "bottom" | "top", amplitude?: number, durationMs?: number): void;
  updatePulses(realDtMs: number): void;
  resize(w: number, h: number): void;
  render(): void;
}

export type InitiativeTint = "Bottom" | "Top" | "Neutral";

export interface BlockmanRig {
  root: THREE.Group;
  body: THREE.Mesh;
  setBreakBucket(bucket: number): void;
  // §D3 — colour limbs by FSM state so input → state changes are visible
  // even though we don't animate joints. State enum strings come from
  // [hand_fsm.ts] / [foot_fsm.ts]; "IDLE" reverts to base body colour.
  setLimbState(limb: "armL" | "armR" | "legL" | "legR", state: string): void;
}

// -----------------------------------------------------------------------------

export function createScene(canvas: HTMLCanvasElement): Scene3D {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x15161a);

  const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 100);
  camera.position.set(0, 1.4, 2.6);
  camera.lookAt(0, 1.0, 0);

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

  const bottom = buildBlockman(new THREE.Color(0x5a8cff));
  bottom.root.position.set(0, 0, 0);
  scene.add(bottom.root);

  const top = buildBlockman(new THREE.Color(0xc9b48a));
  top.root.position.set(0, 0, -1.1);
  top.root.rotation.y = Math.PI;
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

  // Shake: randomised per-frame offset on a rig root, decays exponentially.
  type Shake = { remainingMs: number; totalMs: number; amplitude: number; rig: "bottom" | "top" };
  const shakes: Shake[] = [];

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
      const baseZ = 2.6;
      const dz = tint === "Bottom" ? -0.2 : tint === "Top" ? 0.2 : 0;
      camera.position.z = baseZ + dz;
      const warm = 0xffe8c4;
      const cool = 0xbfd4ff;
      const neutral = 0xffffff;
      const hex = tint === "Bottom" ? warm : tint === "Top" ? cool : neutral;
      initiativeLight.color.setHex(hex);
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

      // Shake — we overwrite each rig's base position offset every frame
      // with the current total shake vector. The caller's scene-apply
      // step sets the "base" position first; our shake offset is added
      // on top via a small extra group in the future. For Stage 1 we
      // just nudge the rig's root.position by a randomised delta.
      for (let i = shakes.length - 1; i >= 0; i -= 1) {
        const s = shakes[i]!;
        s.remainingMs -= realDtMs;
        if (s.remainingMs <= 0) {
          shakes.splice(i, 1);
          continue;
        }
        const t = s.remainingMs / s.totalMs;
        const rig = s.rig === "bottom" ? bottom : top;
        rig.root.position.x += (Math.random() - 0.5) * s.amplitude * t;
        rig.root.position.y += (Math.random() - 0.5) * s.amplitude * t * 0.3;
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

function buildBlockman(baseColor: THREE.Color): BlockmanRig {
  const root = new THREE.Group();
  const material = new THREE.MeshStandardMaterial({
    color: baseColor.clone(),
    roughness: 0.6,
  });

  const torso = new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.7, 0.28), material);
  torso.position.y = 1.15;
  root.add(torso);

  const head = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.28, 0.28), material);
  head.position.y = 1.65;
  root.add(head);

  // Limbs need their own materials so we can tint them per FSM state
  // without affecting the torso/head.
  const armLMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const armRMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const legLMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });
  const legRMat = new THREE.MeshStandardMaterial({ color: baseColor.clone(), roughness: 0.6 });

  const armL = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.58, 0.14), armLMat);
  armL.position.set(-0.36, 1.2, 0);
  root.add(armL);
  const armR = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.58, 0.14), armRMat);
  armR.position.set(0.36, 1.2, 0);
  root.add(armR);

  const legL = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.72, 0.18), legLMat);
  legL.position.set(-0.13, 0.46, 0);
  root.add(legL);
  const legR = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.72, 0.18), legRMat);
  legR.position.set(0.13, 0.46, 0);
  root.add(legR);

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

  return {
    root,
    body: torso,
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
  };
}
