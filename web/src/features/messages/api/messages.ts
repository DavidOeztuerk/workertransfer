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

/** The conversation of an application, as the caller's side sees it. */
export async function getConversation(
  application: string,
  signal?: AbortSignal
): Promise<ConversationResult> {
  const answer = await request<Conversation>(
    MESSAGES_BASE_URL,
    `/messages/application/${application}`,
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
export async function sendMessage(application: string, text: string): Promise<MessageResult> {
  const answer = await request<Message>(
    MESSAGES_BASE_URL,
    `/messages/application/${application}`,
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
export async function requestDraft(application: string): Promise<DraftResult> {
  const answer = await request<Message>(
    MESSAGES_BASE_URL,
    `/messages/application/${application}/draft`,
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
