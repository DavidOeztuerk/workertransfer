// „Meine Freigaben" durch den echten Stack.
//
// Der Punkt der Seite ist, dass sie ALLES zeigt — auch das, was auf keiner
// Fachseite auftaucht. Deshalb entstehen hier Freigaben über drei verschiedene
// Wege (Profilschalter, Marktstatus-Anfrage, Portfolio-Schalter), und die Seite
// muss sie alle drei kennen.

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

test("eine Seite zeigt alle Freigaben — auch die, die anderswo nicht auftauchen", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Freigaben ${Date.now()}`;
  const headline = `E2E Freigaben-Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  // Vorher steht dort nichts — und das steht auch so da.
  await candidate.goto("/freigaben");
  await expect(candidate.getByText(/Niemand sieht etwas von dir/)).toBeVisible();

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  await candidate.goto("/freigaben");
  await expect(candidate.getByText(/Profil · Alle Unternehmen/)).toBeVisible();

  // Ein Unternehmen holt sich eine empfängerbezogene Freigabe.
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
  await expect(recruiter.getByRole("link", { name: /Kandidaten/i })).toBeVisible();

  // Ein Unternehmensprofil, damit die Seite einen Namen statt einer UUID zeigt.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: /Speichern/i }).click();
  await expect(recruiter.getByText(/gespeichert/i)).toBeVisible();

  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  await candidate.goto("/markt");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Beide Freigaben stehen auf EINER Seite — mit Firmennamen, nicht mit UUID.
  await candidate.goto("/freigaben");
  await expect(candidate.getByText(/Profil · Alle Unternehmen/)).toBeVisible();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toBeVisible();

  // Zurückziehen wirkt sofort — der nächste Zugriff des Unternehmens ist leer.
  const marketEntry = candidate.locator("li").filter({ hasText: "Marktstatus ·" });
  await marketEntry.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toHaveCount(0);

  await recruiter.goto("/candidates");
  const afterCard = recruiter.locator("li").filter({ hasText: headline });
  await expect(afterCard.getByText(/Marktstatus gerade nicht einsehbar/)).toBeVisible();

  // Das Profil steht weiterhin da: zurückgezogen wurde genau eine Freigabe.
  await candidate.goto("/freigaben");
  await expect(candidate.getByText(/Profil · Alle Unternehmen/)).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
