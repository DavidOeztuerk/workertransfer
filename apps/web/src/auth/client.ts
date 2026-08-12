// Cookie-based auth client for the identity-service. The backend sets HTTP-only
// `access` and `refresh` cookies on login (POST /auth/login) and authorises via
// the `access` cookie on GET /me; the browser jar is carried with
// `credentials: "include"`. LoginResult is a discriminated union (never throws on
// a bad credential — the caller renders the German message) so the UI stays out
// of the exception-control-flow business.

import { API_BASE_URL } from "../env";

export { API_BASE_URL };

export type LoginResult = { ok: true } | { ok: false; message: string };

export interface LoginInput {
  email: string;
  password: string;
}

export interface MeResponse {
  user_id: string;
  // The user's own address. Needed to show the company domain that would be
  // derived from it — the server derives it either way, this is only display.
  email: string | null;
  // null while acting as a person. A tenant is a company (ADR-0017) and only
  // becomes active after switching into one the user is a member of.
  tenant_id: string | null;
  roles: readonly string[];
}

export interface RegisterInput {
  email: string;
  password: string;
  displayName: string;
  /**
   * Gesetzt heißt: hier registriert sich ein Unternehmen.
   *
   * Das Unternehmen entsteht erst mit der Bestätigung der Adresse — eine
   * unbestätigte Adresse beweist keine Domain (ADR-0019). Bis dahin merkt der
   * SERVER die Absicht, nicht der Browser: Bestätigungsmails werden oft auf
   * einem anderen Gerät geöffnet.
   */
  companyName?: string;
}

export type RegisterResult = { ok: true } | { ok: false; message: string };
// `expired` is its own case so the UI can offer "resend" instead of a dead end.
//
// Der Erfolgsfall hat drei Ausprägungen, nicht eine: bestätigt · bestätigt MIT
// Unternehmen · bestätigt OHNE Unternehmen samt Grund. Ohne die dritte zeigt die
// Seite „alles gut", während die halbe Absicht verpufft ist.
export type VerifyResult =
  | { ok: true; company?: string; companyError?: string }
  | { ok: false; expired: boolean; message: string };
export interface Company {
  id: string;
  name: string;
  domain: string;
}

export interface Membership extends Company {
  role: string;
}

// Mirrors the server list. Only decides whether the entry point is OFFERED —
// the refusal itself always comes from the server (422), never from here.
const PUBLIC_EMAIL_DOMAINS = new Set([
  "aol.com",
  "freenet.de",
  "gmail.com",
  "googlemail.com",
  "gmx.at",
  "gmx.ch",
  "gmx.de",
  "gmx.net",
  "hotmail.com",
  "icloud.com",
  "mail.com",
  "me.com",
  "outlook.com",
  "proton.me",
  "protonmail.com",
  "t-online.de",
  "web.de",
  "yahoo.com",
  "yahoo.de",
  "yandex.com",
  "zoho.com",
]);

export function emailDomain(email: string): string {
  return email.split("@")[1]?.trim().toLowerCase() ?? "";
}

export function isPublicEmailDomain(email: string): boolean {
  return PUBLIC_EMAIL_DOMAINS.has(emailDomain(email));
}

export async function login(input: LoginInput): Promise<LoginResult> {
  const res = await fetch(`${API_BASE_URL}/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: input.email,
      password: input.password,
    }),
  });
  if (res.ok) {
    return { ok: true };
  }
  let message = "Anmeldung fehlgeschlagen";
  try {
    const body = (await res.json()) as { detail?: string };
    if (typeof body.detail === "string" && body.detail.length > 0) message = body.detail;
  } catch {
    // keep default German message
  }
  return { ok: false, message };
}

/**
 * Eine laufende Erneuerung, die sich gleichzeitige Aufrufer teilen.
 *
 * Nötig, weil der Refresh die jti ROTIERT: das alte Token wird entwertet, ein
 * neues ausgegeben (ADR-0008, damit ein gestohlenes Token einmalig ist). Zwei
 * gleichzeitige Erneuerungen hiessen deshalb, dass die zweite mit einem bereits
 * entwerteten Token ankommt — und die Sitzung genau dadurch verliert, was die
 * Erneuerung retten sollte. Die Sitzungsabfrage wird von TanStack Query
 * entdoppelt, `my-data.tsx` ruft `fetchMe` aber daneben auf.
 */
let laufendeErneuerung: Promise<boolean> | null = null;

async function erneuereSitzung(): Promise<boolean> {
  laufendeErneuerung ??= (async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });
      return res.ok;
    } catch {
      // Netzfehler ist keine Aussage über die Sitzung.
      return false;
    } finally {
      // Erst NACH dem Auflösen freigeben, sonst startet der nächste Aufrufer
      // eine zweite Rotation, während die erste noch unterwegs ist.
      queueMicrotask(() => {
        laufendeErneuerung = null;
      });
    }
  })();
  return laufendeErneuerung;
}

interface SessionResponse {
  user: MeResponse | null;
  state: "active" | "renewable" | "anonymous";
}

/**
 * "Ist gerade jemand angemeldet?" — die Frage, die jede Seite stellt.
 *
 * Sie geht bewusst an `GET /auth/session` und nicht an `/me`. `/me` heisst
 * "gib mir mein Profil": eine geschützte Ressource, und ohne Nachweis ist 401
 * die richtige Antwort. Diese Frage hier ist öffentlich — sie über `/me` zu
 * stellen erzeugte für jeden abgemeldeten Besucher einen Fehlereintrag über
 * einen völlig normalen Zustand.
 *
 * Drei Zustände, und der mittlere ist der Grund:
 *
 * - `anonymous` — es wird KEINE weitere Anfrage gestellt. Eine Anfrage, eine
 *   200, fertig.
 * - `renewable` — das Access-Token trägt nicht mehr, ein Refresh-Cookie liegt
 *   aber vor. Ohne diesen Fall ist man eine Viertelstunde nach dem Anmelden
 *   abgemeldet, lautlos und mitten im Ausfüllen eines Formulars (Access 15
 *   Minuten, Refresh 24 Stunden, ADR-0007).
 * - `active` — das Profil liegt der Antwort schon bei, kein zweiter Weg nötig.
 *
 * Erneuert wird also nur, wenn der Server sagt, dass es etwas zu erneuern gibt
 * — nie auf gut Glück.
 */
export async function fetchSession(): Promise<MeResponse | null> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}/auth/session`, { credentials: "include" });
  } catch {
    return null;
  }
  if (!res.ok) return null;

  const session = (await res.json()) as SessionResponse;
  if (session.state === "active") return session.user;
  if (session.state !== "renewable") return null;

  if (!(await erneuereSitzung())) return null;

  const zweiter = await fetch(`${API_BASE_URL}/auth/session`, { credentials: "include" });
  if (!zweiter.ok) return null;
  return ((await zweiter.json()) as SessionResponse).user;
}

