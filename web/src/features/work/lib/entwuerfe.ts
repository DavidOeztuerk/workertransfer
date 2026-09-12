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

/**
 * Höchstens drei Schreibaufträge gleichzeitig.
 *
 * Vorher stand hier `Promise.all` über ALLE gewählten Stellen: wer zwölf
 * ankreuzte, schickte zwölf Aufträge gleichzeitig an applications-service und
 * von dort an den KI-Anbieter — beides Stellen, an denen eine Person allein
 * eine Warteschlange für alle anderen erzeugt. Die Zahl ist die aus dem
 * Auftrag und keine gemessene Optimierung; sie darf steigen, wenn jemand misst.
 */
export const GLEICHZEITIG = 3;

/**
 * Wie viele von wie vielen geschrieben sind.
 *
 * Wird nach JEDEM fertigen Entwurf gerufen, einmal mit `0` vorab — ohne den
 * ersten Ruf steht die Leiste erst nach dem ersten Entwurf da und springt
 * dann aus dem Nichts auf „1 von 12".
 */
export type Fortschrittsmelder = (fertig: number, gesamt: number) => void;

export async function starteEntwuerfe(
  jobIds: string[],
  melde?: Fortschrittsmelder,
): Promise<Entwurfsstart> {
  const result = await createDrafts(jobIds);
  if (!result.ok) return { ok: false, drafts: [], fehler: result.error.detail };

  const drafts = result.drafts;
  const offen = drafts.filter(
    (draft) => draft.status === "generating" || draft.status === "failed",
  );

  melde?.(0, offen.length);

  /*
   * Eine Schlange und drei Arbeiter, nicht drei Blöcke nacheinander.
   *
   * `chunk(3)` wäre kürzer und wäre langsamer: ein Block wartet auf seinen
   * langsamsten Entwurf, bevor der nächste anfängt — bei einem Anbieter, der
   * je Anfrage verschieden lange braucht, stünden zwei von drei Plätzen einen
   * grossen Teil der Zeit leer. Hier zieht jeder Arbeiter nach, sobald er
   * fertig ist.
   */
  let naechster = 0;
  let fertig = 0;

  async function arbeite(): Promise<void> {
    for (;;) {
      const eigener = naechster;
      naechster += 1;
      const entwurf = offen[eigener];
      if (entwurf === undefined) return;

      // Das Ergebnis wird NICHT geprüft: ein einzelner fehlgeschlagener
      // Entwurf steht danach auf `failed` und ist auf der Entwurfsseite
      // sichtbar — die Reihe deshalb abzubrechen nähme den anderen elf ihren
      // Text weg.
      await writeDraft(entwurf.id);
      fertig += 1;
      melde?.(fertig, offen.length);
    }
  }

  await Promise.all(
    Array.from({ length: Math.min(GLEICHZEITIG, offen.length) }, () => arbeite()),
  );

  return { ok: true, drafts, fehler: null };
}
