# Jiu-Jitsu Simulator

Realistic BJJ action game that doubles as cognitive training for practitioners.

## Project Structure

```
docs/
  visual/          # Visual Pillar Document and art direction references
  design/          # Game design documents (input system, state machines, etc.)
  m0_paper_prototype/  # Paper prototype materials for M0 milestone
src/
  prototype/       # Engine prototypes (UE5 / Unity)
assets/
  mocap/           # Motion capture data
  textures/        # Character / Gi / environment textures
  audio/           # Sound effects, ambient audio
tests/             # Test scripts and validation tools
```

## Milestones

| Milestone | Criteria | Status |
|-----------|----------|--------|
| M0 | 4/5 BJJ practitioners validate the decision space | Pending |
| M1 | 15/20 testers confirm grip fight feel | Pending |
| M2 | 60%+ want to play weekly | Pending |

## Key Design Decisions

- **Characters**: Photorealistic (Undisputed / EA UFC tier)
- **Camera**: Over-the-shoulder immersive (player/guard side)
- **Tone**: IBJJF/ADCC tournament realism + cinematic decision windows
- **Invisible info**: Conveyed through color grading, not HUD meters
- **Engine**: Unreal Engine 5 (recommended, conditional)

See `docs/visual/Visual_Pillar_Document_v1.docx` for the full visual direction.
