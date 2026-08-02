// Der Lebenslauf durch den Browser: anfragen, freigeben, zurückziehen.
//
// Was der Integrationstest auf HTTP-Ebene belegt, prüft dieser Test dort, wo es
// ankommt — ein Knopf, der die Anfrage nicht stellt, wäre auf beiden Ebenen
// darunter unsichtbar.

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
  await expect(page.getByText(/bestätigt|aktiv|anmelden/i).first()).toBeVisible();
}

async function login(page: import("@playwright/test").Page, email: string) {
  await page.goto("/login");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).fill(PASSWORD);
  await page.getByRole("button", { name: /Anmelden/i }).click();
  await expect(page.getByRole("link", { name: /Mein Profil/i })).toBeVisible();
}

test("ein Lebenslauf erreicht nur das Unternehmen, dem er freigegeben wurde", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const headline = `E2E CV-Kandidat ${Date.now()}`;
  const employer = `Frühere Firma ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  // Profil anlegen und freigeben — ohne das darf niemand nach dem Lebenslauf
  // fragen, sonst wäre die Anfrage ein Kanal, um die Existenz zu erfahren.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  await candidate.goto("/resume");
  await candidate.getByRole("button", { name: /Station hinzufügen/i }).click();
  await candidate.getByLabel(/Arbeitgeber/i).fill(employer);
  await candidate.getByLabel(/Position/i).fill("Backend-Entwicklerin");
  await candidate.getByLabel("Von").fill("2020-01");
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Lebenslauf gespeichert/i)).toBeVisible();

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

  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await card.getByRole("button", { name: /Lebenslauf anfragen/i }).click();
  await expect(card.getByText(/Anfrage gestellt/i)).toBeVisible();

  // Die Person entscheidet — vorher gibt es nichts zu sehen.
  await candidate.goto("/resume");
  const row = candidate.locator("li").filter({ hasText: /fragt nach deinem Lebenslauf/i });
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Zurückziehen wirkt sofort; die Anfrage selbst bleibt als Vorgang stehen.
  await row.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(row.getByText(/zurückgezogen/i)).toBeVisible();
  await expect(row.getByRole("button", { name: /Zurückziehen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
