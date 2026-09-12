import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { API_BASE_URL } from "../../../env";

/**
 * Die Benachrichtigungs-Einstellungen bei notification-service.
 *
 * Acht Schalter, alle voreingestellt AN: wer nicht erfährt, dass gefragt wurde,
 * hat keine Wahl, sondern nur den Anschein einer.
 *
 * `profile_discovered` ist der jüngste (ADR-0033) und der einzige, der von
 * einer Suche handelt statt von einem Vorgang: „dein Profil wurde von einem
 * Unternehmen entdeckt". Er ist EINZELN abbestellbar, und das ist der Grund für
 * die eigene Art — es ist die einzige Auskunft, die jemand über seine eigene
 * Sichtbarkeit bekommt, und wer sie nicht will, soll nicht alles andere mit
 * abstellen müssen.
 *
 * Die Feldnamen sind der Draht und deshalb snake_case — nicht der Geschmack
 * dieser Datei.
 */
export interface Benachrichtigungswahl {
  resume_request: boolean;
  market_request: boolean;
  application_update: boolean;
  transfer_update: boolean;
  application_received: boolean;
  profile_discovered: boolean;
  /**
   * „Ein Unternehmen hat ein Gespräch eröffnet" (ADR-0037).
   *
   * <strong>Die beiden letzten standen hier nicht</strong> — der Server kennt
   * sie seit advisor-service, diese Schnittstelle nicht. Das war folgenlos,
   * solange niemand sie abstellen konnte; sobald aber ein achter Schalter
   * dazukommt, schriebe ein `PUT` ohne diese Felder sie stillschweigend
   * wieder auf `true` zurück, weil der Vertrag sie so vorbelegt. Zwei Zeilen,
   * und der Schalter tut wieder, was er sagt.
   */
  advisor_conversation: boolean;
  /** „An deiner Arbeitsprobe hat sich etwas bewegt" (ADR-0042). */
  assessment_update: boolean;
}

/** Was gilt, solange niemand etwas eingestellt hat. */
export const ALLES_AN: Benachrichtigungswahl = {
  resume_request: true,
  market_request: true,
  application_update: true,
  transfer_update: true,
  application_received: true,
  profile_discovered: true,
  advisor_conversation: true,
  assessment_update: true,
};

const PFAD = "/me/notification-preferences";

/**
 * Was eingestellt ist. `null` heisst: nicht abrufbar.
 *
 * Der Endpunkt antwortet immer `200` mit den Voreinstellungen, auch wenn nie
 * etwas gespeichert wurde — diese Zusage kommt vom Server. `null` bedeutet
 * deshalb wirklich „wir wissen es nicht", und die Seite darf dann keine
 * Schalterstellung behaupten.
 */
export async function ladeWahl(signal?: AbortSignal): Promise<Benachrichtigungswahl | null> {
  const answer = await request<Benachrichtigungswahl>(
    API_BASE_URL,
    PFAD,
    { signal },
    "fehler.einstellungenNichtAbrufbar"
  );

  return answer.ok ? (answer.value ?? null) : null;
}

export type Speicherergebnis =
  | { ok: true; choice: Benachrichtigungswahl }
  | { ok: false; error: ApiError };

/** Speichert alle sechs auf einmal — der Endpunkt kennt keine Teiländerung. */
export async function speichereWahl(
  choice: Benachrichtigungswahl,
  signal?: AbortSignal
): Promise<Speicherergebnis> {
  const answer = await request<Benachrichtigungswahl>(
    API_BASE_URL,
    PFAD,
    { method: "PUT", body: choice, signal },
    "fehler.einstellungenNichtGespeichert"
  );

  return answer.ok ? { ok: true, choice: answer.value ?? choice } : { ok: false, error: answer.error };
}
