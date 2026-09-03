// Die EINE Reise, die die Sprache wirklich umschaltet.
//
// Alle anderen Reisen fahren mit `locale: "de-DE"` aus `playwright.config.ts`
// und suchen deutsche Texte. Das ist Absicht: ohne die Festlegung hinge ihr
// Ergebnis am Systemgebietsschema des Rechners, auf dem sie laufen, und ein
// Test, dessen Ergebnis von der Umgebung abhängt, sagt nichts.
//
// Diese hier schaltet selbst um und prüft die zwei Dinge, die man nur an der
// laufenden Anlage sehen kann:
//
//   1. Die Oberfläche wechselt wirklich — nicht nur der Store, sondern der Text
//      auf der Seite. Ein Katalogtest sagt, dass eine Übersetzung EXISTIERT; er
//      sagt nicht, dass irgendwer sie zeichnet.
//   2. Die MAIL folgt der Wahl. Das ist der eigentliche Grund für die Spalte am
//      Konto: die Bestätigungsmail schreibt der Server, und Tage später schreibt
//      er die Löschbestätigung ohne jede Anfrage. Stünde die Sprache am
//      `Accept-Language`, wäre genau die letzte Nachricht deutsch.

import { expect, test } from "@playwright/test";

import {
  lastMailFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("die Sprachwahl wechselt die Oberfläche und erreicht die Mail", async ({ page }) => {
  const email = uniqueEmail("sprache.example");

  await registerAndConfirm(page, email, "E2E Sprache");
  await login(page, email);

  // Deutsch ist die Ausgangslage — der Browser fährt mit `de-DE`.
  await page.goto("/market");
  await expect(page.getByRole("heading", { name: "Mein Marktstatus" })).toBeVisible();

  // Umschalten auf Französisch. Der Wähler steht im Kopf und heisst dort noch
  // „Sprache", weil die Oberfläche in diesem Moment deutsch ist.
  await page.getByLabel("Sprache").selectOption("fr");

  // Der Beleg, dass wirklich gezeichnet wird und nicht nur der Store umfiel.
  await expect(
    page.getByRole("heading", { name: "Mon statut sur le marché" })
  ).toBeVisible();

  // Und dass es das Neuladen überlebt: die Wahl liegt im lokalen Speicher UND
  // am Konto, und beide Wege müssen dasselbe ergeben.
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Mon statut sur le marché" })
  ).toBeVisible();

  // Jetzt der Teil, den nur der Server kann. `resend-verification` schickt eine
  // Bestätigungsmail an ein bereits bestätigtes Konto — der Endpunkt antwortet
  // absichtlich gleich, ob die Adresse bekannt ist oder nicht, und schickt der
  // bekannten eine Mail. Das ist der kürzeste Weg zu einer echten Zustellung.
  //
  // Der Browser sagt weiterhin `de-DE`. Wenn hier Französisch ankommt, ist es
  // die Spalte gewesen und nicht der Kopf.
  // Der Zeitpunkt VOR der Anfrage: sonst fände `lastMailFor` die deutsche
  // Bestätigungsmail von vorhin und der Test wäre grün, ohne etwas zu belegen.
  const seit = Date.now();

  const antwort = await page.request.post("/auth/resend-verification", {
    data: { email },
  });
  expect(antwort.ok()).toBeTruthy();

  const mail = await lastMailFor(email, { after: seit });
  expect(mail).not.toBeNull();
  expect(mail!.subject).toContain("Merci de confirmer");

  // Zurück auf Deutsch, damit dieselbe Anlage für die nächste Reise nicht in
  // einem Zustand steht, den niemand erwartet. Die Sprache heisst jetzt
  // „Langue" — die Oberfläche ist ja französisch.
  await page.goto("/market");
  await page.getByLabel("Langue").selectOption("de");
  await expect(page.getByRole("heading", { name: "Mein Marktstatus" })).toBeVisible();
});
