// The invitation flow through the browser: invite, fetch the mail, join.
//
// The core is the point that only becomes visible here — the link comes by
// mail, and the invited person has to open it with the right account.

import { expect, test } from "@playwright/test";

import {
  invitationTokenFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  pickOption,
} from "./stack";

skipWithoutStack();

/**
 * Switch to a company — and wait until it really holds.
 *
 * The choice only sets the switch off: the server issues a new token, the
 * cookie is replaced, the session reloaded. Clicking on immediately sometimes
 * wins the race and sometimes not — the page then shows "Wähle oben ein
 * Unternehmen" although one was chosen. The company menu appears only with an
 * active company and is therefore the honest signal.
 */
async function actAsCompany(page: import("@playwright/test").Page, name: string) {
  await page.goto("/");
  await pickOption(page, /Handeln als/i, name);
  await expect(page.getByRole("button", { name: "Unternehmen" })).toBeVisible();
}

test("an invitation lets exactly the invited person in", async ({ browser }) => {
  const domain = uniqueCompanyDomain();
  const adminEmail = uniqueEmail(domain);
  const colleagueEmail = uniqueEmail(domain);
  const companyName = `E2E Team ${Date.now()}`;

  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await registerAndConfirm(admin, adminEmail, "E2E Boss", companyName);
  await login(admin, adminEmail);
  await actAsCompany(admin, companyName);
  await admin.goto("/company/team");
  await expect(admin.getByText("E2E Boss")).toBeVisible();

  // Inviting lives at an address of its own, not as a form in the list.
  // "Einladen" (invite) therefore means two different things: on the team page
  // it is a LINK there, on the page itself the submit button. Keeping the roles
  // apart is not cosmetic here — `getByRole("button")` would not find the link,
  // and a `getByText` would find both.
  await admin.getByRole("link", { name: /Einladen/i }).click();
  await expect(admin).toHaveURL(/\/company\/team\/invite$/);
  await admin.getByLabel(/E-Mail/i).fill(colleagueEmail);
  await admin.getByRole("button", { name: /Einladen/i }).click();
  await expect(admin.getByText(/Einladung verschickt/i)).toBeVisible();

  // And the list stands on the team page — the open invitation has to show up
  // there, otherwise submitting only produced a message.
  await admin.getByRole("link", { name: /Zurück zur Mannschaft/i }).click();
  await expect(admin.getByTestId("invitation-list").getByText(colleagueEmail)).toBeVisible();

  const token = await invitationTokenFor(colleagueEmail);

  const colleagueContext = await browser.newContext();
  const colleague = await colleagueContext.newPage();
  await registerAndConfirm(colleague, colleagueEmail, "E2E Colleague");
  await login(colleague, colleagueEmail);
  await colleague.goto(`/invitation?token=${token}`);
  // The heading, not just any text: after joining, the company name also
  // stands as an <option> in the company switcher, and an <option> counts as
  // hidden — the test then failed with "Received: hidden", although the page
  // showed exactly what it should.
  await expect(
    colleague.getByRole("heading", { name: new RegExp(companyName) })
  ).toBeVisible();

  // Joining does NOT switch the session — the person has to do that.
  await actAsCompany(colleague, companyName);
  await colleague.goto("/company/team");
  await expect(colleague.getByText("E2E Boss")).toBeVisible();
  await expect(colleague.getByText("E2E Colleague")).toBeVisible();

  // A member may not invite — the form is not offered at all.
  await expect(colleague.getByRole("button", { name: /Einladen/i })).toHaveCount(0);

  await adminContext.close();
  await colleagueContext.close();
});
