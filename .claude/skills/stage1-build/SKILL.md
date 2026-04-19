---
name: stage1-build
description: Runs `npm run typecheck` and `npm run test:run` for the Stage 1 web prototype in src/prototype/web/ and summarises the result. Use after any Stage 1 code change to verify nothing broke.
---

# Stage 1 build check

Runs the Stage 1 prototype's gate checks in sequence and returns a compact
summary. Use this whenever you've edited anything under
`src/prototype/web/` and want to confirm the tree is still green before
reporting back to the user.

## Steps

1. `cd src/prototype/web` and run:
   ```bash
   npm run typecheck
   ```
   If typecheck fails, STOP and report the first error. Do not proceed to
   tests — a type error almost always invalidates the test run.

2. If typecheck passes, run:
   ```bash
   npm run test:run
   ```

3. Parse the Vitest output and produce a 3-line summary:
   ```
   typecheck: OK
   tests: <passed>/<total> passed (<failed> failed)
   duration: <ms>ms
   ```
   If anything failed, append the test names that failed and the first
   assertion message per failure.

## Notes

- The prototype lives in `c:/Users/angie/git/jiu_jitsu_game/src/prototype/web/`.
  All commands run from there.
- Use `cd` inline (`cd path && npm run ...`) — do NOT rely on persistent
  shell state, because parallel tool calls can reset the cwd.
- Do not attempt `npm run dev`. That's for manual browser verification
  and runs forever; it won't fit this skill's one-shot contract.
- Test names map 1:1 to `describe()` blocks. When reporting failures,
  keep the §doc citation they contain (the design docs are the source of
  truth for what the tests mean).
