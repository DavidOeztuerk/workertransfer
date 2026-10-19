// The unsolicited application (VISION-FRAGEN B2): a person writes to a
// company from its careers page, and sending releases her profile to it.

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

test("a person applies without an advert, and the company answers", async ({ browser }) => {
  const stamp = Date.now();
  const companyName = `E2E Initiative ${stamp}`;
  const question = `Haben Sie Bedarf an einer Schweißerin? ${stamp}`;
  const answer = `Ja, gern — kommen Sie vorbei. ${stamp}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  const recruiterEmail = uniqueEmail(uniqueCompanyDomain());
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  const slug = await recruiter.evaluate(async () => {
    const res = await fetch(
      `${window.location.origin.replace(":5173", ":8008")}/companies/me/profile`,
      { credentials: "include" },
    );
    return ((await res.json()) as { slug: string }).slug;
  });

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  const candidateEmail = uniqueEmail("candidate.example");
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(`E2E Initiative candidate ${stamp}`);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  await candidate.goto(`/careers/${slug}`);
  await candidate.getByRole("button", { name: "Nachricht schreiben" }).click();
  // The release is said before the button.
  await expect(candidate.getByText(/gibst du diesem Unternehmen dein Profil frei/)).toBeVisible();
  await candidate.getByLabel("Deine Nachricht").fill(question);
  await candidate.getByRole("button", { name: "Senden und Profil freigeben" }).click();
  await expect(candidate.getByText("Gesendet.")).toBeVisible();

  // The company finds it under /messages and answers.
  await recruiter.goto("/messages");
  await recruiter.locator("li").filter({ hasText: "Initiativbewerbung" }).first().click();
  await expect(recruiter.getByText(question)).toBeVisible();
  await recruiter.getByLabel("Nachricht", { exact: true }).fill(answer);
  await recruiter.getByRole("button", { name: "Senden", exact: true }).click();
  await expect(recruiter.getByText(answer)).toBeVisible();

  // And she reads the answer.
  await candidate.goto("/messages");
  await candidate.locator("li").filter({ hasText: "Initiativbewerbung" }).first().click();
  await expect(candidate.getByText(answer)).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
