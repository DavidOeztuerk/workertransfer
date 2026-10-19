// The transfer case, as the UI uses it.
//
// One path per transition, no `PATCH status`: every transition belongs to one
// side, and a shared one would have to find out on every call who may do what.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import {
  type ErrorReading,
  type ErrorKey,
  type Failure,
  type ServerCase,
  interpretError,
} from "../../../shared/api/errors";

export type TransferStatus =
  | "interested"
  | "talking"
  | "offered"
  | "accepted"
  | "completed"
  | "declined"
  | "withdrawn";

export interface Transfer {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: TransferStatus;
  /**
   * Copied from the market status on creation. The employer is asked only over
   * the route both opened (ADR-0050) — see `releases.ts`; the case itself never
   * names it.
   */
  requires_release: boolean;
  release_confirmed: boolean;
  message: string;
  offer_note: string;
  offer_start_on: string | null;
  /**
   * Only in the person's own view (H2): whether the offer starts before she
   * could leave. Absent when no notice period is stated or the start is not a
   * month — absent is "we do not know", never "fine".
   */
  starts_before_earliest?: boolean;
  created_at: string;
  updated_at: string;
}

/** Text and start — the fee is agreed between two companies now (ADR-0050). */
export interface OfferInput {
  note: string;
  start_on: string | null;
}

export type PersonAction = "accept-talk" | "accept-offer" | "confirm-release" | "decline";
export type CompanyAction = "complete" | "withdraw";

export type TransferError = "not-available" | "no-company" | "conflict" | "unavailable" | "offline";
export type TransferResult = { ok: true; transfer: Transfer } | Failure<TransferError>;
export type TransferList = { ok: true; transfers: Transfer[] } | Failure<"failed">;

/** Is the case still running? Only then is there a way out. */
export const RUNNING: TransferStatus[] = ["interested", "talking", "offered", "accepted"];

/**
 * A release fee in words, in the CHOSEN language.
 *
 * `Intl.NumberFormat` and not by hand: `5.000,00 €` stands like that in the E2E
 * journey, and a hand-written format misses either the narrow space before the
 * euro sign or the thousands separators.
 *
 * The language comes from i18next and not from the device: otherwise a German
 * sentence would stand beside an American-formatted number. The CURRENCY stays
 * euro — it is a fact about the amount, not a question of display.
 */
export function euro(cents: number | null): string {
  if (cents === null) return "—";
  return new Intl.NumberFormat(i18n.language, {
    style: "currency",
    currency: "EUR",
  }).format(cents / 100);
}

const TRANSITION_ERRORS: Partial<Record<number, ErrorReading<TransferError>>> = {
  0: { reason: "offline", titleKey: "errors.noConnection" },
  // No status, no release, "not right now", someone else's case — deliberately
  // not told apart by the server. The UI does not tell them apart either.
  404: { reason: "not-available", titleKey: "errors.notPossibleNow" },
  403: {
    reason: "no-company",
    titleKey: "errors.activeCompanyRequired",
    detailKey: "errors.chooseCompanyShort",
  },
  503: {
    reason: "unavailable",
    titleKey: "errors.ledgerSilent",
    detailKey: "errors.tryLater",
  },
};

async function move(
  path: string,
  body?: unknown,
  fallback: ErrorKey = "errors.stepFailed"
): Promise<TransferResult> {
  const answer = await request<Transfer>(TRANSFER_BASE_URL, path, { method: "POST", body }, fallback);
  if (answer.ok) return { ok: true, transfer: answer.value };
  if (answer.error.status === 409) {
    return { ok: false, reason: "conflict", error: answer.error };
  }
  return interpretError<TransferError>(answer.error, TRANSITION_ERRORS, "offline");
}

/**
 * The company's steps on a transfer, as the person at the company reads them —
 * never transfer-service's sentence, which is English on the wire.
 *
 * The status would say "conflict" twice where the company must do two
 * different things: look for the transfer that already runs, or reload because
 * the person moved it on. And an offer names what was wrong with it. Each
 * pattern matches a sentence in transfer-service (`Transfer.cs`,
 * `Responses.cs`) that points back here.
 */
export const COMPANY_TRANSFER_CASES: readonly ServerCase[] = [
  { status: 409, pattern: /already a running transfer/i, key: "companyTransfers.alreadyRunning" },
  { status: 409, key: "companyTransfers.stepNotNow" },
  { status: 422, pattern: /exceeds \d+ characters/i, key: "companyTransfers.textTooLong" },
  { status: 422, pattern: /start must be a month/i, key: "companyTransfers.startNotAMonth" },
];

export function expressInterest(subjectId: string, message: string): Promise<TransferResult> {
  return move(
    "/transfers",
    { subject_id: subjectId, message },
    "errors.interestNotRecorded"
  );
}

export function personMove(transferId: string, action: PersonAction): Promise<TransferResult> {
  return move(`/transfers/${transferId}/${action}`);
}

export function companyMove(transferId: string, action: CompanyAction): Promise<TransferResult> {
  return move(`/transfers/${transferId}/${action}`);
}

export function makeOffer(transferId: string, input: OfferInput): Promise<TransferResult> {
  // `null` instead of "" for the month: "" is no month and failed the
  // contract's pattern — with a message that looks like a server problem.
  return move(
    `/transfers/${transferId}/offer`,
    { note: input.note, start_on: input.start_on },
    "errors.offerNotMade"
  );
}

async function listTransfers(path: string, signal?: AbortSignal): Promise<TransferList> {
  const answer = await request<Transfer[]>(
    TRANSFER_BASE_URL,
    path,
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, transfers: answer.value ?? [] };
  // Do not show as an empty list: "no cases" is a statement, and it would be
  // false.
  return interpretError<"failed">(
    answer.error,
    { 0: { reason: "failed", titleKey: "errors.noConnection" } },
    "failed"
  );
}

export function listMyTransfers(signal?: AbortSignal): Promise<TransferList> {
  return listTransfers("/transfers/me", signal);
}

export function listCompanyTransfers(signal?: AbortSignal): Promise<TransferList> {
  return listTransfers("/transfers", signal);
}
