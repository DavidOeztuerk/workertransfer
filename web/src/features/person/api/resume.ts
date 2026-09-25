import { type ApiResult, request } from "../../../core/api/client";
import { RESUME_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import { interpretError } from "../../../shared/api/errors";

/**
 * The CV — stricter than the profile, and that is the whole design.
 *
 * A profile is a notice board; a CV names real employers with dates — exactly
 * what a <em>current</em> employer must not see. So there is <strong>no public
 * switch</strong> here: a company ASKS, the person answers, and the release
 * applies to that one company.
 */
export interface Position {
  employer: string;
  title: string;
  started_on: string;
  /** `null` means "still running" — not "unknown". */
  ended_on: string | null;
  description: string;
  /**
   * What was used there — named by the person themselves.
   *
   * They do not make the position searchable: a CV is released individually
   * (ADR-0020). A skill becomes searchable only in the profile, and it gets
   * there with one click.
   */
  technologies: string[];
}

/** The wire words of an education entry's kind. */
export type EducationKind = "schule" | "ausbildung";

export interface Education {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
  /** Missing on older entries — then vocational training. */
  kind?: EducationKind;
}

/** The wire words of the three CV templates. */
export type CurriculumVitaeTemplate = "schlicht" | "klassisch" | "modern";

export interface CurriculumVitae {
  subject_id: string;
  positions: Position[];
  education: Education[];
  updated_at: string;
  template: CurriculumVitaeTemplate;
}

export interface CurriculumVitaeInput {
  positions: Position[];
  education: Education[];
}

export type DocumentKind = "zeugnis" | "zertifikat" | "sonstiges" | "lebenslauf";

export interface DocumentRecordV1 {
  id: string;
  name: string;
  kind: DocumentKind;
  content_type: string;
  size_bytes: number;
  uploaded_at: string;
}

/**
 * What was read in one of one's own documents — or that it was never read.
 *
 * <strong>Three states, and they must never become two.</strong>
 * `read_at === null` means "never read"; `has_text === false` means "read, but
 * there was no text in it" (a photographed master craftsman's certificate is a
 * picture); an empty `terms` with `has_text === true` means "read, no known word
 * found". Merging the three would silently tell somebody that nothing stands in
 * their certificate — ADR-0022 §3.
 *
 * `terms` is <strong>evidence</strong> and not a naming: a statement about this
 * document. A word becomes searchable only when the person types it into the
 * profile and <em>saves</em> (ADR-0033).
 */
export interface DocumentTerms {
  document_id: string;
  name: string;
  read_at: string | null;
  has_text: boolean;
  terms: string[];
}

/**
 * What was read last — <strong>without reading</strong>.
 *
 * The profile page asks this on load, and it may, because nothing happens
 * doing so: no file is opened, no recogniser called, no row written. Whoever
 * attaches the reading here turns opening a page into the background run that
 * ADR-0004 rules out.
 */
export async function getDocumentTerms(
  signal?: AbortSignal
): Promise<ApiResult<DocumentTerms[]>> {
  const answer = await request<DocumentTerms[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents/terms",
    { signal },
    "errors.documentsNotLoaded"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/**
 * <strong>The button.</strong> Only here is anything read.
 *
 * A human presses it, and only then does the service open the files. No
 * reading at upload, no nightly run over the document store: looking once
 * when asked is something different from watching permanently (ADR-0004).
 */
export async function readDocuments(
  signal?: AbortSignal
): Promise<ApiResult<DocumentTerms[]>> {
  const answer = await request<DocumentTerms[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents/read",
    { method: "POST", signal },
    "errors.documentsNotRead"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

export type ResumeRequestState = "PENDING" | "GRANTED" | "DECLINED";

/**
 * A company's request.
 *
 * <strong>The request is not the permission.</strong> `GRANTED` means "was
 * granted once", not "holds now" — so a request has neither an active flag nor
 * a revocation timestamp. After a withdrawal it stays `GRANTED` and the read
 * still comes back empty. That is the design, not a defect.
 */
export interface ResumeRequest {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: ResumeRequestState;
  created_at: string;
  answered_at?: string | null;
  active?: boolean | null;
}

/**
 * One's own CV.
 *
 * <strong>Three outcomes, not two.</strong> `value: null` means "none written
 * yet" — that is an answer and no error, because `404` is the normal case here
 * for everyone who has just started. A transport error is something else and
 * comes back as `error`. Merging the two would mean showing somebody an empty
 * form although their CV is merely unreachable at the moment — and the next
 * click on Save would overwrite it.
 */
export async function getMyCurriculumVitae(
  signal?: AbortSignal
): Promise<ApiResult<CurriculumVitae | null>> {
  const answer = await request<CurriculumVitae>(
    RESUME_BASE_URL,
    "/resumes/me",
    { signal },
    "errors.resumeUnavailable"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

export async function saveMyCurriculumVitae(
  input: CurriculumVitaeInput,
  signal?: AbortSignal
): Promise<ApiResult<CurriculumVitae>> {
  const answer = await request<CurriculumVitae>(
    RESUME_BASE_URL,
    "/resumes/me",
    { method: "PUT", body: input, signal },
    "errors.resumeNotSaved"
  );

  return answer.ok
    ? { ok: true, value: answer.value as CurriculumVitae }
    : { ok: false, error: answer.error };
}

export async function getMyDocuments(
  signal?: AbortSignal
): Promise<ApiResult<DocumentRecordV1[]>> {
  const answer = await request<DocumentRecordV1[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents",
    { signal },
    "errors.documentsNotLoaded"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/** Upload of a document. */
export async function uploadDocument(
  file: File,
  name: string,
  kind: DocumentKind,
  signal?: AbortSignal
): Promise<ApiResult<DocumentRecordV1>> {
  const formData = new FormData();
  formData.append("file", file);
  formData.append("name", name);
  formData.append("kind", kind);

  const answer = await request<DocumentRecordV1>(
    RESUME_BASE_URL,
    "/resumes/me/documents",
    { method: "POST", body: formData, signal },
    "errors.documentNotUploaded"
  );

  if (answer.ok) return { ok: true, value: answer.value };
  const reading = interpretError<"invalid" | "too-large" | "too-many" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      413: { reason: "too-large", titleKey: "errors.fileTooLarge" },
      415: { reason: "invalid", titleKey: "errors.fileNotAccepted" },
      422: { reason: "too-many", titleKey: "errors.fileNotAccepted" },
    },
    "invalid"
  );
  return { ok: false, error: reading.error };
}

export interface DocumentContent {
  url: string;
  contentType: string;
}

/** Content of one's own document as an object URL — to display, not to download. */
export async function getOwnDocumentContent(
  documentId: string,
  signal?: AbortSignal
): Promise<ApiResult<DocumentContent>> {
  return fetchDocumentContent(`/resumes/me/documents/${documentId}/content`, signal);
}

/** Content of somebody else's document — checked against the ledger. */
export async function getVisibleDocumentContent(
  subjectId: string,
  documentId: string,
  signal?: AbortSignal
): Promise<ApiResult<DocumentContent>> {
  return fetchDocumentContent(`/resumes/${subjectId}/documents/${documentId}/content`, signal);
}

async function fetchDocumentContent(
  path: string,
  signal?: AbortSignal
): Promise<ApiResult<DocumentContent>> {
  try {
    const response = await fetch(`${RESUME_BASE_URL}${path}`, {
      credentials: "include",
      signal,
    });
    if (!response.ok) {
      return {
        ok: false,
        error: {
          status: response.status,
          title: i18n.t("errors.documentsNotLoaded"),
          detail: i18n.t("errors.documentsNotLoaded"),
        },
      };
    }
    const blob = await response.blob();
    return {
      ok: true,
      value: { url: URL.createObjectURL(blob), contentType: blob.type },
    };
  } catch {
    return {
      ok: false,
      error: {
        status: 0,
        title: i18n.t("errors.noConnectionShort"),
        detail: i18n.t("errors.serviceUnreachable"),
      },
    };
  }
}

/** A person's visible CV — 404 means hidden or non-existent. */
export async function getVisibleCurriculumVitae(
  subjectId: string,
  signal?: AbortSignal
): Promise<ApiResult<CurriculumVitae | null>> {
  const answer = await request<CurriculumVitae>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}`,
    { signal },
    "errors.resumeUnavailable"
  );
  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

export async function getVisibleDocuments(
  subjectId: string,
  signal?: AbortSignal
): Promise<ApiResult<DocumentRecordV1[]>> {
  const answer = await request<DocumentRecordV1[]>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}/documents`,
    { signal },
    "errors.documentsNotLoaded"
  );
  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/** This file is the CV. The previous CV file becomes an enclosure. */
export async function markAsCurriculumVitae(
  documentId: string,
  signal?: AbortSignal
): Promise<ApiResult<void>> {
  const answer = await request<void>(
    RESUME_BASE_URL,
    `/resumes/me/documents/${documentId}/as-cv`,
    { method: "PUT", signal },
    "errors.documentNotUploaded"
  );

  return answer.ok
    ? { ok: true, value: undefined }
    : { ok: false, error: answer.error };
}

/** Deleting a document. */
export async function deleteDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<ApiResult<void>> {
  const answer = await request<void>(
    RESUME_BASE_URL,
    `/resumes/me/documents/${documentId}`,
    { method: "DELETE", signal },
    "errors.documentNotDeleted"
  );

  return answer.ok
    ? { ok: true, value: undefined }
    : { ok: false, error: answer.error };
}

/** Sets the template. */
export async function setTemplate(
  template: CurriculumVitaeTemplate,
  signal?: AbortSignal
): Promise<ApiResult<{ template: string }>> {
  const answer = await request<{ template: string }>(
    RESUME_BASE_URL,
    "/resumes/me/template",
    { method: "PUT", body: { template }, signal },
    "errors.templateNotSet"
  );

  return answer.ok
    ? { ok: true, value: answer.value }
    : { ok: false, error: answer.error };
}

/** The requests that were made to me. */
export async function getMyResumeRequests(
  signal?: AbortSignal
): Promise<ApiResult<ResumeRequest[]>> {
  const answer = await request<ResumeRequest[]>(
    RESUME_BASE_URL,
    "/resumes/me/requests",
    { signal },
    "errors.requestsUnavailable"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/**
 * Answer a request or take a release back.
 *
 * <strong>503 is a statement of its own here</strong> and must not be passed
 * on as an ordinary error: the ledger did not answer, so NOTHING was changed.
 * Whoever turns that into "failed" leaves open whether the withdrawal perhaps
 * took effect after all.
 */
async function act(path: string, signal?: AbortSignal): Promise<ApiResult<ResumeRequest>> {
  const answer = await request<ResumeRequest>(
    RESUME_BASE_URL,
    path,
    { method: "POST", signal },
    "errors.requestNotAnswered"
  );

  if (!answer.ok && answer.error.status === 503) {
    return {
      ok: false,
      error: {
        ...answer.error,
        detail: i18n.t("errors.ledgerSilentNothingChanged"),
      },
    };
  }

  return answer.ok
    ? { ok: true, value: answer.value as ResumeRequest }
    : { ok: false, error: answer.error };
}

export const answerResumeRequest = (id: string, grant: boolean, signal?: AbortSignal) =>
  act(`/resumes/requests/${id}/${grant ? "grant" : "decline"}`, signal);

export const revokeResumeRequest = (id: string, signal?: AbortSignal) =>
  act(`/resumes/requests/${id}/revoke`, signal);
