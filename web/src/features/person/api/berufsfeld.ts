import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";

/**
 * Das Berufsfeld des eigenen Kontos setzen — oder die Angabe zurücknehmen.
 *
 * `null` heisst ENTFERNEN, nicht „unverändert". Ohne diesen Weg wäre eine
 * einmal getroffene Wahl endgültig (ADR-0039).
 *
 * Es gibt keine Kennung im Rumpf, und das ist der eigentliche Schutz: der
 * Server schreibt immer nur in das Konto, das gerade angemeldet ist.
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
