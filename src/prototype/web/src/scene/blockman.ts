// Minimal Three.js scene — one blockman + ground. Visuals are intentionally
// placeholder. The purpose here is to have something on screen that moves
// in response to input so Layer A can be verified end-to-end in a browser.

import * as THREE from "three";

export interface Scene3D {
  renderer: THREE.WebGLRenderer;
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  blockman: THREE.Group;
  resize(w: number, h: number): void;
  render(): void;
}

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

  const blockman = buildBlockman();
  scene.add(blockman);

  return {
    renderer,
    scene,
    camera,
    blockman,
    resize(w, h) {
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
    },
    render() {
      renderer.render(scene, camera);
    },
  };
}

function buildBlockman(): THREE.Group {
  const g = new THREE.Group();
  const mat = new THREE.MeshStandardMaterial({ color: 0x5a8cff, roughness: 0.6 });

  const torso = new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.7, 0.28), mat);
  torso.position.y = 1.15;
  g.add(torso);

  const head = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.28, 0.28), mat);
  head.position.y = 1.65;
  g.add(head);

  const armL = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.58, 0.14), mat);
  armL.position.set(-0.36, 1.2, 0);
  g.add(armL);
  const armR = armL.clone();
  armR.position.x = 0.36;
  g.add(armR);

  const legL = new THREE.Mesh(new THREE.BoxGeometry(0.18, 0.72, 0.18), mat);
  legL.position.set(-0.13, 0.46, 0);
  g.add(legL);
  const legR = legL.clone();
  legR.position.x = 0.13;
  g.add(legR);

  return g;
}
