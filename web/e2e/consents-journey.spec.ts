// „Meine Freigaben" durch den echten Stack.
//
// Der Punkt der Seite ist, dass sie ALLES zeigt — auch das, was auf keiner
// Fachseite auftaucht. Deshalb entstehen hier Freigaben über drei verschiedene
// Wege (Profilschalter, Marktstatus-Anfrage, Portfolio-Schalter), und die Seite
// muss sie alle drei kennen.

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
  await candidate.goto("/consents");
  await expect(candidate.getByText(/Niemand sieht etwas von dir/)).toBeVisible();

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  /*
    Die Profilfreigabe steht auf dieser Seite jetzt als SCHALTER, nicht als
    Zeile mit „Zurückziehen": sie lässt sich hier auch ERTEILEN, und dieselbe
    Freigabe zweimal zu zeigen — einmal als Schalter, einmal als Zeile — liesse
    niemanden wissen, welche der beiden gilt.

    Der Prüfgegenstand bleibt: was gilt, steht hier. Nur eben als Zustand eines
    Schalters statt als Eintrag einer Liste.
  */
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();

  // Ein Unternehmen holt sich eine empfängerbezogene Freigabe.
  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // Ein Unternehmensprofil, damit die Seite einen Namen statt einer UUID zeigt.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: "Speichern", exact: true }).click();
  // Auf die BESTAETIGUNG warten, nicht auf `/gespeichert/i`: der Knopf heisst
  // waehrend des Speicherns „Wird gespeichert…", und das Muster traf ihn. Die
  // Reise zog dann weiter, waehrend die Anfrage noch lief — `goto()` bricht sie
  // ab, und das Profil wurde nie geschrieben. Gemessen: die Seite zeigte
  // spaeter „Marktstatus · Ein Unternehmen" statt des Firmennamens, und in
  // `company_profiles` stand keine Zeile.
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(card).toBeVisible();
  // Erst warten, dann klicken. `click()` hat nur das actionTimeout (15 s);
  // unter Last — elf Container, Vite und Chromium auf einer Maschine — braucht
  // die Kandidatenliste länger, und der Test scheiterte am Klick statt am
  // Prüfgegenstand. Genau so ist er zweimal umgefallen.
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  await candidate.goto("/market");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Beide Freigaben stehen auf EINER Seite — die globale als Schalter, die
  // empfängerbezogene als Zeile mit Firmennamen statt UUID.
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toBeVisible();

  // Zurückziehen wirkt sofort — der nächste Zugriff des Unternehmens ist leer.
  const marketEntry = candidate.locator("li").filter({ hasText: "Marktstatus ·" });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(marketEntry).toBeVisible();
  await marketEntry.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toHaveCount(0);

  await recruiter.goto("/candidates");
  const afterCard = recruiter.locator("li").filter({ hasText: headline });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(afterCard).toBeVisible();
  await expect(afterCard.getByText(/Marktstatus gerade nicht einsehbar/)).toBeVisible();

  // Das Profil steht weiterhin da: zurückgezogen wurde genau eine Freigabe.
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();

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
  await visible.getByLabel("Standort", { exact: true }).fill("Berlin");
  await visible.getByRole("button", { name: "Speichern", exact: true }).click();
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
  await hidden.getByLabel("Standort", { exact: true }).fill("Berlin");
  await hidden.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(hidden.getByText(/Profil gespeichert/i)).toBeVisible();
  // Kein Klick auf den Schalter: dieses Profil bleibt verborgen.

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

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


test("die Auskunft nennt jeden Abschnitt — auch die leeren", async ({ browser }) => {
  const email = uniqueEmail("kandidat.example");
  const stamp = Date.now();
  const headline = `E2E Auskunft ${stamp}`;

  const context = await browser.newContext();
  const person = await context.newPage();
  await registerAndConfirm(person, email, "E2E Auskunft");
  await login(person, email);

  await person.goto("/profile");
  await person.getByLabel(/Überschrift/i).fill(headline);
  await person.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(person.getByText(/Profil gespeichert/i)).toBeVisible();
  await person.getByRole("switch").click();
  await expect(person.getByRole("switch")).toBeChecked();

  await person.goto("/my-data");

  // Jeder Abschnitt steht da — auch die, zu denen es nichts gibt. „Kein
  // Lebenslauf" ist eine Auskunft und fehlt sonst.
  //
  // Die Aufstellung ist ein `<dl>`: der Name im `<dt>`, die Auskunft im `<dd>`
  // daneben. Vorher war beides ein Textknoten („profil — enthalten"), und eine
  // Prüfung darauf konnte nicht scheitern, wenn die ZUORDNUNG verrutscht. Diese
  // Hilfe bewegt sich an derselben Beziehung, die auch ein Vorleser nutzt.
  const auskunft = (abschnitt: string) =>
    person.getByText(abschnitt, { exact: true }).locator("xpath=following-sibling::dd[1]");
  await expect(auskunft("profil")).toHaveText("enthalten");
  await expect(auskunft("lebenslauf")).toHaveText("enthalten");
  await expect(auskunft("portfolio")).toHaveText("enthalten");
  await expect(auskunft("freigaben verlauf")).toHaveText("enthalten");
  // Nichts fehlt: bei laufendem Stack gibt es keine Warnung.
  await expect(person.getByRole("alert")).toHaveCount(0);

  // Die Datei entsteht im Browser — der Download beweist, dass sie zustande kommt.
  const download = person.waitForEvent("download");
  await person.getByRole("button", { name: /Als JSON herunterladen/i }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/^workertransfer-meine-daten-\d{4}-\d{2}-\d{2}\.json$/);

  await context.close();
});


