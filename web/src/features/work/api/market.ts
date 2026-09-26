// The market status and its release, as the UI uses them.
//
// Both at transfer-service, not at the consent ledger directly: the capability
// string `market.visibility:tenant:<id>` comes about in exactly one place, and
// that place is not the browser.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import {
  type ErrorReading,
  type Failure,
  interpretError,
} from "../../../shared/api/errors";

export type Availability = "open" | "listening" | "unavailable";

export interface MarketStatus {
  subject_id: string;
  availability: Availability;
  employed: boolean;
  note: string;
  is_approachable: boolean;
  updated_at: string;
}

export interface MarketStatusInput {
  availability: Availability;
  employed: boolean;
  note: string;
}

export type MarketRequestStatus = "PENDING" | "GRANTED" | "DECLINED";

export interface MarketRequest {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: MarketRequestStatus;
  created_at: string;
  answered_at: string | null;
  /**
   * What HOLDS right now — from the ledger, not from the record. `null` for the
   * asking company. `status` says what happened; only `active` says what holds
   * now, and only `active` decides about a withdrawal.
   */
  active: boolean | null;
}

export type MarketStatusResult = { ok: true; status: MarketStatus } | Failure<"unavailable">;
export type MarketStatusSaveResult =
  | { ok: true; status: MarketStatus }
  | Failure<"unauthenticated" | "invalid" | "offline">;

export type MarketRequestError =
  | "already-asked"
  | "no-company"
  | "not-available"
  | "unavailable"
  | "offline";
export type MarketRequestResult =
  | { ok: true; request: MarketRequest }
  | Failure<MarketRequestError>;
export type MarketRequestList =
  | { ok: true; requests: MarketRequest[] }
  | Failure<"unavailable">;

/**
 * One's own market status — and no substitute value when it cannot be fetched.
 *
 * Here once stood `unavailable` with an empty note. The reasoning was right and
 * still holds — *the default must never fall out in the market's favour*. It
 * only overlooked that this substitute was not merely shown but written into a
 * FORM: after an outage "not approachable right now" stood there with an empty
 * note, the loading state was over, and whoever then touched something and
 * saved sent `availability: "unavailable"` and `note: ""` — and had withdrawn
 * their approachability and deleted their note without wanting to. On a
 * transfer market that is the most expensive silent write: the person
 * disappears.
 *
 * `ok: false` means "we do not know". The page then shows no form, and so
 * nothing is invented in ANY direction any more.
 *
 * By the way, the server never answers `GET /market/me` empty; a missing record
 * comes back as `unavailable`. So that default is a statement of the server and
 * not one of the UI.
 */
export async function getMyMarketStatus(signal?: AbortSignal): Promise<MarketStatusResult> {
  const answer = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    "/market/me",
    { signal },
    "errors.marketStatusUnavailable"
  );
  if (answer.ok) return { ok: true, status: answer.value };
  return {
    ok: false,
    reason: "unavailable",
    error: {
      ...answer.error,
      title: i18n.t("errors.marketStatusUnavailable"),
      detail: i18n.t("errors.marketStatusUnchangeable"),
      titleKey: "errors.marketStatusUnavailable",
      detailKey: "errors.marketStatusUnchangeable",
    },
  };
}

export async function saveMyMarketStatus(
  input: MarketStatusInput
): Promise<MarketStatusSaveResult> {
  const answer = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    "/market/me",
    {
      method: "PUT",
      // Exactly the three fields of the contract.
      body: { availability: input.availability, employed: input.employed, note: input.note },
    },
    "errors.marketStatusNotSaved"
  );
  if (answer.ok) return { ok: true, status: answer.value };
  return interpretError<"unauthenticated" | "invalid" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      401: {
        reason: "unauthenticated",
        titleKey: "errors.sessionExpired",
        detailKey: "errors.signInAgain",
      },
    },
    "invalid"
  );
}

const REQUEST_ERRORS: Partial<Record<number, ErrorReading<MarketRequestError>>> = {
  0: { reason: "offline", titleKey: "errors.noConnection" },
  409: { reason: "already-asked", titleKey: "errors.alreadyAsked" },
  403: {
    reason: "no-company",
    titleKey: "errors.onlyCompaniesAsk",
    detailKey: "errors.chooseCompany",
  },
  // `404` deliberately does not say whether the person exists — the UI must
  // not build a piece of information out of it that the server has just
  // refused to give.
  404: { reason: "not-available", titleKey: "errors.personNotRequestable" },
  503: {
    reason: "unavailable",
    titleKey: "errors.ledgerSilent",
    detailKey: "errors.tryLater",
  },
};

export async function requestMarketStatus(subjectId: string): Promise<MarketRequestResult> {
  const answer = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    `/market/${subjectId}/requests`,
    { method: "POST" },
    "errors.requestNotSent"
  );
  if (answer.ok) return { ok: true, request: answer.value };
  return interpretError<MarketRequestError>(answer.error, REQUEST_ERRORS, "offline");
}

async function post(path: string): Promise<MarketRequestResult> {
  const answer = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    path,
    { method: "POST" },
    "errors.requestNotAnswered"
  );
  if (answer.ok) return { ok: true, request: answer.value };
  return interpretError<MarketRequestError>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      503: {
        reason: "unavailable",
        titleKey: "errors.ledgerSilent",
        detailKey: "errors.nothingChanged",
      },
    },
    "offline"
  );
}

export function answerMarketRequest(
  requestId: string,
  grant: boolean
): Promise<MarketRequestResult> {
  // Two paths instead of one endpoint with a flag: granting and declining are
  // different acts, and in the ledger's record they stay that.
  return post(`/market/requests/${requestId}/${grant ? "grant" : "decline"}`);
}

export function revokeMarketAccess(requestId: string): Promise<MarketRequestResult> {
  return post(`/market/requests/${requestId}/revoke`);
}

async function listRequests(path: string, signal?: AbortSignal): Promise<MarketRequestList> {
  const answer = await request<MarketRequest[]>(
    TRANSFER_BASE_URL,
    path,
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, requests: answer.value ?? [] };
  // Do NOT show `503` as an empty list: that would be the claim that nobody
  // asked or released — and at this moment nobody knows that.
  return interpretError<"unavailable">(
    answer.error,
    {
      0: { reason: "unavailable", titleKey: "errors.noConnection" },
      503: { reason: "unavailable", titleKey: "errors.ledgerSilent" },
    },
    "unavailable"
  );
}

export function listMyMarketRequests(signal?: AbortSignal): Promise<MarketRequestList> {
  return listRequests("/market/me/requests", signal);
}

export function listCompanyMarketRequests(signal?: AbortSignal): Promise<MarketRequestList> {
  return listRequests("/market/requests", signal);
}

/**
 * Another person's market status.
 *
 * `404` becomes `status: null` — "does not exist", "not released" and "not
 * right now" are the same case for the UI, just as the server keeps them
 * indistinguishable.
 */
export async function getMarketStatus(
  subjectId: string,
  signal?: AbortSignal
): Promise<{ ok: true; status: MarketStatus | null } | Failure<"unavailable">> {
  const answer = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    `/market/${subjectId}`,
    { signal },
    "errors.marketStatusNotLoaded"
  );
  if (answer.ok) return { ok: true, status: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, status: null };
  return interpretError<"unavailable">(
    answer.error,
    { 503: { reason: "unavailable", titleKey: "errors.ledgerSilent" } },
    "unavailable"
  );
}
