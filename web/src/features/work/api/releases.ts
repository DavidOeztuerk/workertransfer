// The way to the current employer (ADR-0050): two consents, and a negotiation
// between the company a person would move to and her employer.
//
// <strong>Every refusal about a company's reach is one 404</strong> — no
// route, the employer not asking, the consent taken back. The UI draws
// nothing for it, never "not possible": the difference is exactly what the
// server refuses to tell.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { ADMIN_CASES, type Failure, type ServerCase } from "../../../shared/api/errors";
import type { ApiError } from "../../../core/store/thunkHelpers";

export type ReleaseStatus =
  | "possible"
  | "asked"
  | "agreed"
  | "settled"
  | "refused"
  | "withdrawn"
  | "declined"
  | "closed";

export type ReleaseSide = "hiring" | "current" | "person";

export interface Release {
  /** `null` while nothing is asked yet ("possible"). */
  id: string | null;
  transfer_id: string;
  hiring_id: string;
  employer_id: string;
  side: ReleaseSide;
  status: ReleaseStatus;
  /** yyyy-MM */
  release_month: string | null;
  /** `null`: a release without a fee. */
  fee_cents: number | null;
  terms_by: "hiring" | "current" | null;
  /**
   * The person's own earliest regular start from her notice period (H2) —
   * shown to both companies here, and only here, under her consent. `null`:
   * she stated none, which is not "now".
   */
  regular_from: string | null;
  updated_at: string | null;
  /** Only for her employer, who must know whom an inquiry is about. */
  person_name?: string;
}

export interface ReleaseTerms {
  release_month: string;
  fee_cents: number | null;
}

export type ReleaseMove = "terms" | "accept" | "refuse" | "withdraw" | "agree" | "decline";

export type ReleaseResult = { ok: true; release: Release } | Failure<"failed">;
export type ReleaseRead = { ok: true; release: Release | null } | Failure<"failed">;

/** A negotiation that still runs — only then is there a step to take. */
export const RELEASE_RUNNING: ReleaseStatus[] = ["asked", "agreed"];

/**
 * The server's sentences that need their own answer. Each pattern matches a
 * sentence in transfer-service (`ReleaseNegotiation.cs`,
 * `ReleaseCommands.cs`) that points back here.
 */
export const RELEASE_CASES: readonly ServerCase[] = [
  ...ADMIN_CASES,
  { status: 422, pattern: /release month must be a month/i, key: "release.monthInvalid" },
  { status: 422, pattern: /fee cannot be negative/i, key: "release.feeNegative" },
  { status: 409, pattern: /already a release negotiation/i, key: "release.alreadyAsked" },
  { status: 409, pattern: /belongs to another party/i, key: "release.notYourStep" },
  { status: 409, key: "release.stepNotNow" },
];

function failed(error: ApiError): Failure<"failed"> {
  return { ok: false, reason: "failed", error };
}

/**
 * The negotiation of a transfer, as the caller's side sees it — `null` when
 * there is none for the caller (404): no route, or nothing asked yet for her.
 */
export async function getRelease(transferId: string, signal?: AbortSignal): Promise<ReleaseRead> {
  const answer = await request<Release>(
    TRANSFER_BASE_URL,
    `/transfers/${transferId}/release`,
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, release: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, release: null };
  return failed(answer.error);
}

/** The hiring company asks her employer — a month, and a fee or none. */
export async function askRelease(transferId: string, terms: ReleaseTerms): Promise<ReleaseResult> {
  const answer = await request<Release>(
    TRANSFER_BASE_URL,
    `/transfers/${transferId}/release`,
    { method: "POST", body: terms },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, release: answer.value } : failed(answer.ok ? missing() : answer.error);
}

/** One step on a running negotiation. */
export async function moveRelease(id: string, move: ReleaseMove, terms?: ReleaseTerms): Promise<ReleaseResult> {
  const answer = await request<Release>(
    TRANSFER_BASE_URL,
    `/transfers/releases/${id}/${move}`,
    { method: "POST", body: terms },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, release: answer.value } : failed(answer.ok ? missing() : answer.error);
}

export type InquiryList = { ok: true; inquiries: Release[] } | Failure<"failed">;

/** What other companies asked this employer about its staff. */
export async function listInquiries(signal?: AbortSignal): Promise<InquiryList> {
  const answer = await request<Release[]>(
    TRANSFER_BASE_URL,
    "/transfers/employer/inquiries",
    { signal },
    "errors.listNotLoaded"
  );
  return answer.ok ? { ok: true, inquiries: answer.value ?? [] } : failed(answer.error);
}

export type SettingResult = { ok: true; accepts: boolean } | Failure<"failed">;

/** Whether this company lets other companies ask about its staff. */
export async function getEmployerSetting(signal?: AbortSignal): Promise<SettingResult> {
  const answer = await request<{ accepts_inquiries: boolean }>(
    TRANSFER_BASE_URL,
    "/transfers/employer/setting",
    { signal },
    "errors.listNotLoaded"
  );
  return answer.ok ? { ok: true, accepts: answer.value?.accepts_inquiries ?? false } : failed(answer.error);
}

/** Switch it — an admin step. */
export async function setEmployerSetting(accepts: boolean): Promise<SettingResult> {
  const answer = await request<{ accepts_inquiries: boolean }>(
    TRANSFER_BASE_URL,
    "/transfers/employer/setting",
    { method: "PUT", body: { accepts_inquiries: accepts } },
    "errors.stepFailed"
  );
  return answer.ok ? { ok: true, accepts: answer.value?.accepts_inquiries ?? accepts } : failed(answer.error);
}

export interface EmployerRoute {
  employer_id: string | null;
  /** The ledger's answer now: `false` after a revocation on /consents. */
  open: boolean;
  opened_at: string | null;
}

export type RouteResult = { ok: true; route: EmployerRoute } | Failure<"failed">;

/** The person's own route — empty fields when none is open. */
export async function getEmployerRoute(signal?: AbortSignal): Promise<RouteResult> {
  const answer = await request<EmployerRoute>(TRANSFER_BASE_URL, "/market/me/employer", { signal }, "errors.listNotLoaded");
  return answer.ok && answer.value != null ? { ok: true, route: answer.value } : failed(answer.ok ? missing() : answer.error);
}

/**
 * Names the employer — and with it consents to the route. One act: without
 * the consent "works at X" is never recorded.
 */
export async function openEmployerRoute(employerId: string): Promise<RouteResult> {
  const answer = await request<EmployerRoute>(
    TRANSFER_BASE_URL,
    "/market/me/employer",
    { method: "PUT", body: { employer_id: employerId } },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, route: answer.value } : failed(answer.ok ? missing() : answer.error);
}

/** Closes it: the consent, the named employer and every running negotiation go. */
export async function closeEmployerRoute(): Promise<boolean> {
  const answer = await request<null>(TRANSFER_BASE_URL, "/market/me/employer", { method: "DELETE" }, "errors.stepFailed");
  return answer.ok;
}

/** A 2xx without a body is no answer — read like a silent service. */
function missing(): ApiError {
  return { status: 503, title: "", detail: "" };
}
