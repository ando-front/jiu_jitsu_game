/// <reference types="vitest" />
import { defineConfig } from "vite";

export default defineConfig({
  root: ".",
  server: {
    port: 5173,
    strictPort: false,
  },
  build: {
    outDir: "dist",
    target: "es2022",
  },
  test: {
    globals: true,
    environment: "node",
    include: ["tests/**/*.test.ts"],
    // Unit and scenario tests are both picked up by the ** glob above.
    // The split is purely organisational — tests/unit/* cover single-
    // module pure logic, tests/scenario/* cover multi-module flows via
    // stepSimulation. See docs/design/architecture_overview_v1.md §6.
  },
});
