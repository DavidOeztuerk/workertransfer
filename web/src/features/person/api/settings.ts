import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { API_BASE_URL } from "../../../env";

/**
 * Die Benachrichtigungs-Einstellungen bei identity-service.
 *
 * Vier Schalter, alle voreingestellt AN: wer nicht erfährt, dass gefragt wurde,
 * hat keine Wahl, sondern nur den Anschein einer.
 *
 * Die Feldnamen sind der Draht und deshalb snake_case — nicht der Geschmack
 * dieser Datei.
 */
export interface Benachrichtigungswahl {
  resume_request: boolean;
  market_request: boolean;
  application_update: boolean;
  transfer_update: boolean;
}

/** Was gilt, solange niemand etwas eingestellt hat. */
export const ALLES_AN: Benachrichtigungswahl = {
  resume_request: true,
  market_request: true,
  application_update: true,
  transfer_update: true,
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
  const antwort = await request<Benachrichtigungswahl>(
    API_BASE_URL,
    PFAD,
    { signal },
    "fehler.einstellungenNichtAbrufbar"
  );

  return antwort.ok ? (antwort.value ?? null) : null;
}

export type Speicherergebnis =
  | { ok: true; wahl: Benachrichtigungswahl }
  | { ok: false; error: ApiError };

/** Speichert alle vier auf einmal — der Endpunkt kennt keine Teiländerung. */
export async function speichereWahl(
  wahl: Benachrichtigungswahl,
  signal?: AbortSignal
): Promise<Speicherergebnis> {
  const antwort = await request<Benachrichtigungswahl>(
    API_BASE_URL,
    PFAD,
    { method: "PUT", body: wahl, signal },
    "fehler.einstellungenNichtGespeichert"
  );

  return antwort.ok ? { ok: true, wahl: antwort.value ?? wahl } : { ok: false, error: antwort.error };
}
