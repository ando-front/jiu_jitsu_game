// PLATFORM — minimal Three.js scene: bottom actor (player) + top actor
// (opponent) + ground + vignette overlay for the judgment window. Visuals
// are intentionally placeholder; the scene exists to give Stage 1 a
// feedback surface so state flows can be verified end-to-end.

import * as THREE from "three";

export interface Scene3D {
  renderer: THREE.WebGLRenderer;
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  bottom: BlockmanRig;
  top: BlockmanRig;
  // Tint applied to the whole scene when the judgment window is active.
  setWindowTint(strength: number): void;
  // Initiative cue: shifts camera position and overall saturation.
  setInitiative(tint: InitiativeTint): void;
  resize(w: number, h: number): void;
  render(): void;
}

export type InitiativeTint = "Bottom" | "Top" | "Neutral";

export interface BlockmanRig {
  root: THREE.Group;
  body: THREE.Mesh;
  // Per-actor tint base — derived from posture_break bucket when this rig
  // is the opponent; the player rig keeps a fixed colour in Stage 1.
  setBreakBucket(bucket: number): void;
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

  // Player (bottom) rig — blue team.
  const bottom = buildBlockman(new THREE.Color(0x5a8cff));
  bottom.root.position.set(0, 0, 0);
  scene.add(bottom.root);

  // Opponent (top) rig — neutral beige, tints to warm on posture break.
  const top = buildBlockman(new THREE.Color(0xc9b48a));
  top.root.position.set(0, 0, -1.1);
  // Opponent sits on top of player in closed guard; rotate 180 and raise
  // slightly so the blockman appears "kneeling".
  top.root.rotation.y = Math.PI;
  scene.add(top.root);

  // Full-screen quad for the judgment window vignette. Separate scene so
  // it always renders on top without depth writes.
  const overlayScene = new THREE.Scene();
  const overlayCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  const overlayMaterial = new THREE.ShaderMaterial({
    transparent: true,
    depthTest: false,
    depthWrite: false,
    uniforms: {
      uStrength: { value: 0 },
      uColor: { value: new THREE.Color(0xfff2d0) },
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
      uniform float uStrength;
      uniform vec3 uColor;
      void main() {
        vec2 c = vUv - 0.5;
        float r = length(c);
        // Vignette + warm wash. Stronger radial falloff outside 0.35.
        float vignette = smoothstep(0.35, 0.8, r) * uStrength;
        float wash = uStrength * 0.15;
        vec3 rgb = mix(vec3(0.0), uColor, wash);
        gl_FragColor = vec4(rgb, vignette);
      }
    `,
  });
  const overlayQuad = new THREE.Mesh(
    new THREE.PlaneGeometry(2, 2),
    overlayMaterial,
  );
  overlayScene.add(overlayQuad);

  // Ambient saturation cue via a subtle hemisphere-light colour shift.
  const initiativeLight = new THREE.HemisphereLight(0xffffff, 0x0, 0.2);
  scene.add(initiativeLight);

  return {
    renderer,
    scene,
    camera,
    bottom,
    top,
    setWindowTint(strength: number) {
      overlayMaterial.uniforms.uStrength!.value = Math.max(0, Math.min(1, strength));
    },
    setInitiative(tint: InitiativeTint) {
      // Camera z nudges slightly forward on "Bottom" (attacker view) and
      // back on "Top" (defender view). Matches §7.3 design note.
      const baseZ = 2.6;
      const dz = tint === "Bottom" ? -0.2 : tint === "Top" ? 0.2 : 0;
      camera.position.z = baseZ + dz;
      // Hemisphere top-colour nudges slightly warm on Bottom, cool on Top.
      const warm = 0xffe8c4;
      const cool = 0xbfd4ff;
      const neutral = 0xffffff;
      const hex = tint === "Bottom" ? warm : tint === "Top" ? cool : neutral;
      initiativeLight.color.setHex(hex);
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

  const armL = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.58, 0.14), material);
  armL.position.set(-0.36, 1.2, 0);
  root.add(armL);
  const armR = armL.clone();
  armR.position.x = 0.36;
  root.add(armR);

  const legL = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.72, 0.18), material);
  legL.position.set(-0.13, 0.46, 0);
  root.add(legL);
  const legR = legL.clone();
  legR.position.x = 0.13;
  root.add(legR);

  // Break-bucket tint: bucket 0 = base colour, bucket 4 = heavily warm-shifted
  // and desaturated — a "this opponent is getting dumped" visual.
  const breakTints: readonly THREE.Color[] = [
    baseColor.clone(),
    baseColor.clone().lerp(new THREE.Color(0xe9c878), 0.2),
    baseColor.clone().lerp(new THREE.Color(0xe9a85a), 0.4),
    baseColor.clone().lerp(new THREE.Color(0xc87a3a), 0.6),
    baseColor.clone().lerp(new THREE.Color(0x8a3a1a), 0.8),
  ];

  return {
    root,
    body: torso,
    setBreakBucket(bucket: number) {
      const idx = Math.max(0, Math.min(4, Math.floor(bucket)));
      material.color.copy(breakTints[idx]!);
    },
  };
}
