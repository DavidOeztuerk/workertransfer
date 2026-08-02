// Der Einladungsfluss durch den Browser: einladen, Mail abholen, beitreten.
//
// Der Kern ist die Stelle, die nur hier sichtbar wird — der Link kommt per
// Mail, und die eingeladene Person muss ihn mit dem richtigen Konto öffnen.

import { expect, test } from "@playwright/test";

import {
  invitationTokenFor,
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
  // Genau die Erfolgsmeldung der Bestätigungsseite. Ein weiches
  // /anmelden/i träfe den "Anmelden"-Link in der Kopfzeile und ließe eine
  // GESCHEITERTE Bestätigung wie eine geglückte aussehen — der Test wäre
  // dann erst viel später und an falscher Stelle rot geworden.
  await expect(page.getByRole("heading", { name: /bestätigt/i })).toBeVisible();
}

/**
 * Auf ein Unternehmen wechseln — und warten, bis es wirklich gilt.
 *
 * `selectOption` stößt den Wechsel nur an: der Server stellt ein neues Token
 * aus, das Cookie wird ersetzt, die Sitzung neu geladen. Sofort weiterzuklicken
 * gewinnt das Rennen manchmal und manchmal nicht — die Seite zeigt dann „Wähle
 * oben ein Unternehmen", obwohl eines gewählt wurde. Der Mannschaftslink
 * erscheint erst mit aktivem Unternehmen und ist damit das ehrliche Signal.
 */
async function actAsCompany(page: import("@playwright/test").Page, name: string) {
  await page.goto("/");
  await page.getByLabel(/Handeln als/i).selectOption({ label: name });
  await expect(page.getByRole("link", { name: /Mannschaft/i })).toBeVisible();
}

async function login(page: import("@playwright/test").Page, email: string) {
  await page.goto("/login");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).fill(PASSWORD);
  await page.getByRole("button", { name: /Anmelden/i }).click();
  await expect(page.getByRole("link", { name: /Mein Profil/i })).toBeVisible();
}

test("eine Einladung lässt genau die eingeladene Person herein", async ({ browser }) => {
  const domain = uniqueCompanyDomain();
  const adminEmail = uniqueEmail(domain);
  const colleagueEmail = uniqueEmail(domain);
  const companyName = `E2E Mannschaft ${Date.now()}`;

  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await registerAndConfirm(admin, adminEmail, "E2E Chefin");
  await login(admin, adminEmail);
  await admin.goto("/company/new");
  await admin.getByLabel(/Name des Unternehmens/i).fill(companyName);
  await admin.getByRole("button", { name: /Unternehmen anlegen/i }).click();
  await expect(admin.getByText(/Administrator/i)).toBeVisible();
  await actAsCompany(admin, companyName);
  await admin.goto("/company/team");
  await expect(admin.getByText("E2E Chefin")).toBeVisible();
  await admin.getByLabel(/E-Mail/i).fill(colleagueEmail);
  await admin.getByRole("button", { name: /Einladen/i }).click();
  await expect(admin.getByText(/Einladung verschickt/i)).toBeVisible();
  await expect(admin.getByTestId("invitation-list").getByText(colleagueEmail)).toBeVisible();

  const token = await invitationTokenFor(colleagueEmail);

  const colleagueContext = await browser.newContext();
  const colleague = await colleagueContext.newPage();
  await registerAndConfirm(colleague, colleagueEmail, "E2E Kollege");
  await login(colleague, colleagueEmail);
  await colleague.goto(`/invitation?token=${token}`);
  // Die Überschrift, nicht irgendein Text: der Firmenname steht nach dem
  // Beitritt auch als <option> im Unternehmenswechsler, und eine <option>
  // gilt als versteckt — der Test scheiterte dann mit "Received: hidden",
  // obwohl die Seite genau das zeigte, was sie sollte.
  await expect(
    colleague.getByRole("heading", { name: new RegExp(companyName) })
  ).toBeVisible();

  // Der Beitritt wechselt die Sitzung NICHT — das muss die Person selbst tun.
  await actAsCompany(colleague, companyName);
  await colleague.goto("/company/team");
  await expect(colleague.getByText("E2E Chefin")).toBeVisible();
  await expect(colleague.getByText("E2E Kollege")).toBeVisible();

  // Ein Mitglied darf nicht einladen — das Formular wird gar nicht angeboten.
  await expect(colleague.getByRole("button", { name: /Einladen/i })).toHaveCount(0);

  await adminContext.close();
  await colleagueContext.close();
});
