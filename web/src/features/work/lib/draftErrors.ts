import type { ApiError } from "../../../core/store/thunkHelpers";

/**
 * From the server's sentence to a catalogue key — never the status code.
 *
 * applications-service answers with a problem document whose `detail` is a
 * sentence: English for most reasons ("No writing-help provider is set up.",
 * "… (timeout).", "… answers with 404.", "… sent an unusable or empty
 * response."), German where a step is not allowed ("Ein Entwurf im Stand …
 * kann nicht geschrieben werden."). The UI does not show that sentence and does
 * not interpolate it: a number like 402 or 422 would look like the page's own
 * error, the language would be the server's instead of the person's, and the
 * human being could do nothing with it. It sorts it by its words instead.
 *
 * The patterns match BOTH languages on purpose — older drafts carry the German
 * sentences of the time in their `error` field, and whoever rewords a reason on
 * the server checks it against this file (`CoverLetterUnavailable` names it).
 */
export type WritingReason =
  | "noProvider"
  | "timeout"
  | "providerRejected"
  | "providerEmpty"
  | "stepNotNow"
  | "providerNotWriting";

export type WritingKey = `drafts.${WritingReason}`;

export function writingReason(error: string | null | undefined): WritingReason {
  const err = (error ?? "").trim();
  if (
    err === "" ||
    /kein Entwurfsanbieter/i.test(err) ||
    /no writing-help provider/i.test(err) ||
    /kein Anbieter/i.test(err)
  ) {
    return "noProvider";
  }
  if (/Zeitüberschreitung|Zeitueberschreitung|timeout/i.test(err)) {
    return "timeout";
  }
  if (/leeren Text|unbrauchbare Antwort|empty (text|response)/i.test(err)) {
    return "providerEmpty";
  }
  if (/kann nicht (geschrieben|geändert|überarbeitet) werden/i.test(err)) {
    return "stepNotNow";
  }
  if (/antwortet mit \d+/i.test(err) || /\b[45]\d{2}\b/.test(err)) {
    return "providerRejected";
  }
  return "providerNotWriting";
}

export function writingKey(error: string | null | undefined): WritingKey {
  return `drafts.${writingReason(error)}`;
}

export function emptyLetterHintKey(
  error: string | null | undefined,
): "drafts.emptyLetterHint" | WritingKey | "drafts.emptyLetterStart" {
  const reason = writingReason(error);
  if (reason === "noProvider") return "drafts.emptyLetterHint";
  if (reason === "providerNotWriting") return "drafts.emptyLetterStart";
  return `drafts.${reason}`;
}

/**
 * The steps on a draft whose failure is read here. `write` and `revise` are
 * not among them: their failure is about the writing help and goes through
 * `writingReason`.
 */
export type DraftStep = "save" | "attachments" | "comment" | "approve" | "send" | "delete";

export type DraftStepErrorKey =
  | "errors.noConnection"
  | "errors.sessionExpiredLong"
  | "errors.draftNotFound"
  | "errors.jobClosed"
  | "errors.draftNotSaved"
  | "errors.draftStepFailed"
  | "errors.draftNotDeleted"
  | "drafts.stepNotNow"
  | "drafts.stepOpenComments"
  | "drafts.stepApproveFirst"
  | "drafts.applicationExists"
  | "drafts.commentEmpty"
  | "drafts.commentTooLong"
  | "drafts.tooManyComments"
  | "drafts.approveEmpty"
  | "drafts.letterTooLong";

/** What a step's failure says when the server's sentence names nothing we know. */
const FALLBACK: Record<DraftStep, DraftStepErrorKey> = {
  save: "errors.draftNotSaved",
  attachments: "errors.draftNotSaved",
  comment: "errors.draftNotSaved",
  approve: "errors.draftStepFailed",
  send: "errors.draftStepFailed",
  delete: "errors.draftNotDeleted",
};

/**
 * A failed step on a draft, as a catalogue key — chosen by status first, and
 * by the server's sentence only where the person has to do something about it.
 *
 * <strong>Until 26.09.2026 the draft page showed `detail` raw</strong> for
 * approve, send, comment, save and attachments. Since applications-service
 * answers in English (62e819e), a German person read "There are open comments
 * — revise first." in the middle of a German page. The UI translates by status
 * (`shared/api/errors.ts`), and the sentence decides only between cases the
 * person must act on differently: revise first, approve first, the job is
 * gone, a text is too long or empty.
 *
 * What the patterns do not know falls back to the step's own sentence, never
 * to the server's — an unknown English sentence is still English.
 */
export function draftStepErrorKey(
  step: DraftStep,
  error: Pick<ApiError, "status" | "detail">,
): DraftStepErrorKey {
  const detail = (error.detail ?? "").trim();

  switch (error.status) {
    case 0:
      return "errors.noConnection";
    case 401:
      return "errors.sessionExpiredLong";
    case 404:
      return "errors.draftNotFound";
    case 409:
      // "There are open comments — revise first." (approve)
      if (/open comments/i.test(detail)) return "drafts.stepOpenComments";
      // "Only an approved draft goes out." (send)
      if (/only an approved draft/i.test(detail)) return "drafts.stepApproveFirst";
      // "This job is no longer advertised." (send)
      if (/no longer advertised/i.test(detail)) return "errors.jobClosed";
      // "A Submitted application cannot become Submitted." (send, resubmit)
      if (/application cannot become/i.test(detail)) return "drafts.applicationExists";
      // Everything else, the German "Ein Entwurf im Stand … kann nicht …
      // werden." included: the state moved on elsewhere.
      return "drafts.stepNotNow";
    case 422:
      if (/comment without text/i.test(detail)) return "drafts.commentEmpty";
      if (/comment is longer than/i.test(detail)) return "drafts.commentTooLong";
      if (/no more than \d+ comments/i.test(detail)) return "drafts.tooManyComments";
      if (/approves an empty/i.test(detail)) return "drafts.approveEmpty";
      if (/(cover letter|message) is longer than/i.test(detail)) return "drafts.letterTooLong";
      return FALLBACK[step];
    default:
      return FALLBACK[step];
  }
}
