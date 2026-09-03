import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { RESUME_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";

/**
 * Der Lebenslauf — strenger als das Profil, und das ist der ganze Entwurf.
 *
 * Ein Profil ist ein Aushang; ein Lebenslauf nennt echte Arbeitgeber mit Daten
 * — genau das, was ein <em>jetziger</em> Arbeitgeber nicht sehen darf. Deshalb
 * gibt es hier <strong>keinen öffentlichen Schalter</strong>: eine Firma FRAGT,
 * die Person antwortet, und die Freigabe gilt für diese eine Firma.
 */
export interface Station {
  employer: string;
  title: string;
  started_on: string;
  /** `null` heisst „läuft noch" — nicht „unbekannt". */
  ended_on: string | null;
  description: string;
}

export interface Ausbildung {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
}

export interface Lebenslauf {
  subject_id: string;
  positions: Station[];
  education: Ausbildung[];
  updated_at: string;
}

export interface Lebenslaufeingabe {
  positions: Station[];
  education: Ausbildung[];
}

export type Anfragestand = "PENDING" | "GRANTED" | "DECLINED";

/**
 * Eine Anfrage einer Firma.
 *
 * <strong>Die Anfrage ist nicht die Erlaubnis.</strong> `GRANTED` heisst „wurde
 * einmal erteilt", nicht „gilt jetzt" — deshalb hat eine Anfrage weder ein
 * Aktiv-Kennzeichen noch einen Widerrufszeitpunkt. Nach einer Rücknahme bleibt
 * sie `GRANTED` und das Lesen kommt trotzdem leer zurück. Das ist der Entwurf,
 * kein Fehler.
 */
export interface Lebenslaufanfrage {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: Anfragestand;
  created_at: string;
  answered_at?: string | null;
  active?: boolean | null;
}

export type Antwort<T> = { ok: true; wert: T } | { ok: false; error: ApiError };

/**
 * Der eigene Lebenslauf.
 *
 * <strong>Drei Ausgänge, nicht zwei.</strong> `wert: null` heisst „noch keiner
 * geschrieben" — das ist eine Antwort und kein Fehler, denn `404` ist hier der
 * Normalfall für jeden, der gerade erst angefangen hat. Ein Transportfehler ist
 * etwas anderes und kommt als `error` zurück. Die beiden zusammenzulegen hiesse,
 * jemandem ein leeres Formular zu zeigen, obwohl sein Lebenslauf nur gerade
 * nicht abrufbar ist — und der nächste Speichern-Klick überschriebe ihn.
 */
export async function ladeMeinen(signal?: AbortSignal): Promise<Antwort<Lebenslauf | null>> {
  const antwort = await request<Lebenslauf>(
    RESUME_BASE_URL,
    "/resumes/me",
    { signal },
    "fehler.lebenslaufNichtAbrufbar"
  );

  if (antwort.ok) return { ok: true, wert: antwort.value ?? null };
  if (antwort.error.status === 404) return { ok: true, wert: null };
  return { ok: false, error: antwort.error };
}

export async function speichereMeinen(
  eingabe: Lebenslaufeingabe,
  signal?: AbortSignal
): Promise<Antwort<Lebenslauf>> {
  const antwort = await request<Lebenslauf>(
    RESUME_BASE_URL,
    "/resumes/me",
    { method: "PUT", body: eingabe, signal },
    "fehler.lebenslaufNichtGespeichert"
  );

  return antwort.ok
    ? { ok: true, wert: antwort.value as Lebenslauf }
    : { ok: false, error: antwort.error };
}

/** Die Anfragen, die an mich gestellt wurden. */
export async function ladeMeineAnfragen(
  signal?: AbortSignal
): Promise<Antwort<Lebenslaufanfrage[]>> {
  const antwort = await request<Lebenslaufanfrage[]>(
    RESUME_BASE_URL,
    "/resumes/me/requests",
    { signal },
    "fehler.anfragenNichtAbrufbar"
  );

  return antwort.ok
    ? { ok: true, wert: antwort.value ?? [] }
    : { ok: false, error: antwort.error };
}

/**
 * Eine Anfrage beantworten oder eine Freigabe zurücknehmen.
 *
 * <strong>503 ist hier eine eigene Aussage</strong> und darf nicht als
 * gewöhnlicher Fehler durchgereicht werden: der Ledger hat nicht geantwortet,
 * es wurde also NICHTS geändert. Wer daraus „fehlgeschlagen" macht, lässt offen,
 * ob die Rücknahme vielleicht doch griff.
 */
async function handeln(pfad: string, signal?: AbortSignal): Promise<Antwort<Lebenslaufanfrage>> {
  const antwort = await request<Lebenslaufanfrage>(
    RESUME_BASE_URL,
    pfad,
    { method: "POST", signal },
    "fehler.anfrageNichtBeantwortet"
  );

  if (!antwort.ok && antwort.error.status === 503) {
    return {
      ok: false,
      error: {
        ...antwort.error,
        detail: i18n.t("fehler.ledgerSchweigtOhneAenderung"),
      },
    };
  }

  return antwort.ok
    ? { ok: true, wert: antwort.value as Lebenslaufanfrage }
    : { ok: false, error: antwort.error };
}

export const beantworten = (id: string, erteilen: boolean, signal?: AbortSignal) =>
  handeln(`/resumes/requests/${id}/${erteilen ? "grant" : "decline"}`, signal);

export const zuruecknehmen = (id: string, signal?: AbortSignal) =>
  handeln(`/resumes/requests/${id}/revoke`, signal);
