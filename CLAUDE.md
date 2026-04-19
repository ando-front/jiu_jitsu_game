# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

This repo is a realistic BJJ simulator (see [README.md](README.md)) in the **design-plus-logic-prototype phase**. Design is substantially complete across four documents (input systems, state machines, M0 paper prototype). The codebase is entering a **two-stage implementation strategy** (Option Y):

1. **Stage 1 — HTML/TypeScript logic prototype** in [src/prototype/web/](src/prototype/web/). Purpose: validate the input-system → state-machine → judgment-window logic in a fast-iteration, browser-debuggable form. Blockman-level visuals only. Targets design-level bugs, not M1 grip-fight feel.
2. **Stage 2 — Unreal Engine 5 production build** in [src/prototype/ue5/](src/prototype/ue5/) (not yet scaffolded). Purpose: realise Visual Pillar Document requirements (photoreal characters, SSS, Gi cloth sim, Lumen, HUD-less color-grading state conveyance). Logic is ported from Stage 1 C++-style TypeScript, visuals are built fresh.

The HTML prototype is **deliberately disposable**: its code exists to shake out logic, not to ship. Do not invest in Three.js visual fidelity beyond what is needed to drive the input-system tests. The M1 grip-fight-feel gate (15/20 testers) can only be judged in Stage 2, not Stage 1.

## Stage 1 stack (HTML prototype)

- **Runtime**: Node.js v22 LTS, browsers (Chrome/Edge for Gamepad API)
- **Language**: TypeScript (strict mode). Write state machines and input transforms in a style that ports cleanly to UE5 C++ — prefer tagged unions / pure functions / no framework-specific idioms
- **Build**: Vite (HMR-first dev loop)
- **3D**: Three.js (blockman primitives only)
- **Tests**: Vitest for pure logic units; scenario tests for FSM transitions per [state_machines_v1.md §11](docs/design/state_machines_v1.md)
- **Package manager**: npm (pnpm not installed on this machine)

All Stage-1 code lives in [src/prototype/web/](src/prototype/web/). Node artifacts (`node_modules/`, `dist/`) are gitignored.

## Architecture intent (from design docs)

The product is a hybrid action game + cognitive-training tool for jiu-jitsu practitioners. These pillars constrain implementation and should be respected even at the Stage 1 prototype level where feasible:

- **Camera is over-the-shoulder / guard-side immersive**, not a fighting-game side view. Input mappings, animation blending, and readability decisions all flow from this.
- **Invisible game state is conveyed through color grading, not HUD meters.** In Stage 2 this is hard-enforced. In Stage 1 a debug HUD is acceptable and expected (it's a logic prototype), but do not design gameplay state-readout mechanisms that only work via HUD — they must have a color-grading analog planned for Stage 2.
- **Tournament realism (IBJJF/ADCC) + cinematic decision windows** is the tone target. Mechanics map to real BJJ decisions, not arcade abstractions.
- **Playtester-validation milestone gates**: M0 (paper, 青帯3/3 + 白帯2/3 per [m0_protocol_v1.md §0](docs/m0_paper_prototype/m0_protocol_v1.md)), M1 (15/20 grip-fight feel — Stage 2 only), M2 (60% weekly-play intent). Features exist to serve the next gate; when scoping work, ask which milestone and which stage it's for.

## Design documents (authoritative for logic)

These documents are the source of truth. Implementation must match them; if divergence is needed, update the doc first.

- [docs/design/input_system_v1.md](docs/design/input_system_v1.md) — attacker-side input, 4-layer pipeline (A Input / B Intent / C Animation Driver / D Judgment Window)
- [docs/design/state_machines_v1.md](docs/design/state_machines_v1.md) — HandFSM, FootFSM, posture break (2D vector), stamina, guard FSM, judgment window FSM, control layer
- [docs/design/input_system_defense_v1.md](docs/design/input_system_defense_v1.md) — defender-side input, shared pad with contextual button meanings
- [docs/m0_paper_prototype/m0_protocol_v1.md](docs/m0_paper_prototype/m0_protocol_v1.md) — M0 session protocol (not yet executed)

## Repo layout conventions

- [docs/design/](docs/design/) — game design docs. New design notes go here.
- [docs/m0_paper_prototype/](docs/m0_paper_prototype/) — paper-prototype materials for M0.
- [docs/visual/](docs/visual/) — Visual Pillar Document, art direction reference.
- [src/prototype/web/](src/prototype/web/) — Stage 1 HTML/TypeScript logic prototype.
- [src/prototype/ue5/](src/prototype/ue5/) — Stage 2 UE5 project (not yet scaffolded; requires RTX-class GPU + 32GB RAM + ~100GB free).
- [assets/mocap/](assets/mocap/), [assets/textures/](assets/textures/), [assets/audio/](assets/audio/) — large binaries. `.gitignore` has commented-out `*.fbx` / `*.uasset` / `*.umap` entries; enable them and switch to **Git LFS** before committing binaries.
- [tests/](tests/) — cross-cutting test scripts / validation tools. Stage-1 Vitest tests live next to the code in `src/prototype/web/`, not here.

## Working on Stage 1

- Dev server: `npm run dev` in `src/prototype/web/` (Vite, port 5173 default)
- Tests: `npm test` (Vitest watch), `npm run test:run` (single pass)
- Build: `npm run build` (for deploy check only; Stage 1 is not deployed)
- Lint/typecheck: `npm run typecheck` (tsc --noEmit)

When adding logic: match the layer structure in [input_system_v1.md](docs/design/input_system_v1.md) exactly — A→B→C→D unidirectional data flow, one struct per layer boundary. This is the structure that will be ported to UE5 C++ unchanged.
