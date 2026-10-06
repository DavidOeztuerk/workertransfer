import { expect, test } from "@playwright/test";

import { PROFILE_URL, login, registerAndConfirm, skipWithoutStack, uniqueEmail } from "./stack";

skipWithoutStack();

/**
 * Signing out in one tab signs out every tab of the browser — at once.
 *
 * The tabs share the cookies, so the session was over for all of them on the
 * server, but the other tabs kept showing a signed-in page until their next
 * request (docs/VISION-FRAGEN.md, K). And the old token is refused by the
 * services themselves, not only hidden in the browser.
 */
test("signing out in one tab signs out the other at once", async ({ browser }) => {
  const email = uniqueEmail("example.com");
  const context = await browser.newContext();
  const first = await context.newPage();
  await registerAndConfirm(first, email, "E2E Two tabs");
  await login(first, email);

  const second = await context.newPage();
  await second.goto("/profile");
  await expect(second.getByLabel(/Überschrift/i)).toBeVisible();

  const token = (await context.cookies()).find((cookie) => cookie.name === "access")?.value;
  expect(token, "no access cookie — the sign-in did not hold").toBeTruthy();

  await first.goto("/overview");
  await first.getByRole("button", { name: "Mein Konto" }).click();
  await first.getByRole("menuitem", { name: "Abmelden", exact: true }).click();

  // The other tab leaves the protected page without being touched.
  await expect(second).toHaveURL(/\/$/);
  await expect(second.getByLabel(/Überschrift/i)).toHaveCount(0);

  // And the token itself is dead at a service, not only absent in the browser.
  const answer = await second.request.get(`${PROFILE_URL}/profiles/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(answer.status()).toBe(401);

  await context.close();
});
