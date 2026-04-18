# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

This repo is currently a **pre-code design skeleton** for a realistic BJJ simulator (see [README.md](README.md)). All source/asset/test directories exist only as `.gitkeep` placeholders — there is no build system, no test runner, no source code, and no engine project committed yet. The only substantive content is [docs/visual/Visual_Pillar_Document_v1.docx](docs/visual/Visual_Pillar_Document_v1.docx).

When asked to add code, first confirm with the user which engine/toolchain to scaffold into ([src/prototype/](src/prototype/)) — the README lists Unreal Engine 5 as recommended-but-conditional, and `.gitignore` is pre-configured for **both Unreal and Unity** (plus Python and Node), so the choice has not been locked in.

## Architecture intent (from design docs, not yet implemented)

The product is positioned as a hybrid action game + cognitive-training tool for jiu-jitsu practitioners. Several design pillars constrain how future systems should be built and should be respected when proposing implementations:

- **Camera is over-the-shoulder / guard-side immersive**, not a fighting-game side view. Input mappings, animation blending, and readability decisions all flow from this.
- **Invisible game state is conveyed through color grading, not HUD meters.** Avoid proposing health bars, stamina bars, or floating indicators — surface state through post-process / lighting instead.
- **Tournament realism (IBJJF/ADCC) + cinematic decision windows** is the tone target. Mechanics should map to real BJJ decisions, not arcade abstractions.
- Milestone gates are **playtester-validation-based** (M0: 4/5 practitioners validate decision space; M1: 15/20 confirm grip-fight feel; M2: 60% want to play weekly). Features exist to serve the next gate — when scoping work, ask which milestone it's for.

## Repo layout conventions

- [docs/design/](docs/design/) — game design docs (input system, state machines). New design notes go here.
- [docs/m0_paper_prototype/](docs/m0_paper_prototype/) — paper-prototype materials for the M0 milestone specifically.
- [docs/visual/](docs/visual/) — visual pillar / art direction reference.
- [src/prototype/](src/prototype/) — engine prototype(s) live here once scaffolded.
- [assets/mocap/](assets/mocap/), [assets/textures/](assets/textures/), [assets/audio/](assets/audio/) — large binaries belong here. The `.gitignore` has commented-out entries for `*.fbx` / `*.uasset` / `*.umap`; if these grow, switch them on and move binaries to **Git LFS** rather than committing them directly.
- [tests/](tests/) — test scripts and validation tools (currently empty).
