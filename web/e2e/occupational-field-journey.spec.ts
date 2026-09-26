// The journey that accepts PBI-1: an account in the trades sees no GitHub but
// credentials — and the route stays reachable all the same.
//
// Why on the running system and not in a component suite:
//
//   1. Between the select field in the browser and the column `users.berufsfeld`
//      lie the wire (`occupational_field`, snake_case), the command, the
//      aggregate and `GET /auth/session`. A field sent here in camelCase would
//      never arrive — and every other suite would stay green, because none of
//      them crosses this seam.
//   2. "Hiding is not access control" can only be shown here: the menu entry is
//      gone AND the page answers. A component test knows no routes.
//   3. Taking it back in the settings has to move the HEADER, not only the
//      store.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("an account in the trades gets credentials instead of GitHub", async ({ page }) => {
  const email = uniqueEmail("trades.example");

  // "Handwerk" is the option's visible label (the trades), not test data.
  await registerAndConfirm(page, email, "E2E Trades", undefined, "Handwerk");
  await login(page, email);

  // 1. The account menu no longer offers GitHub.
  await page.goto("/overview");
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "Lebenslauf" })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toHaveCount(0);
  await page.keyboard.press("Escape");

  // 2. Instead the profile page names the credentials that belong to this
  //    work — journeyman's and master craftsman's certificate stand where
  //    before only a repository was conceivable.
  await page.goto("/profile");
  await expect(
    page.getByRole("heading", { name: "Nachweise, die zu deiner Arbeit passen" })
  ).toBeVisible();
  await expect(page.getByText("Meisterbrief", { exact: true })).toBeVisible();
  await expect(page.getByText("Gesellenbrief", { exact: true })).toBeVisible();
  // Every field inherits the general part: everybody has a job reference.
  await expect(page.getByText("Arbeitszeugnis", { exact: true })).toBeVisible();

  // 3. IT HIDES, IT DOES NOT PROTECT. Whoever knows the address gets the page
  //    — unchanged. A navigation mistaken for access control is more dangerous
  //    than none at all.
  await page.goto("/github");
  await expect(
    page.getByRole("heading", { name: "GitHub verbinden" })
  ).toBeVisible();

  // 4. The choice can be changed, and the header follows it at once. Without
  //    this step a person would see their choice as ineffective until the next
  //    load.
  await page.goto("/profile");
  await page.getByRole("combobox", { name: "Berufsfeld" }).click();
  await page.getByRole("option", { name: "IT und Software", exact: true }).click();

  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toBeVisible();
});

test("an account without an occupational field sees today's view", async ({ page }) => {
  const email = uniqueEmail("neutral.example");

  // No occupational field chosen — the preselection stays.
  await registerAndConfirm(page, email, "E2E Neutral");
  await login(page, email);

  // The promise this work is measured against: nothing is taken away from
  // anybody who chose nothing.
  await page.goto("/overview");
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toBeVisible();
  await page.keyboard.press("Escape");

  // And no empty credentials box on the profile: a greyed-out area would be
  // the tacit claim that something is missing there (ADR-0022 §3).
  await page.goto("/profile");
  await expect(
    page.getByRole("heading", { name: "Nachweise, die zu deiner Arbeit passen" })
  ).toHaveCount(0);
});
