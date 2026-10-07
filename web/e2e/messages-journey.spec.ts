// One conversation per application (ADR-0047), across the wire between two
// browsers: the person writes, the company reads and answers, the person reads.
//
// And the draft button says what is true on a stack without an AI key: no
// model is set up. A draft that appeared anyway would be one nobody asked a
// model for.

import { expect, test } from "@playwright/test";

import {
  login,
  pickOption,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("person and company write to each other about one application", async ({ browser }) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Messages ${Date.now()}`;
  const jobTitle = `E2E Messages Job ${Date.now()}`;
  const question = `Gibt es schon einen Stand? ${Date.now()}`;
  const answer = `Wir melden uns bis Freitag. ${Date.now()}`;

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
  await expect(jobRow).toBeVisible();
  await jobRow.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(jobRow.getByText("Veröffentlicht")).toBeVisible();

  // The person applies.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Applicant");
  await login(candidate, candidateEmail);
  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(jobTitle);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  const jobCard = candidate.locator("li").filter({ hasText: jobTitle });
  await expect(jobCard).toBeVisible();
  await jobCard.getByRole("button", { name: /Bewerben/i }).click();
  await expect(candidate).toHaveURL(/\/applications\/drafts\/[0-9a-f-]{36}$/, { timeout: 30_000 });
  await candidate.getByRole("button", { name: /Selbst schreiben/i }).click();
  await candidate.getByLabel(/Betreff/i).fill("Application");
  await candidate.getByLabel(/^Text$/i).fill("Dear Sir or Madam,\n\nI am applying.");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByRole("button", { name: /Freigeben/i })).toBeVisible({ timeout: 10_000 });
  await candidate.getByRole("button", { name: /Freigeben/i }).click();
  await candidate.getByRole("button", { name: /Senden/i }).click();
  await expect(candidate.getByRole("button", { name: /Senden/i })).toHaveCount(0);

  // The person writes.
  await candidate.goto("/applications");
  const row = candidate.locator("li").filter({ hasText: /Abgeschickt/ });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Nachrichten" }).click();
  await expect(candidate.getByText(/keine Gesundheitsdaten/i)).toBeVisible();

  // No model on this stack: the draft button names that, and nothing appears.
  await candidate.getByRole("button", { name: "Antwort entwerfen" }).click();
  await expect(candidate.getByText(/kein Sprachmodell eingerichtet/i)).toBeVisible();
  await expect(candidate.getByRole("group", { name: "Entwurf" })).toHaveCount(0);

  await candidate.getByLabel("Nachricht", { exact: true }).fill(question);
  await candidate.getByRole("button", { name: "Senden", exact: true }).click();
  await expect(candidate.getByText(question)).toBeVisible();

  // The company reads it at the application and answers.
  await recruiter.goto("/company/applications");
  const application = recruiter.locator("li").filter({ hasText: jobTitle });
  await expect(application).toBeVisible();
  await application.getByRole("link").first().click();
  await recruiter.getByRole("tab", { name: "Nachrichten" }).click();
  await expect(recruiter.getByText(question)).toBeVisible();
  await recruiter.getByLabel("Nachricht", { exact: true }).fill(answer);
  await recruiter.getByRole("button", { name: "Senden", exact: true }).click();
  await expect(recruiter.getByText(answer)).toBeVisible();

  // And the person reads the answer.
  await candidate.reload();
  const again = candidate.locator("li").filter({ hasText: /Abgeschickt|Wird gelesen/ });
  await again.getByRole("button", { name: "Nachrichten" }).click();
  await expect(candidate.getByText(answer)).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
