// The advisor through the real stack — at exactly the two places where it
// differs from everything else.
//
// FIRST: the mandate is a VIEW. What a company sees stands in the ledger and not
// in a column of this service. This journey releases stage by stage and reads
// back on the company side each time — and it checks that what is not released
// is not there at all.
//
// SECOND: a withdrawal takes effect on the next request. The same company page,
// the same person, nothing any more.
//
// The wire is the reason this journey has to exist: `salary_min` bound as
// `salaryMin` would never arrive, and every handler and component suite would
// stay green.

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

test("three stages, and what is not released is not there", async ({ browser }) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("advisor.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Advisory ${stamp}`;
  const headline = `E2E Advisory candidate ${stamp}`;

  // --- The person: profile, release, mandate ------------------------------

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Advisee");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Fähigkeiten/i).fill(`Advising${stamp}`);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // "Für alle Unternehmen" (for all companies) — otherwise the scout does not
  // find her at all.
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // The mandate. FOUR values, and none of them is a visibility.
  await candidate.goto("/advisor");
  await candidate.getByLabel(/Frühester Eintritt/i).fill("2026-11");
  await candidate.getByLabel(/Gehalt ab/i).fill("4000");
  await candidate.getByLabel(/Gehalt bis/i).fill("5200");
  await candidate.getByLabel(/Pensum/i).fill("80");
  await candidate.getByRole("button", { name: /Mandat speichern/i }).click();
  await expect(candidate.getByText(/^Gespeichert\.$/)).toBeVisible();

  // --- The company opens a conversation ----------------------------------

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(`Advising${stamp}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();

  await card.getByRole("button", { name: /Gespräch eröffnen/i }).click();
  await expect(card.getByText(/Gespräch eröffnet/i)).toBeVisible();

  // --- Stage 1: entry and workload, nothing else --------------------------

  await recruiter.goto("/company/advisor");
  await expect(recruiter.getByText(/2026-11/)).toBeVisible();

  // THE PROMISE: the salary range is not there. Not empty, not locked, not "not
  // released yet" — not at all.
  await expect(recruiter.getByText(/4000/)).toHaveCount(0);
  await expect(recruiter.getByText(/E2E Advisee/)).toHaveCount(0);
  await expect(recruiter.getByText(/gesperrt|noch nicht freigegeben/i)).toHaveCount(0);

  // --- Stage 2: the range is added --------------------------------------

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Stufe 2 freigeben/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(/4000–5200/)).toBeVisible();
  await expect(recruiter.getByText(/E2E Advisee/)).toHaveCount(0);

  // --- Stage 3: civil name and address -----------------------------------

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Stufe 3 freigeben/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(candidateEmail)).toBeVisible();

  // --- A withdrawal takes effect on the next request ----------------------
  //
  // BACK TO STAGE 2: civil name and address disappear, the range stays. That is
  // the sharp case — not "everything gone", but exactly the one release that was
  // withdrawn, and immediately.

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Auf Stufe 2 zurück/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(/4000–5200/)).toBeVisible();
  await expect(recruiter.getByText(candidateEmail)).toHaveCount(0);

  // --- And all the way back: the conversation drops out of the list -------
  //
  // FIRST THE PUBLIC PROFILE OFF. That is not fussiness but the promise itself:
  // a button in ONE conversation does not flip a platform-wide switch. As long
  // as "for all companies" stands, stage 1 stands too — and that is right,
  // because then the person really is visible.

  await candidate.goto("/profile");
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Auf Stufe 1 zurück/i }).click();
  await candidate.getByRole("button", { name: /Freigabe zurücknehmen/i }).click();

  await recruiter.reload();
  // At stage 0 the conversation does not exist for the other side. An empty row
  // would be the "locked" hint in list form.
  await expect(recruiter.getByText(/Es läuft gerade kein Gespräch/)).toBeVisible();
  await expect(recruiter.getByText(/2026-11/)).toHaveCount(0);

  // The person keeps it and can release again — a withdrawal that took the row
  // with it would be a one-way street.
  await candidate.goto("/advisor");
  await expect(candidate.getByRole("button", { name: /Stufe 1 freigeben/i })).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});

test("whoever excludes a company loses \"for all companies\"", async ({
  browser,
}) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("exclusion.example");
  const headline = `E2E Exclusion ${stamp}`;

  const context = await browser.newContext();
  const candidate = await context.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Excluder");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // THE ACCEPTANCE FROM PBI-4, made visible: the ledger knows no negation, so
  // the mode "everyone" stops existing as soon as somebody names a company.
  // Afterwards the person is visible only to companies she releases to one by
  // one — and the current employer is none of them.
  await candidate.goto("/advisor");
  await candidate.getByLabel(/Diese Unternehmen nicht/i).fill("employer.test");
  await candidate.getByRole("button", { name: /Mandat speichern/i }).click();
  await expect(candidate.getByText(/^Gespeichert\.$/)).toBeVisible();

  // The switch on the profile page is OFF afterwards — in the ledger, not only
  // in the display.
  await candidate.goto("/profile");
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await context.close();
});
