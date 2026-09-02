import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { API_BASE_URL } from "../../../env";

export type Loeschergebnis = { ok: true } | { ok: false; error: ApiError };

/**
 * <c>POST /account/erasure</c> — nur für einen selbst, und ohne Begründung.
 *
 * <strong>Kein Grundfeld, und das ist keine Auslassung.</strong> Von jemandem,
 * der gehen will, eine Rechtfertigung zu verlangen, ist ein Hebel gegen ihn.
 * Deshalb schickt dieser Aufruf einen leeren Rumpf, und es gibt nichts, was man
 * hier ergänzen könnte, ohne die Zusage zu brechen.
 */
export async function loeschungVerlangen(signal?: AbortSignal): Promise<Loeschergebnis> {
  const antwort = await request<void>(
    API_BASE_URL,
    "/account/erasure",
    { method: "POST", signal },
    "Die Löschung konnte nicht angenommen werden."
  );

  return antwort.ok ? { ok: true } : { ok: false, error: antwort.error };
}
