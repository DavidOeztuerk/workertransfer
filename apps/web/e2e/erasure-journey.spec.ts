// Der ganze Weg der Löschung: verlangen → Kaskade läuft → die Daten sind weg →
// der Nachweis im Ledger steht noch (ADR-0027).
//
// Diese Reise prüft etwas, das keine der anderen prüfen kann: dass eine
// Zusage, die die Oberfläche gibt, am Ende auch in den DATENBANKEN eingelöst
// ist — über sieben Dienstgrenzen hinweg und ohne dass jemand nachhilft. Genau
// dieser Zusammenhang fehlte, als ROADMAP 10.5 noch ⛔ stand: der Endpunkt sagte
// „angenommen", und niemand tat etwas.
//
// WARUM DER TOKEN NACH DER LÖSCHUNG NOCH TRÄGT — und warum das kein Schlupfloch
// ist, sondern der einzige Weg, hinterher überhaupt nachzusehen: profile- und
// consent-service prüfen die SIGNATUR eines Zugangstokens, sie fragen
// identity-service nicht nach der Sitzung (ADR-0015). Der Token überlebt die
// Löschung deshalb bis zu seinem Ablauf. Für die Person ändert das nichts —
// ihr Browser bekommt beim Löschen ein leeres Cookie und kann sich nie wieder
// anmelden. Für diesen Test ist es die Sonde, mit der sich beweisen lässt, dass
// die Zeilen wirklich fallen: sonst müsste er das Löschgeheimnis kennen, und
// eine Testreihe, die das Löschgeheimnis hält, wäre ein schlechterer Tausch.

import { expect, test } from "@playwright/test";
import type { APIRequestContext, Page } from "@playwright/test";

