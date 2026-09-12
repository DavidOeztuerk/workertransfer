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

test("mehrfach auswählen → entwürfe → kommentieren → freigeben → senden → mappe mit reitern", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const headline = `E2E Bewerber ${Date.now()}`;
  const jobTitle1 = `E2E Stelle 1 ${Date.now()}`;
  const jobTitle2 = `E2E Stelle 2 ${Date.now() + 1}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await waehleImFeld(recruiter, /Handeln als/i, companyName);

  for (const titel of [jobTitle1, jobTitle2]) {
    await recruiter.goto("/company/jobs/new");
    await recruiter.getByLabel("Titel").fill(titel);
    await recruiter.getByLabel(/Beschreibung/i).fill(`${titel} Beschreibung.`);
    await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
    const jobRow = recruiter.locator("li").filter({ hasText: titel });
    await expect(jobRow).toBeVisible();
    await jobRow.getByRole("button", { name: /Veröffentlichen/i }).click();
    await expect(jobRow.getByText("Veröffentlicht")).toBeVisible();
  }

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Bewerber");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill("E2E Stelle");
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  await expect(candidate.locator("li").filter({ hasText: jobTitle1 })).toBeVisible();
  await expect(candidate.locator("li").filter({ hasText: jobTitle2 })).toBeVisible();

  await candidate.locator("li").filter({ hasText: jobTitle1 }).getByRole("checkbox").check();
  await candidate.locator("li").filter({ hasText: jobTitle2 }).getByRole("checkbox").check();
  await expect(candidate.getByText(/2 Stellen ausgewählt/i)).toBeVisible();
  await candidate.getByRole("button", { name: /Für alle bewerben/i }).click();

  await expect(candidate).toHaveURL(/\/applications\/drafts$/);
  // Auf die ZEILE eingegrenzt: der Titel steht in der Liste zweimal — einmal
  // als Überschrift, einmal in der Zeile darunter. `getByText` träfe beide.
  await expect(
    candidate.locator("li").filter({ hasText: jobTitle1 }).first(),
  ).toBeVisible({ timeout: 20_000 });
  await expect(
    candidate.locator("li").filter({ hasText: jobTitle2 }).first(),
  ).toBeVisible();
  await expect(candidate.getByText(/kein Anbieter|Fehlgeschlagen|Wird geschrieben/i).first()).toBeVisible({
    timeout: 20_000,
  });

  // GENAU DEN ENTWURF ÖFFNEN, DER SPÄTER GEPRÜFT WIRD.
  //
  // `.first()` nahm den obersten der Liste, und die ist nach Änderungszeit
  // sortiert — also mal den einen, mal den anderen. Gesendet wurde dann der
  // Entwurf zur zweiten Stelle, geprüft wurde die erste: drei erfolgreiche
  // Sends im Protokoll und trotzdem Rot. Eine Reihenfolge, auf die sich ein
  // Test verlässt, ohne sie zu setzen, ist eine Wette.
  await candidate
    .locator("li")
    .filter({ hasText: jobTitle1 })
    .getByRole("button", { name: /Öffnen/i })
    .click();
  await expect(candidate).toHaveURL(/\/applications\/drafts\/[0-9a-f-]{36}$/);

  await expect(candidate.getByText("Noch kein Anschreiben.")).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Generieren/i })).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Selbst schreiben/i })).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Bearbeiten/i })).toHaveCount(0);
  await expect(candidate.getByRole("button", { name: /^Schreiben$/i })).toHaveCount(0);

  await candidate.getByRole("button", { name: /Selbst schreiben/i }).click();
  await candidate.getByLabel(/Betreff/i).fill("Bewerbung als Entwickler");
  await candidate.getByLabel(/^Text$/i).fill("Sehr geehrte Damen und Herren,\n\nich bewerbe mich.");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();

  await expect(candidate.getByRole("button", { name: /Freigeben/i })).toBeVisible({ timeout: 10_000 });
  await candidate.getByRole("button", { name: /Freigeben/i }).click();

  // GEPRUEFT WIRD DIE FOLGE, NICHT DIE BESCHRIFTUNG.
  //
  // `getByText(/Freigegeben/i)` traf zwei Elemente: ein MUI-Chip rendert den
  // Text in einem `span` INNERHALB eines `div`, und beide enthalten ihn. Eines
  // davon willkuerlich zu greifen (`.first()`) waere kein Test, sondern eine
  // Wette darauf, welches zuerst kommt.
  //
  // Was „freigegeben" fachlich BEDEUTET, ist ohnehin schaerfer: erst jetzt darf
  // gesendet werden. Genau das steht hier.
  await expect(candidate.getByRole("button", { name: /Senden/i })).toBeEnabled();
  await candidate.getByRole("button", { name: /Senden/i }).click();

  // Und „gesendet" heisst: es geht kein zweites Mal hinaus.
  await expect(candidate.getByRole("button", { name: /Senden/i })).toHaveCount(0);

  await recruiter.goto("/company/applications");
  await expect(recruiter.getByText(jobTitle1)).toBeVisible({ timeout: 15_000 });
  await recruiter.getByRole("link", { name: /Mappe öffnen/i }).first().click();
  await expect(recruiter.getByRole("tablist")).toBeVisible();
  await expect(recruiter.getByRole("tab", { name: /Anschreiben/i })).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});
