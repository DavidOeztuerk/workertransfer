// The mandate and the conversations — advisor-service (ADR-0037).
//
// <strong>The mandate is a VIEW, not a second store.</strong> Who may see me
// stands in the consent ledger; whether I am approachable, in the market status
// at transfer-service. This client therefore knows no visibility switch and
// must never get one — it would have nowhere to write to.
//
// <strong>And the stage is an answer, not a field.</strong> `stage` comes fresh
// from the ledger with every answer. Caching it here would be the same as a
// stage column on the server, only more fleeting: a revocation must take effect
// on the next request (ADR-0013).

import { request } from "../../../core/api/client";
import { ADVISOR_BASE_URL } from "../../../env";
import { type ErrorReading, type Failure, interpretError } from "../../../shared/api/errors";

/**
 * The mandate: four values, all optional.
 *
 * `null` means "said nothing" everywhere, and that is the normal case. An empty
 * mandate is complete — nobody has to name a salary to hold a conversation.
 */
export interface Mandate {
  /** Month `YYYY-MM`. A month and not a date: "from March" is what one can say. */
  entry_month: string | null;
  /** Euros per month. */
  salary_min: number | null;
  salary_max: number | null;
  /** 10–100. */
  workload_percent: number | null;
  /**
   * Domains with which no conversation comes about.
   *
   * <strong>Whoever enters something here gives up "visible to all
   * companies".</strong> The ledger knows no negation: "visible to all, but not
   * to X" cannot be expressed in it. The server therefore takes the public
   * visibility back in the same step — and only then does the promise hold
   * that the current employer does not find its own staff.
   */
  excluded_domains: string[];
  updated_at: string;
}

/** What is written. All fields optional — what is missing is cleared. */
export interface MandateInput {
  entry_month: string | null;
  salary_min: number | null;
  salary_max: number | null;
  workload_percent: number | null;
  excluded_domains: string[];
}

/** Where a conversation stands. Not to be confused with the stage. */
export type ConversationState = "talking" | "agreed" | "handed_over" | "ended";

/**
 * A conversation as the person sees it.
 *
 * `stage` is 0 to 3 and comes from the ledger. `0` means: for the other side
 * this conversation does not exist right now.
 */
export interface MyConversation {
  id: string;
  tenant_id: string;
  state: ConversationState;
  stage: number;
  note: string;
  opened_at: string;
  updated_at: string;
}

/**
 * A conversation as the company sees it.
 *
 * <strong>The fields of the higher stages are ABSENT, they are not
 * `null`.</strong> That is why they are optional here rather than nullable: a
 * `| null` would invite showing "not released yet" — and that is exactly the
 * hint that must not exist. If the field is missing there is nothing to show,
 * and why it is missing is none of the company's business.
 */
export interface Conversation {
  id: string;
  subject_id: string;
  state: ConversationState;
  stage: number;
  note: string;
  opened_at: string;
  updated_at: string;
  entry_month?: string;
  workload_percent?: number;
  salary_min?: number;
  salary_max?: number;
  name?: string;
  email?: string;
}

export type MandateResult = { ok: true; mandate: Mandate } | Failure<"unavailable">;
export type MandateSaveResult =
  | { ok: true; mandate: Mandate }
  | Failure<"unauthenticated" | "invalid" | "unavailable" | "offline">;
export type MyConversationList =
  | { ok: true; items: MyConversation[] }
  | Failure<"unavailable">;
export type CompanyConversationList =
  | { ok: true; items: Conversation[] }
  | Failure<"no-company" | "unavailable">;
export type StageResult =
  | { ok: true; stage: number }
  | Failure<"gone" | "unavailable" | "offline">;
export type StepResult =
  | { ok: true }
  | Failure<"gone" | "conflict" | "unavailable" | "offline">;

const LEDGER_SILENT: ErrorReading<"unavailable"> = {
  reason: "unavailable",
  titleKey: "errors.ledgerSilent",
  detailKey: "errors.tryLater",
};

/**
 * One's own mandate. Never `404` — "said nothing" is a state.
 *
 * As with the market status there is <strong>no substitute value</strong>
 * here. An invented empty mandate would land in the form, and whoever then
 * touched something and saved silently erased their salary range. `ok: false`
 * means "we do not know", and the page then shows no form.
 */
export async function getMandate(signal?: AbortSignal): Promise<MandateResult> {
  const answer = await request<Mandate>(
    ADVISOR_BASE_URL,
    "/advisor/me/mandate",
    { signal },
    "errors.mandateUnavailable"
  );

  if (answer.ok) return { ok: true, mandate: answer.value };

  return interpretError<"unavailable">(answer.error, { 503: LEDGER_SILENT }, "unavailable");
}

