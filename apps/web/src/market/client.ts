// Der Marktstatus und seine Freigabe, wie die Oberfläche sie benutzt.
//
// Beides beim transfer-service, nicht beim Consent-Ledger direkt: der
// Capability-String `market.visibility:tenant:<id>` entsteht an genau einer
// Stelle, und die liegt nicht im Browser.

import { TRANSFER_BASE_URL } from "../env";

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
  /** Was gerade gilt — aus dem Ledger, nicht aus dem Vorgang. Für das
   *  anfragende Unternehmen `null`. */
  active: boolean | null;
}

export type SaveStatusResult =
  | { ok: true; status: MarketStatus }
  | { ok: false; message: string };

export type MarketRequestResult =
  | { ok: true; request: MarketRequest }
  | {
      ok: false;
      reason: "already-asked" | "no-company" | "not-available" | "unavailable" | "offline";
      message: string;
    };

export type MarketRequestListResult =
  | { ok: true; requests: MarketRequest[] }
  | { ok: false; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      const title = (body as { title?: unknown }).title;
      if (typeof title === "string") return title;
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${TRANSFER_BASE_URL}${path}`, { credentials: "include", ...init });
}

/**
 * Der Ersatz ist `unavailable`, nie `null` und nie „offen".
 *
 * Der Server antwortet auf `GET /market/me` schon nie mit `null`, damit sich
 * die Oberfläche keine Voreinstellung ausdenken muss. Für den Fall, dass er gar
 * nicht antwortet, gilt hier dieselbe Regel: die Voreinstellung darf nie
 * zugunsten des Marktes ausfallen. Ein Netzfehler ist keine Zustimmung.
 */
export async function getMyMarketStatus(): Promise<MarketStatus> {
  const fallbackStatus: MarketStatus = {
    subject_id: "",
    availability: "unavailable",
    employed: false,
    note: "",
    is_approachable: false,
    updated_at: "",
  };
  try {
    const res = await send("/market/me");
    if (!res.ok) return fallbackStatus;
    return (await res.json()) as MarketStatus;
  } catch {
    return fallbackStatus;
  }
}

export async function saveMyMarketStatus(input: MarketStatusInput): Promise<SaveStatusResult> {
  let res: Response;
  try {
    res = await send("/market/me", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      // Genau die drei Felder des Vertrags.
      body: JSON.stringify({
        availability: input.availability,
        employed: input.employed,
        note: input.note,
      }),
    });
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return { ok: false, message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an." };
  }
  if (!res.ok) {
    return {
      ok: false,
      message: await problemMessage(res, "Der Marktstatus konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, status: (await res.json()) as MarketStatus };
}

/**
 * Ein Unternehmen fragt: „darf ich sehen, ob du gerade zuhörst?"
 *
 * `404` sagt bewusst nicht, ob es die Person gibt — die Oberfläche darf daraus
 * keine Auskunft basteln, die der Server gerade verweigert hat.
 */
export async function requestMarketStatus(subjectId: string): Promise<MarketRequestResult> {
  let res: Response;
  try {
    res = await send(`/market/${subjectId}/requests`, { method: "POST" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 409) {
    return { ok: false, reason: "already-asked", message: "Ihr habt diese Person bereits gefragt." };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Das fragen nur Unternehmen an. Wechsle oben auf ein Unternehmen.",
    };
  }
  if (res.status === 404) {
    return {
      ok: false,
      reason: "not-available",
      message: "Diese Person ist gerade nicht anfragbar.",
    };
  }
  if (res.status === 503) {
    return {
      ok: false,
      reason: "unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht. Bitte später erneut versuchen.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "offline",
      message: await problemMessage(res, "Die Anfrage konnte nicht gestellt werden."),
    };
  }
  return { ok: true, request: (await res.json()) as MarketRequest };
}

async function post(path: string): Promise<MarketRequestResult> {
  let res: Response;
  try {
    res = await send(path, { method: "POST" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 503) {
    return {
      ok: false,
      reason: "unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht — es wurde nichts geändert.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "offline",
      message: await problemMessage(res, "Die Anfrage konnte nicht beantwortet werden."),
    };
  }
  return { ok: true, request: (await res.json()) as MarketRequest };
}

export function answerMarketRequest(
  requestId: string,
  grant: boolean
): Promise<MarketRequestResult> {
  // Zwei Pfade statt eines Endpunkts mit Flag: erteilen und ablehnen sind
  // verschiedene Handlungen, und im Protokoll des Ledgers bleiben sie das.
  return post(`/market/requests/${requestId}/${grant ? "grant" : "decline"}`);
}

export function revokeMarketAccess(requestId: string): Promise<MarketRequestResult> {
  return post(`/market/requests/${requestId}/revoke`);
}

export function listMyMarketRequests(): Promise<MarketRequestListResult> {
  return listRequests("/market/me/requests");
}

export function listCompanyMarketRequests(): Promise<MarketRequestListResult> {
  return listRequests("/market/requests");
}

async function listRequests(path: string): Promise<MarketRequestListResult> {
  try {
    const res = await send(path);
    if (res.status === 503) {
      // Nicht als leere Liste zeigen: das wäre die Behauptung, niemand habe
      // gefragt oder freigegeben — und das weiß gerade niemand.
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Liste ließ sich nicht laden.") };
    }
    return { ok: true, requests: (await res.json()) as MarketRequest[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

/**
 * Der Marktstatus einer anderen Person.
 *
 * `404` wird zu `status: null` — „gibt es nicht", „nicht freigegeben" und
 * „gerade nicht" sind für die Oberfläche derselbe Fall, genau wie der Server
 * sie ununterscheidbar hält.
 */
export async function getMarketStatus(
  subjectId: string
): Promise<{ ok: true; status: MarketStatus | null } | { ok: false; message: string }> {
  try {
    const res = await send(`/market/${subjectId}`);
    if (res.status === 404) return { ok: true, status: null };
    if (res.status === 503) {
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) {
      return {
        ok: false,
        message: await problemMessage(res, "Der Marktstatus ließ sich nicht laden."),
      };
    }
    return { ok: true, status: (await res.json()) as MarketStatus };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
