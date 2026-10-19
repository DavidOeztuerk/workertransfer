// A company's sources of adverts (ADR-0052): an ATS board or its own feed.
//
// <strong>A board is proven by a code, not by its name.</strong> Anybody can
// type a board name, and every board is publicly readable — so the platform
// hands out a code, the company writes it into one advert at its ATS, and the
// next fetch finds it there. Until then nothing is imported. A feed is proven
// by lying on the company's own domain.

import { request } from "../../../core/api/client";
import { JOBS_BASE_URL } from "../../../env";
import { ADMIN_CASES, type Failure, type MessageKey, type ServerCase } from "../../../shared/api/errors";
import type { ApiError } from "../../../core/store/thunkHelpers";

export type SourceKind = "greenhouse" | "lever" | "personio" | "feed";
export type SourceMode = "draft" | "direct";

export const SOURCE_KINDS: SourceKind[] = ["greenhouse", "lever", "personio", "feed"];

export interface Source {
  id: string;
  kind: SourceKind;
  /** The board's name — or, for a feed, its address. */
  key: string;
  mode: SourceMode;
  /** `null`: the board has not been found carrying the code yet. */
  proven_at: string | null;
  fetched_at: string | null;
  /** A short word: `new`, `ok`, `cut`, `code_missing`, `not_found`, … */
  outcome: string;
  /** Only while unproven — what the company writes into one advert. */
  code?: string;
}

export interface Fetched {
  source: Source;
  added: number;
  changed: number;
  closed: number;
}

/** The outcome word as a catalogue key; an unknown word reads as "could not be read". */
export function outcomeKey(outcome: string): MessageKey {
  switch (outcome) {
    case "new":
      return "sources.outcomeNew";
    case "ok":
      return "sources.outcomeOk";
    case "cut":
      return "sources.outcomeCut";
    case "code_missing":
      return "sources.outcomeCodeMissing";
    case "not_found":
      return "sources.outcomeNotFound";
    case "unreachable":
      return "sources.outcomeUnreachable";
    case "too_large":
      return "sources.outcomeTooLarge";
    default:
      return "sources.outcomeUnreadable";
  }
}

/**
 * The server's sentences that need their own answer. Each pattern matches a
 * sentence in jobs-service (`JobSource.cs`) that points back here.
 */
export const SOURCE_CASES: readonly ServerCase[] = [
  ...ADMIN_CASES,
  { status: 422, pattern: /board name may hold only/i, key: "sources.badBoard" },
  { status: 422, pattern: /feed must lie on the company's own domain/i, key: "sources.feedForeign" },
  { status: 422, pattern: /source key is missing or too long/i, key: "sources.keyMissing" },
  { status: 422, pattern: /company has no proven domain/i, key: "sources.noDomain" },
  { status: 503, key: "errors.serviceSilent" },
];

function failed(error: ApiError): Failure<"failed"> {
  return { ok: false, reason: "failed", error };
}

function missing(): ApiError {
  return { status: 503, title: "", detail: "" };
}

export type SourceList = { ok: true; sources: Source[] } | Failure<"failed">;
export type SourceResult = { ok: true; source: Source } | Failure<"failed">;
export type FetchResult = { ok: true; fetched: Fetched } | Failure<"failed">;

export async function listSources(signal?: AbortSignal): Promise<SourceList> {
  const answer = await request<Source[]>(JOBS_BASE_URL, "/jobs/sources", { signal }, "errors.listNotLoaded");
  return answer.ok ? { ok: true, sources: answer.value ?? [] } : failed(answer.error);
}

/** Sets a source up — an admin step. */
export async function createSource(kind: SourceKind, key: string, mode: SourceMode): Promise<SourceResult> {
  const answer = await request<Source>(
    JOBS_BASE_URL,
    "/jobs/sources",
    { method: "POST", body: { kind, key, mode } },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, source: answer.value } : failed(answer.ok ? missing() : answer.error);
}

export async function setSourceMode(id: string, mode: SourceMode): Promise<SourceResult> {
  const answer = await request<Source>(
    JOBS_BASE_URL,
    `/jobs/sources/${id}`,
    { method: "PUT", body: { mode } },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, source: answer.value } : failed(answer.ok ? missing() : answer.error);
}

/** Removes it; its adverts stay and become the company's own. */
export async function deleteSource(id: string): Promise<{ ok: true } | Failure<"failed">> {
  const answer = await request<null>(JOBS_BASE_URL, `/jobs/sources/${id}`, { method: "DELETE" }, "errors.stepFailed");
  return answer.ok ? { ok: true } : failed(answer.error);
}

/** Fetches now — the read that finds the code proves the board. */
export async function fetchSource(id: string): Promise<FetchResult> {
  const answer = await request<Fetched>(
    JOBS_BASE_URL,
    `/jobs/sources/${id}/fetch`,
    { method: "POST" },
    "errors.stepFailed"
  );
  return answer.ok && answer.value != null ? { ok: true, fetched: answer.value } : failed(answer.ok ? missing() : answer.error);
}
