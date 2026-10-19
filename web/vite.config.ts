/// <reference types="vitest/config" />
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    // Only src/: e2e/ belongs to Playwright. Vitest's default would collect
    // the .spec.ts files there and fail on `import { test } from
    // "@playwright/test"`.
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
    // 5 seconds (the default) are enough for the assertion, but not always for
    // what happens before it: under load — a running docker compose stack, a
    // Playwright journey in parallel — transforming and importing single
    // modules takes longer than the whole test may. The result was one run
    // with 28 red tests, the next with 18, the one after with one — all at
    // changing places, all with "Test timed out in 5000ms", and all green as
    // soon as the machine had air.
    //
    // A test suite whose result depends on machine load is none. The higher
    // limit slows no green run down: it only takes effect where the run used
    // to abort.
    testTimeout: 20_000,
  },
});
