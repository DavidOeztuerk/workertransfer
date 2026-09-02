// Das Portfolio durch den Browser — und die Aussage, die nur hier sichtbar wird:
// Profil und Portfolio sind zwei Freigaben, auch wenn beide Schalter
// „für Unternehmen sichtbar" heißen.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("Profil und Portfolio sind zwei getrennte Freigaben", async ({ browser }) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const headline = `E2E Portfolio-Kandidat ${Date.now()}`;
  const workTitle = `Ein Werkzeug ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  // Profil anlegen und freigeben.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // Arbeiten anlegen — aber NICHT freigeben.
  //
  // Das Formular hat eine eigene Adresse (`/portfolio/new`); „Arbeit
  // hinzufügen" ist ein LINK. Nach dem Speichern führt es zurück auf die Liste,
  // und dort steht die Arbeit — das ist die Bestätigung, nicht ein Satz im
  // Formular.
  await candidate.goto("/portfolio");
  await candidate.getByRole("link", { name: /Arbeit hinzufügen/i }).click();
  await expect(candidate).toHaveURL(/\/portfolio\/new$/);
  await candidate.getByLabel("Titel").fill(workTitle);
  await candidate.getByLabel(/Link/i).fill("https://example.org/werkzeug");
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(workTitle)).toBeVisible();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  // Und der Deep-Link auf das Formular trägt: nur ein Neuladen geht wirklich
  // durchs Gateway, ein Klick schaltet bloß den Router um.
  await candidate.goto("/portfolio/0");
  await expect(candidate.getByLabel("Titel")).toHaveValue(workTitle);
  await candidate.goto("/portfolio");

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await recruiter.getByLabel(/Handeln als/i).selectOption({ label: companyName });
  await expect(recruiter.locator("summary", { hasText: "Unternehmen" })).toBeVisible();

  // Das Profil ist da — die Arbeiten nicht. Genau das ist der Punkt: die
  // Profilfreigabe öffnet das Portfolio nicht.
  await recruiter.goto("/candidates");
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

  // Erst die zweite Freigabe öffnet sie.
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