export async function saveMandate(input: MandateInput): Promise<MandateSaveResult> {
  const answer = await request<Mandate>(
    ADVISOR_BASE_URL,
    "/advisor/me/mandate",
    { method: "PUT", body: input },
    "errors.mandateNotSaved"
  );

  if (answer.ok) return { ok: true, mandate: answer.value };

  return interpretError<"unauthenticated" | "invalid" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      401: {
        reason: "unauthenticated",
        titleKey: "errors.sessionExpired",
        detailKey: "errors.signInAgain",
      },
      503: LEDGER_SILENT,
    },
    "invalid"
  );
}

export async function getMyConversations(signal?: AbortSignal): Promise<MyConversationList> {
  const answer = await request<{ items: MyConversation[] }>(
    ADVISOR_BASE_URL,
    "/advisor/me/conversations",
    { signal },
    "errors.listNotLoaded"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  // Do NOT show `503` as an empty list: that would be the claim that nobody has
  // opened a conversation — and at this moment nobody knows that.
  return interpretError<"unavailable">(answer.error, { 503: LEDGER_SILENT }, "unavailable");
}

export async function getCompanyConversations(
  signal?: AbortSignal
): Promise<CompanyConversationList> {
  const answer = await request<{ items: Conversation[] }>(
    ADVISOR_BASE_URL,
    "/advisor/conversations",
    { signal },
    "errors.listNotLoaded"
  );

  if (answer.ok) return { ok: true, items: answer.value?.items ?? [] };

  return interpretError<"no-company" | "unavailable">(
    answer.error,
    {
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesAsk",
        detailKey: "errors.chooseCompany",
      },
      503: LEDGER_SILENT,
    },
    "unavailable"
  );
}

/**
 * Release a stage or take it back.
 *
 * Two paths instead of one endpoint with a flag: releasing and revoking are
 * different acts, and in the ledger's record they stay that.
 */
async function moveStage(id: string, path: string, stage: number): Promise<StageResult> {
  const answer = await request<{ stage: number }>(
    ADVISOR_BASE_URL,
    `/advisor/me/conversations/${id}/${path}`,
    { method: "POST", body: { stage } },
    "errors.stageNotChanged"
  );

  if (answer.ok) return { ok: true, stage: answer.value?.stage ?? 0 };

  return interpretError<"gone" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      // `404` deliberately does not say whether the conversation exists.
      404: { reason: "gone", titleKey: "errors.conversationGone" },
      503: LEDGER_SILENT,
    },
    "offline"
  );
}

export const advanceStage = (id: string, stage: number) => moveStage(id, "advance", stage);
export const withdrawStage = (id: string, stage: number) => moveStage(id, "withdraw", stage);

async function step(path: string): Promise<StepResult> {
  const answer = await request<unknown>(
    ADVISOR_BASE_URL,
    path,
    { method: "POST" },
    "errors.conversationStepFailed"
  );

  if (answer.ok) return { ok: true };

  return interpretError<"gone" | "conflict" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      404: { reason: "gone", titleKey: "errors.conversationGone" },
      // `409` comes with the server's sentence: its problem document knows more
      // about this transition than a table here.
      409: { reason: "conflict", titleKey: "errors.stepNotPossible" },
      503: LEDGER_SILENT,
    },
    "offline"
  );
}

export const agree = (id: string) => step(`/advisor/me/conversations/${id}/agree`);
export const endAsPerson = (id: string) => step(`/advisor/me/conversations/${id}/end`);
export const endAsCompany = (id: string) => step(`/advisor/conversations/${id}/end`);
export const handOver = (id: string) => step(`/advisor/conversations/${id}/hand-over`);

/** Open a conversation — only a company, and only with a release. */
export async function openConversation(subjectId: string, note: string): Promise<StepResult> {
  const answer = await request<Conversation>(
    ADVISOR_BASE_URL,
    "/advisor/conversations",
    { method: "POST", body: { subject_id: subjectId, note } },
    "errors.conversationNotOpened"
  );

  if (answer.ok) return { ok: true };

  return interpretError<"gone" | "conflict" | "unavailable" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      403: { reason: "conflict", titleKey: "errors.onlyCompaniesAsk", detailKey: "errors.chooseCompany" },
      // `404` means: this person has released nothing to this company — or has
      // excluded it, or does not exist. The three are indistinguishable, and the
      // UI builds no piece of information out of them.
      404: { reason: "gone", titleKey: "errors.personNotRequestable" },
      503: LEDGER_SILENT,
    },
    "offline"
  );
}
