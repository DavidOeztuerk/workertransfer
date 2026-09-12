// Das Mandat und die Gespräche — advisor-service (ADR-0037).
//
// <strong>Das Mandat ist eine SICHT, kein zweiter Speicher.</strong> Wer mich
// sehen darf, steht im Consent-Ledger; ob ich ansprechbar bin, im Marktstatus
// bei transfer-service. Dieser Client kennt deshalb keinen Sichtbarkeitsschalter
// und darf nie einen bekommen — er hätte nichts, wohin er schreiben könnte.
//
// <strong>Und die Stufe ist eine Auskunft, kein Feld.</strong> `stage` kommt bei
// jeder Antwort frisch aus dem Ledger. Sie hier zwischenzuspeichern wäre
// dasselbe wie eine Stufenspalte auf dem Server, nur flüchtiger: ein Widerruf
// muss bei der nächsten Anfrage wirken (ADR-0013).

import { request } from "../../../core/api/client";
import { ADVISOR_BASE_URL } from "../../../env";
import { type Deutung, type Fehlschlag, deuten } from "../../../shared/api/fehler";

/**
 * Das Mandat: vier Werte, alle freiwillig.
 *
 * `null` heisst überall „nichts gesagt", und das ist der Normalfall. Ein leeres
 * Mandat ist vollständig — niemand muss ein Gehalt nennen, um ein Gespräch zu
 * führen.
 */
export interface Mandat {
  /** Monat `JJJJ-MM`. Ein Monat und kein Datum: „ab März" ist, was man sagen kann. */
  entry_month: string | null;
  /** Euro im Monat. */
  salary_min: number | null;
  salary_max: number | null;
  /** 10–100. */
  workload_percent: number | null;
  /**
   * Domains, mit denen kein Gespräch zustande kommt.
   *
   * <strong>Wer hier etwas einträgt, gibt „für alle Unternehmen sichtbar"
   * auf.</strong> Der Ledger kennt keine Verneinung: „sichtbar für alle, aber
   * nicht für X" ist darin nicht ausdrückbar. Der Server nimmt die öffentliche
   * Sichtbarkeit deshalb im selben Schritt zurück — und erst damit hält die
   * Zusage, dass der jetzige Arbeitgeber die eigene Belegschaft nicht findet.
   */
  excluded_domains: string[];
  updated_at: string;
}

/** Was geschrieben wird. Alle Felder optional — was fehlt, wird geleert. */
export interface MandatEingabe {
  entry_month: string | null;
  salary_min: number | null;
  salary_max: number | null;
  workload_percent: number | null;
  excluded_domains: string[];
}

/** Wo ein Gespräch steht. Nicht zu verwechseln mit der Stufe. */
export type Gespraechsstand = "talking" | "agreed" | "handed_over" | "ended";

/**
 * Ein Gespräch, wie die Person es sieht.
 *
 * `stage` ist 0 bis 3 und kommt aus dem Ledger. `0` heisst: für die Gegenseite
 * gibt es dieses Gespräch gerade nicht.
 */
export interface MeinGespraech {
  id: string;
  tenant_id: string;
  state: Gespraechsstand;
  stage: number;
  note: string;
  opened_at: string;
  updated_at: string;
}

/**
 * Ein Gespräch, wie das Unternehmen es sieht.
 *
 * <strong>Die Felder der höheren Stufen FEHLEN, sie sind nicht `null`.</strong>
 * Deshalb sind sie hier optional statt nullbar: ein `| null` lüde dazu ein,
 * „noch nicht freigegeben" anzuzeigen — und genau das ist der Hinweis, den es
 * nicht geben darf. Fehlt das Feld, gibt es nichts zu zeigen, und warum es
 * fehlt, geht das Unternehmen nichts an.
 */
export interface Gespraech {
  id: string;
  subject_id: string;
  state: Gespraechsstand;
  stage: number;
  note: string;
  opened_at: string;
  updated_at: string;
  entry_month?: string;
  workload_percent?: number;
  salary_min?: number;
  salary_max?: number;
  name?: string;
  email?: string;
}

export type MandatErgebnis = { ok: true; mandat: Mandat } | Fehlschlag<"unavailable">;
export type SchreibErgebnis =
  | { ok: true; mandat: Mandat }
  | Fehlschlag<"unauthenticated" | "invalid" | "unavailable" | "offline">;
export type MeineListe = { ok: true; items: MeinGespraech[] } | Fehlschlag<"unavailable">;
export type Firmenliste =
  | { ok: true; items: Gespraech[] }
  | Fehlschlag<"no-company" | "unavailable">;
export type Stufenergebnis =
  | { ok: true; stage: number }
  | Fehlschlag<"gone" | "unavailable" | "offline">;
export type Zugergebnis =
  | { ok: true }
  | Fehlschlag<"gone" | "conflict" | "unavailable" | "offline">;

const SCHWEIGT: Deutung<"unavailable"> = {
  reason: "unavailable",
  titel: "fehler.ledgerSchweigt",
  text: "fehler.spaeterErneut",
};

/**
 * Das eigene Mandat. Nie `404` — „nichts gesagt" ist ein Zustand.
 *
 * Wie beim Marktstatus gibt es hier <strong>keinen Ersatzwert</strong>. Ein
 * erfundenes leeres Mandat landete im Formular, und wer dann etwas anfasste und
 * speicherte, löschte still seine Gehaltsspanne. `ok: false` heisst „wir wissen
 * es nicht", und die Seite zeigt dann kein Formular.
 */
