import type { ApiError } from "../../../core/store/thunkHelpers";
import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";

/**
 * A 2xx without a body is no confirmation of what was stored. It reads as the
 * step's own failure — there is no sentence of the server's to show anyway.
 */
const EMPTY_ANSWER: ApiError = { status: 500, title: "", detail: "" };

/**
 * Whose model drafts. `none` is the platform's model, not "off"; a provider
 * with a stored key replaces it for everything the person drafts (ADR-0051).
 * The server's labels, unchanged.
 */
export type AiProvider = "none" | "openai" | "anthropic";

/**
 * What the server returns about the settings.
 *
 * <strong>Without the key.</strong> `keyPresent` and `keyTail` are enough to
 * recognise it; nothing more ever comes back over the wire. A secret that can
 * be fetched is one that can be extracted.
 */
export interface AccountSettings {
  deleteAfterMonths: number | null;
  provider: AiProvider;
  model: string;
  keyPresent: boolean;
  keyTail: string;
  auditLog: boolean;
}

interface AccountSettingsWire {
  delete_after_months: number | null;
  ai_provider: string;
  ai_base_url: string;
  ai_model: string;
  ai_key_present: boolean;
  ai_key_tail: string;
  ai_audit_log: boolean;
}

const toView = (wire: AccountSettingsWire): AccountSettings => ({
  deleteAfterMonths: wire.delete_after_months ?? null,
  provider: (wire.ai_provider as AiProvider) ?? "none",
  model: wire.ai_model ?? "",
  keyPresent: wire.ai_key_present === true,
  keyTail: wire.ai_key_tail ?? "",
  auditLog: wire.ai_audit_log === true,
});

/** One's own settings. `null` means: not retrievable. */
export async function getAccountSettings(
  signal?: AbortSignal
): Promise<AccountSettings | null> {
  const answer = await request<AccountSettingsWire>(
    API_BASE_URL,
    "/account/settings",
    { signal },
    "errors.settingsUnavailable"
  );
  return answer.ok && answer.value !== undefined ? toView(answer.value) : null;
}

export type AccountSettingsSaveResult =
  | { ok: true; settings: AccountSettings }
  | { ok: false; error: ApiError };

/** Writes the settings — without the key, which goes separately. */
export async function saveAccountSettings(
  choice: Omit<AccountSettings, "keyPresent" | "keyTail">
): Promise<AccountSettingsSaveResult> {
  const answer = await request<AccountSettingsWire>(
    API_BASE_URL,
    "/account/settings",
    {
      method: "PUT",
      body: {
        delete_after_months: choice.deleteAfterMonths,
        ai_provider: choice.provider,
        ai_model: choice.model,
        ai_audit_log: choice.auditLog,
      },
    },
    "errors.settingsNotSaved"
  );

  return answer.ok && answer.value !== undefined
    ? { ok: true, settings: toView(answer.value) }
    : { ok: false, error: answer.ok ? EMPTY_ANSWER : answer.error };
}

/**
 * Stores or removes the key.
 *
 * Empty means REMOVE and not "leave unchanged": a field that does nothing when
 * empty has no way back to "none".
 */
export async function saveAiKey(plaintext: string): Promise<boolean> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/ai-key",
    { method: "PUT", body: { key: plaintext } },
    "errors.settingsNotSaved"
  );
  return answer.ok;
}
