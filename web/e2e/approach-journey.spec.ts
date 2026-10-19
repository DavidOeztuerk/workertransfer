// The first contact (ADR-0048, VISION-FRAGEN B2): a company approaches a
// person who released her profile, she reads it under /messages and answers.
// Nothing leaves without a click — and on this stack without an AI key, the
// AI recruiter's draft says that no model is set up.

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

test("a company approaches a person, and she answers under /messages", async ({ browser }) => {
  const stamp = Date.now();
  const skill = `Brazing${stamp}`;
  const headline = `E2E Approach candidate ${stamp}`;
  const companyName = `E2E Approach ${stamp}`;
  const greeting = `Wir suchen jemanden für Hartlöten. ${stamp}`;
  const reply = `Gern, erzählen Sie mehr. ${stamp}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  const candidateEmail = uniqueEmail("candidate.example");
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Fähigkeiten/i).fill(skill);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // The founder is admin — and an admin may make a first contact (the roles
  // are ordered).
  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  const recruiterEmail = uniqueEmail(uniqueCompanyDomain());
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(skill);
  await recruiter.getByRole("button", { name: "Suchen" }).click();
  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();

  await card.getByRole("button", { name: "Ansprechen" }).click();

  // No model here: the draft button names that, and nothing is written for us.
  await card.getByRole("button", { name: "Entwurf vom KI-Recruiter" }).click();
  await expect(card.getByText(/kein Sprachmodell eingerichtet|kein Anbieter/i)).toBeVisible();
  await expect(card.getByLabel("Nachricht")).toHaveValue("");

  await card.getByLabel("Nachricht").fill(greeting);
  await card.getByRole("button", { name: "Senden", exact: true }).click();
  await expect(card.getByText("Gesendet.")).toBeVisible();

  // She finds it under /messages — as an approach, from this company.
  await candidate.goto("/messages");
  const row = candidate.locator("li").filter({ hasText: "Ansprache" });
  await expect(row).toBeVisible();
  // The company's name stands here once it has a public profile; this fresh
  // one has none, so the row says "Ein Unternehmen" rather than inventing one.
  await row.click();
  await expect(candidate.getByText(greeting)).toBeVisible();

  await candidate.getByLabel("Nachricht", { exact: true }).fill(reply);
  await candidate.getByRole("button", { name: "Senden", exact: true }).click();
  await expect(candidate.getByText(reply)).toBeVisible();

  // And the company reads the answer.
  await recruiter.goto("/messages");
  await recruiter.locator("li").filter({ hasText: "Ansprache" }).first().click();
  await expect(recruiter.getByText(reply)).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
