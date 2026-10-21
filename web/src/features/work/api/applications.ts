// Client for applications-service.
//
// The application carries no profile data — only a `subject_id`. Whoever wants
// to see profile, CV or portfolio asks the responsible services, and there the
// consent ledger applies. A second path to the same data would have a second
// filter, and some day it would diverge from the first.

import { request } from "../../../core/api/client";
import { APPLICATIONS_BASE_URL } from "../../../env";
import {
  type ErrorReading,
  type Failure,
  interpretError,
} from "../../../shared/api/errors";

export type ApplicationStatus = "submitted" | "reviewing" | "rejected" | "withdrawn" | "hired";

export interface ApplicantContact {
  full_name: string;
  line1: string;
  line2: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
  email: string;
}

export interface Application {
  id: string;
  job_id: string;
  tenant_id: string;
  subject_id: string;
  message: string;
  shares_resume: boolean;
  shares_portfolio: boolean;
  documents: string[];
  status: ApplicationStatus;
  created_at: string;
  updated_at: string;
  applicant_contact?: ApplicantContact;
}

/** Exactly the four fields of the contract — and all four in snake_case. */
export interface ApplicationInput {
  job_id: string;
  message: string;
  shares_resume: boolean;
  shares_portfolio: boolean;
}

export type ApplicationError =
  | "unauthenticated"
  | "gone"
  | "already"
  | "unavailable"
  | "invalid"
  | "offline";

export type ApplicationResult =
  | { ok: true; application: Application }
  | Failure<ApplicationError>;

export type ApplicationListResult =
  | { ok: true; applications: Application[] }
  | Failure<"failed" | "offline">;

export type DraftStatus =
  | "open"
  | "generating"
  | "review"
  | "needs_changes"
  | "approved"
  | "sent"
  | "failed";

export interface DraftComment {
  id: string;
  text: string;
  quote: string;
  resolved: boolean;
  created_at: string;
}

export interface Draft {
  id: string;
  job_id: string;
  subject: string;
  body: string;
  status: DraftStatus;
  version: number;
  error: string | null;
  writing_started_at: string | null;
  shares_resume: boolean;
  documents: string[];
  comments: DraftComment[];
  updated_at: string;
}

function toDraft(raw: Draft): Draft {
  return {
    ...raw,
    subject: raw.subject ?? "",
    body: raw.body ?? "",
    shares_resume: raw.shares_resume === true,
    documents: Array.isArray(raw.documents) ? raw.documents : [],
    comments: Array.isArray(raw.comments) ? raw.comments : [],
    error: raw.error ?? null,
    writing_started_at: raw.writing_started_at ?? null,
  };
}

export type DraftError =
  | "unauthenticated"
  | "not_found"
  | "conflict"
  | "unavailable"
  | "invalid"
  | "offline";

export type DraftResult = { ok: true; draft: Draft } | Failure<DraftError>;
export type DraftListResult = { ok: true; drafts: Draft[] } | Failure<"failed" | "offline">;

const APPLICATION_ERRORS: Partial<Record<number, ErrorReading<ApplicationError>>> = {
  0: { reason: "offline", titleKey: "errors.noConnection" },
  401: {
    reason: "unauthenticated",
    titleKey: "errors.accountToApply",
    detailKey: "errors.signIn",
  },
  404: { reason: "gone", titleKey: "errors.jobClosed" },
  // Neither rejected nor accepted: we do not know right now, and a rejection
  // nobody pronounced would be the worse answer.
  503: {
    reason: "unavailable",
    titleKey: "errors.serviceSilent",
    detailKey: "errors.tryLater",
  },
};

const DRAFT_ERRORS: Partial<Record<number, ErrorReading<DraftError>>> = {
  0: { reason: "offline", titleKey: "errors.noConnection" },
  401: {
    reason: "unauthenticated",
    titleKey: "errors.accountToApply",
    detailKey: "errors.signIn",
  },
  404: { reason: "not_found", titleKey: "errors.draftNotFound" },
  409: { reason: "conflict", titleKey: "errors.stepNotAllowed" },
  503: {
    reason: "unavailable",
    titleKey: "errors.noProvider",
    detailKey: "errors.draftProviderMissing",
  },
};

