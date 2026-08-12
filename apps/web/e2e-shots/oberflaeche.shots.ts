import { expect, test } from "@playwright/test";

import { missingService } from "../e2e/stack";

/**
 * Nimmt die öffentlich erreichbaren Seiten auf — einmal vor einer Umstellung,
 * einmal danach. Die Bilder gehören in den PR-Text, nicht ins Repository.
 *
 *   docker compose up -d
 *   SHOT_TAG=nachher pnpm --filter @workertransfer/web run shots
 *
 * Für den VORHER-Stand dieses Schnitts genügt kein `git stash`: die
 * Palettenvereinigung ist schon committet. Man braucht einen Arbeitsbaum auf
 * `origin/develop`:
 *
 *   git worktree add ../wt-vorher origin/develop
 *   cd ../wt-vorher && pnpm install
 *   # dort den Stapel fahren und SHOT_TAG=vorher aufnehmen
 *
 * Nur vier Seiten, und alle vier ohne Anmeldung erreichbar: Hero, Auth-Panel,
 * Karten, Knöpfe, Felder und ein <select> sind damit abgedeckt. Eine
 * angemeldete Seite hätte je Lauf eine Registrierung samt Mailabruf gekostet.
 *
 * BEKANNTE LÜCKE: die drei Bestandsnutzer von `.wt-checkbox` (jobs.tsx
 * ApplyBox 2x, profile.tsx 1x) liegen alle hinter der Anmeldung und sind hier
 * NICHT im Bild. Wer sie braucht, nimmt `login()` und `registerAndConfirm()`
 * aus `../e2e/stack`.
 */
const TAG = process.env.SHOT_TAG ?? "aktuell";

const SEITEN: { name: string; pfad: string }[] = [
  { name: "startseite", pfad: "/" },
  { name: "anmelden", pfad: "/login" },
  { name: "registrieren", pfad: "/register" },
  // Öffentlich, und die einzige der vier mit einem <select> und Karten.
  { name: "stellen", pfad: "/jobs" },
];

test.describe("Aufnahmen der Oberfläche", () => {
  test.beforeAll(async () => {
    const missing = await missingService();
    test.skip(missing !== null, `Stapel steht nicht: ${missing}`);
  });

  for (const seite of SEITEN) {
    test(`${seite.name} (${TAG})`, async ({ page }) => {
      // Der Direktlink, nicht ein Klick: nur das Eintippen der Adresse geht
      // wirklich durchs Gateway (docker/traefik/dynamic.yml, Sec-Fetch-Dest).
      // Ein Klick schaltet im Browser um und beweist nichts.
      // `networkidle` und nicht die Voreinstellung: die Kopfzeile zeigt ihre
      // Links erst, wenn `useSession()` fertig ist (`isLoading ? null : …` in
      // app.tsx). Ohne das Warten entstand das Bild unter Last VOR der Antwort
      // von `GET /me` — dieselbe Seite sah dann je nach Maschinenlast anders
      // aus, und ein Vergleich zweier solcher Bilder beweist nichts.
      await page.goto(seite.pfad, { waitUntil: "networkidle" });
      // Eine <h1> ist der Beleg, dass die Seite kam und nicht rohes JSON.
      await expect(page.locator("h1").first()).toBeVisible();
      // Schriften: ein Bild vor dem Laden der Schrift zeigt eine andere
      // Textbreite und damit andere Umbrüche.
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({ path: `.screenshots/${TAG}/${seite.name}.png`, fullPage: true });
    });
  }
});
