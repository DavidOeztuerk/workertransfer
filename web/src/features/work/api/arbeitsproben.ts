// Die Arbeitsprobe — assessment-service (ADR-0042).
//
// <strong>Ein Typ für beide Seiten, und das ist kein Sparen an Zeilen.</strong>
// `GET /assessments/{id}` antwortet Person und Unternehmen byte-gleich
// dasselbe Dokument; zwei Schnittstellen hier wären die Stelle, an der die
// Oberfläche anfängt, für die eine Seite etwas wegzulassen. „Die Person sieht
// die Bewertung, immer" ist eine Eigenschaft des Baus und keine Regel, an die
// man sich erinnern muss.
//
// <strong>Und es gibt keine Zahl.</strong> Kein Score, kein Prozentwert, keine
// Sterne. `hours` ist eine Zahl und darf es sein — sie handelt von der
// AUFGABE, nicht vom Menschen. Genau das ist die Trennlinie von ADR-0022.

import { request } from "../../../core/api/client";
import { ASSESSMENT_BASE_URL } from "../../../env";
import {
  type Deutung,
  type Fehlerschluessel,
  type Fehlschlag,
  deuten,
} from "../../../shared/api/fehler";

/**
 * Wo ein Vorgang steht.
 *
 * `expired` heisst: die Frist ist verstrichen, ohne dass etwas kam. Es heisst
 * NICHT „abgelehnt" — wer eine Aufgabe nicht will, tut nichts, und das ist von
 * „hat es nicht geschafft" ununterscheidbar. Die Oberfläche darf daraus keine
 * Auskunft basteln.
 */
export type Vorgangsstand = "set" | "submitted" | "evaluated" | "expired";

/** Wie das Unternehmen die Arbeit beantwortet. Zwei Werte, keine Skala. */
export type Ausgang = "accepted" | "rejected";

/** Die abgegebene Lösung. */
export interface Einreichung {
  text: string;
  /**
   * Wohin sie zeigt.
   *
   * <strong>Der Server ruft diese Adresse nie ab</strong>, und diese Seite
   * bettet sie nicht ein: sie steht als Verweis da, und ein Mensch klickt sie.
   */
  url: string | null;
  submitted_at: string;
}

/**
 * Die Rückmeldung — Text und ein Ausgang.
 *
 * <strong>Die Person liest genau dieses Feld</strong>, und es gibt kein zweites
 * daneben. Wo es zwei gäbe, stünde im zweiten die Wahrheit.
 */
export interface Bewertung {
  outcome: Ausgang;
  text: string;
  evaluated_at: string;
}

/**
 * Ein Vorgang, wie ihn BEIDE Seiten sehen.
 *
 * `submission` und `evaluation` fehlen, solange es sie nicht gibt — hier
 * ausdrücklich nicht, um etwas zu verbergen, sondern weil ein Feld ohne Wert
 * kein Feld ist.
 */
export interface Vorgang {
  id: string;
  subject_id: string;
  tenant_id: string;
  title: string;
  task: string;
  /** Der genannte Umfang. Er steht in der Ausschreibung, und das ist die Zusage. */
  hours: number;
  due_at: string;
  state: Vorgangsstand;
  created_at: string;
  updated_at: string;
  submission?: Einreichung;
  evaluation?: Bewertung;
}

/** Was ein Unternehmen schickt, um eine Aufgabe zu stellen. */
export interface Aufgabeneingabe {
  subject_id: string;
  title: string;
  task: string;
  hours: number;
  due_at: string;
}

/** Die Grenzen, die der Server durchsetzt — hier nur, damit das Formular sie nennt. */
export const KLEINSTER_UMFANG = 1;

/**
 * Acht Stunden, und das ist eine Entscheidung.
 *
 * Mehr als ein Arbeitstag ist keine Probe mehr, sondern Arbeit — und für Arbeit
 * gibt es einen Vertrag und kein Formular (ADR-0042 §3). Der Server weist
 * darüber ab; diese Zahl steht hier nur, damit das Formular es sagt, statt es
 * jemanden ausprobieren zu lassen.
 */
export const HOECHSTER_UMFANG = 8;

/** Und die Frist liegt mindestens zwei Tage voraus. */
export const MINDESTFRIST_TAGE = 2;

export type Liste = { ok: true; items: Vorgang[] } | Fehlschlag<"no-company" | "unavailable">;
export type Einzeln = { ok: true; vorgang: Vorgang } | Fehlschlag<"gone" | "unavailable">;
export type Zugergebnis =
  | { ok: true; vorgang: Vorgang }
  | Fehlschlag<"gone" | "conflict" | "invalid" | "unavailable" | "offline">;

const SCHWEIGT: Deutung<"unavailable"> = {
  reason: "unavailable",
  titel: "fehler.ledgerSchweigt",
  text: "fehler.spaeterErneut",
};

