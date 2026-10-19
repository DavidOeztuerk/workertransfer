// Only the one piece of profile-service this area needs: one's own skills.
//
// The comparison with a job happens in the browser (`lib/match.ts`), and for
// that the page must know what the person has entered. Nothing more is read
// here and nothing written — the profile itself belongs to `features/person`.

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

/**
 * One's own profile, or `null`.
 *
 * `null` means "none created yet" OR "not retrievable". For the comparison both
 * are the same case — and the important difference lies one level higher:
 * `null` means "nothing to compare" and stays silent, a profile WITH an empty
 * skill list means "nothing entered" and gets a sentence. Never a "0 of 3": the
 * person said nothing, not that they can do nothing.
 */
export async function getMyProfile(signal?: AbortSignal): Promise<Profile | null> {
  const answer = await request<Profile | null>(PROFILE_BASE_URL, "/profiles/me", { signal });
  return answer.ok ? (answer.value ?? null) : null;
}
