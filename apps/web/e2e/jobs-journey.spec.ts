// Stellen durch den Browser — mit der Stelle, die nur hier prüfbar ist:
// eine veröffentlichte Ausschreibung findet auch jemand OHNE Konto.

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

test("eine veröffentlichte Stelle findet auch, wer kein Konto hat", async ({ browser }) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const title = `E2E Stelle ${Date.now()}`;

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
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Hamburg");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  await expect(row.getByText(/Entwurf/)).toBeVisible();

  // Ein anonymer Besucher: eigener Kontext, kein Cookie, keine Anmeldung.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  // Ein Entwurf ist für die Öffentlichkeit nicht vorhanden.
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  await anonymous.reload();
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(title)).toBeVisible();

  // Geschlossen heißt: für die Öffentlichkeit wieder weg.
  await row.getByRole("button", { name: /Schließen/i }).click();
  await expect(row.getByText("Geschlossen")).toBeVisible();

  await anonymous.reload();
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await recruiterContext.close();
  await anonymousContext.close();
});
