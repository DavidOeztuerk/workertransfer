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

