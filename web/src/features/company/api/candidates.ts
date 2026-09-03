// Die Kandidatenliste — profile-service, aber nicht der Profil-Präfix.
//
// <strong>`GET /candidates` und NICHT `/profiles`.</strong> Der blanke Präfix
// ist eine absichtlich tote Tür: er antwortet `404`, in allen drei
// Handlungsformen, so steht es in `docs/routenkarte.yml`. Die Liste hing
// dadurch monatelang im Fehlerfall — „Die Liste konnte nicht geladen werden." —
// und niemand sah es, weil kein Gate die Oberfläche gegen den Server fuhr.

import { request } from "../../../core/api/client";
import { PROFILE_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "../../../shared/api/fehler";

export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  updated_at: string;
}

export interface CandidateFilters {
  /** Mit UND verknüpft: wer zwei eingibt, sucht jemanden, der beides kann. */
  skills: string[];
  location: string;
  /** Nur `true` filtert. Es gibt kein „nur ohne Remote" — siehe Server. */
  remoteOnly: boolean;
}

export const NO_FILTERS: CandidateFilters = { skills: [], location: "", remoteOnly: false };

export type KandidatenFehler = "no-company" | "unauthenticated" | "consent-unavailable" | "offline";

export type KandidatenSeite =
  | { ok: true; items: Profile[]; nextCursor: string | null }
  | Fehlschlag<KandidatenFehler>;

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

/**
 * Eine Seite freigegebener Profile — nur für Unternehmen.
 *
 * Die Gründe sind unterscheidbar, weil die Seite auf jeden davon anders
 * antwortet: „kein Unternehmen aktiv" ist behebbar (umschalten), „Ledger
 * schweigt" ist es nicht, und eine leere Seite ist überhaupt kein Fehler.
 * Alles in eine Meldung zu werfen würde die Person raten lassen, was zu tun ist.
 */
export async function listCandidates(
  cursor?: string,
  filters: CandidateFilters = NO_FILTERS,
  signal?: AbortSignal
): Promise<KandidatenSeite> {
  const answer = await request<{ items?: Profile[]; next_cursor?: string | null }>(
    PROFILE_BASE_URL,
    `/candidates${candidateQuery(cursor, filters)}`,
    { signal },
    "fehler.listeNichtGeladen2"
  );
  if (answer.ok) {
    return {
      ok: true,
      items: answer.value?.items ?? [],
      nextCursor: answer.value?.next_cursor ?? null,
    };
  }
  return deuten<KandidatenFehler>(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      401: {
        reason: "unauthenticated",
        titel: "fehler.sitzungAbgelaufen",
        text: "fehler.erneutAnmelden",
      },
      403: {
        reason: "no-company",
        titel: "fehler.nurFirmenProfile",
        text: "fehler.firmaWaehlen",
      },
      // NICHT als leere Liste zeigen: das wäre die Behauptung, niemand habe
      // freigegeben — und genau das weiß in diesem Moment niemand.
      503: {
        reason: "consent-unavailable",
        titel: "fehler.ledgerSchweigt",
        text: "fehler.lieberNichts",
      },
    },
    "offline"
  );
}
