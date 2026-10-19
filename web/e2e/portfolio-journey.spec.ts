// The portfolio through the browser — and the statement that only becomes
// visible here: profile and portfolio are two releases, even though both
// switches are called "für Unternehmen sichtbar" (visible to companies).

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

test("profile and portfolio are two separate releases", async ({ browser }) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Employer ${Date.now()}`;
  const headline = `E2E Portfolio candidate ${Date.now()}`;
  const workTitle = `A tool ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  // Create the profile and release it.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // Create work samples — but do NOT release them.
  //
  // The form has an address of its own (`/portfolio/new`); "Arbeit hinzufügen"
  // (add work) is a LINK. After saving it leads back to the list, and the work
  // stands there — that is the confirmation, not a sentence in the form.
  await candidate.goto("/portfolio");
  await candidate.getByRole("link", { name: /Arbeit hinzufügen/i }).click();
  await expect(candidate).toHaveURL(/\/portfolio\/new$/);
  await candidate.getByLabel("Titel").fill(workTitle);
  await candidate.getByLabel(/Link/i).fill("https://example.org/tool");
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(workTitle)).toBeVisible();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  // And the deep link to the form holds: only a fresh load really asks the
  // server that serves the UI (the Vite dev server since ADR-0040, no longer
  // the gateway); a click merely switches the router.
  await candidate.goto("/portfolio/0");
  await expect(candidate.getByLabel("Titel")).toHaveValue(workTitle);
  await candidate.goto("/portfolio");

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // The profile is there — the work samples are not. That is exactly the
  // point: the profile release does not open the portfolio.
  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toBeVisible();

  const subjectId = await candidate.evaluate(async () => {
    const res = await fetch(`${window.location.origin.replace(":5173", ":8001")}/me`, {
      credentials: "include",
    });
    return ((await res.json()) as { user_id: string }).user_id;
  });

  const hidden = await recruiter.evaluate(async (id: string) => {
    const res = await fetch(
      `${window.location.origin.replace(":5173", ":8005")}/portfolios/${id}`,
      { credentials: "include" }
    );
    return res.status;
  }, subjectId);
  expect(hidden).toBe(404);

  // Only the second release opens them.
  await candidate.goto("/portfolio");
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  const visible = await recruiter.evaluate(async (id: string) => {
    const res = await fetch(
      `${window.location.origin.replace(":5173", ":8005")}/portfolios/${id}`,
      { credentials: "include" }
    );
    return res.status;
  }, subjectId);
  expect(visible).toBe(200);

  await candidateContext.close();
  await recruiterContext.close();
});
