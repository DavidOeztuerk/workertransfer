// One conversation per process — messages-service (ADR-0047).
//
// <strong>A draft is a message only its own side sees.</strong> The server
// filters the other side's drafts out in the query; this page never receives
// them and must not try to infer one.
//
// <strong>No read state, no unread count.</strong> Whoever answers has read.

import { request } from "../../../core/api/client";
import { MESSAGES_BASE_URL } from "../../../env";
import { interpretError, type Failure, type ServerCase } from "../../../shared/api/errors";

export type Side = "person" | "company";

export interface Message {
  id: string;
  side: Side;
  text: string;
  state: "sent" | "draft";
  /** Whether it is the caller's own side. */
  own: boolean;
  created_at: string;
  sent_at: string | null;
  /** Only for the own side: whether a model drafted it. */
  origin?: "human" | "agent";
}

export interface Conversation {
  kind: string;
  reference: string;
  side: Side;
  messages: Message[];
}

export type ConversationError = "gone" | "unavailable" | "failed";
export type DraftError = "gone" | "failed";

export type ConversationResult = { ok: true; conversation: Conversation } | Failure<ConversationError>;
export type MessageResult = { ok: true; message: Message } | Failure<ConversationError | "invalid">;
export type DraftResult = { ok: true; message: Message } | Failure<DraftError>;

const GONE = { reason: "gone" as const, titleKey: "errors.conversationGone" as const };
const SILENT = { reason: "unavailable" as const, titleKey: "errors.serviceSilent" as const };

/** The kinds of process a conversation hangs on. */
export type ProcessKind = "application" | "approach" | "initiative";

/** The conversation of a process, as the caller's side sees it. */
export async function getConversation(
  reference: string,
  signal?: AbortSignal,
  kind: ProcessKind = "application"
): Promise<ConversationResult> {
  const answer = await request<Conversation>(
    MESSAGES_BASE_URL,
    `/messages/${kind}/${reference}`,
    { signal },
    "errors.conversationNotLoaded"
  );

  if (answer.ok && answer.value != null) return { ok: true, conversation: answer.value };

  return interpretError<ConversationError>(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    { 404: GONE, 503: SILENT },
    "failed"
  );
}

/** A human being writes and sends. */
export async function sendMessage(
  reference: string,
  text: string,
  kind: ProcessKind = "application"
): Promise<MessageResult> {
  const answer = await request<Message>(
    MESSAGES_BASE_URL,
    `/messages/${kind}/${reference}`,
    { method: "POST", body: { text } },
    "errors.messageNotSent"
  );

  if (answer.ok && answer.value != null) return { ok: true, message: answer.value };

  return interpretError<ConversationError | "invalid">(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    {
      404: GONE,
      422: { reason: "invalid", titleKey: "errors.messageInvalid" },
      503: SILENT,
    },
    "failed"
  );
}

/**
 * Asks for a reply draft. It comes back as a draft of the own side and goes
 * nowhere until a human being releases it (VISION-FRAGEN D2).
 */
export async function requestDraft(
  reference: string,
  kind: ProcessKind = "application"
): Promise<DraftResult> {
  const answer = await request<Message>(
    MESSAGES_BASE_URL,
    `/messages/${kind}/${reference}/draft`,
    { method: "POST" },
    "errors.draftNotCreated"
  );

  if (answer.ok && answer.value != null) return { ok: true, message: answer.value };

  return interpretError<DraftError>(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    { 404: GONE },
    "failed"
  );
}

/**
 * "Nothing is set up" and "the provider is silent" are both 503 — and the
 * person has to act differently: the first is a setting, the second a wait.
 * Sorted by `failureKey`, the one place allowed to read the server's sentence.
 */
export const DRAFT_CASES: readonly ServerCase[] = [
  { status: 503, pattern: /no draft provider/i, key: "errors.noDraftProvider" },
  { status: 503, key: "errors.serviceSilent" },
];

/** Releases a draft — as it stands, or as changed. */
export async function releaseDraft(id: string, text: string): Promise<MessageResult> {
  const answer = await request<Message>(
    MESSAGES_BASE_URL,
    `/messages/drafts/${id}/release`,
    { method: "POST", body: { text } },
    "errors.messageNotSent"
  );

  if (answer.ok && answer.value != null) return { ok: true, message: answer.value };

  return interpretError<ConversationError | "invalid">(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    {
      404: GONE,
      422: { reason: "invalid", titleKey: "errors.messageInvalid" },
      503: SILENT,
    },
    "failed"
  );
}

