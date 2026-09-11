// Ein einzelnes fremdes Profil — profile-service.
//
// <strong>Was hier NICHT mehr steht, ist die Liste.</strong> Sie lag auf
// `GET /candidates` und ist am 11.09.2026 mit dem Umzug auf scout-service
// gefallen (ADR-0036); der Pfad beantwortet seither nichts mehr, und
// `docs/routenkarte.yml` hält das mit 404 in allen vier Spalten fest.
//
// Geblieben ist der Einzelabruf: `GET /profiles/{id}`, am Ledger, für die
// Ansicht einer Bewerbung. Er gehört profile-service und ist von der Suche
// unabhängig.

import { request } from "../../../core/api/client";
import { PROFILE_BASE_URL } from "../../../env";

export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  updated_at: string;
}

/** Ein einzelnes Profil — 404 heißt verborgen oder nicht vorhanden. */
export async function getCandidateProfile(
  subjectId: string,
  signal?: AbortSignal
): Promise<Profile | null> {
  const answer = await request<Profile>(
    PROFILE_BASE_URL,
    `/profiles/${subjectId}`,
    { signal },
    "fehler.profilNichtAbrufbar"
  );
  if (answer.ok) return answer.value ?? null;
  return null;
}
