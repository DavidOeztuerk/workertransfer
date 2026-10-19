import type { ApiError } from "../../core/store/thunkHelpers";
import type { Catalog } from "../../core/i18n/catalogs/de";

/**
 * Turn a status code into a reason and a sentence — in ONE place.
 *
 * Why a "reason" beside the status code at all: the pages do not answer `403`,
 * `404` and `503` in the same shape. `503` means "the ledger is silent" and
 * must never appear as an empty list — that would be the claim that nobody has
 * released anything, and at that moment nobody knows that. `403` means "you are
 * not acting for a company" and is a statement about the CALLER, so a calm hint
 * and not an error message. `404` stays indistinguishable from "not released";
 * the UI does not build out of it a piece of information the server has just
 * refused to give.
 *
 * What is NOT lost: `status` and `correlationId` stay. The id is the only
 * thread along which a complaint can be traced back through every service — a
 * sentence of our own must not cut it off.
 *
 * If the table holds no entry, the server's sentence stays in `detail` — to be
 * READ, not drawn. Its problem document knows more about the case at `409` and
 * `422` than a status does, so a site may sort it by its words into a key
 * (`failureKey` below). It is English on the wire (CLAUDE.md, "Frontend"), and
 * until 26.09.2026 a dozen pages drew it raw: a German person read "No draft
 * provider is set up." in the middle of a German page.
 *
 * <strong>This file existed twice</strong> — once under `work/api` and once
 * under `company/api`, word for word, and the comment there said itself that it
 * belonged in `shared/`. Here it is.
 */

/**
 * A key from the catalogue's `errors` section — never a sentence.
 *
 * The type is the actual tool: i18next hands an unknown key back unchanged, so
 * a German sentence left behind here would look RIGHT in the UI and be
 * translated into no other language. With this type it is a compile error.
 * It is derived from the catalogue, so a renamed or deleted key is one too.
 */
export type ErrorKey = `errors.${keyof Catalog["errors"] & string}`;

/**
 * How one status code reads. The fields hold KEYS and are named so — a field
 * called `title` would be one the UI draws, and `keys.test.ts` rightly refuses
 * a raw key there.
 */
export interface ErrorReading<R extends string> {
  reason: R;
  titleKey: ErrorKey;
  /** If absent, the title carries alone. */
  detailKey?: ErrorKey;
}

export interface Failure<R extends string> {
  ok: false;
  reason: R;
  error: ApiError;
}

export function interpretError<R extends string>(
  error: ApiError,
  table: Partial<Record<number, ErrorReading<R>>>,
  fallbackReason: R
): Failure<R> {
  const reading = table[error.status];
  if (reading === undefined) return { ok: false, reason: fallbackReason, error };

  // KEYS, not sentences: `title` and `detail` stay the server's words, so that
  // a site can still sort a status the table names by its sentence. The keys
  // are looked up where they are drawn — at the answer and in the language
  // chosen then, not when this module loaded. Until 26.09.2026 the texts were
  // written into `title`/`detail` here, and a page drawing `detail` could not
  // tell a catalogue text from the server's English.
  return {
    ok: false,
    reason: reading.reason,
    error: {
      ...error,
      titleKey: reading.titleKey,
      detailKey: reading.detailKey ?? reading.titleKey,
    },
  };
}

/**
 * Any text in the catalogue, by section — so that a sentence rule naming a
 * key that does not exist is a compile error, not a key drawn as text.
 */
export type MessageKey = {
  [S in keyof Catalog & string]: `${S}.${keyof Catalog[S] & string}`;
}[keyof Catalog & string];

/**
 * One server case the person must answer differently from the step's own
 * failure. Without `pattern` the status alone decides.
 *
 * A pattern matches a sentence the BACKEND writes, and that line carries a
 * comment pointing back here — whoever rewords it there knows who reads it.
 */
export interface ServerCase {
  status: number;
  pattern?: RegExp;
  key: MessageKey;
}

/**
 * The refusal on the eight routes that demand an `admin` (PBI-2), as every
 * service answers it — `RejectionShape.cs` in ServiceDefaults writes both
 * sentences. A 403 there is about the ROLE, not about a missing company, and
 * a silent role lookup is an outage, not a refusal.
 */
export const ADMIN_CASES: readonly ServerCase[] = [
  { status: 403, pattern: /^not permitted$/i, key: "errors.adminOnly" },
  { status: 503, pattern: /role lookup did not answer/i, key: "errors.serviceSilent" },
];

/**
 * The refusal on the three routes of a FIRST CONTACT (ADR-0048): asking for a
 * CV, asking for the market status, opening an advisor conversation. A 403
 * there with "not permitted" is about the ROLE — a member, not a recruiter — and
 * must not read "choose a company", which the status table says for a person
 * acting as themselves.
 */
export const RECRUITER_CASES: readonly ServerCase[] = [
  { status: 403, pattern: /^not permitted$/i, key: "errors.recruiterOnly" },
  { status: 503, pattern: /role lookup did not answer/i, key: "errors.serviceSilent" },
];

/**
 * A failure as a catalogue key — <strong>never the server's sentence.</strong>
 *
 * In this order: the site's own cases (status, and the sentence only where
 * the person has to act differently — "revise first" is not "the job is
 * gone"); the key `interpretError` chose by status; no connection; an expired
 * session; and otherwise the step's own `fallback`. An unknown
 * English sentence stays unknown and is still English — so it falls to the
 * step, not through to the page.
 */
export function failureKey(
  error: Pick<ApiError, "status" | "detail" | "detailKey">,
  fallback: MessageKey,
  cases: readonly ServerCase[] = []
): MessageKey {
  const detail = (error.detail ?? "").trim();
  const found = cases.find(
    (entry) => entry.status === error.status && (entry.pattern?.test(detail) ?? true)
  );
  if (found !== undefined) return found.key;

  if (error.detailKey !== undefined) return error.detailKey;
  if (error.status === OFFLINE) return "errors.noConnection";
  if (error.status === 401) return "errors.sessionExpiredLong";
  return fallback;
}

/** A network error arrives as `status: 0` — there is no problem document there. */
export const OFFLINE = 0;