/** Throws a draft away. Nothing remains of it. */
export async function discardDraft(id: string): Promise<boolean> {
  const answer = await request<null>(
    MESSAGES_BASE_URL,
    `/messages/drafts/${id}`,
    { method: "DELETE" },
    "errors.requestFailed"
  );
  return answer.ok;
}

/** One line of "my conversations" — the counterpart and nothing else. */
export interface ConversationSummary {
  kind: ProcessKind;
  reference: string;
  last_message_at: string;
  /** For the person: which company wrote. */
  company_id?: string;
  /** For the company: which person. */
  person_id?: string;
}

export type ConversationListResult = { ok: true; items: ConversationSummary[] } | Failure<"failed">;

/** The caller's conversations, newest first. No unread count (ADR-0047). */
export async function listConversations(signal?: AbortSignal): Promise<ConversationListResult> {
  const answer = await request<ConversationSummary[]>(
    MESSAGES_BASE_URL,
    "/messages/me",
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, items: answer.value ?? [] };
  return { ok: false, reason: "failed", error: answer.error };
}

export type ApproachResult =
  | { ok: true; reference: string }
  | Failure<ConversationError | "invalid">;

/**
 * A company's first message to a person (ADR-0048) — released by a recruiter.
 *
 * `drafted` records only where the words came from (the scout's draft); the
 * person never sees it. Nothing is sent without this call, and this call is a
 * click.
 */
export async function openApproach(
  subjectId: string,
  text: string,
  drafted: boolean
): Promise<ApproachResult> {
  const answer = await request<Conversation>(
    MESSAGES_BASE_URL,
    "/messages/approaches",
    { method: "POST", body: { subject_id: subjectId, text, drafted } },
    "errors.messageNotSent"
  );
  if (answer.ok && answer.value != null) return { ok: true, reference: answer.value.reference };
  return interpretError<ConversationError | "invalid">(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    {
      404: { reason: "gone", titleKey: "errors.personNotApproachable", detailKey: "errors.personNotApproachable" },
      422: { reason: "invalid", titleKey: "errors.messageInvalid", detailKey: "errors.messageInvalid" },
      503: SILENT,
    },
    "failed"
  );
}

/**
 * A person's unsolicited application to a company (VISION-FRAGEN B2).
 *
 * <strong>Sending it releases her profile to that company</strong> — the same
 * as applying to an advert. The page says so before the button.
 */
export async function openInitiative(companyId: string, text: string): Promise<ApproachResult> {
  const answer = await request<Conversation>(
    MESSAGES_BASE_URL,
    "/messages/initiatives",
    { method: "POST", body: { company_id: companyId, text } },
    "errors.messageNotSent"
  );
  if (answer.ok && answer.value != null) return { ok: true, reference: answer.value.reference };
  return interpretError<ConversationError | "invalid">(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    {
      404: { reason: "gone", titleKey: "errors.companyNotReachable", detailKey: "errors.companyNotReachable" },
      422: { reason: "invalid", titleKey: "errors.messageInvalid", detailKey: "errors.messageInvalid" },
      503: SILENT,
    },
    "failed"
  );
}

export type AdviceResult = { ok: true; advice: string } | Failure<"gone" | "failed">;

/**
 * The AI advisor (ADR-0049): advice for the person about one of her
 * conversations. Nothing is stored and nothing is sent — it comes back here.
 */
export async function askAdvisor(reference: string, kind: ProcessKind = "application"): Promise<AdviceResult> {
  const answer = await request<{ advice: string }>(
    MESSAGES_BASE_URL,
    `/messages/${kind}/${reference}/advice`,
    { method: "POST" },
    "errors.adviceNotGiven"
  );
  if (answer.ok && answer.value != null) return { ok: true, advice: answer.value.advice };
  return interpretError<"gone" | "failed">(
    answer.ok ? { status: 503, title: "", detail: "" } : answer.error,
    { 404: GONE },
    "failed"
  );
}
