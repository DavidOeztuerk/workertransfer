// Der Berater durch den echten Stapel — und zwar an den zwei Stellen, an denen
// er von allem anderen abweicht.
//
// ERSTENS: das Mandat ist eine SICHT. Was eine Firma sieht, steht im Ledger und
// nicht in einer Spalte dieses Dienstes. Diese Reise gibt Stufe für Stufe frei
// und liest jedes Mal auf der Firmenseite nach — und sie prüft, dass das, was
// nicht freigegeben ist, gar nicht erst dasteht.
//
// ZWEITENS: ein Rückzug wirkt bei der nächsten Anfrage. Dieselbe Firmenseite,
// dieselbe Person, nichts mehr.
//
// Der Draht ist der Grund, warum es diese Reise geben muss: `salary_min` als
// `salaryMin` gebunden käme nie an, und jede Handler- und Komponentenreihe
// bliebe grün.

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

test("drei Stufen, und was nicht freigegeben ist, steht nicht da", async ({ browser }) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("berater.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Berater ${stamp}`;
  const headline = `E2E Berater-Kandidat ${stamp}`;

  // --- Die Person: Profil, Freigabe, Mandat -------------------------------

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Beraterin");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Fähigkeiten/i).fill(`Beraten${stamp}`);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // „Fuer alle Unternehmen" — sonst findet der Scout sie gar nicht.
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // Das Mandat. VIER Werte, und keiner davon ist eine Sichtbarkeit.
  await candidate.goto("/advisor");
  await candidate.getByLabel(/Frühester Eintritt/i).fill("2026-11");
  await candidate.getByLabel(/Gehalt ab/i).fill("4000");
  await candidate.getByLabel(/Gehalt bis/i).fill("5200");
  await candidate.getByLabel(/Pensum/i).fill("80");
  await candidate.getByRole("button", { name: /Mandat speichern/i }).click();
  await expect(candidate.getByText(/^Gespeichert\.$/)).toBeVisible();

  // --- Das Unternehmen eröffnet ein Gespräch ------------------------------

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Werber", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(`Beraten${stamp}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  const karte = recruiter.locator("li").filter({ hasText: headline });
  await expect(karte).toBeVisible();

  await karte.getByRole("button", { name: /Gespräch eröffnen/i }).click();
  await expect(karte.getByText(/Gespräch eröffnet/i)).toBeVisible();

  // --- Stufe 1: Eintritt und Pensum, sonst nichts -------------------------

  await recruiter.goto("/company/advisor");
  await expect(recruiter.getByText(/2026-11/)).toBeVisible();

  // DIE ZUSAGE: die Gehaltsspanne ist nicht da. Nicht leer, nicht gesperrt,
  // nicht „noch nicht freigegeben" — gar nicht.
  await expect(recruiter.getByText(/4000/)).toHaveCount(0);
  await expect(recruiter.getByText(/E2E Beraterin/)).toHaveCount(0);
  await expect(recruiter.getByText(/gesperrt|noch nicht freigegeben/i)).toHaveCount(0);

  // --- Stufe 2: die Spanne kommt dazu ------------------------------------

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Stufe 2 freigeben/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(/4000–5200/)).toBeVisible();
  await expect(recruiter.getByText(/E2E Beraterin/)).toHaveCount(0);

  // --- Stufe 3: Klarname und Adresse -------------------------------------

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Stufe 3 freigeben/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(candidateEmail)).toBeVisible();

  // --- Eine Rücknahme wirkt bei der nächsten Anfrage ----------------------
  //
  // ZURÜCK AUF STUFE 2: Klarname und Adresse verschwinden, die Spanne bleibt.
  // Das ist der scharfe Fall — nicht „alles weg", sondern genau die eine
  // Freigabe, die zurückgenommen wurde, und sofort.

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Auf Stufe 2 zurück/i }).click();

  await recruiter.reload();
  await expect(recruiter.getByText(/4000–5200/)).toBeVisible();
  await expect(recruiter.getByText(candidateEmail)).toHaveCount(0);

  // --- Und ganz zurück: das Gespräch fällt aus der Liste -------------------
  //
  // ERST DAS ÖFFENTLICHE PROFIL AUS. Das ist keine Umständlichkeit, sondern
  // die Zusage selbst: ein Knopf in EINEM Gespräch legt keinen plattformweiten
  // Schalter um. Solange „für alle Unternehmen" steht, steht auch Stufe 1 —
  // und das ist richtig, denn sichtbar ist die Person dann wirklich.

  await candidate.goto("/profile");
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await candidate.goto("/advisor");
  await candidate.getByRole("button", { name: /Auf Stufe 1 zurück/i }).click();
  await candidate.getByRole("button", { name: /Freigabe zurücknehmen/i }).click();

  await recruiter.reload();
  // Auf Stufe 0 gibt es das Gespraech fuer die Gegenseite nicht. Eine leere
  // Zeile waere der „gesperrt"-Hinweis in Listenform.
  await expect(recruiter.getByText(/Es läuft gerade kein Gespräch/)).toBeVisible();
  await expect(recruiter.getByText(/2026-11/)).toHaveCount(0);

  // Die Person behaelt es und kann wieder freigeben — eine Ruecknahme, die die
  // Zeile mit wegnaehme, waere eine Einbahnstrasse.
  await candidate.goto("/advisor");
  await expect(candidate.getByRole("button", { name: /Stufe 1 freigeben/i })).toBeVisible();

  await candidateContext.close();
  await recruiterContext.close();
});

test("wer ein Unternehmen ausschliesst, verliert „für alle Unternehmen“", async ({
  browser,
}) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("ausschluss.example");
  const headline = `E2E Ausschluss ${stamp}`;

  const context = await browser.newContext();
  const candidate = await context.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Ausschliessende");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // DIE ABNAHME AUS PBI-4, sichtbar gemacht: der Ledger kennt keine
  // Verneinung, also gibt es den Modus „alle" nicht mehr, sobald jemand ein
  // Unternehmen nennt. Danach ist die Person nur noch fuer Unternehmen
  // sichtbar, denen sie einzeln freigibt — und der jetzige Arbeitgeber ist
  // keines davon.
  await candidate.goto("/advisor");
  await candidate.getByLabel(/Diese Unternehmen nicht/i).fill("arbeitgeber.test");
  await candidate.getByRole("button", { name: /Mandat speichern/i }).click();
  await expect(candidate.getByText(/^Gespeichert\.$/)).toBeVisible();

  // Der Schalter auf der Profilseite steht danach AUS — im Ledger, nicht nur
  // in der Anzeige.
  await candidate.goto("/profile");
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await context.close();
});
