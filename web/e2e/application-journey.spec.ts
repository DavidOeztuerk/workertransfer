// The journey that joins phase 3 and phase 4.
//
// Applying gives a company access to one's own data; withdrawing takes it away.
// What the integration test proves at the HTTP level, this test checks where it
// arrives — including the question whether the UI offers withdrawing at all.

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

test("applying opens one's own data, withdrawing closes it", async ({ browser }) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Employer ${Date.now()}`;
  const headline = `E2E Applicant ${Date.now()}`;
  const jobTitle = `E2E Job ${Date.now()}`;

  // The company advertises.
  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();
  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel("Titel").fill(jobTitle);
  await recruiter.getByLabel(/Beschreibung/i).fill("What there is to do.");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const jobRow = recruiter.locator("li").filter({ hasText: jobTitle });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(jobRow).toBeVisible();
  await jobRow.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(jobRow.getByText("Veröffentlicht")).toBeVisible();

  // The person creates a profile — WITHOUT releasing it publicly.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Applicant");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  // The company does not see her: nothing is released.
  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  // Apply.
  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(jobTitle);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  const jobCard = candidate.locator("li").filter({ hasText: jobTitle });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(jobCard).toBeVisible();

  // THE WAY TO AN APPLICATION IS ONE BUTTON, and since PBI-6 there is no second
  // one. `/jobs/{id}/apply` was last only a switch — it created a draft and
  // redirected — and beneath it lay an unreachable form with two release
  // checkboxes. Address and form are gone; what it did is now done by
  // `lib/startDrafts`, called from the job list AND from the careers page.
  //
  // So the button on the card is clicked. The goal stays the same: whoever
  // applies lands at the cover letter and not at an empty text field.
  await jobCard.getByRole("button", { name: /Bewerben/i }).click();
  await expect(candidate).toHaveURL(
    /\/applications\/drafts\/[0-9a-f-]{36}$/, { timeout: 30_000 });

  // And the same state after F5. THAT is what only this journey proves: a deep
  // link to `/applications/drafts/{id}` arrives through router AND gateway. In
  // the browser test the route is mocked; a fault in serving the UI shows up
  // only here.
  await candidate.reload();
  await expect(candidate.getByRole("heading", { level: 1 })).toBeVisible();

  // Write it yourself, release, send. The promise of this journey is not the
  // form but the CONSENT: sending shows the company the profile, withdrawing
  // takes it away.
  await candidate.getByRole("button", { name: /Selbst schreiben/i }).click();
  await candidate.getByLabel(/Betreff/i).fill("Application");
  await candidate
    .getByLabel(/^Text$/i)
    .fill("Dear Sir or Madam,\n\nI am applying.");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();

  await expect(candidate.getByRole("button", { name: /Freigeben/i }))
    .toBeVisible({ timeout: 10_000 });
  await candidate.getByRole("button", { name: /Freigeben/i }).click();
  await expect(candidate.getByRole("button", { name: /Senden/i })).toBeEnabled();
  await candidate.getByRole("button", { name: /Senden/i }).click();
  await expect(candidate.getByRole("button", { name: /Senden/i })).toHaveCount(0);

  // Now the company sees the profile — solely because of the application.
  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toBeVisible();

  // Withdraw.
  await candidate.goto("/applications");
  const applicationRow = candidate.locator("li").filter({ hasText: /Abgeschickt/ });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(applicationRow).toBeVisible();
  await applicationRow.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(candidate.getByText(/sieht deine Daten nicht mehr/i)).toBeVisible();

  // And gone.
  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
