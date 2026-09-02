// Client für companies-service.
//
// Das öffentliche Profil braucht keine Anmeldung — es ist die Selbstdarstellung
// eines Unternehmens, und sie hinter eine Anmeldung zu legen widerspricht ihrem
// Zweck.

import { request } from "../../../core/api/client";
import { COMPANIES_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "./fehler";

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

export type SpeicherErgebnis =
  | { ok: true; profile: CompanyProfile }
  | Fehlschlag<"no-company" | "invalid" | "offline">;

export type EigenesProfilErgebnis =
  | { ok: true; profile: CompanyProfile | null }
  | Fehlschlag<"unavailable">;

export type SlugErgebnis =
  | { ok: true; profile: CompanyProfile }
  | Fehlschlag<"not-found" | "unavailable">;

/**
 * Das Profil eines Unternehmens, oder `null`.
 *
 * `null` heißt „hat noch keins" — dann bleibt eine Stelle anonym, und die
 * Oberfläche zeigt schlicht keinen Unternehmensteil. Das ist kein Fehler,
 * sondern ein Zustand, den das Unternehmen selbst herbeigeführt hat. Ihn mit
 * „Unbekanntes Unternehmen" zu füllen wäre eine Aussage, die niemand gemacht
 * hat.
 */
export async function getCompanyProfile(
  tenantId: string,
  signal?: AbortSignal
): Promise<CompanyProfile | null> {
  const antwort = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    `/companies/${tenantId}/profile`,
    { signal }
  );
  return antwort.ok ? (antwort.value ?? null) : null;
}

/**
 * Das eigene Firmenprofil — mit Unterscheidung zwischen „noch keins" und
 * „nicht abrufbar".
 *
 * Hier stand `null` für beides, und das war die gefährlichste Stelle dieser Art
 * im Projekt: ein leeres Formular, der Ladezustand vorbei (die Abfrage gelang
 * ja), und die Seite sah aus wie „noch nichts eingetragen". Wer dann den
 * Anzeigenamen tippte und speicherte, schickte ein `PUT` mit leerem Über-uns,
 * leerer Website, leeren Standorten und leeren Benefits — und überschrieb damit,
 * was vorher dastand.
 *
 * `{ ok: true, profile: null }` heißt „es gibt noch keins", `{ ok: false }`
 * heißt „wir wissen es nicht". Nur das Erste darf ein leeres Formular zeigen.
 */
export async function getOwnCompanyProfile(
  signal?: AbortSignal
): Promise<EigenesProfilErgebnis> {
  const antwort = await request<CompanyProfile | null>(
    COMPANIES_BASE_URL,
    "/companies/me/profile",
    { signal },
    "Das Profil ist gerade nicht abrufbar."
  );
  if (antwort.ok) return { ok: true, profile: antwort.value ?? null };
  return deuten<"unavailable">(
    antwort.error,
    {
      0: { reason: "unavailable", title: "Das Profil ist gerade nicht abrufbar." },
    },
    "unavailable"
  );
}

export async function saveCompanyProfile(
  input: CompanyProfileInput
): Promise<SpeicherErgebnis> {
  const antwort = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    "/companies/me/profile",
    // Kein `tenant_id`: das Unternehmen steht im Token und wird gegen die
    // Mitgliedschaft geprüft.
    { method: "PUT", body: input },
    "Das Profil konnte nicht gespeichert werden."
  );
  if (antwort.ok) return { ok: true, profile: antwort.value };
  return deuten<"no-company" | "invalid" | "offline">(
    antwort.error,
    {
      0: { reason: "offline", title: "Keine Verbindung zum Server." },
      403: { reason: "no-company", title: "Wähle oben ein Unternehmen, für das du handelst." },
    },
    "invalid"
  );
}

/**
 * Die Karriere-Seite eines Unternehmens, über ihr Kürzel.
 *
 * Hier stand `null` für „gibt es nicht", für `500`, für `503` und für „kein
 * Netz" — und die Seite machte daraus „Unter dieser Adresse ist kein
 * Unternehmen hinterlegt."
 *
 * Das ist die einzige Stelle dieser Art, die FREMDE sehen: ein Unternehmen gibt
 * seinen Karriere-Link an Bewerber, der Dienst stolpert, und der Empfänger
 * liest, dass es die Firma nicht gibt. Eine Aussage über ein Unternehmen, die
 * niemand treffen wollte.
 *
 * `not-found` ist eine Auskunft, `unavailable` ist eine Störung.
 */
export async function getCompanyBySlug(
  slug: string,
  signal?: AbortSignal
): Promise<SlugErgebnis> {
  const antwort = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    `/companies/by-slug/${encodeURIComponent(slug)}`,
    { signal },
    "Diese Seite ist gerade nicht abrufbar."
  );
  if (antwort.ok && antwort.value !== undefined && antwort.value !== null) {
    return { ok: true, profile: antwort.value };
  }
  if (antwort.ok) {
    return {
      ok: false,
      reason: "unavailable",
      error: {
        status: 0,
        title: "Diese Seite ist gerade nicht abrufbar.",
        detail: "Diese Seite ist gerade nicht abrufbar.",
      },
    };
  }
  return deuten<"not-found" | "unavailable">(
    antwort.error,
    {
      0: { reason: "unavailable", title: "Keine Verbindung zum Server." },
      404: {
        reason: "not-found",
        title: "Unter dieser Adresse ist kein Unternehmen hinterlegt.",
      },
    },
    "unavailable"
  );
}
