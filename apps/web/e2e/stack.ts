// Hilfen für die E2E-Reise: Erreichbarkeit prüfen, Konten anlegen, Mails lesen.
//
// Ist der Stack nicht da, überspringen sich die Tests selbst — dasselbe Muster
// wie ADR-0011 für die Python-Integrationstests. Ein rotes `make check` auf
// einer Maschine ohne Docker sagt nichts über den Code; ein grünes, das eine
// Lücke verschweigt, wäre schlimmer. Deshalb überspringen statt bestehen.

import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

export const WEB_URL = process.env.E2E_WEB_URL ?? "http://localhost:5173";
export const IDENTITY_URL = process.env.E2E_IDENTITY_URL ?? "http://localhost:8001";
export const CONSENT_URL = process.env.E2E_CONSENT_URL ?? "http://localhost:8002";
export const PROFILE_URL = process.env.E2E_PROFILE_URL ?? "http://localhost:8003";
export const RESUME_URL = process.env.E2E_RESUME_URL ?? "http://localhost:8004";
export const PORTFOLIO_URL = process.env.E2E_PORTFOLIO_URL ?? "http://localhost:8005";
export const JOBS_URL = process.env.E2E_JOBS_URL ?? "http://localhost:8006";
export const APPLICATIONS_URL = process.env.E2E_APPLICATIONS_URL ?? "http://localhost:8007";
export const COMPANIES_URL = process.env.E2E_COMPANIES_URL ?? "http://localhost:8008";
export const TRANSFER_URL = process.env.E2E_TRANSFER_URL ?? "http://localhost:8009";
export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

const REQUIRED: ReadonlyArray<readonly [string, string]> = [
  ["web", WEB_URL],
  ["identity-service", `${IDENTITY_URL}/health/live`],
  ["consent-service", `${CONSENT_URL}/health/live`],
  ["profile-service", `${PROFILE_URL}/health/live`],
  ["resume-service", `${RESUME_URL}/health/live`],
  ["portfolio-service", `${PORTFOLIO_URL}/health/live`],
  ["jobs-service", `${JOBS_URL}/health/live`],
  ["applications-service", `${APPLICATIONS_URL}/health/live`],
  ["companies-service", `${COMPANIES_URL}/health/live`],
  ["transfer-service", `${TRANSFER_URL}/health/live`],
  ["mailpit", `${MAILPIT_URL}/api/v1/messages?limit=1`],
];

let cached: string | null | undefined;

async function reachable(url: string): Promise<boolean> {
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 2_000);
    const res = await fetch(url, { signal: controller.signal });
    clearTimeout(timer);
    return res.ok;
  } catch {
    return false;
  }
}

/** `null`, wenn alles läuft — sonst der Grund fürs Überspringen. */
export async function missingService(): Promise<string | null> {
  if (cached !== undefined) return cached;
  for (const [name, url] of REQUIRED) {
    if (!(await reachable(url))) {
      cached = name;
      return cached;
    }
  }
  cached = null;
  return cached;
}

/** In einer Datei einmal aufrufen; überspringt sie, wenn der Stack fehlt. */
export function skipWithoutStack(): void {
  test.beforeAll(async () => {
    const missing = await missingService();
    test.skip(
      missing !== null,
      `${missing} ist nicht erreichbar — starte den Stack mit "docker compose up"`
    );
  });
}