/**
 * Das eigene Profil — geschützt, und hier ist 401 die richtige Antwort.
 *
 * Bleibt für Stellen, die wirklich das Profil brauchen (`/my-data`). Für
 * die Frage "ist jemand angemeldet?" gibt es `fetchSession`; wer sie hier
 * stellt, bekommt einen 401 auf einen normalen Zustand.
 */
export async function fetchMe(): Promise<MeResponse | null> {
  const res = await fetch(`${API_BASE_URL}/me`, { credentials: "include" });
  if (res.ok) return (await res.json()) as MeResponse;
  if (res.status !== 401) return null;

  if (!(await erneuereSitzung())) return null;

  const zweiter = await fetch(`${API_BASE_URL}/me`, { credentials: "include" });
  if (!zweiter.ok) return null;
  return (await zweiter.json()) as MeResponse;
}

// Idempotent by design on the backend (204 even without a refresh cookie), so a
// failed call still leaves the caller free to drop its cached session.
export async function logout(): Promise<void> {
  await fetch(`${API_BASE_URL}/auth/logout`, { method: "POST", credentials: "include" });
}

async function detail(res: Response, fallback: string): Promise<string> {
  try {
    const body = (await res.json()) as { detail?: string };
    if (typeof body.detail === "string" && body.detail.length > 0) return body.detail;
  } catch {
    // keep the German fallback
  }
  return fallback;
}

export async function registerUser(input: RegisterInput): Promise<RegisterResult> {
  const res = await fetch(`${API_BASE_URL}/auth/register`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: input.email,
      password: input.password,
      display_name: input.displayName,
      // Nur mitschicken, wenn es eine Absicht gibt: `company_name` ist im
      // Vertrag optional, und ein `null` im Body wäre eine Aussage, die niemand
      // gemacht hat.
      ...(input.companyName !== undefined ? { company_name: input.companyName } : {}),
    }),
  });
  // A known address answers 201 too — the server sends the real owner a warning
  // instead of telling us they exist. So there is no "already taken" branch here.
  if (res.ok) return { ok: true };
  return { ok: false, message: await detail(res, "Registrierung fehlgeschlagen") };
}

export async function verifyEmail(token: string): Promise<VerifyResult> {
  const res = await fetch(`${API_BASE_URL}/auth/verify-email`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ token }),
  });
  if (res.ok) {
    // Der Ausgang der Unternehmensanlage steht in der Antwort. Ein Körper, der
    // sich nicht lesen lässt, macht die Bestätigung NICHT ungültig — sie ist
    // serverseitig längst passiert, und ein 200 auf dem Papier
    // nachträglich in ein Scheitern zu drehen wäre die falsche Auskunft.
    const body: unknown = await res.json().catch(() => ({}));
    const data = (typeof body === "object" && body !== null ? body : {}) as Record<string, unknown>;
    return {
      ok: true,
      ...(typeof data.company === "string" ? { company: data.company } : {}),
      ...(typeof data.company_error === "string" ? { companyError: data.company_error } : {}),
    };
  }
  const expired = res.status === 410;
  return {
    ok: false,
    expired,
    message: expired
      ? "Dieser Bestätigungslink ist abgelaufen."
      : "Dieser Bestätigungslink ist ungültig.",
  };
}

// Always resolves: the endpoint answers 202 whether or not anything was sent,
// so there is nothing for the caller to distinguish.
export async function resendVerification(email: string): Promise<void> {
  await fetch(`${API_BASE_URL}/auth/resend-verification`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email }),
  });
}

export async function listCompanies(): Promise<Membership[]> {
  const res = await fetch(`${API_BASE_URL}/me/companies`, { credentials: "include" });
  if (!res.ok) return [];
  return (await res.json()) as Membership[];
}

export async function switchCompany(companyId: string): Promise<boolean> {
  const res = await fetch(`${API_BASE_URL}/auth/company/${companyId}`, {
    method: "POST",
    credentials: "include",
  });
  return res.ok;
}
