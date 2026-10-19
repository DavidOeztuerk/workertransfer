import { defineConfig, devices } from "@playwright/test";

// E2E runs against the real stack from `docker compose up`, not against a dev
// server of its own. There is deliberately no `webServer` here: the journey
// under test crosses three services, a database and a mail server — bringing
// up half an environment would abstract away exactly the integration these
// tests exist for.
//
// If the stack is not up, the tests skip themselves (see e2e/stack.ts, the
// same pattern ADR-0011 set for the integration tests): `make check` and CI
// stay green without a gap passing as a success.

export default defineConfig({
  testDir: "./e2e",
  // The journeys share one consent ledger and one mail server; mailboxes read
  // in parallel would snatch each other's mails.
  workers: 1,
  fullyParallel: false,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],
  // Generous, because a journey is a lot of real work: two registrations with
  // bcrypt, two mail deliveries including the fetch, creating a company,
  // switching, and calls across four services. 60 s were enough in the good
  // case and turned normal variance under Docker-on-macOS into a red that
  // says nothing about the code.
  timeout: 150_000,
  // 30 s, not 15: measured, about every third run fails at sign-in when eleven
  // containers and the Vite dev server run on the same machine at once. The
  // operation itself takes under a second; it is waiting for a busy machine,
  // not slowness in the code. This is a tolerance and not a repair — it stands
  // here so that nobody concludes from the green run that something was fixed.
  expect: { timeout: 30_000 },
  // One retry — declared as a TOLERANCE, not as a repair. Measured, in a run
  // over the whole suite (about five minutes, eleven containers, Vite and
  // Chromium on one machine) about one test fails at SIGN-IN: the link
  // "Mein Profil" does not appear because the session is not up in time. The
  // same test alone passes in 2.4 seconds.
  //
  // The danger is known: a retry can hide a real, sporadic fault. That is why
  // a retried test stays in the report as "flaky", and `scripts/validate.sh`
  // names the number — a green run with three wobbly candidates is not a green
  // run, just as one with twenty skips is none.
  retries: 1,
  use: {
    // Without this limit an action inherits the whole test's time limit: a
    // fill() on a field that does not exist on this page then burns the entire
    // budget and in the end reports only "Test timeout", without saying where.
    // That is exactly how a navigation fault hid in this journey for seven
    // minutes.
    actionTimeout: 15_000,

    // The language is pinned, and that is not convenience.
    //
    // Without this line Chromium runs with `en-US`, the UI follows the device
    // (ADR-0031) and EVERY journey that looks for a German text fails — on this
    // machine. On the next person's machine with a German system it would be
    // green. A test whose result depends on the environment says nothing.
    //
    // That switching really works is checked by EXACTLY ONE journey, and it
    // switches itself: `language-journey.spec.ts`. The others check their own
    // matter and should not be bothered by the language.
    locale: "de-DE",
    // The UI's own door on :5173 — the same port in compose and in the chart.
    // Since ADR-0040 the gateway serves no UI; a journey pointed at :8090
    // would find only the API there.
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