function nonce(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/** Eindeutig je Lauf: der Stack wird zwischen Läufen nicht zurückgesetzt. */
export function uniqueEmail(domain: string): string {
  return `e2e-${nonce()}@${domain}`;
}

/**
 * Eine Domain, die es in diesem Stack noch nie gab.
 *
 * Nötig für alles, was ein Unternehmen anlegt: die Domain wird beansprucht und
 * ist danach vergeben (ADR-0019). Mit einer festen Domain besteht der Test
 * genau einmal und scheitert ab dem zweiten Lauf an einem Konflikt, der nichts
 * mit dem Prüfgegenstand zu tun hat.
 */
export function uniqueCompanyDomain(): string {
  return `arbeitgeber-${nonce()}.example`;
}

interface MailpitMessage {
  ID: string;
  Subject: string;
  To: Array<{ Address: string }>;
}

/**
 * Ein Token aus der zuletzt an `address` zugestellten Mail dieser Art.
 *
 * Der Betreff ist Teil der Suche, nicht nur die Adresse. Vorher wurde die
 * neueste Mail an die Adresse genommen und daraus irgendein `token=`
 * herausgelesen — seit es zwei Sorten Links gibt (Bestätigung und Einladung),
 * greift das mal die eine und mal die andere, je nachdem welche Mail beim
 * Nachsehen schon da war. Der Test schlug dann an einer Stelle fehl, die mit
 * der Ursache nichts zu tun hatte.
 *
 * Gewartet wird, statt einmal zu schauen: der Versand läuft nach dem Commit
 * und damit nach der HTTP-Antwort, auf die der Browser reagiert hat.
 */
async function tokenFromMail(
  address: string,
  subjectPart: string,
  linkPath: string
): Promise<string> {
  const pattern = new RegExp(`${linkPath}\\?token=([A-Za-z0-9_-]+)`);
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    const list = (await (await fetch(`${MAILPIT_URL}/api/v1/messages?limit=50`)).json()) as {
      messages?: MailpitMessage[];
    };
    const hit = (list.messages ?? []).find(
      (message) =>
        message.Subject.includes(subjectPart) &&
        message.To.some((to) => to.Address.toLowerCase() === address.toLowerCase())
    );
    if (hit !== undefined) {
      const body = (await (await fetch(`${MAILPIT_URL}/api/v1/message/${hit.ID}`)).json()) as {
        Text?: string;
        HTML?: string;
      };
      const token = pattern.exec(`${body.Text ?? ""}${body.HTML ?? ""}`)?.[1];
      if (token !== undefined) return token;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`Keine ${subjectPart}-Mail für ${address} in Mailpit gefunden`);
}

export function verificationTokenFor(address: string): Promise<string> {
  return tokenFromMail(address, "bestätige deine E-Mail-Adresse", "/verify");
}

export function invitationTokenFor(address: string): Promise<string> {
  return tokenFromMail(address, "eingeladen", "/invitation");
}

interface MailpitDetail {
  Text?: string;
  HTML?: string;
  Subject?: string;
}

/**
 * Die zuletzt an `address` zugestellte Mail — ohne Filter auf den Betreff.
 *
 * Anders als `tokenFromMail`: dort wird eine bestimmte Sorte gesucht, hier soll
 * gerade geprüft werden, WAS überhaupt ankommt. Ein Filter würde die Frage
 * beantworten, bevor sie gestellt ist.
 */
export async function lastMailFor(
  address: string,
  { after = 0 }: { after?: number } = {}
): Promise<{ subject: string; text: string } | null> {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    const list = (await (await fetch(`${MAILPIT_URL}/api/v1/messages?limit=50`)).json()) as {
      messages?: (MailpitMessage & { Created?: string })[];
    };
    const hit = (list.messages ?? []).find(
      (message) =>
        message.To.some((to) => to.Address.toLowerCase() === address.toLowerCase()) &&
        new Date(message.Created ?? 0).getTime() >= after
    );
    if (hit !== undefined) {
      const body = (await (
        await fetch(`${MAILPIT_URL}/api/v1/message/${hit.ID}`)
      ).json()) as MailpitDetail;
      return { subject: hit.Subject, text: `${body.Text ?? ""}${body.HTML ?? ""}` };
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return null;
}


/**
 * Das Passwort aller Testkonten. Eines für alle: es prüft nichts, es muss nur
 * die Regeln erfüllen.
 */
export const E2E_PASSWORD = "e2e-Passwort-mit-Laenge-1!";

export async function registerAndConfirm(
  page: Page,
  email: string,
  displayName: string
): Promise<void> {
  await page.goto("/register");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).first().fill(E2E_PASSWORD);
  // Pflichtfeld. Fehlt es, blockt die native Formularvalidierung das Absenden
  // lautlos — kein POST, keine Meldung, und der Test hängt an der Mail, die nie
  // kommt. Genau so ist die erste Reise beim ersten Lauf gescheitert.
  await page.getByLabel(/Anzeigename/i).fill(displayName);
  await page.getByRole("button", { name: /Registrieren/i }).click();
  // Die Antwort ist absichtlich dieselbe für bekannte und unbekannte Adressen —
  // deshalb wird hier nicht auf eine Erfolgsmeldung gewartet, sondern auf die
  // Mail, die es nur bei einer echten Neuanlage gibt.
  const token = await verificationTokenFor(email);
  await page.goto(`/verify?token=${token}`);
  // GENAU die Erfolgsüberschrift, buchstabengetreu.
  //
  // Die Seite hat drei: „Wird bestätigt…" (lädt), „E-Mail bestätigt" (fertig)
  // und „Bestätigung fehlgeschlagen". Ein weiches /bestätigt/i trifft AUCH die
  // erste — die Hilfe war also zufrieden, während die Bestätigung noch lief
  // oder gerade scheiterte, und der Test lief weiter. Beim Anmelden kam dann
  // „email not confirmed", und zwar an einer Stelle, die mit der Ursache nichts
  // zu tun hatte.
  //
  // Der Vorgänger dieses Kommentars warnte vor genau diesem Fehler in einer
  // anderen Ausprägung (/anmelden/i traf den Link in der Kopfzeile). Die Lehre
  // ist dieselbe und hier zweimal bezahlt: eine Erfolgsmeldung prüft man
  // buchstabengetreu, nicht mit einem Teilstring.
  await expect(page.getByRole("heading", { name: "E-Mail bestätigt", exact: true })).toBeVisible();
}

/**
 * Anmelden — und bei einem Fehlschlag sagen, woran es lag.
 *
 * Vorher wartete diese Hilfe stumpf auf den Link „Mein Profil" und lief nach
 * 30 Sekunden in eine Zeitüberschreitung, die nichts verriet: nicht, ob die
 * Anmeldung abgelehnt wurde, nicht, ob die Seite überhaupt geladen hat. Genau
 * dieser Fehlschlag ist mehrfach im Suite-Lauf aufgetreten und war jedes Mal
 * gleich aussagelos.
 *
 * Jetzt wird auf BEIDE Ausgänge gewartet — Erfolg oder Fehlermeldung — und wenn
 * keiner eintritt, steht in der Ausnahme, was stattdessen auf der Seite stand.
 * Das behebt den Wackelkandidaten nicht; es sorgt dafür, dass der nächste
 * Fehlschlag ihn erklärt.
 */
export async function login(page: Page, email: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).fill(E2E_PASSWORD);
  await page.getByRole("button", { name: /Anmelden/i }).click();

  const signedIn = page.getByRole("link", { name: /Mein Profil/i });
  const failure = page.getByRole("alert");
  try {
    await expect(signedIn.or(failure).first()).toBeVisible();
  } catch {
    const seen = await page
      .locator("main")
      .first()
      .innerText()
      .catch(() => "(keine Seite lesbar)");
    throw new Error(
      `Anmeldung als ${email}: weder Profil-Link noch Fehlermeldung erschienen.\n` +
        `URL: ${page.url()}\nSeite:\n${seen.slice(0, 500)}`
    );
  }
  if (await failure.isVisible()) {
    throw new Error(`Anmeldung als ${email} abgelehnt: ${await failure.innerText()}`);
  }
  await expect(signedIn).toBeVisible();
}
