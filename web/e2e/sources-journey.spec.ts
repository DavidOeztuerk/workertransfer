// A company connects its ATS board (ADR-0052) — the wire between browser and
// jobs-service: the board is set up unproven, the code stands on the page,
// the mode switches, the source goes again. Nothing here asks a real ATS: the
// fetch is the journeys' blind spot on purpose, and `SourceJourneyTests`
// covers it against a fake board.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  switchToCompany,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("a company sets up its board and is shown the code that proves it", async ({ page }) => {
  const stamp = Date.now();
  const companyName = `E2E Sources ${stamp}`;
  const board = `e2e-${stamp}`;
  const email = uniqueEmail(uniqueCompanyDomain());

  await registerAndConfirm(page, email, "E2E Admin", companyName);
  await login(page, email);
  await switchToCompany(page, companyName);
  await page.goto("/company/jobs");

  const form = page.getByRole("form", { name: "Neue Quelle" });
  await form.getByLabel("Name des Boards").fill(board);
  await form.getByRole("button", { name: "Quelle hinzufügen" }).click();

  const source = page.getByRole("article", { name: `Greenhouse ${board}` });
  await expect(source).toBeVisible();
  // The code is the proof, and it stands before anything is fetched.
  await expect(source.getByLabel("Bestätigungscode")).toHaveText(/^wt-[a-z2-9]{10}$/);
  await expect(source.getByText("Noch nie abgerufen.")).toBeVisible();

  await source.getByLabel("Neue Stellen").click();
  await page.getByRole("option", { name: "direkt veröffentlichen", exact: true }).click();
  await page.reload();
  await expect(page.getByRole("article", { name: `Greenhouse ${board}` }).getByLabel("Neue Stellen"))
    .toHaveText("direkt veröffentlichen");

  const again = page.getByRole("article", { name: `Greenhouse ${board}` });
  await again.getByRole("button", { name: "Entfernen" }).click();
  await again.getByRole("button", { name: /Wirklich entfernen/ }).click();
  await expect(page.getByRole("article", { name: `Greenhouse ${board}` })).toHaveCount(0);
});
