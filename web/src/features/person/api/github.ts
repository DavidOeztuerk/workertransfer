import { type ApiResult, request } from "../../../core/api/client";
import { GITHUB_BASE_URL } from "../../../env";
import type { ErrorKey } from "../../../shared/api/errors";

/**
 * One's own, verified GitHub connection.
 *
 * <strong>Evidence, not marks.</strong> What comes from here are public
 * repositories with a link — checkable, with provenance, without an
 * intermediate calculation. There is no score and no ranking, and there must
 * not be one either: "requirement in, evidence out" is the direction, "person
 * in, number out" would be the forbidden one.
 */
export interface Repository {
  name: string;
  description: string;
  language: string | null;
  stars: number;
  url: string;
  pushed_at: string | null;
  /**
   * Which languages occur in the repository — the SET, never their share.
   *
   * Without bytes and without percentages: exactly that is what the deleted
   * package computed its "skill" from (ADR-0022 §2). Empty when the service
   * runs without a GitHub token — then only the main language is available.
   */
  languages: string[];
  /**
   * The topics the owner set on the repository themselves.
   *
   * The strongest evidence of all, because it is a NAMING: a human wrote
   * "kubernetes" there, no counter derived it.
   */
  topics: string[];
}

export interface Connection {
  subject_id: string;
  /**
   * `null`: no account named yet.
   *
   * Whoever signs in over GitHub names none — GitHub reports it. Until the
   * redirect is back there is nothing to show here.
   */
  login: string | null;
  verified: boolean;
  /** Filled only in one's own view: the line for the gist. */
  challenge_description: string | null;
  fetched_at: string | null;
  repositories: Repository[];
  /**
   * Could the languages be fetched for EVERY repository?
   *
   * `false` means: for some only the main language stands, because GitHub's
   * rate limit caps the per-repository call. The page MUST say so — an
   * incomplete set that does not identify itself as such is the silent
   * completeness of ADR-0022 §3: "Python" would then read like "only Python".
   */
  languages_complete: boolean;
}

/** One's own connection. `null` means: none named yet. */
export async function getMyConnection(
  signal?: AbortSignal
): Promise<ApiResult<Connection | null>> {
  const answer = await request<Connection>(
    GITHUB_BASE_URL,
    "/github/me",
    { signal },
    "errors.connectionUnavailable"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

async function act(
  path: string,
  options: Parameters<typeof request>[2],
  fallback: ErrorKey
): Promise<ApiResult<Connection>> {
  const answer = await request<Connection>(GITHUB_BASE_URL, path, options, fallback);

  return answer.ok
    ? { ok: true, value: answer.value as Connection }
    : { ok: false, error: answer.error };
}

/** NAME an account — that does not connect it yet. */
export const nameAccount = (login: string, signal?: AbortSignal) =>
  act(
    "/github/me",
    { method: "POST", body: { login }, signal },
    "errors.accountNotAccepted"
  );

/**
 * Check the proof of ownership.
 *
 * Without it a field "my GitHub name" would be an invitation to adorn oneself
 * with somebody else's work. The proof runs over a public gist — the same
 * shape as the domain proof for companies.
 */
export const verifyConnection = (signal?: AbortSignal) =>
  act(
    "/github/me/verify",
    { method: "POST", signal },
    "errors.proofNotChecked"
  );

/**
 * Does this server offer signing in over GitHub?
 *
 * A statement about the SETUP, not about a person. It is fetched while the
 * page is viewed, because the page does not know beforehand whether to show a
 * button or the gist instructions — and because, unlike `startOAuth`, it
 * creates nothing.
 */
export async function isOAuthAvailable(signal?: AbortSignal): Promise<boolean> {
  const answer = await request<{ available?: boolean }>(
    GITHUB_BASE_URL,
    "/github/oauth",
    { signal }
  );

  return answer.ok && answer.value?.available === true;
}

/**
 * Where the browser goes to sign in at GitHub.
 *
 * ON CLICK ONLY. The call creates a connection if there is none yet — merely
 * viewing the page would otherwise leave a row for everyone who once looked in.
 *
 * `null` means: no sign-in configured. The gist then stays the way.
 */
export async function startOAuth(
  signal?: AbortSignal
): Promise<string | null> {
  const answer = await request<{ url?: string | null }>(
    GITHUB_BASE_URL,
    "/github/me/oauth/start",
    { method: "POST", signal }
  );

  return answer.ok ? (answer.value?.url ?? null) : null;
}

/** The return from GitHub: redeem the one-time code and the state. */
export const finishOAuth = (
  code: string,
  state: string,
  signal?: AbortSignal
) =>
  act(
    "/github/me/oauth/finish",
    { method: "POST", body: { code, state }, signal },
    "errors.proofNotChecked"
  );

/**
 * Fetch the repositories anew.
 *
 * <strong>Only when triggered.</strong> No reconciliation runs in the
 * background: a platform that permanently watches does something different from
 * one that looks once when asked.
 */
export const refreshRepositories = (signal?: AbortSignal) =>
  act("/github/me/refresh", { method: "POST", signal }, "errors.nothingFetched");

/** Disconnect. */
export async function disconnect(signal?: AbortSignal): Promise<boolean> {
  const answer = await request<void>(
    GITHUB_BASE_URL,
    "/github/me",
    { method: "DELETE", signal },
    "errors.disconnectFailed"
  );

  return answer.ok;
}
