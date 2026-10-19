// Signing in with Google, Microsoft, Apple (ADR-0053).
//
// <strong>The browser leaves the page for the provider and comes back through
// identity-service</strong>, not through this app: Apple answers with a form
// post, which a page cannot receive. The service sets the session and sends
// the browser to `/overview`, or back here with `?external=<outcome>`.

import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { ApiError } from "../../../core/store/thunkHelpers";
import type { Failure, MessageKey, ServerCase } from "../../../shared/api/errors";

export type ExternalProvider = "google" | "microsoft" | "apple";

const KNOWN: readonly ExternalProvider[] = ["google", "microsoft", "apple"];

/** The providers this server has credentials for — one button each, none without. */
export async function getProviders(signal?: AbortSignal): Promise<ExternalProvider[]> {
  const answer = await request<{ providers: string[] }>(API_BASE_URL, "/auth/external", { signal });
  if (!answer.ok) return [];
  return (answer.value?.providers ?? []).filter((word): word is ExternalProvider =>
    (KNOWN as readonly string[]).includes(word),
  );
}

/** Where the browser goes to sign in — or, signed in, to bind the provider to the account. */
export function startAddress(provider: ExternalProvider, intent: "signin" | "link" = "signin"): string {
  return `${API_BASE_URL}/auth/external/${provider}/start?intent=${intent}`;
}

/** What `?external=` means, as a catalogue key and a severity; `null` for a word nobody sent. */
export function outcomeOf(word: string | null): { key: MessageKey; severity: "success" | "info" | "warning" | "error" } | null {
  switch (word) {
    case "linked":
      return { key: "externalLogin.linked", severity: "success" };
    case "check_mail":
      return { key: "externalLogin.checkMail", severity: "info" };
    case "address_taken":
      return { key: "externalLogin.addressTaken", severity: "warning" };
    case "link_taken":
      return { key: "externalLogin.linkTaken", severity: "warning" };
    case "unavailable":
      return { key: "externalLogin.unavailable", severity: "error" };
    case "refused":
      return { key: "externalLogin.refused", severity: "error" };
    case "failed":
      return { key: "externalLogin.failed", severity: "error" };
    default:
      return null;
  }
}

export interface ExternalLinks {
  has_password: boolean;
  linked: { provider: ExternalProvider; created_at: string }[];
  available: ExternalProvider[];
}

export type LinksResult = { ok: true; links: ExternalLinks } | Failure<"failed">;

export async function getLinks(signal?: AbortSignal): Promise<LinksResult> {
  const answer = await request<ExternalLinks>(API_BASE_URL, "/auth/external/links", { signal }, "errors.listNotLoaded");
  return answer.ok && answer.value
    ? { ok: true, links: answer.value }
    : { ok: false, reason: "failed", error: answer.ok ? ({ status: 503, title: "", detail: "" } as ApiError) : answer.error };
}

/**
 * The service's sentence for the one refusal that needs its own answer. The
 * pattern matches identity-service `ExternalLoginEndpoints.cs`.
 */
export const UNLINK_CASES: readonly ServerCase[] = [
  { status: 409, pattern: /last way to sign in stays/i, key: "externalLogin.lastWayIn" },
];

export async function unlink(provider: ExternalProvider): Promise<{ ok: true } | Failure<"failed">> {
  const answer = await request<null>(API_BASE_URL, `/auth/external/links/${provider}`, { method: "DELETE" }, "errors.stepFailed");
  return answer.ok ? { ok: true } : { ok: false, reason: "failed", error: answer.error };
}