/**
 * Die eigenen Vorgänge.
 *
 * <strong>Diese Liste fragt den Ledger nicht</strong>, und deshalb gibt es hier
 * keinen 503-Fall aus dem Ledger: wer seine Sichtbarkeit widerrufen hat, liest
 * seine Bewertungen weiter. Das ist die Zusage aus ADR-0042 §2, und sie hängt
 * daran, dass diese Seite sie nicht nachträglich einschränkt.
 */
export async function ladeMeineVorgaenge(signal?: AbortSignal): Promise<Liste> {
  const answer = await request<{ items: Vorgang[] }>(
    ASSESSMENT_BASE_URL,
    "/assessments/me",
    { signal },
    "fehler.listeNichtGeladen"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  return deuten<"no-company" | "unavailable">(answer.error, { 503: SCHWEIGT }, "unavailable");
}

/** Die Vorgänge des Unternehmens. */
export async function ladeFirmenvorgaenge(signal?: AbortSignal): Promise<Liste> {
  const answer = await request<{ items: Vorgang[] }>(
    ASSESSMENT_BASE_URL,
    "/assessments",
    { signal },
    "fehler.listeNichtGeladen"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  // `503` NICHT als leere Liste zeigen: das waere die Behauptung, niemand habe
  // eine Aufgabe bekommen — und das weiss in diesem Moment niemand.
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
 * Eine 2xx-Antwort ohne Rumpf.
 *
 * Sie darf nie als „gibt es nicht" gelesen werden — ein Dienst, der antwortet
 * und dabei nichts sagt, SCHWEIGT, und Schweigen ist 503 (ADR-0020 §3).
 */
const LEERE_ANTWORT = { status: 503, title: "", detail: "" };

/** Ein einzelner Vorgang — dieselbe Adresse für beide Seiten. */
export async function ladeVorgang(id: string, signal?: AbortSignal): Promise<Einzeln> {
  const answer = await request<Vorgang>(
    ASSESSMENT_BASE_URL,
    `/assessments/${id}`,
    { signal },
    "fehler.vorgangNichtGeladen"
  );

  if (answer.ok && answer.value != null) return { ok: true, vorgang: answer.value };

  return deuten<"gone" | "unavailable">(
    answer.ok ? LEERE_ANTWORT : answer.error,
    // `404` sagt bewusst nicht, WARUM. Vier Lagen, eine Antwort.
    { 404: { reason: "gone", titel: "fehler.vorgangFort" }, 503: SCHWEIGT },
    "unavailable"
  );
}

async function schicke(
  pfad: string,
  body: unknown,
  schluessel: Fehlerschluessel
): Promise<Zugergebnis> {
  const answer = await request<Vorgang>(
    ASSESSMENT_BASE_URL,
    pfad,
    { method: "POST", body },
    schluessel
  );

  if (answer.ok && answer.value != null) return { ok: true, vorgang: answer.value };

  return deuten<"gone" | "conflict" | "invalid" | "unavailable" | "offline">(
    answer.ok ? LEERE_ANTWORT : answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      // `404` heisst: diese Person hat diesem Unternehmen nichts freigegeben —
      // oder es gibt den Vorgang nicht, oder er gehoert jemand anderem. Die
      // Oberflaeche bastelt daraus keine Auskunft.
      404: { reason: "gone", titel: "fehler.vorgangFort" },
      409: { reason: "conflict", titel: "fehler.schrittNichtMoeglich" },
      503: SCHWEIGT,
    },
    // `422` steht ABSICHTLICH nicht in der Tabelle, und das ist hier mehr als
    // eine Konvention: das Problemdokument des Servers nennt die Regel, die
    // verletzt wurde — „the stated effort is between 1 and 8 hours", „an
    // evaluation carries a text, a rejection too". Genau das braucht jemand,
    // der gerade ein Formular ausfuellt. Ein eigener Satz („Bitte die Angaben
    // pruefen") waere kuerzer und saegte die Auskunft ab.
    //
    // Der Grund steht als Rueckfall auf `invalid`: er dient nur der
    // Verzweigung, der SATZ kommt vom Server.
    "invalid"
  );
}

/** Eine Aufgabe stellen. Umfang und Frist sind Pflicht — der Server denkt sich keine aus. */
export const stelleAufgabe = (eingabe: Aufgabeneingabe) =>
  schicke("/assessments", eingabe, "fehler.aufgabeNichtGestellt");

/** Einreichen. Text, Adresse, oder beides — aber nicht nichts. */
export const reicheEin = (id: string, text: string, url: string | null) =>
  schicke(`/assessments/${id}/submission`, { text, url }, "fehler.nichtEingereicht");

/**
 * Bewerten.
 *
 * <strong>Beide Felder sind Pflicht, auch bei <c>rejected</c>.</strong> Der
 * Server weist eine Absage ohne Text mit 422 ab, und dieses Formular macht den
 * Knopf deshalb gar nicht erst scharf: Ablehnen und Begründen sind ein Schritt.
 */
export const bewerte = (id: string, outcome: Ausgang, text: string) =>
  schicke(`/assessments/${id}/evaluation`, { outcome, text }, "fehler.nichtBewertet");
