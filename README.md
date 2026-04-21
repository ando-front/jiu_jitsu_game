# Jiu-Jitsu Simulator

Realistic BJJ action game that doubles as cognitive training for practitioners.

## Current State (2026-04-21)

**Stage 1 (HTML/TypeScript logic prototype) is feature-complete**: all 6 M1
techniques fire end-to-end, attacker + defender input pipelines are wired,
an opponent AI plays both roles with full commit authority, stamina color
grading matches the Visual Pillar requirement, and **268 tests pass**.

- Live dev: `cd src/prototype/web && npm install && npm run dev` → `http://localhost:5173`
- Role select: **BOTTOM / TOP / SPECTATE** (AI vs AI). Scenario presets on digit keys `1..7`.
- Japanese in-game tutorial (`H` key).

**M0 paper-prototype materials** are ready (technique cards, GM quickref,
session log, interview sheet). Running the sessions is a human task —
青帯3名 + 白帯3名をリクルートして実施。See
[docs/m0_paper_prototype/README.md](docs/m0_paper_prototype/README.md).

**Stage 2 (UE5)** is not yet scaffolded. The file-by-file TS → C++ port
plan is captured in [docs/design/stage2_port_plan_v1.md](docs/design/stage2_port_plan_v1.md).

## Project Structure

```
docs/
  design/              # Game design docs (source of truth for logic)
    architecture_overview_v1.md     # Module layout, pure/platform split
    input_system_v1.md              # Attacker input pipeline (A/B/C/D)
    input_system_defense_v1.md      # Defender input
    state_machines_v1.md            # HandFSM, FootFSM, posture_break, ...
    opponent_ai_v1.md               # AI priority tables
    stage2_port_plan_v1.md          # TS → UE5 C++ port map
    stage1_implementation_notes_v1.md  # Stage 1-time design decisions
  visual/              # Visual Pillar Document, art direction
  m0_paper_prototype/  # M0 session protocol + printable materials
src/
  prototype/
    web/               # Stage 1 HTML/TypeScript prototype (Vite + Three.js + Vitest)
    ue5/               # Stage 2 UE5 project (README only; not scaffolded)
assets/
  mocap/, textures/, audio/   # Reserved for large binaries; empty today.
                              # LFS is NOT yet enabled (no .gitattributes).
                              # Run `git lfs install` + `git lfs track` before
                              # first binary commit. Setup steps live in
                              # src/prototype/ue5/README.md §2.2.
tests/                 # Cross-cutting test scripts
```

## Milestones

| Gate | Criteria | Status |
|------|----------|--------|
| **M0** | 青帯3/3 + 白帯2/3 validate the decision space (paper prototype) | Session materials ready. Sessions not yet run. |
| **M1** | 15/20 testers confirm grip-fight feel (Stage 2, UE5) | Blocked on Stage 2 scaffold. |
| **M2** | 60%+ want to play weekly (post-M1) | Future. |

## Key Design Decisions

- **Characters**: Photorealistic (Undisputed / EA UFC tier) — Stage 2 only
- **Camera**: Over-the-shoulder immersive (player/guard side)
- **Tone**: IBJJF/ADCC tournament realism + cinematic decision windows (判断窓)
- **Invisible info**: Conveyed through color grading, not HUD meters (Visual Pillar §2.4)
- **Engine**: Unreal Engine 5 (Stage 2)
- **Two-stage strategy**: Stage 1 proves logic in TypeScript; Stage 2 realises visuals in UE5

See [docs/visual/Visual_Pillar_Document_v1.docx](docs/visual/Visual_Pillar_Document_v1.docx) for the full visual direction.

## Stage 1 Quickstart

```bash
cd src/prototype/web
npm install
npm run dev       # Vite dev server, port 5173
npm run test:run  # Vitest single pass
npm run typecheck # tsc --noEmit
```

In the browser:
- **Role select**: LS left/right cycles BOTTOM → TOP → SPECTATE. A/Space to start.
- **H**: open/close the 日本語 tutorial
- **Esc**: pause / resume
- **1..7**: load a practice scenario; **0**: reset to neutral
- Controls: WASD (L-stick), Arrow keys (R-stick), F/J (triggers), R/U (bumpers),
  Space (A/Base), X (B/Release), C (Y/Breath), V (X/Pass commit)

Full control map + game concepts in the in-game tutorial (H key).

## Contributing / AI agent context

[CLAUDE.md](CLAUDE.md) has the canonical project-state brief for AI coding
agents. Design docs are the source of truth; divergence in implementation
requires updating the doc first.
