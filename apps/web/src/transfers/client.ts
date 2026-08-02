// Der Transfer-Vorgang, wie die Oberfläche ihn benutzt.
//
// Ein Pfad je Übergang, kein `PATCH status`: jeder Übergang gehört einer Seite,
// und ein gemeinsamer müsste bei jedem Aufruf herausfinden, wer gerade was darf.

import { TRANSFER_BASE_URL } from "../env";

export type TransferStatus =
  | "interested"
  | "talking"
  | "offered"
  | "accepted"
  | "completed"
  | "declined"
  | "withdrawn";

export interface Transfer {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: TransferStatus;
  /** Beim Anlegen aus dem Marktstatus kopiert. Die Plattform kontaktiert den
   *  aktuellen Arbeitgeber NICHT — sie weiß nicht, wer er ist. */
  requires_release: boolean;
  release_confirmed: boolean;
  message: string;
  offer_note: string;
  offer_start_on: string | null;
  offer_fee_cents: number | null;
  created_at: string;
  updated_at: string;
}

export interface OfferInput {
  note: string;
  start_on: string | null;
  fee_cents: number | null;
}

export type PersonAction = "accept-talk" | "accept-offer" | "confirm-release" | "decline";
export type CompanyAction = "complete" | "withdraw";

export type TransferResult =
  | { ok: true; transfer: Transfer }
  | {
      ok: false;
      reason: "not-available" | "no-company" | "conflict" | "unavailable" | "offline";
      message: string;
    };

export type TransferListResult =
  | { ok: true; transfers: Transfer[] }
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
    // Kein verwertbarer Körper.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${TRANSFER_BASE_URL}${path}`, { credentials: "include", ...init });
}

async function toResult(res: Response, fallback: string): Promise<TransferResult> {
  if (res.status === 404) {
    // Kein Status, keine Freigabe, „gerade nicht", fremder Vorgang — vom Server
    // bewusst nicht unterschieden. Die Oberfläche unterscheidet sie auch nicht.
    return {
      ok: false,
      reason: "not-available",
      message: "Das ist gerade nicht möglich.",
    };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Dafür braucht es ein aktives Unternehmen. Wechsle oben auf eines.",
    };
  }
  if (res.status === 409) {
    return {
      ok: false,
      reason: "conflict",
      message: await problemMessage(res, "Dieser Schritt passt nicht zum aktuellen Stand."),
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
    return { ok: false, reason: "offline", message: await problemMessage(res, fallback) };
  }
  return { ok: true, transfer: (await res.json()) as Transfer };
}

export async function expressInterest(
  subjectId: string,
  message: string
): Promise<TransferResult> {
  let res: Response;
  try {
    res = await send("/transfers", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ subject_id: subjectId, message }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  return toResult(res, "Das Interesse konnte nicht hinterlegt werden.");
}

async function move(path: string): Promise<TransferResult> {
  let res: Response;
  try {
    res = await send(path, { method: "POST" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  return toResult(res, "Der Schritt konnte nicht ausgeführt werden.");
}

export function personMove(transferId: string, action: PersonAction): Promise<TransferResult> {
  return move(`/transfers/${transferId}/${action}`);
}

export function companyMove(transferId: string, action: CompanyAction): Promise<TransferResult> {
  return move(`/transfers/${transferId}/${action}`);
}

export async function makeOffer(transferId: string, input: OfferInput): Promise<TransferResult> {
  let res: Response;
  try {
    res = await send(`/transfers/${transferId}/offer`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      // `null` statt "" für den Monat: "" ist kein Monat und scheiterte am
      // Muster des Vertrags — mit einer Meldung, die nach einem Serverproblem
      // aussieht.
      body: JSON.stringify({
        note: input.note,
        start_on: input.start_on,
        fee_cents: input.fee_cents,
      }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  return toResult(res, "Das Angebot konnte nicht gemacht werden.");
}

export function listMyTransfers(): Promise<TransferListResult> {
  return listTransfers("/transfers/me");
}

export function listCompanyTransfers(): Promise<TransferListResult> {
  return listTransfers("/transfers");
}

async function listTransfers(path: string): Promise<TransferListResult> {
  try {
    const res = await send(path);
    if (!res.ok) {
      // Nicht als leere Liste zeigen: „keine Vorgänge" ist eine Aussage, und
      // sie wäre falsch.
      return { ok: false, message: await problemMessage(res, "Die Liste ließ sich nicht laden.") };
    }
    return { ok: true, transfers: (await res.json()) as Transfer[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
