// A single profile of somebody else — profile-service.
//
// <strong>What does NOT stand here any more is the list.</strong> It lay on
// `GET /candidates` and fell on 11.09.2026 with the move to scout-service
// (ADR-0036); the path has answered nothing since, and `docs/routenkarte.yml`
// records that with 404 in all four columns.
//
// What remained is the single read: `GET /profiles/{id}`, at the ledger, for
// the view of an application. It belongs to profile-service and is independent
// of the search.

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

/** A single profile — 404 means hidden or non-existent. */
export async function getCandidateProfile(
  subjectId: string,
  signal?: AbortSignal
): Promise<Profile | null> {
  const answer = await request<Profile>(
    PROFILE_BASE_URL,
    `/profiles/${subjectId}`,
    { signal },
    "errors.profileUnavailable"
  );
  if (answer.ok) return answer.value ?? null;
  return null;
}
