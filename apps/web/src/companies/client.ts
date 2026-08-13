// Client für companies-service.
//
// Das öffentliche Profil braucht keine Anmeldung — es ist die
// Selbstdarstellung eines Unternehmens, und sie hinter eine Anmeldung zu legen
// widerspricht ihrem Zweck.

import { COMPANIES_BASE_URL } from "../env";

export interface CompanyProfile {
  tenant_id: string;
  /** Die Adresse der Karriere-Seite — vom Server vergeben, unveränderlich. */
  slug: string;
  display_name: string;
  about: string;
  website: string | null;
  locations: string[];
  benefits: string[];
  updated_at: string;
}

export interface CompanyProfileInput {
  display_name: string;
  about: string;
  website: string | null;
  locations: string[];
  benefits: string[];
}

export type SaveResult =
  | { ok: true; profile: CompanyProfile }
  | { ok: false; reason: "no-company" | "invalid" | "offline"; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      if (Array.isArray(detail)) {
        const first: unknown = detail[0];
        if (typeof first === "object" && first !== null) {
          const msg = (first as { msg?: unknown }).msg;
          if (typeof msg === "string") return msg;
        }
      }
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${COMPANIES_BASE_URL}${path}`, { credentials: "include", ...init });
}

/**
 * Das Profil eines Unternehmens, oder `null`.
 *
 * `null` heißt „hat noch keins" — dann bleibt eine Stelle anonym, und die
 * Oberfläche zeigt schlicht keinen Unternehmensteil. Das ist kein Fehler,
 * sondern ein Zustand, den das Unternehmen selbst herbeigeführt hat.
 */
export async function getCompanyProfile(tenantId: string): Promise<CompanyProfile | null> {
  try {
    const res = await send(`/companies/${tenantId}/profile`);
    if (!res.ok) return null;
    return (await res.json()) as CompanyProfile;
  } catch {
    return null;
  }
}

export type OwnProfileResult =
  | { ok: true; profile: CompanyProfile | null }
  | { ok: false; message: string };

/**
 * Das eigene Firmenprofil — mit Unterscheidung zwischen „noch keins" und
 * „nicht abrufbar".
 *
 * Hier stand `null` für beides, und das war die gefährlichste Stelle dieser Art
 * im Projekt: `toForm(null)` ist ein LEERES Formular, der Ladezustand ist zu
 * diesem Zeitpunkt vorbei (die Abfrage gelang ja), und die Seite sah aus wie
 * „noch nichts eingetragen". Wer dann den Anzeigenamen tippte und speicherte,
 * schickte ein `PUT` mit leerem Über-uns, leerer Website, leeren Standorten und
 * leeren Benefits — und überschrieb damit, was vorher dastand.
 *
 * `{ ok: true, profile: null }` heißt „es gibt noch keins", `{ ok: false }`
 * heißt „wir wissen es nicht". Nur das Erste darf ein leeres Formular zeigen.
 */
export async function getOwnCompanyProfile(): Promise<OwnProfileResult> {
  let res: Response;
  try {
    res = await send("/companies/me/profile");
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
  if (!res.ok) {
    return { ok: false, message: "Das Profil ist gerade nicht abrufbar." };
  }
  try {
    const body: unknown = await res.json();
    return { ok: true, profile: body === null ? null : (body as CompanyProfile) };
  } catch {
    return { ok: false, message: "Das Profil ist gerade nicht abrufbar." };
  }
}

export async function saveCompanyProfile(input: CompanyProfileInput): Promise<SaveResult> {
  let res: Response;
  try {
    res = await send("/companies/me/profile", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      // Kein tenant_id: das Unternehmen steht im Token und wird gegen die
      // Mitgliedschaft geprüft.
      body: JSON.stringify(input),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Wähle oben ein Unternehmen, für das du handelst.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Das Profil konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, profile: (await res.json()) as CompanyProfile };
}


export type BySlugResult =
  | { ok: true; profile: CompanyProfile }
  | { ok: false; reason: "not-found" | "unavailable"; message: string }; 

/**
 * Die Karriere-Seite eines Unternehmens, über ihr Kürzel.
 *
 * Hier stand `null` für „gibt es nicht", für `500`, für `503` und für „kein
 * Netz". `career.tsx` machte daraus „Diese Seite gibt es nicht. Unter dieser
 * Adresse ist kein Unternehmen hinterlegt."
 *
 * Das ist die einzige Stelle dieser Art, die **Fremde** sehen: ein Unternehmen
 * gibt seinen Karriere-Link an Bewerber, der Dienst stolpert, und der Empfänger
 * liest, dass es die Firma nicht gibt. Eine Aussage über ein Unternehmen, die
 * niemand treffen wollte.
 *
 * `not-found` ist eine Auskunft, `unavailable` ist eine Störung. Die Seite sagt
 * beides verschieden.
 */
export async function getCompanyBySlug(slug: string): Promise<BySlugResult> {
  let res: Response;
  try {
    res = await send(`/companies/by-slug/${encodeURIComponent(slug)}`);
  } catch {
    return { ok: false, reason: "unavailable", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 404) {
    return {
      ok: false,
      reason: "not-found",
      message: "Unter dieser Adresse ist kein Unternehmen hinterlegt.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "unavailable",
      message: "Diese Seite ist gerade nicht abrufbar.",
    };
  }
  try {
    return { ok: true, profile: (await res.json()) as CompanyProfile };
  } catch {
    return { ok: false, reason: "unavailable", message: "Diese Seite ist gerade nicht abrufbar." };
  }
}
