import { COMPANIES_BASE_URL, PROFILE_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";

/**
 * Whether the signed-in person has a profile yet — `null` when unknown.
 *
 * Its own read and not an import from `features/person`, for the reason
 * `pendingTasks.ts` gives. "None yet" arrives as **200 with `null`**
 * (`ProfileEndpoints.cs`: the normal case, not an error) — and as 404 from an
 * older service, so both count. Any other failure is unknown, and unknown
 * shows no hint rather than a wrong one.
 */
export async function hasOwnProfile(signal?: AbortSignal): Promise<boolean | null> {
  const answer = await request<unknown>(
    PROFILE_BASE_URL,
    "/profiles/me",
    { signal },
    "errors.ownProfileNotLoaded"
  );
  if (answer.ok) return answer.value !== null && answer.value !== undefined;
  return answer.error.status === 404 ? false : null;
}

/**
 * Whether the company one acts for has its profile yet — `null` when unknown.
 * The same reading as above: 200 with `null` is "none yet".
 */
export async function hasOwnCompanyProfile(signal?: AbortSignal): Promise<boolean | null> {
  const answer = await request<unknown>(
    COMPANIES_BASE_URL,
    "/companies/me/profile",
    { signal },
    "errors.profileUnavailable"
  );
  if (answer.ok) return answer.value !== null && answer.value !== undefined;
  return answer.error.status === 404 ? false : null;
}

