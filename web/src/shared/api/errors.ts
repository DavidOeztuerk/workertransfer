import type { ApiError } from "../../core/store/thunkHelpers";
import { i18n } from "../../core/i18n/i18n";
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
 * If the table holds no entry, the server's sentence stays: its problem
 * document knows more about the case at `409` and `422` than we do here.
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

  // Translated HERE and not when the table is built: the tables stand at module
  // level, so their values would be built on load — long before anybody could
  // choose a language.
  const title = i18n.t(reading.titleKey);
  return {
    ok: false,
    reason: reading.reason,
    error: {
      ...error,
      title,
      detail: reading.detailKey === undefined ? title : i18n.t(reading.detailKey),
    },
  };
}

/** A network error arrives as `status: 0` — there is no problem document there. */
export const OFFLINE = 0;
