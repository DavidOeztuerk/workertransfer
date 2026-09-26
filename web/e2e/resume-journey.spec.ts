// The resume through the browser: request, release, withdraw.
//
// What the integration test proves at the HTTP level, this test checks where it
// arrives — a button that does not make the request would be invisible on both
// levels below.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  pickOption,
} from "./stack";

skipWithoutStack();

test("a resume reaches only the company it was released to", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Employer ${Date.now()}`;
  const headline = `E2E CV candidate ${Date.now()}`;
  const employer = `Former Company ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  // Create the profile and release it — without that nobody may ask for the
  // resume, otherwise the request would be a channel for learning it exists.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  await candidate.goto("/resume");
  // The page first asks HOW the resume should come — written by hand or as a
  // file. This journey writes it by hand.
  const writeByHand = candidate.getByRole("button", { name: /Selbst schreiben/i });
  await expect(writeByHand).toBeVisible();
  await writeByHand.click();
  // NO click on "hinzufügen" (add): choosing "selbst schreiben" already creates
  // an empty row. An extra click created a second one, and
  // `getByLabel(/Arbeitgeber/i)` would then match both. (The button is called
  // "Stelle hinzufügen" by now anyway — work, training and school are separate.)
  await expect(candidate.getByLabel(/Arbeitgeber/i)).toBeVisible();
  await candidate.getByLabel(/Arbeitgeber/i).fill(employer);
  await candidate.getByLabel(/Tätigkeit/i).fill("Backend developer");
  await candidate.getByLabel("Von").fill("2020-01");
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Lebenslauf gespeichert/i)).toBeVisible();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  // Wait until the switch really holds. The choice only sets it off: the server
  // issues a new token, the cookie is replaced, the session reloaded. Clicking
  // on immediately wins the race about every second time — /scout then shows
  // "Profile sehen nur Unternehmen", the card is missing, and the test runs
  // into a timeout at a place that has nothing to do with the cause. The
  // company menu appears only with an active company and is therefore the
  // honest signal.
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Lebenslauf anfragen/i }).click();
  await expect(card.getByText(/Anfrage gestellt/i)).toBeVisible();

  // The request has to reach the person without them looking for it.
  await candidate.goto("/");
  // Since the rebuild the overview says WHAT it is about instead of only "1
  // offene Anfrage" on a badge. The promise of this journey is unchanged: the
  // request has to reach the person without them looking for it.
  await expect(
    candidate.getByText("1 Anfrage nach deinem Lebenslauf")
  ).toBeVisible();

  // The person decides — before that there is nothing to see.
  await candidate.goto("/resume");
  const row = candidate.locator("li").filter({ hasText: /fragt nach deinem Lebenslauf/i });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Withdrawing takes effect immediately; the request itself remains as a case.
  await row.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(row.getByText(/zurückgezogen/i)).toBeVisible();
  await expect(row.getByRole("button", { name: /Zurückziehen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
