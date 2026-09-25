import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { CONSENT_BASE_URL } from "../../../env";

/**
 * The consent ledger as the UI uses it.
 *
 * One place for all capabilities: profile and portfolio answer different
 * questions, but they ask them over the same three endpoints. Two copies of
 * these calls would at some point disagree about the format.
 *
 * <strong>Never cached.</strong> A withdrawal must take effect on the next read
 * (ADR-0013) — a cache here would be no speed detail but a rule violation.
 */
export const PROFILE_VISIBILITY = "profile.visibility:public";
export const PORTFOLIO_VISIBILITY = "portfolio.visibility:public";
// The service has always checked it (`IConsentGate.MayBeShownAsync`); the UI knew
// it only to DISPLAY it — nobody could grant it anywhere.
export const GITHUB_VISIBILITY = "github.visibility:public";

export type ConsentResult = { ok: true; granted: boolean } | { ok: false; error: ApiError };

/**
 * Does this consent hold right now? `null` means: the ledger did not answer.
 *
 * This used to return `false` for that case, with a reason that still holds: a
 * switch that accidentally claims "released" is the more dangerous lie. But it
 * closed only one half. `false` means "not released" to the caller, and a
 * switch in that position is <strong>operable</strong>: the next click sends a
 * `grant` for a consent whose state nobody knows — and whoever had long since
 * released reads "not released" and takes the withdrawal for done.
 *
 * `null` separates the two: the display stays off, the switch is locked, and
 * the page says what is going on.
 */
export async function isGranted(
  subjectId: string,
  capability: string,
  signal?: AbortSignal
): Promise<boolean | null> {
  const answer = await request<{ granted?: unknown; deleted?: unknown }>(
    CONSENT_BASE_URL,
    "/consent/check",
    { method: "POST", body: { subject_id: subjectId, capability }, signal }
  );
  if (!answer.ok) return null;
  return answer.value?.granted === true && answer.value.deleted !== true;
}

/**
 * Grant or withdraw.
 *
 * A revocation always carries a reason because the contract demands one: a
 * withdrawal must be explainable, a grant need not be. The default says
 * honestly where the revocation was triggered instead of inventing something.
 */
export async function setGranted(
  subjectId: string,
  capability: string,
  granted: boolean,
  withdrawalReason: string
): Promise<ConsentResult> {
  const answer = await request<{ granted?: unknown }>(
    CONSENT_BASE_URL,
    granted ? "/consent/grant" : "/consent/revoke",
    {
      method: "POST",
      body: granted
        ? { subject_id: subjectId, capability }
        : { subject_id: subjectId, capability, reason: withdrawalReason },
    },
    "errors.consentNotChanged"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, granted: answer.value?.granted === true };
}

export interface GrantedConsent {
  capability: string;
  granted_at: string;
}

export type MyConsentsResult =
  | { ok: true; consents: GrantedConsent[] }
  | { ok: false; error: ApiError };

/**
 * What holds right now — only one's own releases.
 *
 * `GET /consent/me` takes <strong>no</strong> subject id, neither in the path
 * nor in the query: a foreign list would say which other companies hold
 * access.
 *
 * An error is NOT shown as an empty list. "You have released nothing" is a
 * statement, and on exactly this page it would be the most reassuring wrong
 * answer the system can give.
 */
export async function listMyConsents(signal?: AbortSignal): Promise<MyConsentsResult> {
  const answer = await request<GrantedConsent[]>(
    CONSENT_BASE_URL,
    "/consent/me",
    { signal },
    "errors.consentsNotLoaded"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, consents: answer.value ?? [] };
}

export interface ParsedCapability {
  /** The area in words, or `null` if the shape is unknown. */
  area: string | null;
  /** The tenant UUID, if the release applies to one company. */
  tenantId: string | null;
  /** Does it apply to all companies? */
  public: boolean;
}

/** The known areas. The value is a catalogue key, not a sentence. */
const AREAS: Record<string, string> = {
  profile: "consents.areaProfile",
  resume: "consents.areaResume",
  portfolio: "consents.areaPortfolio",
  market: "consents.areaMarket",
  github: "consents.areaGithub",
};

/**
 * `resume.visibility:tenant:<uuid>` → readable parts.
 *
 * Unknown shapes give `area: null` — the UI then shows them raw instead of
 * swallowing them. Hiding a release because its format was not recognised
 * would be the worst conceivable defect on this page.
 */
export function parseCapability(capability: string): ParsedCapability {
  const match = /^([a-z]+)\.visibility:(public|tenant:([0-9a-fA-F-]{36}))$/.exec(capability);
  if (match === null) return { area: null, tenantId: null, public: false };
  const area = AREAS[match[1] ?? ""] ?? null;
  return { area, tenantId: match[3] ?? null, public: match[2] === "public" };
}

export interface ConsentHistoryEntry {
  capability: string;
  action: "GRANT" | "REVOKE" | "DELETE";
  recorded_at: string;
  /** Filled only towards the person concerned. */
  reason: string | null;
}

export type ConsentHistoryResult =
  | { ok: true; events: ConsentHistoryEntry[] }
  | { ok: false; error: ApiError };

/** One's own history — for the data export, not for the overview. */
export async function listMyConsentHistory(
  signal?: AbortSignal
): Promise<ConsentHistoryResult> {
  const answer = await request<ConsentHistoryEntry[]>(
    CONSENT_BASE_URL,
    "/consent/me/history",
    { signal },
    "errors.historyNotLoaded"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, events: answer.value ?? [] };
}
