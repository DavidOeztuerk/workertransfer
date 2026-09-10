import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";

/**
 * Das Berufsfeld des eigenen Kontos setzen. `null` heisst entfernen. Keine
 * Kennung im Rumpf — der Server schreibt nur in das angemeldete Konto.
 */
export async function speichereBerufsfeld(
  feld: BerufsfeldWahl,
): Promise<{ ok: true } | { ok: false; detail: string }> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/occupational-field",
    { method: "PUT", body: { occupational_field: feld } },
    "fehler.berufsfeldNichtGespeichert",
  );

  return answer.ok ? { ok: true } : { ok: false, detail: answer.error.detail };
}
