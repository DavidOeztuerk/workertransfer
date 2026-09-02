// Der Marktstatus und seine Freigabe, wie die Oberfläche sie benutzt.
//
// Beides beim transfer-service, nicht beim Consent-Ledger direkt: der
// Capability-String `market.visibility:tenant:<id>` entsteht an genau einer
// Stelle, und die liegt nicht im Browser.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "./fehler";

export type Availability = "open" | "listening" | "unavailable";

export interface MarketStatus {
  subject_id: string;
  availability: Availability;
  employed: boolean;
  note: string;
  is_approachable: boolean;
  updated_at: string;
}

export interface MarketStatusInput {
  availability: Availability;
  employed: boolean;
  note: string;
}

export type MarketRequestStatus = "PENDING" | "GRANTED" | "DECLINED";

export interface MarketRequest {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: MarketRequestStatus;
  created_at: string;
  answered_at: string | null;
  /**
   * Was gerade GILT — aus dem Ledger, nicht aus dem Vorgang. Für das anfragende
   * Unternehmen `null`. `status` sagt, was geschehen ist; nur `active` sagt, was
   * jetzt gilt, und nur `active` entscheidet über ein Zurückziehen.
   */
  active: boolean | null;
}

export type StatusErgebnis = { ok: true; status: MarketStatus } | Fehlschlag<"unavailable">;
export type SpeicherErgebnis =
  | { ok: true; status: MarketStatus }
  | Fehlschlag<"unauthenticated" | "invalid" | "offline">;

export type AnfrageFehler =
  | "already-asked"
  | "no-company"
  | "not-available"
  | "unavailable"
  | "offline";
export type AnfrageErgebnis = { ok: true; request: MarketRequest } | Fehlschlag<AnfrageFehler>;
export type AnfragenListe = { ok: true; requests: MarketRequest[] } | Fehlschlag<"unavailable">;

/**
 * Der eigene Marktstatus — und kein Ersatzwert, wenn er nicht abrufbar ist.
 *
 * Hier stand einmal `unavailable` mit leerer Notiz. Die Begründung war richtig
 * und gilt weiter — *die Voreinstellung darf nie zugunsten des Marktes
 * ausfallen*. Sie übersah nur, dass dieser Ersatz nicht bloß angezeigt, sondern
 * in ein FORMULAR geschrieben wurde: nach einem Ausfall stand dort „gerade
 * nicht ansprechbar" mit leerer Notiz, der Ladezustand war vorbei, und wer dann
 * etwas anfasste und speicherte, schickte `availability: "unavailable"` und
 * `note: ""` — und hatte seine Ansprechbarkeit zurückgezogen und seine Notiz
 * gelöscht, ohne es zu wollen. Auf einem Transfermarkt ist das der teuerste
 * stille Schreibvorgang: die Person verschwindet.
 *
 * `ok: false` heißt „wir wissen es nicht". Die Seite zeigt dann kein Formular,
 * und damit wird in KEINE Richtung mehr etwas erfunden.
 *
 * Der Server antwortet auf `GET /market/me` übrigens nie leer; ein fehlender
 * Datensatz kommt als `unavailable` zurück. Diese Voreinstellung ist also eine
 * Aussage des Servers und keine der Oberfläche.
 */
export async function getMyMarketStatus(signal?: AbortSignal): Promise<StatusErgebnis> {
  const antwort = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    "/market/me",
    { signal },
    "Dein Marktstatus ist gerade nicht abrufbar."
  );
  if (antwort.ok) return { ok: true, status: antwort.value };
  return {
    ok: false,
    reason: "unavailable",
    error: {
      ...antwort.error,
      title: "Dein Marktstatus ist gerade nicht abrufbar.",
      detail:
        "Ändern lässt er sich erst wieder, wenn er lesbar ist — sonst würdest du womöglich zurücknehmen, was du nie zurückgenommen hast.",
    },
  };
}

export async function saveMyMarketStatus(input: MarketStatusInput): Promise<SpeicherErgebnis> {
  const antwort = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    "/market/me",
    {
      method: "PUT",
      // Genau die drei Felder des Vertrags.
      body: { availability: input.availability, employed: input.employed, note: input.note },
    },
    "Der Marktstatus konnte nicht gespeichert werden."
  );
  if (antwort.ok) return { ok: true, status: antwort.value };
  return deuten<"unauthenticated" | "invalid" | "offline">(
    antwort.error,
    {
      0: { reason: "offline", title: "Keine Verbindung zum Server." },
      401: {
        reason: "unauthenticated",
        title: "Deine Sitzung ist abgelaufen.",
        detail: "Bitte melde dich erneut an.",
      },
    },
    "invalid"
  );
}