export async function ladeMandat(signal?: AbortSignal): Promise<MandatErgebnis> {
  const answer = await request<Mandat>(
    ADVISOR_BASE_URL,
    "/advisor/me/mandate",
    { signal },
    "fehler.mandatNichtAbrufbar"
  );

  if (answer.ok) return { ok: true, mandat: answer.value };

  return deuten<"unavailable">(answer.error, { 503: SCHWEIGT }, "unavailable");
}

export async function schreibeMandat(eingabe: MandatEingabe): Promise<SchreibErgebnis> {
  const answer = await request<Mandat>(
    ADVISOR_BASE_URL,
    "/advisor/me/mandate",
    { method: "PUT", body: eingabe },
    "fehler.mandatNichtGespeichert"
  );

  if (answer.ok) return { ok: true, mandat: answer.value };

  return deuten<"unauthenticated" | "invalid" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      401: {
        reason: "unauthenticated",
        titel: "fehler.sitzungAbgelaufen",
        text: "fehler.erneutAnmelden",
      },
      503: SCHWEIGT,
    },
    "invalid"
  );
}

export async function ladeMeineGespraeche(signal?: AbortSignal): Promise<MeineListe> {
  const answer = await request<{ items: MeinGespraech[] }>(
    ADVISOR_BASE_URL,
    "/advisor/me/conversations",
    { signal },
    "fehler.listeNichtGeladen"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  // `503` NICHT als leere Liste zeigen: das waere die Behauptung, niemand habe
  // ein Gespraech eroeffnet — und das weiss in diesem Moment niemand.
  return deuten<"unavailable">(answer.error, { 503: SCHWEIGT }, "unavailable");
}

export async function ladeFirmengespraeche(signal?: AbortSignal): Promise<Firmenliste> {
  const answer = await request<{ items: Gespraech[] }>(
    ADVISOR_BASE_URL,
    "/advisor/conversations",
    { signal },
    "fehler.listeNichtGeladen"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  return deuten<"no-company" | "unavailable">(
    answer.error,
    {
      403: {
        reason: "no-company",
        titel: "fehler.nurFirmenFragen",
        text: "fehler.firmaWaehlen",
      },
      503: SCHWEIGT,
    },
    "unavailable"
  );
}

/**
 * Eine Stufe freigeben oder zurücknehmen.
 *
 * Zwei Pfade statt eines Endpunkts mit Flag: freigeben und widerrufen sind
 * verschiedene Handlungen, und im Protokoll des Ledgers bleiben sie das.
 */
async function stufenzug(id: string, pfad: string, stage: number): Promise<Stufenergebnis> {
  const answer = await request<{ stage: number }>(
    ADVISOR_BASE_URL,
    `/advisor/me/conversations/${id}/${pfad}`,
    { method: "POST", body: { stage } },
    "fehler.stufeNichtGeaendert"
  );

  if (answer.ok) return { ok: true, stage: answer.value?.stage ?? 0 };

  return deuten<"gone" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      // `404` sagt bewusst nicht, ob es das Gespraech gibt.
      404: { reason: "gone", titel: "fehler.gespraechFort" },
      503: SCHWEIGT,
    },
    "offline"
  );
}

export const gibFrei = (id: string, stage: number) => stufenzug(id, "advance", stage);
export const nimmZurueck = (id: string, stage: number) => stufenzug(id, "withdraw", stage);

async function zug(pfad: string): Promise<Zugergebnis> {
  const answer = await request<unknown>(
    ADVISOR_BASE_URL,
    pfad,
    { method: "POST" },
    "fehler.gespraechNichtGeaendert"
  );

  if (answer.ok) return { ok: true };

  return deuten<"gone" | "conflict" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      404: { reason: "gone", titel: "fehler.gespraechFort" },
      // `409` kommt mit dem Satz des Servers: sein Problemdokument weiss ueber
      // diesen Uebergang mehr als eine Tabelle hier.
      409: { reason: "conflict", titel: "fehler.schrittNichtMoeglich" },
      503: SCHWEIGT,
    },
    "offline"
  );
}

export const stimmeZu = (id: string) => zug(`/advisor/me/conversations/${id}/agree`);
export const beendeAlsPerson = (id: string) => zug(`/advisor/me/conversations/${id}/end`);
export const beendeAlsFirma = (id: string) => zug(`/advisor/conversations/${id}/end`);
export const uebergib = (id: string) => zug(`/advisor/conversations/${id}/hand-over`);

/** Ein Gespräch eröffnen — nur ein Unternehmen, und nur bei Freigabe. */
export async function eroeffne(subjectId: string, note: string): Promise<Zugergebnis> {
  const answer = await request<Gespraech>(
    ADVISOR_BASE_URL,
    "/advisor/conversations",
    { method: "POST", body: { subject_id: subjectId, note } },
    "fehler.gespraechNichtEroeffnet"
  );

  if (answer.ok) return { ok: true };

  return deuten<"gone" | "conflict" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      403: { reason: "conflict", titel: "fehler.nurFirmenFragen", text: "fehler.firmaWaehlen" },
      // `404` heisst: diese Person hat diesem Unternehmen nichts freigegeben —
      // oder sie hat es ausgeschlossen, oder es gibt sie nicht. Die drei sind
      // ununterscheidbar, und die Oberflaeche bastelt daraus keine Auskunft.
      404: { reason: "gone", titel: "fehler.personNichtAnfragbar" },
      503: SCHWEIGT,
    },
    "offline"
  );
}