// Die Formulierungshilfe (ADR-0024). Was hier prüfbar ist, ist NICHT der
// Anbieter — das bräuchte ein Geheimnis und würde Geld kosten. Prüfbar ist der
// Zustand „nicht eingerichtet", und der ist in jeder Umgebung ohne Schlüssel
// der echte: kein Absturz, kein leerer Text, sondern eine Seite, die es sagt,
// und ein Profiltext, der unverändert dasteht.
test("ohne eingerichteten Anbieter bleibt der eigene Text unangetastet", async ({ page }) => {
  const email = uniqueEmail("hilfe.example");
  await registerAndConfirm(page, email, "E2E Hilfe");
  await login(page, email);

  await page.goto("/profile");
  await page.getByLabel(/Überschrift/i).fill("Pflegefachkraft");
  await page.getByLabel(/Über mich/i).fill("Mein eigener Text.");
  await page.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(page.getByText(/Profil gespeichert/i)).toBeVisible();

  await page.getByRole("button", { name: /Vorschlag holen/i }).click();

  // Der Compose-Stack setzt WORKER_ANTHROPIC_API_KEY leer, und CI hat keinen
  // Schlüssel — der geprüfte Zustand ist hier also immer „nicht eingerichtet".
  // Wer lokal mit ANTHROPIC_API_KEY startet, prüft mit diesem Test etwas
  // anderes; das ist der Preis dafür, dass die Reihe kein Geheimnis braucht.
  //
  // Eine `.or()`-Bedingung stand hier zuerst und war wertlos: das Textfeld ist
  // immer sichtbar, also traf sie immer.
  await expect(page.getByRole("alert")).toHaveText(/nicht verfügbar/i);
  // Und der Text der Person steht unverändert da — kein leeres Feld, kein
  // halber Entwurf.
  await expect(page.getByLabel(/Über mich/i)).toHaveValue("Mein eigener Text.");
});

test("GitHub verbinden verlangt einen Nachweis — und rechnet keine Note", async ({ browser }) => {
  const email = uniqueEmail("kandidat.example");
  const stamp = Date.now();

  const context = await browser.newContext();
  const person = await context.newPage();
  await registerAndConfirm(person, email, "E2E GitHub");
  await login(person, email);

  await person.goto("/github");

  // Die Zusage steht auf der Seite, auf der jemand sein Konto verbindet — nicht
  // in einer ADR, die er nie liest.
  await expect(person.getByText(/Belege, keine Noten/)).toBeVisible();
  await expect(person.getByText(/kein Abgleich im Hintergrund/)).toBeVisible();

  // DER SCHNELLE WEG STEHT VORN und verlangt keinen Namen: GitHub meldet das
  // Konto selbst. Er ist hier nur zu sehen, nicht zu gehen — die Reise darf
  // github.com nicht aufrufen.
  await expect(
    person.getByRole("button", { name: "Mit GitHub anmelden" })
  ).toBeVisible();

  // Der Weg von Hand liegt darunter und wird aufgeklappt.
  await person.getByRole("button", { name: /Oder von Hand/ }).click();
  await person.getByLabel(/GitHub-Benutzername/).fill(`e2e-nutzer-${stamp}`);
  await person.getByRole("button", { name: "Weiter" }).click();

  // Ohne Gist keine Verbindung: ein Feld „mein GitHub-Name" ohne Nachweis wäre
  // eine Einladung, sich mit fremder Arbeit zu schmücken.
  const challenge = person.locator(".github__challenge");
  await expect(challenge).toBeVisible();
  await expect(challenge).toHaveText(/^workertransfer-verify-/);

  await person.getByRole("button", { name: "Nachweis prüfen" }).click();
  // GitHub kennt diesen Benutzer nicht — also bleibt es bei „nicht bewiesen",
  // und nichts wird gezeigt.
  await expect(person.getByRole("alert")).toBeVisible();
  // NACH DEM NEULADEN ist der Weg von Hand wieder zu — der Gist draengt sich
  // niemandem auf. Was offen dasteht, ist das genannte Konto samt Ausweg: wer
  // sich vertippt hat, scheitert sonst an der Anmeldung (der Namensvergleich
  // lehnt ab) und faende den Knopf dagegen hinter einem Aufklapper.
  await person.reload();
  await expect(person.getByText(/Genanntes Konto/)).toBeVisible();
  await expect(person.getByRole("button", { name: "Anderes Konto" })).toBeVisible();
  await expect(person.locator(".github__challenge")).toBeHidden();

  await person.getByRole("button", { name: /Oder von Hand/ }).click();
  await expect(person.locator(".github__challenge")).toBeVisible();

  await context.close();
});
