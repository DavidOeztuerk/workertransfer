/**
 * Aus dem Satz des Servers einen Katalogschlüssel — nie den Statuscode.
 *
 * Der Draht trägt englische Problemdokumente und deutsche Gründe wie
 * „antwortet mit 404" oder „Zeitüberschreitung". Die Oberfläche interpoliert
 * das nicht: eine Zahl wie 402 oder 422 sähe aus wie der Fehler der Seite,
 * und der Mensch kann damit nichts anfangen.
 */
export type Schreibgrund =
  | "noProvider"
  | "timeout"
  | "providerRejected"
  | "providerEmpty"
  | "stepNotNow"
  | "providerNotWriting";

export type Schreibschluessel = `drafts.${Schreibgrund}`;

export function schreibgrund(error: string | null | undefined): Schreibgrund {
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

export function schreibschluessel(error: string | null | undefined): Schreibschluessel {
  return `drafts.${schreibgrund(error)}`;
}

export function leerBriefHinweisSchluessel(
  error: string | null | undefined,
): "drafts.emptyLetterHint" | Schreibschluessel | "drafts.emptyLetterStart" {
  const grund = schreibgrund(error);
  if (grund === "noProvider") return "drafts.emptyLetterHint";
  if (grund === "providerNotWriting") return "drafts.emptyLetterStart";
  return `drafts.${grund}`;
}
