// The journey this slice is about — through the browser, across three
// services, with real mails.
//
// Register → confirm → create a profile → release it → a company sees it →
// revoke → it is gone. With no wait in between, because ADR-0013 explicitly
// rejected a cache.
//
// The integration tests in tests/WorkerTransfer.Profile.Tests prove the same at
// the HTTP level. What counts here is that the UI really offers it: a switch
// that does not switch would be invisible on both levels below.

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

test("a released profile appears, a revoked one disappears immediately", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  // No freemail domain: a company can only be claimed on a domain of its own
  // (ADR-0019). And a new one per run, because the domain is taken afterwards —
  // with a fixed one the test would pass exactly once.
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Employer ${Date.now()}`;
  const headline = `E2E Candidate ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel("Standort", { exact: true }).fill("Hamburg");
  await candidate.getByLabel(/Fähigkeiten/i).fill("Python, PostgreSQL");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // Switchable only after saving: only what exists can be released.
  const release = candidate.getByRole("switch");
  await expect(release).toBeEnabled();
  await release.click();
  await expect(release).toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);

  // Without an active company the page shows only a notice — the switch is the
  // point where the server writes the tenant into the token (ADR-0018).
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toBeVisible();

  // The actual proof: revoke in one browser, reload in the other.
  await release.click();
  await expect(release).not.toBeChecked();

  await recruiter.reload();
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("without an active company the candidate list leads nowhere, not to data", async ({
  page,
}) => {
  const email = uniqueEmail("private-e2e.example");
  await registerAndConfirm(page, email, "E2E Private Person");
  await login(page, email);

  await page.goto("/scout");

  await expect(page.getByText(/Profile sehen nur Unternehmen/i)).toBeVisible();
  await expect(page.getByRole("list")).toHaveCount(0);
});
