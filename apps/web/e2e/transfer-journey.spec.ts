// Der Transfermarkt durch den Browser, vom Marktstatus bis zum Abschluss.
//
// Der Weg aus der Definition of Done, einmal ganz: Status setzen → Anfrage →
// Freigabe → Interesse → Gespräch → Angebot → Annahme → Freigabe bestätigen →
// Abschluss. Zwei Browser-Kontexte, weil es zwei Seiten sind und jede nur ihre
// eigenen Knöpfe haben darf.

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

test("ein Transfer entsteht nur aus drei Ja — und der Arbeitgeber wird nie gefragt", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Transfermarkt ${Date.now()}`;
  const headline = `E2E Transfer-Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  // Profil anlegen und freigeben — ohne das darf niemand nach dem Marktstatus
  // fragen, sonst wäre die Anfrage ein Kanal, um die Existenz zu erfahren.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // „Ich höre zu" UND „ich arbeite gerade irgendwo" — der Normalfall auf einem
  // Transfermarkt, und der Weg, auf dem eine Freigabe nötig wird.
  await candidate.goto("/markt");
  await candidate.getByRole("radio", { name: /Ich höre zu/ }).check();
  await candidate.getByRole("checkbox", { name: /Ich arbeite gerade irgendwo/ }).check();
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Marktstatus gespeichert/i)).toBeVisible();

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
  // Warten, bis der Wechsel wirklich gilt: `selectOption` stößt ihn nur an.
  await expect(recruiter.getByRole("link", { name: /Kandidaten/i })).toBeVisible();

  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout.
  await expect(card).toBeVisible();
  // Der Marktstatus ist eine eigene Freigabe, getrennt vom Lebenslauf. Vorher
  // gibt es hier nichts zu sehen und nichts anzubieten.
  await expect(card.getByRole("button", { name: /Interesse zeigen/i })).toHaveCount(0);
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  // Die Startseite sagt der Person, dass etwas ansteht — ohne dass sie danach
  // sucht. Zusammengesetzt im Browser aus vier Diensten.
  await candidate.goto("/");
  await expect(
    candidate.getByText(/Unternehmen möchte(n)? sehen, ob du ansprechbar bist/)
  ).toBeVisible();

  // Die Person entscheidet.
  await candidate.goto("/markt");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Beantwortet heißt: verschwindet aus „Was liegt an".
  await candidate.goto("/");
  await expect(
    candidate.getByText(/Unternehmen möchte(n)? sehen, ob du ansprechbar bist/)
  ).toHaveCount(0);

  // Jetzt erst sieht das Unternehmen den Status — und darf zugehen.
  await recruiter.goto("/candidates");
  const openCard = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(openCard).toBeVisible();
  await expect(openCard.getByText(/Hört zu/)).toBeVisible();
  await openCard.getByRole("button", { name: /Interesse zeigen/i }).click();
  await expect(openCard.getByText(/Interesse hinterlegt/i)).toBeVisible();

  // Die Person nimmt das Gespräch an.
  await candidate.goto("/transfers");
  await expect(
    candidate.getByRole("heading", { name: /Ein Unternehmen hat Interesse/ })
  ).toBeVisible();
  await candidate.getByRole("button", { name: /Gespräch annehmen/i }).click();
  await expect(candidate.getByRole("heading", { name: /Ihr seid im Gespräch/ })).toBeVisible();

  // Das Unternehmen macht ein Angebot. Die Ablöse wird festgehalten, nicht
  // bewegt — die Plattform führt kein Geld.
  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByText(/Braucht eine Freigabe/)).toBeVisible();
  await recruiter.getByLabel(/^Angebot/).fill("Teamleitung Backend");
  await recruiter.getByLabel(/^Start/).fill("2026-11");
  await recruiter.getByLabel(/Ablöse in Euro/).fill("5000");
  await recruiter.getByRole("button", { name: /Angebot machen/i }).click();
  await expect(recruiter.getByRole("heading", { name: /Angebot abgegeben/ })).toBeVisible();

  // Die Person nimmt an — und muss danach selbst bestätigen, dass ihr
  // Arbeitgeber sie gehen lässt. Diese Plattform fragt ihn nicht.
  await candidate.goto("/transfers");
  await expect(candidate.getByText("5.000,00 €")).toBeVisible();
  await candidate.getByRole("button", { name: /Angebot annehmen/i }).click();
  await expect(candidate.getByText(/Diese Plattform fragt ihn nicht/)).toBeVisible();

  // Das Unternehmen kann hier nicht abschließen — und zwar nie. Braucht der
  // Vorgang eine Freigabe, gehört der letzte Schritt der Person: sie ist die
  // einzige, die weiß, ob sie gehen darf.
  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByRole("button", { name: /^Abschließen$/ })).toHaveCount(0);

  // Ihre Bestätigung schließt den Transfer ab.
  await candidate.getByRole("button", { name: /Freigabe bestätigen und abschließen/i }).click();
  await expect(candidate.getByRole("heading", { name: /^Abgeschlossen$/ })).toBeVisible();

  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByRole("heading", { name: /^Abgeschlossen$/ })).toBeVisible();

  // Aus einem endgültigen Ausgang führt kein Weg zurück.
  await expect(recruiter.getByRole("button", { name: /Zurückziehen/i })).toHaveCount(0);
  await candidate.goto("/transfers");
  await expect(candidate.getByRole("button", { name: /Ablehnen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("ohne Freigabe des Marktstatus gibt es nichts zu sehen und nichts zu tun", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Abgelehnt ${Date.now()}`;
  const headline = `E2E Nein-Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  await candidate.goto("/markt");
  await candidate.getByRole("radio", { name: /Ich suche aktiv/ }).check();
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Marktstatus gespeichert/i)).toBeVisible();

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
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  // Die Person lehnt ab. „Sucht aktiv" bleibt damit unsichtbar — und dass sie
  // sucht, ist die heikelste Angabe im ganzen System.
  await candidate.goto("/markt");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Ablehnen/i }).click();
  await expect(row.getByText(/Abgelehnt/i)).toBeVisible();

  await recruiter.goto("/candidates");
  const shut = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(shut).toBeVisible();
  await expect(shut.getByText(/Marktstatus abgelehnt/)).toBeVisible();
  await expect(shut.getByText(/Sucht aktiv/)).toHaveCount(0);
  await expect(shut.getByRole("button", { name: /Interesse zeigen/i })).toHaveCount(0);
  // Und erneut fragen geht nicht: ein Nein, das man umgehen kann, ist keins.
  await expect(shut.getByRole("button", { name: /Marktstatus anfragen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
