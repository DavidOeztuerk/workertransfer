import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { PROFILE_BASE_URL } from "../../../env";
import type { ServerCase } from "../../../shared/api/errors";
import { PROFILE_VISIBILITY, type ConsentResult, isGranted, setGranted } from "./consent";
import { i18n } from "../../../core/i18n/i18n";

/**
 * profile-service and the consent ledger — two services, because they are two
 * truths.
 *
 * What stands in the profile belongs to profile-service; whether anybody may
 * see it belongs to the ledger (ADR-0013). The UI shows both side by side, but
 * it writes in two places — and a switch that touches only the ledger is
 * exactly right that way. There is <strong>no</strong> visibility field in the
 * profile (ADR-0020); one would be a second truth, and the client would set it.
 */
/**
 * How far somebody is willing to commute — a STEP, not a number (ADR-0041).
 *
 * The members are wire words and stay as they are. `null` means "said nothing"
 * and is a valid state: a profile without this statement is complete. In the
 * scout it becomes a dash, never a cross.
 */
export type CommuteReadiness = "bis_10" | "bis_25" | "bis_50" | "bis_100" | "egal";

/** Whether somebody would relocate. `offen` is the most honest answer and no defect. */
export type RelocationReadiness = "ja" | "nein" | "offen";

export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  /** `null` means "said nothing" — a state, not an omission (ADR-0041). */
  commute_km: CommuteReadiness | null;
  relocation: RelocationReadiness | null;
  /** Her own XING profile — it names her (VISION-FRAGEN-2 N2). `null`: none. */
  xing_url: string | null;
  updated_at: string;
}

export interface ProfileInput {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  commute_km: CommuteReadiness | null;
  relocation: RelocationReadiness | null;
  /** Required on purpose: every caller writes the WHOLE profile, and `null` takes the link back. */
  xing_url: string | null;
}

/**
 * The server's sentences that need their own answer. The pattern matches
 * profile-service `ProfileErrors.cs`, which points back here.
 */
export const PROFILE_CASES: readonly ServerCase[] = [
  { status: 422, pattern: /XING profile address/i, key: "profile.xingInvalid" },
];

/**
 * "None created yet" and "not loadable" are different answers.
 *
 * The old client returned `null` on <em>every</em> non-2xx, and `null` means
 * "none yet" there. The page then showed an empty form over a failed request —
 * whoever typed something into it and saved overwrote headline, text, location
 * and skills with empty.
 */
export type ProfileLoad = { ok: true; profile: Profile | null } | { ok: false; error: ApiError };

export type SaveResult = { ok: true; profile: Profile } | { ok: false; error: ApiError };

export async function getMyProfile(signal?: AbortSignal): Promise<ProfileLoad> {
  const answer = await request<Profile | null>(
    PROFILE_BASE_URL,
    "/profiles/me",
    { signal },
    "errors.ownProfileNotLoaded"
  );
  // 404 means "none yet" here — for one's OWN resource the ambiguity "hidden
  // or non-existent" that ADR-0020 creates for foreign profiles does not exist.
  if (!answer.ok) {
    if (answer.error.status === 404) return { ok: true, profile: null };
    return { ok: false, error: answer.error };
  }
  return { ok: true, profile: answer.value ?? null };
}

export async function saveMyProfile(input: ProfileInput): Promise<SaveResult> {
  const answer = await request<Profile>(
    PROFILE_BASE_URL,
    "/profiles/me",
    {
      method: "PUT",
      // Exactly the contract's fields, in the wire's spelling. `remote_ok` and
      // not `remoteOk`: at exactly this spot the "remote possible" tick was
      // once silently lost.
      body: {
        headline: input.headline,
        bio: input.bio,
        location: input.location,
        remote_ok: input.remote_ok,
        skills: input.skills,
        // `null` is SENT and not left out: it takes the statement back. A
        // "missing means: leave it as it was" would make taking it back
        // impossible — and a statement that cannot be withdrawn is not a
        // voluntary one (ADR-0041).
        commute_km: input.commute_km,
        relocation: input.relocation,
        xing_url: input.xing_url,
      },
    },
    "errors.profileNotSaved"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, profile: answer.value };
}

/**
 * A draft for one's OWN profile text (ADR-0024).
 *
 * The text goes to a third-party provider — that stands at the button that
 * triggers it, not in a privacy policy. Nothing is stored: the draft lives in
 * the form until the person saves it themselves.
 *
 * Only their wish is sent. The context (headline, text, skills) comes from the
 * stored profile on the server — what the client cannot send, it cannot
 * smuggle into a foreign service.
 */
export type DraftResult =
  | { ok: true; draft: string }
  | { ok: false; message: string };

export async function draftProfileText(wish: string): Promise<DraftResult> {
  const answer = await request<{ draft: string }>(
    PROFILE_BASE_URL,
    "/profiles/me/draft",
    { method: "POST", body: { wish } },
    "errors.draftHelpUnavailable"
  );
  if (!answer.ok) {
    // TRANSLATED, not passed through. `message` is a finished sentence for the
    // UI here and not a key — unlike in the `interpretError` tables, which
    // `interpretError()` resolves itself. The difference was overlooked once,
    // and then `errors.draftHelpUnavailableLong` stood verbatim in the warning
    // box: i18next hands a key back unchanged when nobody looks it up.
    if (answer.error.status === 401) {
      return { ok: false, message: i18n.t("errors.sessionExpiredLong") };
    }
    // 503 means "not configured OR provider silent" — the same from outside,
    // and both mean "try again later", not "done wrong". What is reported is
    // the kind of failure, never the content.
    return { ok: false, message: i18n.t("errors.draftHelpUnavailableLong") };
  }
  return { ok: true, draft: answer.value?.draft ?? "" };
}

/** `null` means: the ledger did not answer (see `isGranted`). */
export function getVisibility(subjectId: string, signal?: AbortSignal): Promise<boolean | null> {
  return isGranted(subjectId, PROFILE_VISIBILITY, signal);
}

export function setVisibility(subjectId: string, granted: boolean): Promise<ConsentResult> {
  return setGranted(
    subjectId,
    PROFILE_VISIBILITY,
    granted,
    i18n.t("errors.revokedInProfileSettings")
  );
}
