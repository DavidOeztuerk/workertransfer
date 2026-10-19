import { defineConfig, devices } from "@playwright/test";

/**
 * A separate configuration for screenshots — deliberately NOT part of
 * `playwright.config.ts`.
 *
 * The reason is a counter: `scripts/validate.sh` reads the number of passed
 * journeys from the Playwright log, because once all 16 journeys silently
 * skipped themselves and the report still said "all green". Screenshots in the
 * same suite would inflate that number and thereby devalue exactly the guard
 * that came out of it.
 *
 * There are no checked-in reference images and no CI gate: font smoothing and
 * platform (darwin locally, linux in CI) produce false alarms, and CI does not
 * run this stack. The comparison is by eye, in the PR.
 */
export default defineConfig({
  testDir: "./e2e-shots",
  // The screenshot files are called *.shots.ts so that the main
  // configuration's default testMatch does not collect them.
  testMatch: /.*\.shots\.ts/,
  workers: 1,
  fullyParallel: false,
  reporter: [["list"]],
  timeout: 60_000,
  use: {
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    // A fixed image size: comparing two images of different width shows line
    // breaks, not colours.
    ...devices["Desktop Chrome"],
    viewport: { width: 1280, height: 900 },
  },
});
