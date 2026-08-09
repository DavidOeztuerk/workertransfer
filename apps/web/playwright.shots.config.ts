import { defineConfig, devices } from "@playwright/test";

/**
 * Getrennte Konfiguration für Screenshot-Aufnahmen — bewusst NICHT Teil von
 * `playwright.config.ts`.
 *
 * Der Grund ist ein Zähler: `scripts/validate.sh` liest die Zahl bestandener
 * „Reisen" aus dem Playwright-Protokoll, weil sich einmal alle 16 Reisen
 * stillschweigend übersprungen haben und der Bericht trotzdem „Alles grün"
 * sagte. Aufnahmen in derselben Suite würden diese Zahl aufblähen und damit
 * genau den Wächter entwerten, der daraus entstanden ist.
 *
 * Es gibt keine eingecheckten Referenzbilder und kein CI-Gate: Schriftglättung
 * und Plattform (darwin lokal, linux in der CI) erzeugen Fehlalarme, und die CI
 * fährt den Stapel gar nicht. Verglichen wird mit dem Auge, im PR.
 */
export default defineConfig({
  testDir: "./e2e-shots",
  // Die Aufnahmedateien heißen *.shots.ts, damit die Standard-testMatch der
  // Hauptkonfiguration sie nicht einsammelt.
  testMatch: /.*\.shots\.ts/,
  workers: 1,
  fullyParallel: false,
  reporter: [["list"]],
  timeout: 60_000,
  use: {
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    // Feste Bildgröße: ein Vergleich zweier Bilder unterschiedlicher Breite
    // zeigt Umbrüche, nicht Farben.
    ...devices["Desktop Chrome"],
    viewport: { width: 1280, height: 900 },
  },
});
