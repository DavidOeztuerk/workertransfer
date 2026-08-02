import { defineConfig, devices } from "@playwright/test";

// E2E läuft gegen den echten Stack aus `docker compose up`, nicht gegen einen
// eigenen Dev-Server. Es gibt hier bewusst kein `webServer`: die Reise, die
// geprüft wird, geht über drei Dienste, eine Datenbank und einen Mailserver —
// eine halbe Umgebung hochzufahren würde genau die Integration wegabstrahieren,
// derentwegen diese Tests existieren.
//
// Steht der Stack nicht, überspringen sich die Tests selbst (siehe e2e/stack.ts,
// dasselbe Muster wie ADR-0011 für die Python-Integrationstests): `make check`
// und CI bleiben grün, ohne dass eine Lücke als Erfolg durchgeht.

export default defineConfig({
  testDir: "./e2e",
  // Die Reise teilt sich einen Consent-Ledger und einen Mailserver; parallel
  // gelesene Postfächer würden sich gegenseitig die Mails wegschnappen.
  workers: 1,
  fullyParallel: false,
  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],
  // Großzügig, weil eine Reise viel echte Arbeit ist: zwei Registrierungen
  // mit bcrypt, zwei Mail-Zustellungen samt Abholen, Unternehmensanlage,
  // Wechsel, und Aufrufe über vier Dienste. 60 s reichten im guten Fall und
  // machten aus normaler Schwankung unter Docker-auf-macOS ein Rot, das
  // nichts über den Code aussagt.
  timeout: 150_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
