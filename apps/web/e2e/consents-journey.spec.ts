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


test("die Suche findet nur, was freigegeben ist", async ({ browser }) => {
  const visibleEmail = uniqueEmail("kandidat.example");
  const hiddenEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Suche ${Date.now()}`;
  const stamp = Date.now();
  const skill = `Suchskill${stamp}`;
  const visibleHeadline = `E2E Sichtbar ${stamp}`;
  const hiddenHeadline = `E2E Verborgen ${stamp}`;

  // Zwei Personen mit DERSELBEN Fähigkeit — eine gibt frei, eine nicht.
  const visibleContext = await browser.newContext();
  const visible = await visibleContext.newPage();
  await registerAndConfirm(visible, visibleEmail, "E2E Sichtbar");
  await login(visible, visibleEmail);
  await visible.goto("/profile");
  await visible.getByLabel(/Überschrift/i).fill(visibleHeadline);
  await visible.getByLabel(/Fähigkeiten/i).fill(skill);
  await visible.getByLabel(/Ort/i).fill("Berlin");
  await visible.getByRole("button", { name: /Speichern/i }).click();
  await expect(visible.getByText(/Profil gespeichert/i)).toBeVisible();
  await visible.getByRole("switch").click();
  await expect(visible.getByRole("switch")).toBeChecked();

  const hiddenContext = await browser.newContext();
  const hidden = await hiddenContext.newPage();
  await registerAndConfirm(hidden, hiddenEmail, "E2E Verborgen");
  await login(hidden, hiddenEmail);
  await hidden.goto("/profile");
  await hidden.getByLabel(/Überschrift/i).fill(hiddenHeadline);
  await hidden.getByLabel(/Fähigkeiten/i).fill(skill);
  await hidden.getByLabel(/Ort/i).fill("Berlin");
  await hidden.getByRole("button", { name: /Speichern/i }).click();
  await expect(hidden.getByText(/Profil gespeichert/i)).toBeVisible();
  // Kein Klick auf den Schalter: dieses Profil bleibt verborgen.

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

  await recruiter.goto("/candidates");
  await recruiter.getByLabel("Fähigkeiten").fill(skill);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  // Der Punkt: derselbe Filter, zwei passende Profile, ein Treffer.
  await expect(recruiter.getByText(visibleHeadline)).toBeVisible();
  await expect(recruiter.getByText(hiddenHeadline)).toHaveCount(0);

  // Und eine Suche ohne Treffer redet über die Suche, nicht über die Plattform.
  await recruiter.getByLabel("Fähigkeiten").fill(`Nichts${stamp}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();
  await expect(recruiter.getByText(/Auf diese Suche passt gerade niemand/)).toBeVisible();

  await visibleContext.close();
  await hiddenContext.close();
  await recruiterContext.close();
});
