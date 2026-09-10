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

  // Umschalten auf Französisch. ANGEMELDET liegt die Wahl im Kontomenü unter
  // dem Nutzersymbol — abgemeldet hinter einem Zahnrad, weil es dann kein
  // Nutzersymbol gibt. Beides steht oben und nicht im Fuss: wer die Sprache
  // wechselt, WEIL er die Oberfläche nicht lesen kann, scrollt nicht erst an
  // das Seitenende.
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await page.getByRole("menuitemradio", { name: "Français" }).click();

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

  // Jetzt der Teil, den nur der Server kann — und die Doppelanmeldung ist dafür
  // der schärfste Beleg, den es hier gibt.
  //
  // `POST /auth/register` auf eine bereits vergebene Adresse antwortet
  // absichtlich wie auf eine freie und schickt dem ECHTEN INHABER eine
  // Warnung. Diese Anfrage hat keine Sitzung, und ihr Kopf sagt `de-DE` —
  // Playwright setzt ihn aus `locale`. Kommt die Mail trotzdem französisch an,
  // kann sie das nur aus der Spalte haben.
  //
  // (`resend-verification` wäre der naheliegendere Weg und ist der falsche: an
  // ein bestätigtes Konto schickt der Endpunkt bewusst gar nichts.)
  const seit = Date.now();

  const answer = await page.request.post("/auth/register", {
    data: { email, password: "geheim-und-lang-genug", display_name: "Doppelt" },
  });
  expect(answer.status()).toBe(201);

  // `after` ist nicht kosmetisch: ohne ihn fände die Suche die deutsche
  // Bestätigungsmail von vorhin, und der Test wäre grün, ohne etwas zu belegen.
  const mail = await lastMailFor(email, { after: seit });
  expect(mail).not.toBeNull();
  expect(mail!.subject).toContain("Tentative d’inscription");

  // Zurück auf Deutsch, damit dieselbe Anlage für die nächste Reise nicht in
  // einem Zustand steht, den niemand erwartet. Die Sprache heisst jetzt
  // „Langue" — die Oberfläche ist ja französisch.
  await page.goto("/market");
  // Das Zahnrad heisst jetzt französisch — die Oberfläche ist es ja.
  await page.getByRole("button", { name: "Affichage et langue" }).click();
  await page.getByRole("menuitemradio", { name: "Deutsch" }).click();
  await expect(page.getByRole("heading", { name: "Mein Marktstatus" })).toBeVisible();
});
