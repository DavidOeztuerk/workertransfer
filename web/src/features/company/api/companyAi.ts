// A company's own AI key (ADR-0051, Bauliste 6).
//
// The twin of `features/person/api/accountSettings.ts`, for the company: the
// same two providers, the address set by the platform, and the key travelling
// one way only. All three routes are `admin`; a member gets 403 and the page
// says who may.

import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";

/** `none` is the platform's model, not "off". The server's labels, unchanged. */
export type CompanyAiProvider = "none" | "openai" | "anthropic";

/** What the server returns — without the key, only that one is stored. */
export interface CompanyAi {
  provider: CompanyAiProvider;
  model: string;
  keyPresent: boolean;
  keyTail: string;
}

interface CompanyAiWire {
  ai_provider: string;
  ai_base_url: string;
  ai_model: string;
  ai_key_present: boolean;
  ai_key_tail: string;
}

const toView = (wire: CompanyAiWire): CompanyAi => ({
  provider: (wire.ai_provider as CompanyAiProvider) ?? "none",
  model: wire.ai_model ?? "",
  keyPresent: wire.ai_key_present === true,
  keyTail: wire.ai_key_tail ?? "",
});

export type CompanyAiResult =
  | { ok: true; ai: CompanyAi }
  | Failure<"not-admin" | "invalid" | "failed">;

const REASONS = {
  0: { reason: "failed" as const, titleKey: "errors.noConnection" as const },
  403: { reason: "not-admin" as const, titleKey: "errors.adminOnly" as const },
  422: { reason: "invalid" as const, titleKey: "errors.settingsNotSaved" as const },
};

export async function getCompanyAi(tenantId: string, signal?: AbortSignal): Promise<CompanyAiResult> {
  const answer = await request<CompanyAiWire>(
    API_BASE_URL,
    `/companies/${tenantId}/ai`,
    { signal },
    "errors.settingsUnavailable"
  );
  if (answer.ok && answer.value !== undefined) return { ok: true, ai: toView(answer.value) };
  return interpretError(answer.ok ? { status: 500, title: "", detail: "" } : answer.error, REASONS, "failed");
}

/** Provider and model — never the key, which goes separately. */
export async function saveCompanyAi(
  tenantId: string,
  choice: { provider: CompanyAiProvider; model: string }
): Promise<CompanyAiResult> {
  const answer = await request<CompanyAiWire>(
    API_BASE_URL,
    `/companies/${tenantId}/ai`,
    { method: "PUT", body: { ai_provider: choice.provider, ai_model: choice.model } },
    "errors.settingsNotSaved"
  );
  if (answer.ok && answer.value !== undefined) return { ok: true, ai: toView(answer.value) };
  return interpretError(answer.ok ? { status: 500, title: "", detail: "" } : answer.error, REASONS, "failed");
}

/** Stores the key, or removes it — empty means REMOVE, not "leave unchanged". */
export async function saveCompanyAiKey(tenantId: string, plaintext: string): Promise<boolean> {
  const answer = await request<unknown>(
    API_BASE_URL,
    `/companies/${tenantId}/ai/key`,
    { method: "PUT", body: { key: plaintext } },
    "errors.settingsNotSaved"
  );
  return answer.ok;
}