const ANFRAGEFEHLER: Partial<Record<number, { reason: AnfrageFehler; title: string; detail?: string }>> =
  {
    0: { reason: "offline", title: "Keine Verbindung zum Server." },
    409: { reason: "already-asked", title: "Ihr habt diese Person bereits gefragt." },
    403: {
      reason: "no-company",
      title: "Das fragen nur Unternehmen an.",
      detail: "Wechsle oben auf ein Unternehmen.",
    },
    // `404` sagt bewusst nicht, ob es die Person gibt — die Oberfläche darf
    // daraus keine Auskunft basteln, die der Server gerade verweigert hat.
    404: { reason: "not-available", title: "Diese Person ist gerade nicht anfragbar." },
    503: {
      reason: "unavailable",
      title: "Der Consent-Ledger antwortet gerade nicht.",
      detail: "Bitte später erneut versuchen.",
    },
  };

export async function requestMarketStatus(subjectId: string): Promise<AnfrageErgebnis> {
  const antwort = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    `/market/${subjectId}/requests`,
    { method: "POST" },
    "Die Anfrage konnte nicht gestellt werden."
  );
  if (antwort.ok) return { ok: true, request: antwort.value };
  return deuten<AnfrageFehler>(antwort.error, ANFRAGEFEHLER, "offline");
}

async function post(path: string): Promise<AnfrageErgebnis> {
  const antwort = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    path,
    { method: "POST" },
    "Die Anfrage konnte nicht beantwortet werden."
  );
  if (antwort.ok) return { ok: true, request: antwort.value };
  return deuten<AnfrageFehler>(
    antwort.error,
    {
      0: { reason: "offline", title: "Keine Verbindung zum Server." },
      503: {
        reason: "unavailable",
        title: "Der Consent-Ledger antwortet gerade nicht.",
        detail: "Es wurde nichts geändert.",
      },
    },
    "offline"
  );
}

export function answerMarketRequest(requestId: string, grant: boolean): Promise<AnfrageErgebnis> {
  // Zwei Pfade statt eines Endpunkts mit Flag: erteilen und ablehnen sind
  // verschiedene Handlungen, und im Protokoll des Ledgers bleiben sie das.
  return post(`/market/requests/${requestId}/${grant ? "grant" : "decline"}`);
}

export function revokeMarketAccess(requestId: string): Promise<AnfrageErgebnis> {
  return post(`/market/requests/${requestId}/revoke`);
}

async function listRequests(path: string, signal?: AbortSignal): Promise<AnfragenListe> {
  const antwort = await request<MarketRequest[]>(
    TRANSFER_BASE_URL,
    path,
    { signal },
    "Die Liste ließ sich nicht laden."
  );
  if (antwort.ok) return { ok: true, requests: antwort.value ?? [] };
  // `503` NICHT als leere Liste zeigen: das wäre die Behauptung, niemand habe
  // gefragt oder freigegeben — und das weiß in diesem Moment niemand.
  return deuten<"unavailable">(
    antwort.error,
    {
      0: { reason: "unavailable", title: "Keine Verbindung zum Server." },
      503: { reason: "unavailable", title: "Der Consent-Ledger antwortet gerade nicht." },
    },
    "unavailable"
  );
}

export function listMyMarketRequests(signal?: AbortSignal): Promise<AnfragenListe> {
  return listRequests("/market/me/requests", signal);
}

export function listCompanyMarketRequests(signal?: AbortSignal): Promise<AnfragenListe> {
  return listRequests("/market/requests", signal);
}

/**
 * Der Marktstatus einer anderen Person.
 *
 * `404` wird zu `status: null` — „gibt es nicht", „nicht freigegeben" und
 * „gerade nicht" sind für die Oberfläche derselbe Fall, genau wie der Server
 * sie ununterscheidbar hält.
 */
export async function getMarketStatus(
  subjectId: string,
  signal?: AbortSignal
): Promise<{ ok: true; status: MarketStatus | null } | Fehlschlag<"unavailable">> {
  const antwort = await request<MarketStatus>(
    TRANSFER_BASE_URL,
    `/market/${subjectId}`,
    { signal },
    "Der Marktstatus ließ sich nicht laden."
  );
  if (antwort.ok) return { ok: true, status: antwort.value ?? null };
  if (antwort.error.status === 404) return { ok: true, status: null };
  return deuten<"unavailable">(
    antwort.error,
    { 503: { reason: "unavailable", title: "Der Consent-Ledger antwortet gerade nicht." } },
    "unavailable"
  );
}
