// Hilfen für die E2E-Reise: Erreichbarkeit prüfen, Konten anlegen, Mails lesen.
//
// Ist der Stack nicht da, überspringen sich die Tests selbst — dasselbe Muster
// wie ADR-0011 für die Python-Integrationstests. Ein rotes `make check` auf
// einer Maschine ohne Docker sagt nichts über den Code; ein grünes, das eine
// Lücke verschweigt, wäre schlimmer. Deshalb überspringen statt bestehen.

import { test } from "@playwright/test";

export const WEB_URL = process.env.E2E_WEB_URL ?? "http://localhost:5173";
export const IDENTITY_URL = process.env.E2E_IDENTITY_URL ?? "http://localhost:8001";
export const CONSENT_URL = process.env.E2E_CONSENT_URL ?? "http://localhost:8002";
export const PROFILE_URL = process.env.E2E_PROFILE_URL ?? "http://localhost:8003";
export const RESUME_URL = process.env.E2E_RESUME_URL ?? "http://localhost:8004";
export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

const REQUIRED: ReadonlyArray<readonly [string, string]> = [
  ["web", WEB_URL],
  ["identity-service", `${IDENTITY_URL}/health/live`],
  ["consent-service", `${CONSENT_URL}/health/live`],
  ["profile-service", `${PROFILE_URL}/health/live`],
  ["resume-service", `${RESUME_URL}/health/live`],
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
