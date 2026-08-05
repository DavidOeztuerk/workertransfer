// Stellen durch den Browser — mit der Stelle, die nur hier prüfbar ist:
// eine veröffentlichte Ausschreibung findet auch jemand OHNE Konto.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  switchToCompany,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

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
  await expect(recruiter.locator("summary", { hasText: "Unternehmen" })).toBeVisible();

  // Erst das Unternehmensprofil: ohne es bleibt die Stelle anonym.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: /Speichern/i }).click();
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  await recruiter.goto("/company/jobs");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Hamburg");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(row).toBeVisible();
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
  // Und wer sucht, steht daneben — auch für jemanden ohne Konto.
  await expect(anonymous.getByText(companyName, { exact: true })).toBeVisible();

  // Und dieselbe Stelle steht auf der Karriere-Seite des Unternehmens — einer
  // Adresse, die man weitergeben kann, ohne dass der Empfänger ein Konto
  // braucht. Das Kürzel holt der Recruiter; er ist angemeldet.
  const slug = await recruiter.evaluate(async () => {
    const res = await fetch(
      `${window.location.origin.replace(":5173", ":8008")}/companies/me/profile`,
      { credentials: "include" }
    );
    return ((await res.json()) as { slug: string }).slug;
  });

  await anonymous.goto(`/karriere/${slug}`);
  await expect(anonymous.getByRole("heading", { name: companyName })).toBeVisible();
  await expect(anonymous.getByText(title)).toBeVisible();

  // Geschlossen heißt: für die Öffentlichkeit wieder weg.
  await row.getByRole("button", { name: /Schließen/i }).click();
  await expect(row.getByText("Geschlossen")).toBeVisible();

  // Ausdrücklich zurück zur Suche: der anonyme Browser steht gerade auf der
  // Karriere-Seite, und ein reload() lädt diese neu. Dort gibt es kein
  // Suchfeld, und der Test wartete darauf bis zum Zeitlimit.
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await recruiterContext.close();
  await anonymousContext.close();
});

// Die Passung. Nur hier prüfbar, weil genau das der Punkt ist: sie entsteht
// im Browser aus zwei Antworten verschiedener Dienste und steht in keiner
// einzigen davon. Kein Endpunkt liefert sie, also kann kein Endpunkttest sie
// zeigen.
test("die Passung sieht die Person — und niemand rechnet sie auf dem Server", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Passung ${Date.now()}`;
  const title = `E2E Passung Stelle ${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter");
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/company/new");
  await recruiter.getByLabel(/Name des Unternehmens/i).fill(companyName);
  await recruiter.getByRole("button", { name: /Unternehmen anlegen/i }).click();
  await expect(recruiter.getByText(/Administrator/i)).toBeVisible();
  await switchToCompany(recruiter, companyName);

  await recruiter.goto("/company/jobs");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  // „PostgreSQL" hier, „postgres" gleich im Profil: das Vokabular (ADR-0023)
  // muss beide auf denselben Namen bringen, sonst zeigt der Abgleich der
  // Person eine Lücke, die es nicht gibt.
  await recruiter.getByLabel(/Gesuchte Fähigkeiten/i).fill("Python, PostgreSQL, Go");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  // Ohne Konto: die Anforderungen stehen da, aber niemand wird gezählt.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText("PostgreSQL")).toBeVisible();
  await expect(anonymous.getByText(/von 3 genannten/)).toHaveCount(0);

  // Mit Profil: die Liste wird abgehakt — und sagt, was fehlt.
  const candidateEmail = uniqueEmail("kandidatin.example");
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidatin");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill("Backend-Entwicklerin");
  // Klein geschrieben, mit Leerzeichen — und „postgres" statt „PostgreSQL".
  // Beides darf den Abgleich nicht kosten.
  await candidate.getByLabel(/Fähigkeiten/i).fill("python , postgres");
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  // Sichtbar umbenannt: die Person sieht, was gespeichert wurde. Das ist der
  // Grund, warum das Vokabular nichts erfinden und nichts ablehnen darf — man
  // sieht ja, was es tut.
  await expect(candidate.getByLabel(/Fähigkeiten/i)).toHaveValue(/PostgreSQL/);

  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(title);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  // Zwei von drei — und der zweite Haken ist der, den es ohne das Vokabular
  // nicht gäbe.
  await expect(candidate.getByText(/2 von 3 genannten Fähigkeiten/)).toBeVisible();
  // Keine Prozentzahl, nirgends — und der Name der fehlenden steht da.
  await expect(candidate.locator('[data-match="missing"]')).toHaveText("Go");
  await expect(candidate.getByText(/%/)).toHaveCount(0);

  await recruiterContext.close();
  await anonymousContext.close();
  await candidateContext.close();
});
