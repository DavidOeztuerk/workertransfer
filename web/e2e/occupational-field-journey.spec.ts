// Die Reise, die PBI-1 abnimmt: ein Konto im Handwerk sieht kein GitHub,
// sondern Nachweise — und die Route bleibt trotzdem erreichbar.
//
// Warum an der laufenden Anlage und nicht in einer Komponentenreihe:
//
//   1. Zwischen dem Auswahlfeld im Browser und der Spalte `users.berufsfeld`
//      liegen der Draht (`occupational_field`, snake_case), der Befehl, das
//      Aggregat und `GET /auth/session`. Ein Feld, das hier in camelCase
//      abginge, käme nie an — und jede andere Reihe bliebe grün, weil keine
//      diese Naht überquert.
//   2. „Verstecken ist keine Zugriffskontrolle" lässt sich nur hier zeigen:
//      der Menüeintrag ist weg UND die Seite antwortet. Ein Komponententest
//      kennt keine Routen.
//   3. Die Rücknahme in den Einstellungen muss den KOPF bewegen, nicht nur den
//      Speicher.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

test("ein Konto im Handwerk bekommt Nachweise statt GitHub", async ({ page }) => {
  const email = uniqueEmail("handwerk.example");

  await registerAndConfirm(page, email, "E2E Handwerk", undefined, "Handwerk");
  await login(page, email);

  // 1. Das Kontomenü bietet GitHub nicht mehr an.
  await page.goto("/overview");
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "Lebenslauf" })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toHaveCount(0);
  await page.keyboard.press("Escape");

  // 2. Stattdessen nennt die Profilseite die Nachweise, die zu dieser Arbeit
  //    gehören — Gesellenbrief und Meisterbrief stehen dort, wo vorher nur ein
  //    Repositorium denkbar war.
  await page.goto("/profile");
  await expect(
    page.getByRole("heading", { name: "Nachweise, die zu deiner Arbeit passen" })
  ).toBeVisible();
  await expect(page.getByText("Meisterbrief", { exact: true })).toBeVisible();
  await expect(page.getByText("Gesellenbrief", { exact: true })).toBeVisible();
  // Das Allgemeine erbt jedes Feld: ein Arbeitszeugnis hat jeder.
  await expect(page.getByText("Arbeitszeugnis", { exact: true })).toBeVisible();

  // 3. ES VERBIRGT, ES SCHÜTZT NICHT. Wer die Adresse kennt, bekommt die Seite
  //    — unverändert. Eine Navigation, die man für eine Zugriffskontrolle
  //    hält, ist gefährlicher als gar keine.
  await page.goto("/github");
  await expect(
    page.getByRole("heading", { name: "GitHub verbinden" })
  ).toBeVisible();

  // 4. Die Wahl lässt sich ändern, und der Kopf folgt ihr sofort. Ohne diesen
  //    Schritt sähe eine Person ihre Wahl bis zum nächsten Laden als wirkungslos.
  await page.goto("/settings");
  await page.getByRole("combobox", { name: "Berufsfeld" }).click();
  await page.getByRole("option", { name: "IT und Software", exact: true }).click();
  await expect(page.getByText("Berufsfeld gespeichert.")).toBeVisible();

  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toBeVisible();
});

test("ein Konto ohne Berufsfeld sieht die heutige Ansicht", async ({ page }) => {
  const email = uniqueEmail("neutral.example");

  // Kein Berufsfeld gewählt — die Vorauswahl bleibt stehen.
  await registerAndConfirm(page, email, "E2E Neutral");
  await login(page, email);

  // Die Zusage, an der diese Arbeit gemessen wird: es wird niemandem etwas
  // weggenommen, der nichts gewählt hat.
  await page.goto("/overview");
  await page.getByRole("button", { name: "Mein Konto" }).click();
  await expect(page.getByRole("menuitem", { name: "GitHub" })).toBeVisible();
  await page.keyboard.press("Escape");

  // Und kein leerer Nachweiskasten auf dem Profil: ein ausgegrauter Bereich
  // wäre die stillschweigende Behauptung, dort fehle etwas (ADR-0022 §3).
  await page.goto("/profile");
  await expect(
    page.getByRole("heading", { name: "Nachweise, die zu deiner Arbeit passen" })
  ).toHaveCount(0);
});
