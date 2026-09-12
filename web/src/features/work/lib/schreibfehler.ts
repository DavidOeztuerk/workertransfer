/**
 * Aus dem Satz des Servers einen Katalogschlüssel — nie den Statuscode.
 *
 * Der Draht trägt englische Problemdokumente und deutsche Gründe wie
 * „antwortet mit 404" oder „Zeitüberschreitung". Die Oberfläche interpoliert
 * das nicht: eine Zahl wie 402 oder 422 sähe aus wie der Fehler der Seite,
 * und der Mensch kann damit nichts anfangen.
 */
export type Schreibgrund =
  | "keinAnbieter"
  | "zeitueberschreitung"
  | "anbieterAblehnung"
  | "anbieterLeer"
  | "schrittJetztNicht"
  | "anbieterAntwortet";

export type Schreibschluessel = `entwurfs.${Schreibgrund}`;

export function schreibgrund(error: string | null | undefined): Schreibgrund {
  const err = (error ?? "").trim();
  if (
    err === "" ||
    /kein Entwurfsanbieter/i.test(err) ||
    /no writing-help provider/i.test(err) ||
    /kein Anbieter/i.test(err)
  ) {
    return "keinAnbieter";
  }
  if (/Zeitüberschreitung|Zeitueberschreitung|timeout/i.test(err)) {
    return "zeitueberschreitung";
  }
  if (/leeren Text|unbrauchbare Antwort|empty (text|response)/i.test(err)) {
    return "anbieterLeer";
  }
  if (/kann nicht (geschrieben|geändert|überarbeitet) werden/i.test(err)) {
    return "schrittJetztNicht";
  }
  if (/antwortet mit \d+/i.test(err) || /\b[45]\d{2}\b/.test(err)) {
    return "anbieterAblehnung";
  }
  return "anbieterAntwortet";
}

export function schreibschluessel(error: string | null | undefined): Schreibschluessel {
  return `entwurfs.${schreibgrund(error)}`;
}

export function leerBriefHinweisSchluessel(
  error: string | null | undefined,
): "entwurfs.leerBriefHinweis" | Schreibschluessel | "entwurfs.leerBriefStart" {
  const grund = schreibgrund(error);
  if (grund === "keinAnbieter") return "entwurfs.leerBriefHinweis";
  if (grund === "anbieterAntwortet") return "entwurfs.leerBriefStart";
  return `entwurfs.${grund}`;
}
