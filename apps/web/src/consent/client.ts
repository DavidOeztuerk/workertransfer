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
