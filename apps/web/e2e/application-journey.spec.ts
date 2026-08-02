// Die Reise, die Phase 3 und Phase 4 verbindet.
//
// Bewerben gibt einem Unternehmen Zugriff auf die eigenen Daten; zurückziehen
// nimmt ihn. Was der Integrationstest auf HTTP-Ebene belegt, prüft dieser Test
// dort, wo es ankommt — inklusive der Frage, ob die Oberfläche das Zurückziehen
// überhaupt anbietet.

import { expect, test } from "@playwright/test";

import {
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  verificationTokenFor,
} from "./stack";

skipWithoutStack();

const PASSWORD = "e2e-Passwort-mit-Laenge-1!";

async function registerAndConfirm(
  page: import("@playwright/test").Page,
  email: string,
  displayName: string
) {
  await page.goto("/register");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).first().fill(PASSWORD);
  await page.getByLabel(/Anzeigename/i).fill(displayName);
  await page.getByRole("button", { name: /Registrieren/i }).click();
  const token = await verificationTokenFor(email);
  await page.goto(`/verify?token=${token}`);
  await expect(page.getByRole("heading", { name: /bestätigt/i })).toBeVisible();
}

async function login(page: import("@playwright/test").Page, email: string) {
  await page.goto("/login");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).fill(PASSWORD);
  await page.getByRole("button", { name: /Anmelden/i }).click();
  await expect(page.getByRole("link", { name: /Mein Profil/i })).toBeVisible();
}

test("bewerben öffnet die eigenen Daten, zurückziehen schließt sie", async ({ browser }) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const headline = `E2E Bewerber ${Date.now()}`;
  const jobTitle = `E2E Stelle ${Date.now()}`;

  // Das Unternehmen schreibt aus.
  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter");
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/company/new");
  await recruiter.getByLabel(/Name des Unternehmens/i).fill(companyName);
  await recruiter.getByRole("button", { name: /Unternehmen anlegen/i }).click();
  await expect(recruiter.getByText(/Administrator/i)).toBeVisible();
  await recruiter.goto("/");
  await recruiter.getByLabel(/Handeln als/i).selectOption({ label: companyName });
  await expect(recruiter.getByRole("link", { name: /Unsere Stellen/i })).toBeVisible();
  await recruiter.goto("/company/jobs");
  await recruiter.getByLabel("Titel").fill(jobTitle);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const jobRow = recruiter.locator("li").filter({ hasText: jobTitle });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(jobRow).toBeVisible();
  await jobRow.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(jobRow.getByText("Veröffentlicht")).toBeVisible();

  // Die Person legt ein Profil an — OHNE es öffentlich freizugeben.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Bewerber");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  // Das Unternehmen sieht sie nicht: nichts ist freigegeben.
  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  // Bewerben.
  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(jobTitle);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  const jobCard = candidate.locator("li").filter({ hasText: jobTitle });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(jobCard).toBeVisible();
  await jobCard.getByRole("button", { name: /^Bewerben$/ }).click();
  await jobCard.getByRole("button", { name: /Bewerbung abschicken/i }).click();
  await expect(jobCard.getByText(/Bewerbung abgeschickt/i)).toBeVisible();

  // Jetzt sieht das Unternehmen das Profil — allein wegen der Bewerbung.
  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toBeVisible();

  // Zurückziehen.
  await candidate.goto("/applications");
  const applicationRow = candidate.locator("li").filter({ hasText: /Abgeschickt/ });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(applicationRow).toBeVisible();
  await applicationRow.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(candidate.getByText(/sieht deine Daten nicht mehr/i)).toBeVisible();

  // Und weg.
  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
