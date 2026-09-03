import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";

/** Wer gefragt werden darf. Die Etiketten des Servers, unverändert. */
export type KiAnbieter = "none" | "openai_compatible" | "anthropic";

/**
 * Was der Server über die Einstellungen zurückgibt.
 *
 * <strong>Ohne den Schlüssel.</strong> `keyPresent` und `keyTail` genügen, um
 * ihn wiederzuerkennen; mehr kommt nie über die Leitung zurück. Ein Geheimnis,
 * das man abrufen kann, ist eines, das man abziehen kann.
 */
export interface Kontoeinstellungen {
  deleteAfterMonths: number | null;
  provider: KiAnbieter;
  baseUrl: string;
  model: string;
  keyPresent: boolean;
  keyTail: string;
  auditLog: boolean;
}

interface Draht {
  delete_after_months: number | null;
  ai_provider: string;
  ai_base_url: string;
  ai_model: string;
  ai_key_present: boolean;
  ai_key_tail: string;
  ai_audit_log: boolean;
}

const zuAnsicht = (draht: Draht): Kontoeinstellungen => ({
  deleteAfterMonths: draht.delete_after_months,
  provider: draht.ai_provider as KiAnbieter,
  baseUrl: draht.ai_base_url,
  model: draht.ai_model,
  keyPresent: draht.ai_key_present,
  keyTail: draht.ai_key_tail,
  auditLog: draht.ai_audit_log,
});

/** Die eigenen Einstellungen. `null` heisst: nicht abrufbar. */
export async function ladeEinstellungen(
  signal?: AbortSignal
): Promise<Kontoeinstellungen | null> {
  const answer = await request<Draht>(
    API_BASE_URL,
    "/account/settings",
    { signal },
    "fehler.einstellungenNichtAbrufbar"
  );
  return answer.ok && answer.value !== undefined ? zuAnsicht(answer.value) : null;
}

export type Speicherergebnis =
  | { ok: true; einstellungen: Kontoeinstellungen }
  | { ok: false; detail: string };

/** Schreibt die Einstellungen — ohne den Schlüssel, der geht getrennt. */
export async function speichereEinstellungen(
  wahl: Omit<Kontoeinstellungen, "keyPresent" | "keyTail">
): Promise<Speicherergebnis> {
  const answer = await request<Draht>(
    API_BASE_URL,
    "/account/settings",
    {
      method: "PUT",
      body: {
        delete_after_months: wahl.deleteAfterMonths,
        ai_provider: wahl.provider,
        ai_base_url: wahl.baseUrl,
        ai_model: wahl.model,
        ai_audit_log: wahl.auditLog,
      },
    },
    "fehler.einstellungenNichtGespeichert"
  );

  return answer.ok && answer.value !== undefined
    ? { ok: true, einstellungen: zuAnsicht(answer.value) }
    : { ok: false, detail: answer.ok ? "" : answer.error.detail };
}

/**
 * Hinterlegt oder entfernt den Schlüssel.
 *
 * Leer heisst ENTFERNEN und nicht „unverändert lassen": ein Feld, das bei leer
 * nichts tut, hat keinen Weg zurück zu „keiner".
 */
export async function speichereSchluessel(klartext: string): Promise<boolean> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/ai-key",
    { method: "PUT", body: { key: klartext } },
    "fehler.einstellungenNichtGespeichert"
  );
  return answer.ok;
}