import {
  CONSENT_URL,
  E2E_PASSWORD,
  IDENTITY_URL,
  MAILPIT_URL,
  PROFILE_URL,
  lastMailFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

//: Die Kaskade läuft über drei Takte des Zustellers (Voreinstellung 5 s):
//: sieben Empfänger → Abschlussnachricht → Konto. Großzügig bemessen, weil die
//: Frage „kommt sie an?" lautet und nicht „ist die Maschine schnell?" — ein zu
//: knapper Rahmen macht daraus eine Aussage über die Auslastung des Rechners.
const CASCADE_TIMEOUT_MS = 120_000;

async function accessTokenOf(page: Page): Promise<string> {
  const cookies = await page.context().cookies();
  const access = cookies.find((cookie) => cookie.name === "access");
  expect(access, "kein access-Cookie — die Anmeldung hat nicht getragen").toBeTruthy();
  return access!.value;
}

async function profileOf(
  api: APIRequestContext,
  token: string
): Promise<Record<string, unknown> | null> {
  const res = await api.get(`${PROFILE_URL}/profiles/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok()) return null;
  return (await res.json()) as Record<string, unknown> | null;
}

async function ledgerOf(api: APIRequestContext, token: string): Promise<string[]> {
  const res = await api.get(`${CONSENT_URL}/consent/me/history`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok()) return [];
  const events = (await res.json()) as { action: string }[];
  return events.map((event) => event.action);
}

/**
 * Ein Bestätigungstoken aus einer Mail, die NACH `mark` zugestellt wurde.
 *
 * Der Zeitstempel ist der ganze Punkt: nach einer Löschung liegt die alte
 * Bestätigungsmail derselben Adresse noch in Mailpit, und ihr Token ist längst
 * verbraucht. Ohne die Schranke greift der Test sie — und scheitert an einem
 * „Bestätigung fehlgeschlagen", das mit der Sache nichts zu tun hat.
 */
async function verificationTokenAfter(
  email: string,
  mark: number,
  budgetMs: number
): Promise<string | null> {
  const deadline = Date.now() + budgetMs;
  while (Date.now() < deadline) {
    const list = (await (await fetch(`${MAILPIT_URL}/api/v1/messages?limit=50`)).json()) as {
      messages?: { ID: string; Subject: string; Created: string; To: { Address: string }[] }[];
    };
    const hit = (list.messages ?? []).find(
      (message) =>
        message.Subject.includes("bestätige deine E-Mail-Adresse") &&
        message.To.some((to) => to.Address.toLowerCase() === email.toLowerCase()) &&
        new Date(message.Created).getTime() >= mark
    );
    if (hit !== undefined) {
      const body = (await (await fetch(`${MAILPIT_URL}/api/v1/message/${hit.ID}`)).json()) as {
        Text?: string;
        HTML?: string;
      };
      const token = /\/verify\?token=([A-Za-z0-9_-]+)/.exec(
        `${body.Text ?? ""}${body.HTML ?? ""}`
      )?.[1];
      if (token !== undefined) return token;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return null;
}

test("eine Löschung wird verlangt, läuft durch — und der Nachweis bleibt", async ({
  browser,
  request,
}) => {
  test.setTimeout(CASCADE_TIMEOUT_MS + 120_000);

  const email = uniqueEmail("example.com");
  const headline = `E2E Löschkandidat ${Date.now()}`;

  const context = await browser.newContext();
  const page = await context.newPage();
  await registerAndConfirm(page, email, "E2E Löschkandidat");
  await login(page, email);

  // Etwas anlegen, das verschwinden kann — sonst prüfte die Reise am Ende, dass
  // nichts weg ist, wo nie etwas war.
  await page.goto("/profile");
  await page.getByLabel(/Überschrift/i).fill(headline);
  await page.getByLabel(/Ort/i).fill("Bremen");
  await page.getByLabel(/Fähigkeiten/i).fill("Python, Go");
  await page.getByRole("button", { name: /Speichern/i }).click();
  await expect(page.getByText(/Profil gespeichert/i)).toBeVisible();

  // Eine Freigabe erteilen: sie ist später der BELEG, dass gelöscht wurde.
  const release = page.getByRole("switch");
  await expect(release).toBeEnabled();
  await release.click();
  await expect(release).toBeChecked();

  const token = await accessTokenOf(page);

  // Der Zustand VOR der Löschung — sonst sagt „ist weg" am Ende nichts.
  expect(await profileOf(request, token)).not.toBeNull();
  expect(await ledgerOf(request, token)).toContain("GRANT");

  const startedAt = Date.now();

  await page.goto("/konto-loeschen");

  // Auf den KNOPF warten, nicht auf die Überschrift: „Konto löschen" steht
  // auch über der Anmeldeaufforderung, die diese Seite zeigt, solange die
  // Sitzung noch lädt. Auf sie zu warten hieße, den Text zu lesen, bevor es
  // ihn gibt — genau der Teilstring-Fehler, den `stack.ts` schon zweimal
  // teuer bezahlt hat.
  await expect(page.getByRole("button", { name: /^Konto löschen$/ })).toBeVisible();

  // Die Seite sagt VOR dem Klick, was passiert — nicht in einer
  // Datenschutzerklärung (ADR-0024, dieselbe Regel).
  const seite = await page.locator("main").first().innerText();
  expect(seite).toMatch(/unwiderruflich/i);
  expect(seite).toMatch(/nicht sofort/i);
  expect(seite, "die unbequeme Folge gehört sichtbar auf diese Seite").toMatch(/eingestellt/i);

  // Zwei bewusste Schritte, kein Hürdenlauf: der erste Klick löscht nichts.
  await page.getByRole("button", { name: /^Konto löschen$/ }).click();
  await expect(page.getByRole("button", { name: /Ja, endgültig löschen/i })).toBeVisible();
  expect(await profileOf(request, token), "der erste Klick darf nichts löschen").not.toBeNull();

  await page.getByRole("button", { name: /Ja, endgültig löschen/i }).click();

  // „Angenommen und läuft" — nicht „erledigt".
  const done = page.getByRole("status");
  await expect(done).toBeVisible();
  await expect(done).toContainText(/läuft/i);

  // Sofort abgemeldet: ab hier passiert nichts mehr unter diesem Namen.
  await expect(page.getByRole("link", { name: /Anmelden/i }).first()).toBeVisible();

  // ---- Die Kaskade läuft ----------------------------------------------------

  await expect
    .poll(async () => await profileOf(request, token), {
      timeout: CASCADE_TIMEOUT_MS,
      message: "das Profil ist nach der Löschung immer noch da",
    })
    .toBeNull();

  // ---- Und jetzt der Nachweis ----------------------------------------------

  // Die Daten sind weg.
  expect(await profileOf(request, token)).toBeNull();

  // Der Beleg steht noch: die Kette aus Erteilung UND Löschung.
  await expect
    .poll(async () => await ledgerOf(request, token), {
      timeout: CASCADE_TIMEOUT_MS,
      message: "im Ledger fehlt die DELETE-Zeile",
    })
    .toContain("DELETE");
  const actions = await ledgerOf(request, token);
  expect(actions, "die Erteilung muss stehen bleiben — sonst ist nichts beweisbar").toContain(
    "GRANT"
  );

  // Genau eine Abschlussnachricht, und sie sagt nur, DASS es fertig ist.
  const mail = await lastMailFor(email, { after: startedAt });
  expect(mail, "keine Abschlussnachricht").not.toBeNull();
  expect(mail!.subject).toMatch(/gelöscht/i);
  // Keine Aufstellung dessen, was die Person hatte: das wäre eine Kopie der
  // Daten in einem Postfach, das womöglich nicht nur ihr gehört.
  expect(mail!.text).not.toContain(headline);

  await context.close();
});

test("nach der Löschung ist die Anmeldung zu — und die Adresse wieder frei", async ({
  browser,
  request,
}) => {
  test.setTimeout(CASCADE_TIMEOUT_MS + 120_000);

  const email = uniqueEmail("example.com");
  const context = await browser.newContext();
  const page = await context.newPage();
  await registerAndConfirm(page, email, "E2E Zweitkonto");
  await login(page, email);

  const startedAt = Date.now();

  await page.goto("/konto-loeschen");
  await expect(page.getByRole("button", { name: /^Konto löschen$/ })).toBeVisible();
  await page.getByRole("button", { name: /^Konto löschen$/ }).click();
  await page.getByRole("button", { name: /Ja, endgültig löschen/i }).click();
  await expect(page.getByRole("status")).toBeVisible();

  // Das Fertig-Signal ist die Abschlussnachricht — nicht eine `DELETE`-Zeile
  // im Ledger.
  //
  // Dieses Konto hat NIE eine Freigabe erteilt, und deshalb entsteht dort auch
  // keine `DELETE`-Zeile: die gibt es je Capability, die die Person einmal
  // hielt (ADR-0027 §5). Über jemanden, der nichts erlaubt hat, steht danach
  // gar nichts im Ledger — und das ist richtig so, nicht ein fehlender Fall.
  const done = await lastMailFor(email, { after: startedAt });
  expect(done, "keine Abschlussnachricht — die Kaskade wurde nie fertig").not.toBeNull();
  expect(done!.subject).toMatch(/gelöscht/i);

  // Der eigentliche Prüfgegenstand: die Adresse ist wieder frei.
  //
  // Die Alternative wäre ein dauerhafter Rest ausgerechnet der Angabe, die
  // gelöscht werden sollte — ein E-Mail-Hash, der weiterlebt, um
  // Neuanmeldungen zu erkennen („Kein Grabstein", ADR-0027).
  //
  // WIEDERHOLT VERSUCHT, und das ist kein Schönheitsfehler, sondern die
  // Bauart: zwischen „fertig" gesagt und der gefallenen Kontozeile liegt ein
  // Takt des Zustellers (§6) — die Nachricht braucht die Adresse, die gleich
  // gelöscht wird, also geht sie zwangsläufig vorher raus. In genau diesem
  // Fenster ist die Adresse noch belegt. Gemessen: das Konto fällt rund fünf
  // Sekunden nach der Nachricht.
  //
  // Geprüft wird am stärksten verfügbaren Zeichen: es kommt wieder eine NEUE
  // Bestätigungsmail. Eine abgelehnte Anmeldung wäre KEIN Beweis — die wird
  // schon abgelehnt, solange das Konto nur gesperrt ist.
  let token: string | null = null;
  const deadline = Date.now() + CASCADE_TIMEOUT_MS;
  while (token === null && Date.now() < deadline) {
    const mark = Date.now();
    await request.post(`${IDENTITY_URL}/auth/register`, {
      data: { email, password: E2E_PASSWORD, display_name: "E2E Zweitkonto, zweiter Anlauf" },
    });
    token = await verificationTokenAfter(email, mark, 6_000);
  }
  expect(token, "die Adresse wurde nach der Löschung nie wieder frei").not.toBeNull();

  const fresh = await context.newPage();
  await fresh.goto(`/verify?token=${token}`);
  await expect(fresh.getByRole("heading", { name: "E-Mail bestätigt", exact: true })).toBeVisible();

  await context.close();
});
