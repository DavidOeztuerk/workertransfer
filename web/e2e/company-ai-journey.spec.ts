// The company's own AI key through the browser: an admin finds the settings in
// the company menu, chooses a provider, stores a key — and the key never comes
// back, only its last four characters (ADR-0051, Bauliste 6).
//
// No draft is asked for here: a stored key would send it to the real provider,
// and nothing in a journey may leave the stack. That the drafts use the key is
// measured in `CompanyKeyTests` and the reply drafter's wire suite.

import { expect, test } from "@playwright/test";

import {
  login,
  pickOption,
  registerAndConfirm,
  skipWithoutStack,
  switchToCompany,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("an admin stores the company's own key, and it never comes back", async ({ page }) => {
  const email = uniqueEmail(uniqueCompanyDomain());
  const companyName = `E2E KI ${Date.now()}`;
  const key = `sk-ant-e2e-${Date.now()}-c0de`;

  await registerAndConfirm(page, email, "E2E Admin", companyName);
  await login(page, email);
  await switchToCompany(page, companyName);

  await page.getByRole("button", { name: "Unternehmen" }).click();
  await page.getByRole("menuitem", { name: "Einstellungen der Firma" }).click();
  await expect(page).toHaveURL(/\/company\/settings$/);
  await expect(page.getByRole("heading", { name: "KI-Zugang der Firma" })).toBeVisible();

  // The field appears once the stored choice has arrived. `pickOption` reads
  // an invisible field as "in the account menu" and opens that menu — which
  // then lies over this page.
  await expect(page.getByLabel(/Anbieter/i)).toBeVisible();
  await pickOption(page, /Anbieter/i, "Anthropic");
  await page.getByLabel(/Schlüssel/).fill(key);
  await page.getByRole("button", { name: "Schlüssel speichern" }).click();
  await expect(page.getByText("Hinterlegt, endet auf c0de")).toBeVisible();

  // Stored at the server, not only in the form — and the page never holds it.
  await page.reload();
  await expect(page.getByText("Hinterlegt, endet auf c0de")).toBeVisible();
  await expect(page.getByLabel(/Schlüssel/)).toHaveValue("");
  expect(await page.content()).not.toContain(key);
});
