// Der Transfer-Vorgang, wie die Oberfläche ihn benutzt.
//
// Ein Pfad je Übergang, kein `PATCH status`: jeder Übergang gehört einer Seite,
// und ein gemeinsamer müsste bei jedem Aufruf herausfinden, wer gerade was darf.

import { request } from "../../../core/api/client";
import { TRANSFER_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "./fehler";

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
  /**
   * Beim Anlegen aus dem Marktstatus kopiert. Die Plattform kontaktiert den
   * aktuellen Arbeitgeber NICHT — sie weiß nicht, wer er ist.
   */
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

export type TransferFehler = "not-available" | "no-company" | "conflict" | "unavailable" | "offline";
export type TransferErgebnis = { ok: true; transfer: Transfer } | Fehlschlag<TransferFehler>;
export type TransferListe = { ok: true; transfers: Transfer[] } | Fehlschlag<"fehlgeschlagen">;

/** Läuft der Vorgang noch? Nur dann gibt es einen Ausstieg. */
export const RUNNING: TransferStatus[] = ["interested", "talking", "offered", "accepted"];

/**
 * Die Ablöse in Worten, wie sie in Deutschland gelesen wird.
 *
 * `Intl.NumberFormat("de-DE", …)` und nicht von Hand: `5.000,00 €` steht so in
 * der E2E-Reise, und eine handgeschriebene Formatierung trifft entweder das
 * schmale Leerzeichen vor dem Euro nicht oder die Tausenderpunkte.
 */
export function euro(cents: number | null): string {
  if (cents === null) return "—";
  return new Intl.NumberFormat("de-DE", { style: "currency", currency: "EUR" }).format(cents / 100);
}

const UEBERGANGSFEHLER: Partial<
  Record<number, { reason: TransferFehler; title: string; detail?: string }>
> = {
  0: { reason: "offline", title: "Keine Verbindung zum Server." },
  // Kein Status, keine Freigabe, „gerade nicht", fremder Vorgang — vom Server
  // bewusst nicht unterschieden. Die Oberfläche unterscheidet sie auch nicht.
  404: { reason: "not-available", title: "Das ist gerade nicht möglich." },
  403: {
    reason: "no-company",
    title: "Dafür braucht es ein aktives Unternehmen.",
    detail: "Wechsle oben auf eines.",
  },
  503: {
    reason: "unavailable",
    title: "Der Consent-Ledger antwortet gerade nicht.",
    detail: "Bitte später erneut versuchen.",
  },
};

async function zug(path: string, body?: unknown, fallback = "Der Schritt konnte nicht ausgeführt werden."): Promise<TransferErgebnis> {
  const antwort = await request<Transfer>(TRANSFER_BASE_URL, path, { method: "POST", body }, fallback);
  if (antwort.ok) return { ok: true, transfer: antwort.value };
  if (antwort.error.status === 409) {
    return { ok: false, reason: "conflict", error: antwort.error };
  }
  return deuten<TransferFehler>(antwort.error, UEBERGANGSFEHLER, "offline");
}

export function expressInterest(subjectId: string, message: string): Promise<TransferErgebnis> {
  return zug(
    "/transfers",
    { subject_id: subjectId, message },
    "Das Interesse konnte nicht hinterlegt werden."
  );
}

export function personMove(transferId: string, action: PersonAction): Promise<TransferErgebnis> {
  return zug(`/transfers/${transferId}/${action}`);
}

export function companyMove(transferId: string, action: CompanyAction): Promise<TransferErgebnis> {
  return zug(`/transfers/${transferId}/${action}`);
}

export function makeOffer(transferId: string, input: OfferInput): Promise<TransferErgebnis> {
  // `null` statt "" für den Monat: "" ist kein Monat und scheiterte am Muster
  // des Vertrags — mit einer Meldung, die nach einem Serverproblem aussieht.
  return zug(
    `/transfers/${transferId}/offer`,
    { note: input.note, start_on: input.start_on, fee_cents: input.fee_cents },
    "Das Angebot konnte nicht gemacht werden."
  );
}

async function listTransfers(path: string, signal?: AbortSignal): Promise<TransferListe> {
  const antwort = await request<Transfer[]>(
    TRANSFER_BASE_URL,
    path,
    { signal },
    "Die Liste ließ sich nicht laden."
  );
  if (antwort.ok) return { ok: true, transfers: antwort.value ?? [] };
  // Nicht als leere Liste zeigen: „keine Vorgänge" ist eine Aussage, und sie
  // wäre falsch.
  return deuten<"fehlgeschlagen">(
    antwort.error,
    { 0: { reason: "fehlgeschlagen", title: "Keine Verbindung zum Server." } },
    "fehlgeschlagen"
  );
}

export function listMyTransfers(signal?: AbortSignal): Promise<TransferListe> {
  return listTransfers("/transfers/me", signal);
}

export function listCompanyTransfers(signal?: AbortSignal): Promise<TransferListe> {
  return listTransfers("/transfers", signal);
}
