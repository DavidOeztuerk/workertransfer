// Die Reise, die Phase 3 und Phase 4 verbindet.
//
// Bewerben gibt einem Unternehmen Zugriff auf die eigenen Daten; zurückziehen
// nimmt ihn. Was der Integrationstest auf HTTP-Ebene belegt, prüft dieser Test
// dort, wo es ankommt — inklusive der Frage, ob die Oberfläche das Zurückziehen
// überhaupt anbietet.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  waehleImFeld,
} from "./stack";

skipWithoutStack();

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
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();
  await recruiter.goto("/company/jobs/new");
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
  // „Bewerben" ist ein LINK auf eine eigene Adresse, kein Knopf, der in der
  // Karte etwas aufklappt. Der Klick verlässt die Liste — und dass das durch
  // Router UND Gateway wirklich funktioniert, prüft nur diese Reise: im
  // Browsertest ist die Route gemockt, und ein `Sec-Fetch-Dest`-Fehler zeigt
  // sich ausschließlich am echten Deep-Link.
  await jobCard.getByRole("link", { name: /^Bewerben$/ }).click();
  await expect(candidate).toHaveURL(/\/jobs\/[0-9a-f-]{36}\/apply$/);
  await expect(candidate.getByRole("heading", { level: 1, name: jobTitle })).toBeVisible();
  // Und derselbe Zustand nach F5. Der Klick allein beweist nichts über das
  // Gateway — der Router schaltet im Browser um, ohne zu fragen. Erst das
  // Neuladen schickt die Adresse wirklich hin, und unter `/jobs/…` liegt
  // zusätzlich das echte `GET /jobs/{id}` des jobs-service: dass die
  // Dokumentregel (`Sec-Fetch-Dest: document`, priority 200) darüber gewinnt,
  // ist zu prüfen und nicht anzunehmen.
  await candidate.reload();
  await expect(candidate.getByRole("heading", { level: 1, name: jobTitle })).toBeVisible();
  await candidate.getByRole("button", { name: /Bewerbung abschicken/i }).click();
  // Auf BEIDE Ausgänge warten — Bestätigung oder Fehlermeldung. Nur auf die
  // Bestätigung zu warten meldet nach 30 Sekunden bloß, dass sie fehlt, und
  // verschweigt, ob die Bewerbung abgelehnt wurde oder ob überhaupt etwas
  // ankam. Dieselbe Lehre wie bei der Anmelde-Hilfe in stack.ts, und dort hat
  // sie einen echten Fehler sichtbar gemacht.
  const sent = candidate.getByText(/Bewerbung abgeschickt/i);
  const rejected = candidate.getByRole("alert");
  await expect(sent.or(rejected).first()).toBeVisible();
  if (await rejected.isVisible()) {
    throw new Error(`Bewerbung abgelehnt: ${await rejected.innerText()}`);
  }
  await expect(sent).toBeVisible();

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
