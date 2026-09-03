// Der Marktstatus und seine Freigabe, wie die Oberfläche sie benutzt.
//
// Beides beim transfer-service, nicht beim Consent-Ledger direkt: der
// Capability-String `market.visibility:tenant:<id>` entsteht an genau einer
// Stelle, und die liegt nicht im Browser.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import {
  type Deutung,
  type Fehlschlag,
  deuten,
} from "../../../shared/api/fehler";

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
    "fehler.marktstatusNichtAbrufbar"
  );
  if (antwort.ok) return { ok: true, status: antwort.value };
  return {
    ok: false,
    reason: "unavailable",
    error: {
      ...antwort.error,
      title: i18n.t("fehler.marktstatusNichtAbrufbar"),
      detail: i18n.t("fehler.marktstatusUnveraenderbar"),
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
    "fehler.marktstatusNichtGespeichert"
  );
  if (antwort.ok) return { ok: true, status: antwort.value };
  return deuten<"unauthenticated" | "invalid" | "offline">(
    antwort.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      401: {
        reason: "unauthenticated",
        titel: "fehler.sitzungAbgelaufen",
        text: "fehler.erneutAnmelden",
      },
    },
    "invalid"
  );
}

const ANFRAGEFEHLER: Partial<Record<number, Deutung<AnfrageFehler>>> =
  {
    0: { reason: "offline", titel: "fehler.keineVerbindung" },
    409: { reason: "already-asked", titel: "fehler.bereitsGefragt" },
    403: {
      reason: "no-company",
      titel: "fehler.nurFirmenFragen",
      text: "fehler.firmaWaehlen",
    },
    // `404` sagt bewusst nicht, ob es die Person gibt — die Oberfläche darf
    // daraus keine Auskunft basteln, die der Server gerade verweigert hat.
    404: { reason: "not-available", titel: "fehler.personNichtAnfragbar" },
    503: {
      reason: "unavailable",
      titel: "fehler.ledgerSchweigt",
      text: "fehler.spaeterErneut",
    },
  };

export async function requestMarketStatus(subjectId: string): Promise<AnfrageErgebnis> {
  const antwort = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    `/market/${subjectId}/requests`,
    { method: "POST" },
    "fehler.anfrageNichtGestellt"
  );
  if (antwort.ok) return { ok: true, request: antwort.value };
  return deuten<AnfrageFehler>(antwort.error, ANFRAGEFEHLER, "offline");
}

async function post(path: string): Promise<AnfrageErgebnis> {
  const antwort = await request<MarketRequest>(
    TRANSFER_BASE_URL,
    path,
    { method: "POST" },
    "fehler.anfrageNichtBeantwortet"
  );
  if (antwort.ok) return { ok: true, request: antwort.value };
  return deuten<AnfrageFehler>(
    antwort.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      503: {
        reason: "unavailable",
        titel: "fehler.ledgerSchweigt",
        text: "fehler.nichtsGeaendert",
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
    "fehler.listeNichtGeladen"
  );
  if (antwort.ok) return { ok: true, requests: antwort.value ?? [] };
  // `503` NICHT als leere Liste zeigen: das wäre die Behauptung, niemand habe
  // gefragt oder freigegeben — und das weiß in diesem Moment niemand.
  return deuten<"unavailable">(
    antwort.error,
    {
      0: { reason: "unavailable", titel: "fehler.keineVerbindung" },
      503: { reason: "unavailable", titel: "fehler.ledgerSchweigt" },
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
    "fehler.marktstatusNichtGeladen"
  );
  if (antwort.ok) return { ok: true, status: antwort.value ?? null };
  if (antwort.error.status === 404) return { ok: true, status: null };
  return deuten<"unavailable">(
    antwort.error,
    { 503: { reason: "unavailable", titel: "fehler.ledgerSchweigt" } },
    "unavailable"
  );
}
