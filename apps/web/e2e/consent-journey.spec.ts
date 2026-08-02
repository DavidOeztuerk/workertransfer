// Die Reise, um die es in diesem Slice geht — durch den Browser, über drei
// Dienste, mit echten Mails.
//
// Registrieren → bestätigen → Profil anlegen → freigeben → ein Unternehmen sieht
// es → widerrufen → es ist weg. Ohne Wartezeit dazwischen, weil ADR-0013 einen
// Cache ausdrücklich verworfen hat.
//
// Der Integrationstest in apps/profile-service belegt dasselbe auf HTTP-Ebene.
// Hier zählt, dass die Oberfläche es auch wirklich anbietet: ein Schalter, der
// nicht schaltet, wäre auf beiden Ebenen darunter unsichtbar.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("ein freigegebenes Profil erscheint, ein widerrufenes verschwindet sofort", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  // Keine Freemail-Domain: nur auf einer eigenen Domain lässt sich ein
  // Unternehmen beanspruchen (ADR-0019). Und je Lauf eine neue, weil die
  // Domain danach vergeben ist — mit einer festen bestünde der Test genau
  // einmal.
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const headline = `E2E Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Ort/i).fill("Hamburg");
  await candidate.getByLabel(/Fähigkeiten/i).fill("Python, PostgreSQL");
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // Erst nach dem Speichern schaltbar: freigeben lässt sich nur, was es gibt.
  const release = candidate.getByRole("switch");
  await expect(release).toBeEnabled();
  await release.click();
  await expect(release).toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter");
  await login(recruiter, recruiterEmail);

  await recruiter.goto("/company/new");
  await recruiter.getByLabel(/Name des Unternehmens/i).fill(companyName);
  await recruiter.getByRole("button", { name: /Unternehmen anlegen/i }).click();
  await expect(recruiter.getByText(/Administrator/i)).toBeVisible();

  // Ohne aktives Unternehmen zeigt die Seite nur einen Hinweis — der Wechsel
  // ist der Punkt, an dem der Server den Tenant ins Token schreibt (ADR-0018).
  await recruiter.goto("/");
  await recruiter.getByLabel(/Handeln als/i).selectOption({ label: companyName });
  await expect(recruiter.getByRole("link", { name: /Kandidaten/i })).toBeVisible();

  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toBeVisible();

  // Der eigentliche Beweis: Widerruf im einen Browser, Neuladen im anderen.
  await release.click();
  await expect(release).not.toBeChecked();

  await recruiter.reload();
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("ohne aktives Unternehmen führt die Kandidatenliste ins Leere, nicht zu Daten", async ({
  page,
}) => {
  const email = uniqueEmail("privat-e2e.example");
  await registerAndConfirm(page, email, "E2E Privatperson");
  await login(page, email);

  await page.goto("/candidates");

  await expect(page.getByText(/Profile sehen nur Unternehmen/i)).toBeVisible();
  await expect(page.getByRole("list")).toHaveCount(0);
});
