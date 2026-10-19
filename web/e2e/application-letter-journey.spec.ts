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

test("select several → drafts → comment → release → send → folder with tabs", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Employer ${Date.now()}`;
  const headline = `E2E Applicant ${Date.now()}`;
  const jobTitle1 = `E2E Job 1 ${Date.now()}`;
  const jobTitle2 = `E2E Job 2 ${Date.now() + 1}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await pickOption(recruiter, /Handeln als/i, companyName);

  for (const title of [jobTitle1, jobTitle2]) {
    await recruiter.goto("/company/jobs/new");
    await recruiter.getByLabel("Titel").fill(title);
    await recruiter.getByLabel(/Beschreibung/i).fill(`${title} description.`);
    await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
    const jobRow = recruiter.locator("li").filter({ hasText: title });
    await expect(jobRow).toBeVisible();
    await jobRow.getByRole("button", { name: /Veröffentlichen/i }).click();
    await expect(jobRow.getByText("Veröffentlicht")).toBeVisible();
  }

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Applicant");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill("E2E Job");
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  await expect(candidate.locator("li").filter({ hasText: jobTitle1 })).toBeVisible();
  await expect(candidate.locator("li").filter({ hasText: jobTitle2 })).toBeVisible();

  await candidate.locator("li").filter({ hasText: jobTitle1 }).getByRole("checkbox").check();
  await candidate.locator("li").filter({ hasText: jobTitle2 }).getByRole("checkbox").check();
  await expect(candidate.getByText(/2 Stellen ausgewählt/i)).toBeVisible();
  await candidate.getByRole("button", { name: /Für alle bewerben/i }).click();

  await expect(candidate).toHaveURL(/\/applications\/drafts$/);
  // Narrowed to the ROW: the title stands in the list twice — once as a heading,
  // once in the line below. `getByText` would match both.
  await expect(
    candidate.locator("li").filter({ hasText: jobTitle1 }).first(),
  ).toBeVisible({ timeout: 20_000 });
  await expect(
    candidate.locator("li").filter({ hasText: jobTitle2 }).first(),
  ).toBeVisible();
  await expect(candidate.getByText(/kein Anbieter|Fehlgeschlagen|Wird geschrieben/i).first()).toBeVisible({
    timeout: 20_000,
  });

  // OPEN EXACTLY THE DRAFT THAT IS CHECKED LATER.
  //
  // `.first()` took the topmost of the list, and that is sorted by modification
  // time — so sometimes the one, sometimes the other. The draft for the second
  // job was then sent and the first one checked: three successful sends in the
  // log and red all the same. An order a test relies on without setting it is a
  // bet.
  await candidate
    .locator("li")
    .filter({ hasText: jobTitle1 })
    .getByRole("button", { name: /Öffnen/i })
    .click();
  await expect(candidate).toHaveURL(/\/applications\/drafts\/[0-9a-f-]{36}$/);

  await expect(candidate.getByText("Noch kein Anschreiben.")).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Generieren/i })).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Selbst schreiben/i })).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Bearbeiten/i })).toHaveCount(0);
  await expect(candidate.getByRole("button", { name: /^Schreiben$/i })).toHaveCount(0);

  await candidate.getByRole("button", { name: /Selbst schreiben/i }).click();
  await candidate.getByLabel(/Betreff/i).fill("Application as a developer");
  await candidate.getByLabel(/^Text$/i).fill("Dear Sir or Madam,\n\nI am applying.");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();

  await expect(candidate.getByRole("button", { name: /Freigeben/i })).toBeVisible({ timeout: 10_000 });
  await candidate.getByRole("button", { name: /Freigeben/i }).click();

  // WHAT IS CHECKED IS THE CONSEQUENCE, NOT THE LABEL.
  //
  // `getByText(/Freigegeben/i)` matched two elements: an MUI chip renders the
  // text in a `span` INSIDE a `div`, and both contain it. Grabbing one of them
  // at random (`.first()`) would not be a test but a bet on which comes first.
  //
  // What "released" MEANS in the domain is sharper anyway: only now may it be
  // sent. Exactly that is what stands here.
  await expect(candidate.getByRole("button", { name: /Senden/i })).toBeEnabled();
  await candidate.getByRole("button", { name: /Senden/i }).click();

  // And "sent" means: it does not go out a second time.
  await expect(candidate.getByRole("button", { name: /Senden/i })).toHaveCount(0);

  await recruiter.goto("/company/applications");
  await expect(recruiter.getByText(jobTitle1)).toBeVisible({ timeout: 15_000 });
  await recruiter.getByRole("link", { name: /Mappe öffnen/i }).first().click();
  await expect(recruiter.getByRole("tablist")).toBeVisible();
  await expect(recruiter.getByRole("tab", { name: /Anschreiben/i })).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