async function postApplication(path: string, body?: unknown): Promise<ApplicationResult> {
  const answer = await request<Application>(
    APPLICATIONS_BASE_URL,
    path,
    { method: "POST", body },
    "errors.applicationNotSent"
  );
  if (answer.ok) return { ok: true, application: answer.value };
  // `409` keeps the server's sentence: the state does not fit, and the person
  // should not search the form for the mistake.
  if (answer.error.status === 409) {
    return { ok: false, reason: "already", error: answer.error };
  }
  return interpretError<ApplicationError>(answer.error, APPLICATION_ERRORS, "invalid");
}

/**
 * A change to a draft (text, attachments, comment).
 *
 * `409` and `503` keep the server's sentence here, and that is deliberate: the
 * page sorts it by its words (`lib/draftErrors.ts`) into a catalogue text. The
 * server's sentence is English, and German where the step is not allowed — so
 * it is read, never shown.
 */
async function changeDraft(
  path: string,
  method: "POST" | "PATCH" | "PUT",
  body?: unknown
): Promise<DraftResult> {
  const answer = await request<Draft>(
    APPLICATIONS_BASE_URL,
    path,
    { method, body },
    "errors.draftNotSaved"
  );
  if (answer.ok) return { ok: true, draft: toDraft(answer.value) };
  if (answer.error.status === 409) {
    return { ok: false, reason: "conflict", error: answer.error };
  }
  // 503 stays the server's sentence: "no provider" and "model not loaded" are
  // both 503, and the catalogue line for the first would swallow the second.
  if (answer.error.status === 503) {
    return { ok: false, reason: "unavailable", error: answer.error };
  }
  return interpretError<DraftError>(answer.error, DRAFT_ERRORS, "invalid");
}

async function draftStep(path: string): Promise<DraftResult> {
  const answer = await request<Draft>(
    APPLICATIONS_BASE_URL,
    path,
    { method: "POST" },
    "errors.draftStepFailed"
  );
  if (answer.ok) return { ok: true, draft: toDraft(answer.value) };
  if (answer.error.status === 409) {
    return { ok: false, reason: "conflict", error: answer.error };
  }
  if (answer.error.status === 503) {
    return { ok: false, reason: "unavailable", error: answer.error };
  }
  return interpretError<DraftError>(answer.error, DRAFT_ERRORS, "invalid");
}

export function apply(input: ApplicationInput): Promise<ApplicationResult> {
  return postApplication("/applications", input);
}

export function withdrawApplication(applicationId: string): Promise<ApplicationResult> {
  return postApplication(`/applications/${applicationId}/withdraw`);
}

export function advanceApplication(
  applicationId: string,
  status: "reviewing" | "rejected" | "hired"
): Promise<ApplicationResult> {
  return postApplication(`/applications/${applicationId}/status`, { status });
}

async function list(path: string, signal?: AbortSignal): Promise<ApplicationListResult> {
  const answer = await request<Application[]>(
    APPLICATIONS_BASE_URL,
    path,
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, applications: answer.value ?? [] };
  // No account or no active company: a state one can fix, not an error one
  // would have to report.
  if (answer.error.status === 401 || answer.error.status === 403) {
    return { ok: true, applications: [] };
  }
  return interpretError<"failed" | "offline">(
    answer.error,
    { 0: { reason: "offline", titleKey: "errors.noConnection" } },
    "failed"
  );
}

export function listMyApplications(signal?: AbortSignal): Promise<ApplicationListResult> {
  return list("/applications/me", signal);
}

export function listApplicationsForJob(
  jobId: string,
  signal?: AbortSignal
): Promise<ApplicationListResult> {
  return list(`/jobs/${jobId}/applications`, signal);
}

