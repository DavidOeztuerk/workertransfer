// Die Reise, die Phase 3 und Phase 4 verbindet.
//
// Bewerben gibt einem Unternehmen Zugriff auf die eigenen Daten; zurückziehen
// nimmt ihn. Was der Integrationstest auf HTTP-Ebene belegt, prüft dieser Test
// dort, wo es ankommt — inklusive der Frage, ob die Oberfläche das Zurückziehen
// überhaupt anbietet.

import { expect, test } from "@playwright/test";

import {
  GATEWAY_URL,
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
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  // Das Unternehmen sieht sie nicht: nichts ist freigegeben.
  await recruiter.goto("/scout");
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

  // DER WEG ZUR BEWERBUNGSSEITE HAT SICH GEÄNDERT, ihre Prüfung nicht.
  //
  // „Bewerben" auf der Karte war einmal ein Link auf `/jobs/{id}/apply`; heute
  // ist es ein Knopf, der einen ENTWURF anlegt und dorthin führt — so gewollt.
  // Die Seite `/jobs/{id}/apply` gibt es weiterhin, und sie ist von der
  // Karriereseite aus erreichbar.
  //
  // Was diese Reise als Einzige beweist, hängt aber nicht am Klick, sondern an
  // der ADRESSE: dass ein Deep-Link durch Router UND Gateway ankommt. Im
  // Browsertest ist die Route gemockt, und ein `Sec-Fetch-Dest`-Fehler zeigt
  // sich ausschließlich hier. Also wird die Kennung über die öffentliche
  // Schnittstelle geholt und die Adresse direkt angesteuert — dieselbe
  // Aussage, ohne einen Knopf zu prüfen, den es so nicht mehr gibt.
  const gefunden = await candidate.request.get(
    `${GATEWAY_URL}/jobs?q=${encodeURIComponent(jobTitle)}`);
  const jobId = (await gefunden.json()).items[0].id as string;

  // `/jobs/{id}/apply` IST HEUTE EINE WEICHE, kein Formular mehr: für eine
  // angemeldete Person legt die Seite einen Entwurf an und führt dorthin. Das
  // war ausdrücklich gewollt — wer sich bewirbt, soll beim Anschreiben landen
  // und nicht bei einem leeren Textfeld.
  //
  // Der Deep-Link bleibt trotzdem das, was NUR diese Reise beweist: dass die
  // Adresse durch Router UND Gateway ankommt. Im Browsertest ist die Route
  // gemockt, und ein `Sec-Fetch-Dest`-Fehler zeigt sich ausschliesslich hier.
  await candidate.goto(`/jobs/${jobId}/apply`);
  await expect(candidate).toHaveURL(
    /\/applications\/drafts\/[0-9a-f-]{36}$/, { timeout: 30_000 });

  // Und derselbe Zustand nach F5 — jetzt unter `/applications/…`, wo dieselbe
  // Dokumentregel greifen muss.
  await candidate.reload();
  await expect(candidate.getByRole("heading", { level: 1 })).toBeVisible();

  // Selbst schreiben, freigeben, senden. Die Zusage dieser Reise ist nicht das
  // Formular, sondern die EINWILLIGUNG: das Abschicken zeigt dem Unternehmen
  // das Profil, das Zurückziehen nimmt es weg.
  await candidate.getByRole("button", { name: /Selbst schreiben/i }).click();
  await candidate.getByLabel(/Betreff/i).fill("Bewerbung");
  await candidate
    .getByLabel(/^Text$/i)
    .fill("Sehr geehrte Damen und Herren,\n\nich bewerbe mich.");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();

  await expect(candidate.getByRole("button", { name: /Freigeben/i }))
    .toBeVisible({ timeout: 10_000 });
  await candidate.getByRole("button", { name: /Freigeben/i }).click();
  await expect(candidate.getByRole("button", { name: /Senden/i })).toBeEnabled();
  await candidate.getByRole("button", { name: /Senden/i }).click();
  await expect(candidate.getByRole("button", { name: /Senden/i })).toHaveCount(0);

  // Jetzt sieht das Unternehmen das Profil — allein wegen der Bewerbung.
  await recruiter.goto("/scout");
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
  await recruiter.goto("/scout");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
