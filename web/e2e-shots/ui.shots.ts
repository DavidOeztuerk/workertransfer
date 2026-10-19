import { expect, test } from "@playwright/test";

import { missingService } from "../e2e/stack";

/**
 * Captures the publicly reachable pages — once before a change, once after.
 * The images belong in the PR description, not in the repository.
 *
 *   docker compose up -d
 *   SHOT_TAG=after pnpm shots          # in web/
 *
 * For the BEFORE state a `git stash` is not enough once the change is
 * committed. You need a worktree on `origin/develop`:
 *
 *   git worktree add ../wt-before origin/develop
 *   cd ../wt-before/web && pnpm install
 *   # run the stack there and capture with SHOT_TAG=before
 *
 * Only four pages, and all four reachable without signing in: hero, auth
 * panel, cards, buttons, fields and a select are covered by them. A signed-in
 * page would have cost a registration including a mail fetch per run.
 *
 * KNOWN GAP: everything behind sign-in is NOT in the picture. Whoever needs it
 * takes `login()` and `registerAndConfirm()` from `../e2e/stack`.
 */
const TAG = process.env.SHOT_TAG ?? "current";

const PAGES: { name: string; path: string }[] = [
  { name: "home", path: "/" },
  { name: "login", path: "/login" },
  { name: "register", path: "/register" },
  // Public, and the only one of the four with a select and cards.
  { name: "jobs", path: "/jobs" },
];

test.describe("UI screenshots", () => {
  test.beforeAll(async () => {
    const missing = await missingService();
    test.skip(missing !== null, `stack is not up: ${missing}`);
  });

  // The error state stands on none of the four pages, but it was the only
  // visible change of E2 (.auth__alert -> <Alert>). Without this image exactly
  // the one change there is to document would go undocumented.
  test(`login-error (${TAG})`, async ({ page }) => {
    await page.goto("/login", { waitUntil: "networkidle" });
    await page.getByLabel("E-Mail").fill("nobody@example.invalid");
    await page.getByLabel("Passwort").fill("wrong-password");
    await page.getByRole("button", { name: "Anmelden" }).click();
    // Wait for the message, not for a span of time: otherwise the image shows
    // the state before it, depending on the response time.
    await expect(page.getByRole("alert")).toBeVisible();
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: `.screenshots/${TAG}/login-error.png`, fullPage: true });
  });

  for (const target of PAGES) {
    test(`${target.name} (${TAG})`, async ({ page }) => {
      // The direct link, not a click: only typing the address really asks the
      // server that serves the UI (the Vite dev server since ADR-0040). A click
      // switches inside the browser and proves nothing.
      // `networkidle` and not the default: the header shows its links only once
      // the session request has answered. Without the wait the image was taken
      // under load BEFORE that answer — the same page then looked different
      // depending on machine load, and comparing two such images proves nothing.
      await page.goto(target.path, { waitUntil: "networkidle" });
      // An <h1> is the evidence that the page came and not raw JSON.
      await expect(page.locator("h1").first()).toBeVisible();
      // Fonts: an image taken before the font loads shows a different text width
      // and therefore different line breaks.
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({ path: `.screenshots/${TAG}/${target.name}.png`, fullPage: true });
    });
  }
});