export async function getApplication(
  applicationId: string,
  signal?: AbortSignal
): Promise<ApplicationResult> {
  const answer = await request<Application>(
    APPLICATIONS_BASE_URL,
    `/applications/${applicationId}`,
    { signal },
    "errors.companyApplicationNotLoaded"
  );
  if (answer.ok) return { ok: true, application: answer.value };
  return interpretError<ApplicationError>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      401: {
        reason: "unauthenticated",
        titleKey: "errors.accountToApply",
        detailKey: "errors.signIn",
      },
      403: {
        reason: "gone",
        titleKey: "errors.actForCompanyRequired",
        detailKey: "errors.chooseCompany",
      },
      404: { reason: "gone", titleKey: "errors.jobMissing" },
      503: {
        reason: "unavailable",
        titleKey: "errors.serviceSilent",
        detailKey: "errors.tryLater",
      },
    },
    "invalid"
  );
}

// Drafts
export function createDrafts(jobIds: string[]): Promise<DraftListResult> {
  return request<Draft[]>(
    APPLICATIONS_BASE_URL,
    "/applications/drafts",
    { method: "POST", body: { job_ids: jobIds } },
    "errors.draftsNotCreated"
  ).then((answer) =>
    answer.ok
      ? { ok: true, drafts: (answer.value ?? []).map(toDraft) }
      : interpretError<"failed" | "offline">(
          answer.error,
          { 0: { reason: "offline", titleKey: "errors.noConnection" } },
          "failed"
        )
  );
}

export function writeDraft(draftId: string): Promise<DraftResult> {
  return draftStep(`/applications/drafts/${draftId}/write`);
}

export function getDrafts(signal?: AbortSignal): Promise<DraftListResult> {
  return request<Draft[]>(
    APPLICATIONS_BASE_URL,
    "/applications/drafts",
    { signal },
    "errors.draftsNotLoaded"
  ).then((answer) =>
    answer.ok
      ? { ok: true, drafts: (answer.value ?? []).map(toDraft) }
      : interpretError<"failed" | "offline">(
          answer.error,
          { 0: { reason: "offline", titleKey: "errors.noConnection" } },
          "failed"
        )
  );
}

export function getDraft(draftId: string, signal?: AbortSignal): Promise<DraftResult> {
  return request<Draft>(
    APPLICATIONS_BASE_URL,
    `/applications/drafts/${draftId}`,
    { signal },
    "errors.draftNotLoaded"
  ).then((answer) =>
    answer.ok
      ? { ok: true, draft: toDraft(answer.value) }
      : interpretError<DraftError>(answer.error, DRAFT_ERRORS, "invalid")
  );
}

export function updateDraft(draftId: string, subject: string, body: string): Promise<DraftResult> {
  return changeDraft(`/applications/drafts/${draftId}`, "PATCH", { subject, body });
}

export function chooseAttachments(
  draftId: string,
  sharesResume: boolean,
  documents: string[]
): Promise<DraftResult> {
  return changeDraft(`/applications/drafts/${draftId}/attachments`, "PUT", {
    shares_resume: sharesResume,
    documents,
  });
}

export function addComment(draftId: string, text: string, quote: string): Promise<DraftResult> {
  return changeDraft(`/applications/drafts/${draftId}/comments`, "POST", { text, quote });
}

export function reviseDraft(draftId: string): Promise<DraftResult> {
  return draftStep(`/applications/drafts/${draftId}/revise`);
}

export function approveDraft(draftId: string): Promise<DraftResult> {
  return draftStep(`/applications/drafts/${draftId}/approve`);
}

export function sendDraft(draftId: string): Promise<DraftResult> {
  return draftStep(`/applications/drafts/${draftId}/send`);
}

export function deleteDraft(draftId: string): Promise<{ ok: true } | Failure<DraftError>> {
  return request<void>(
    APPLICATIONS_BASE_URL,
    `/applications/drafts/${draftId}`,
    { method: "DELETE" },
    "errors.draftNotDeleted"
  ).then((answer) =>
    answer.ok
      ? { ok: true }
      : interpretError<DraftError>(answer.error, DRAFT_ERRORS, "invalid")
  );
}
