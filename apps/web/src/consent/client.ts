// Der Consent-Ledger, wie die Oberfläche ihn benutzt.
//
// Eine Stelle für alle Capabilities: Profil und Portfolio beantworten
// verschiedene Fragen, aber sie stellen sie über dieselben drei Endpunkte. Zwei
// Kopien dieser Aufrufe würden sich irgendwann über das Format uneinig.

import { CONSENT_BASE_URL } from "../env";

export const PROFILE_VISIBILITY = "profile.visibility:public";
export const PORTFOLIO_VISIBILITY = "portfolio.visibility:public";

export type ConsentResult = { ok: true; granted: boolean } | { ok: false; message: string };

/**
 * Gilt diese Einwilligung gerade?
 *
 * Bei einem Fehler `false`. Bewusst asymmetrisch zum Server, der in diesem Fall
 * 503 meldet statt zu verbergen: hier steht nur die Stellung eines Schalters auf
 * dem Spiel, und ein Schalter, der versehentlich „freigegeben" behauptet, wäre
 * die gefährlichere Lüge.
 */
export async function isGranted(subjectId: string, capability: string): Promise<boolean> {
  try {
    const res = await fetch(`${CONSENT_BASE_URL}/consent/check`, {
      method: "POST",
      credentials: "include",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ subject_id: subjectId, capability }),
    });
    if (!res.ok) return false;
    const body = (await res.json()) as { granted?: unknown; deleted?: unknown };
    return body.granted === true && body.deleted !== true;
  } catch {
    return false;
  }
}

/**
 * Erteilen oder zurückziehen.
 *
 * Der Widerruf trägt immer eine Begründung, weil der Vertrag sie verlangt: eine
 * Entziehung muss erklärbar sein, eine Erteilung nicht. Die Voreinstellung sagt
 * ehrlich, wo der Widerruf ausgelöst wurde, statt etwas zu erfinden.
 */
export async function setGranted(
  subjectId: string,
  capability: string,
  granted: boolean,
  withdrawalReason: string
): Promise<ConsentResult> {
  const path = granted ? "/consent/grant" : "/consent/revoke";
  const body = granted
    ? { subject_id: subjectId, capability }
    : { subject_id: subjectId, capability, reason: withdrawalReason };
  try {
    const res = await fetch(`${CONSENT_BASE_URL}${path}`, {
      method: "POST",
      credentials: "include",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      return { ok: false, message: "Die Freigabe konnte nicht geändert werden." };
    }
    const state = (await res.json()) as { granted?: unknown };
    return { ok: true, granted: state.granted === true };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Consent-Ledger." };
  }
}

export interface GrantedConsent {
  capability: string;
  granted_at: string;
}

export type MyConsentsResult =
  | { ok: true; consents: GrantedConsent[] }
  | { ok: false; message: string };

/**
 * Was gerade gilt — nur die eigenen Freigaben.
 *
 * Ein Fehler wird NICHT als leere Liste gezeigt. „Du hast nichts freigegeben"
 * ist eine Aussage, und auf genau dieser Seite wäre sie die beruhigendste
 * falsche Antwort, die das System geben kann.
 */
export async function listMyConsents(): Promise<MyConsentsResult> {
  try {
    const res = await fetch(`${CONSENT_BASE_URL}/consent/me`, { credentials: "include" });
    if (!res.ok) {
      return { ok: false, message: "Deine Freigaben ließen sich nicht laden." };
    }
    return { ok: true, consents: (await res.json()) as GrantedConsent[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Consent-Ledger." };
  }
}

export interface ParsedCapability {
  /** Der Bereich in Worten, oder `null`, wenn die Form unbekannt ist. */
  area: string | null;
  /** Die Tenant-UUID, wenn die Freigabe einem Unternehmen gilt. */
  tenantId: string | null;
  /** Gilt sie allen Unternehmen? */
  public: boolean;
}

const AREAS: Record<string, string> = {
  profile: "Profil",
  resume: "Lebenslauf",
  portfolio: "Arbeiten",
  market: "Marktstatus",
};

/**
 * `resume.visibility:tenant:<uuid>` → lesbare Teile.
 *
 * Unbekannte Formen ergeben `area: null` — die Oberfläche zeigt sie dann roh
 * an, statt sie zu verschlucken. Eine Freigabe zu verbergen, weil ihr Format
 * nicht erkannt wurde, wäre auf dieser Seite der schlimmste denkbare Fehler.
 */
export function parseCapability(capability: string): ParsedCapability {
  const match = /^([a-z]+)\.visibility:(public|tenant:([0-9a-fA-F-]{36}))$/.exec(capability);
  if (match === null) return { area: null, tenantId: null, public: false };
  const area = AREAS[match[1] ?? ""] ?? null;
  return { area, tenantId: match[3] ?? null, public: match[2] === "public" };
}


export interface ConsentHistoryEntry {
  capability: string;
  action: "GRANT" | "REVOKE" | "DELETE";
  recorded_at: string;
  /** Nur der betroffenen Person gegenüber gefüllt. */
  reason: string | null;
}

export type ConsentHistoryResult =
  | { ok: true; events: ConsentHistoryEntry[] }
  | { ok: false; message: string };

/** Die eigene Geschichte — für die Auskunft, nicht für die Übersicht. */
export async function listMyConsentHistory(): Promise<ConsentHistoryResult> {
  try {
    const res = await fetch(`${CONSENT_BASE_URL}/consent/me/history`, {
      credentials: "include",
    });
    if (!res.ok) return { ok: false, message: "Die Historie ließ sich nicht laden." };
    return { ok: true, events: (await res.json()) as ConsentHistoryEntry[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Consent-Ledger." };
  }
}
