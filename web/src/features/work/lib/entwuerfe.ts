import type { Draft } from "../api/applications";
import { createDrafts, writeDraft } from "../api/applications";

/**
 * Aus Stellen Entwürfe machen — die eine Stelle im Browser, die das tut.
 *
 * <strong>Zwei Aufrufe und nicht einer.</strong> `POST /applications/drafts`
 * legt nur an (Stand <c>generating</c>); geschrieben wird je Entwurf einzeln
 * über <c>/write</c>. So bleibt jeder Entwurf für sich sichtbar, statt dass ein
 * Sammelaufruf still die Hälfte verschluckt.
 *
 * <strong>Warum das hier liegt und nicht in einer Seite.</strong> Es gab zwei
 * Wege zur Bewerbung — die Stellenliste und die Karriereseite —, und der zweite
 * führte über eine eigene Seite <c>/jobs/:id/apply</c>, die nichts weiter tat,
 * als genau dies zu wiederholen und dann weiterzuleiten. Eine Weiche mit
 * eigener Adresse ist eine zweite Wahrheit über denselben Vorgang; die Adresse
 * ist gefallen, der Vorgang steht hier.
 */
export interface Entwurfsstart {
  ok: boolean;
  drafts: Draft[];
  /** Der Satz des Servers, wenn es schiefging. */
  fehler: string | null;
}

export async function starteEntwuerfe(jobIds: string[]): Promise<Entwurfsstart> {
  const result = await createDrafts(jobIds);
  if (!result.ok) return { ok: false, drafts: [], fehler: result.error.detail };

  const drafts = result.drafts;
  await Promise.all(
    drafts
      .filter((draft) => draft.status === "generating" || draft.status === "failed")
      .map((draft) => writeDraft(draft.id)),
  );
  return { ok: true, drafts, fehler: null };
}
