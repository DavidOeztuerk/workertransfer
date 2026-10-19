import { PROFILE_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";

/**
 * Whether the signed-in person has a profile yet — `null` when unknown.
 *
 * Its own read and not an import from `features/person`, for the reason
 * `pendingTasks.ts` gives. 404 means "none yet" for one's OWN profile (the
 * hidden-or-missing ambiguity of ADR-0020 concerns foreign ones); any other
 * failure is unknown, and unknown shows no hint rather than a wrong one.
 */
export async function hasOwnProfile(signal?: AbortSignal): Promise<boolean | null> {
  const answer = await request<unknown>(
    PROFILE_BASE_URL,
    "/profiles/me",
    { signal },
    "errors.ownProfileNotLoaded"
  );
  if (answer.ok) return true;
  return answer.error.status === 404 ? false : null;
}
