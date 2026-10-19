// The ONE journey that really switches the language.
//
// All other journeys run with `locale: "de-DE"` from `playwright.config.ts` and
// look for German texts. That is intended: without pinning it, their result
// would hang on the system locale of the machine they run on, and a test whose
// result depends on the environment says nothing.
//
// This one switches by itself and checks the two things that can only be seen
// on the running system:
//
//   1. The UI really changes — not only the store, but the text on the page. A
//      catalogue test says that a translation EXISTS; it does not say that
//      anybody renders it.
//   2. The MAIL follows the choice. That is the actual reason for the column on
//      the account: the server writes the confirmation mail, and days later it
//      writes the erasure confirmation without any request. If the language
//      hung on `Accept-Language`, exactly that last message would be German.

import { expect, test } from "@playwright/test";

import {
  lastMailFor,
  GATEWAY_URL,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("the language choice changes the UI and reaches the mail", async ({ page }) => {
  const email = uniqueEmail("language.example");

  await registerAndConfirm(page, email, "E2E Language");
  await login(page, email);

  // German is the starting point — the browser runs with `de-DE`.
  await page.goto("/market");
  await expect(page.getByRole("heading", { name: "Mein Marktstatus" })).toBeVisible();

  // Switch to French. SIGNED IN, the choice lives in the account menu under the
  // user icon — signed out behind a cog, because then there is no user icon.
  // Both stand at the top and not in the footer: whoever changes the language
  // BECAUSE they cannot read the UI does not scroll to the end of the page
  // first.
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await page.getByRole("menuitem", { name: /Sprache/ }).click();
  // The choice closes the menu — otherwise it lies over the page as a modal.
  await page.getByRole("menuitemradio", { name: /Français/ }).click();

  // The evidence that it really renders and not only the store flipped.
  await expect(
    page.getByRole("heading", { name: "Mon statut sur le marché" })
  ).toBeVisible();

  // And that it survives the reload: the choice lives in local storage AND on
  // the account, and both paths must give the same result.
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Mon statut sur le marché" })
  ).toBeVisible();

  // Now the part only the server can do — and the duplicate registration is the
  // sharpest evidence for it available here.
  //
  // `POST /auth/register` on an address already taken deliberately answers as
  // on a free one and sends the REAL OWNER a warning. This request has no
  // session, and its header says `de-DE` — Playwright sets it from `locale`. If
  // the mail still arrives in French, it can only have that from the column.
  //
  // (`resend-verification` would be the more obvious way and is the wrong one:
  // to a confirmed account the endpoint deliberately sends nothing at all.)
  const since = Date.now();

  // Absolute, onto the gateway: `baseURL` is the UI, and that knows no API
  // paths.
  const answer = await page.request.post(`${GATEWAY_URL}/auth/register`, {
    data: { email, password: "secret-and-long-enough", display_name: "Duplicate" },
  });
  expect(answer.status()).toBe(201);

  // `after` is not cosmetic: without it the search would find the German
  // confirmation mail from before, and the test would be green without proving
  // anything.
  const mail = await lastMailFor(email, { after: since });
  expect(mail).not.toBeNull();
  expect(mail!.subject).toContain("Tentative d’inscription");

  // Back to German, so that the same system is not left for the next journey in
  // a state nobody expects. The language entry is now called "Langue" — the UI
  // is French, after all.
  await page.goto("/market");
  // The entries now have French names — the UI is French, after all.
  await page.getByRole("button", { name: "Mon compte" }).click();
  await page.getByRole("menuitem", { name: /Langue/ }).click();
  await page.getByRole("menuitemradio", { name: /Deutsch/ }).click();
  await expect(page.getByRole("heading", { name: "Mein Marktstatus" })).toBeVisible();
});
