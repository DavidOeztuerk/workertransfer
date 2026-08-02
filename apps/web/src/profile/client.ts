// Client für profile-service und den Consent-Ledger.
//
// Zwei Dienste, weil es zwei Wahrheiten sind: was im Profil steht, gehört
// profile-service; ob es jemand sehen darf, gehört dem Ledger (ADR-0013). Die
// Oberfläche zeigt beides nebeneinander, aber sie schreibt an zwei Stellen —
// und ein Schalter, der nur den Ledger anfasst, ist genau richtig so.
//
// Wie im Auth-Client wird nie geworfen, wo ein Formular abgelehnt werden kann:
// das Ergebnis ist eine unterscheidbare Union, damit die Seite nicht raten muss.

import {
  type ConsentResult,
  PROFILE_VISIBILITY,
  isGranted,
  setGranted,
} from "../consent/client";
import { PROFILE_BASE_URL } from "../env";

/** Die eine Capability, die dieses Slice kennt — muss zum Server passen. */
export const VISIBILITY_CAPABILITY = PROFILE_VISIBILITY;

export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  updated_at: string;
}

export interface ProfileInput {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
}

export type SaveResult =
  | { ok: true; profile: Profile }
  | { ok: false; reason: "unauthenticated" | "invalid" | "offline"; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      // FastAPIs Validierungsfehler sind eine Liste von Objekten mit `msg`.
      if (Array.isArray(detail)) {
        const first: unknown = detail[0];
        if (typeof first === "object" && first !== null) {
          const msg = (first as { msg?: unknown }).msg;
          if (typeof msg === "string") return msg;
        }
      }
      const title = (body as { title?: unknown }).title;
      if (typeof title === "string") return title;
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

/**
 * Das eigene Profil, oder `null`.
 *
 * `null` heißt „noch keins angelegt" ODER „nicht angemeldet". Beides ist für
 * die Seite derselbe Fall: sie zeigt ein leeres Formular, und die Anmeldung
 * bewacht ohnehin die Route.
 */
export async function getMyProfile(): Promise<Profile | null> {
  try {
    const res = await fetch(`${PROFILE_BASE_URL}/profiles/me`, { credentials: "include" });
    if (!res.ok) return null;
    const body: unknown = await res.json();
    return body === null ? null : (body as Profile);
  } catch {
    return null;
  }
}

export async function saveMyProfile(input: ProfileInput): Promise<SaveResult> {
  let res: Response;
  try {
    res = await fetch(`${PROFILE_BASE_URL}/profiles/me`, {
      method: "PUT",
      credentials: "include",
      headers: { "content-type": "application/json" },
      // Genau die fünf Felder des Vertrags. Kein Sichtbarkeits-Feld: das wäre
      // eine zweite Wahrheit neben dem Ledger — und eine, die der Client setzt.
      body: JSON.stringify({
        headline: input.headline,
        bio: input.bio,
        location: input.location,
        remote_ok: input.remote_ok,
        skills: input.skills,
      }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }

  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Das Profil konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, profile: (await res.json()) as Profile };
}

export type CandidatePage =
  | { ok: true; items: Profile[]; nextCursor: string | null }
  | {
      ok: false;
      reason: "no-company" | "unauthenticated" | "consent-unavailable" | "offline";
      message: string;
    };

/**
 * Eine Seite freigegebener Profile — nur für Unternehmen.
 *
 * Die Gründe sind unterscheidbar, weil die Seite auf jeden davon anders
 * antwortet: „kein Unternehmen aktiv" ist behebbar (umschalten), „Ledger
 * schweigt" ist es nicht, und eine leere Seite ist überhaupt kein Fehler.
 * Alles in eine Meldung zu werfen würde die Person raten lassen, was zu tun ist.
 */
export interface CandidateFilters {
  /** Mit UND verknüpft: wer zwei eingibt, sucht jemanden, der beides kann. */
  skills: string[];
  location: string;
  /** Nur `true` filtert. Es gibt kein „nur ohne Remote" — siehe Server. */
  remoteOnly: boolean;
}

export const NO_FILTERS: CandidateFilters = { skills: [], location: "", remoteOnly: false };

/**
 * Die Filter wandern in die URL, nicht in den Cursor.
 *
 * Ein Cursor, der Suchbedingungen einpackt, ist ein zweiter Ort, an dem die
 * Abfrage steht — und beim ersten Mal, wenn beide auseinanderlaufen, blättert
 * jemand still durch die falsche Menge.
 */
export function candidateQuery(cursor?: string, filters: CandidateFilters = NO_FILTERS): string {
  const params = new URLSearchParams();
  if (cursor !== undefined && cursor !== "") params.set("cursor", cursor);
  for (const skill of filters.skills) {
    const trimmed = skill.trim();
    if (trimmed !== "") params.append("skill", trimmed);
  }
  if (filters.location.trim() !== "") params.set("location", filters.location.trim());
  // `remote=false` wird gar nicht erst gesendet: es wäre kein Filter, sondern
  // Rauschen in der URL.
  if (filters.remoteOnly) params.set("remote", "true");
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}

export async function listCandidates(
  cursor?: string,
  filters: CandidateFilters = NO_FILTERS
): Promise<CandidatePage> {
  const url = `${PROFILE_BASE_URL}/profiles${candidateQuery(cursor, filters)}`;
  let res: Response;
  try {
    res = await fetch(url, { credentials: "include" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }

  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.",
    };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Profile sehen nur Unternehmen. Wechsle oben auf ein Unternehmen.",
    };
  }
  if (res.status === 503) {
    // Nicht als leere Liste zeigen: das wäre die Behauptung, niemand habe
    // freigegeben — und genau das weiß in diesem Moment niemand.
    return {
      ok: false,
      reason: "consent-unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht. Wir zeigen lieber nichts als das Falsche.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "offline",
      message: await problemMessage(res, "Die Liste konnte nicht geladen werden."),
    };
  }

  const body = (await res.json()) as { items?: Profile[]; next_cursor?: string | null };
  return { ok: true, items: body.items ?? [], nextCursor: body.next_cursor ?? null };
}

/**
 * Ist das Profil dieser Person freigegeben?
 *
 * Dünne Hülle über den gemeinsamen Ledger-Aufrufen: die Capability gehört
 * hierher, die HTTP-Mechanik nicht.
 */
export function getVisibility(subjectId: string): Promise<boolean> {
  return isGranted(subjectId, PROFILE_VISIBILITY);
}

export type VisibilityResult = ConsentResult;

export function setVisibility(subjectId: string, granted: boolean): Promise<VisibilityResult> {
  return setGranted(
    subjectId,
    PROFILE_VISIBILITY,
    granted,
    "Über die Profil-Einstellungen zurückgezogen"
  );
}
