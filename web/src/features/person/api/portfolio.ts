import { type ApiResult, request } from "../../../core/api/client";
import { PORTFOLIO_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";

/**
 * The work samples — a showcase with its own release.
 *
 * <strong>Separate from the profile, and that is deliberate:</strong> somebody
 * can be approachable without showing their work. Two releases, two
 * decisions.
 */
export interface PortfolioItem {
  title: string;
  summary: string;
  /** `null` means "no link", not "empty link". */
  url: string | null;
  role: string;
  year: number | null;
  /** Name of an uploaded file — assigned by the server, never chosen by oneself. */
  attachment: string | null;
  /**
   * What was used — named by the person themselves.
   *
   * Does not make the work searchable: a skill becomes searchable only in the
   * profile, and it gets there with one click.
   */
  technologies: string[];
}

export interface Portfolio {
  subject_id: string;
  items: PortfolioItem[];
  updated_at: string;
}

/**
 * One's own showcase.
 *
 * Three outcomes, as with the CV: `value: null` means "nothing entered yet" (a
 * `404`, the normal case), a transport error is something else. Merged, the
 * page showed an empty list although the work was only unreachable at the
 * moment — and the next save deleted it.
 */
export async function getMyPortfolio(
  signal?: AbortSignal
): Promise<ApiResult<Portfolio | null>> {
  const answer = await request<Portfolio>(
    PORTFOLIO_BASE_URL,
    "/portfolios/me",
    { signal },
    "errors.portfolioUnavailable"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

/**
 * Saves the WHOLE field.
 *
 * The service knows no partial update: an item has no id of its own, its
 * address is its position in the list. Whoever changes one sends all of them.
 */
export async function saveMyPortfolio(
  items: PortfolioItem[],
  signal?: AbortSignal
): Promise<ApiResult<Portfolio>> {
  const answer = await request<Portfolio>(
    PORTFOLIO_BASE_URL,
    "/portfolios/me",
    { method: "PUT", body: { items }, signal },
    "errors.portfolioNotSaved"
  );

  return answer.ok
    ? { ok: true, value: answer.value as Portfolio }
    : { ok: false, error: answer.error };
}

export interface Attachment {
  name: string;
  content_type: string;
  size: number;
}

/**
 * Attach a file.
 *
 * <strong>The local file name does not travel along.</strong> The server
 * assigns the name; the UI therefore shows only <em>that</em> a file is
 * attached and where it points. A displayed local name would be a claim about
 * something the server does not even know.
 *
 * Its own call instead of <c>request</c>: a <c>FormData</c> goes over the wire
 * here, not JSON, and the browser must set the boundary itself.
 */
export async function uploadAttachment(
  file: File,
  signal?: AbortSignal
): Promise<ApiResult<Attachment>> {
  const body = new FormData();
  body.append("file", file);

  let answer: Response;
  try {
    answer = await fetch(`${PORTFOLIO_BASE_URL}/portfolios/me/attachments`, {
      method: "POST",
      credentials: "include",
      body: body,
      signal,
    });
  } catch {
    return {
      ok: false,
      error: {
        status: 0,
        title: i18n.t("errors.noConnectionShort"),
        detail: i18n.t("errors.noConnection"),
      },
    };
  }

  if (!answer.ok) {
    return {
      ok: false,
      error: {
        status: answer.status,
        title: i18n.t("errors.fileRejected"),
        detail: i18n.t("errors.fileNotAccepted"),
      },
    };
  }

  return { ok: true, value: (await answer.json()) as Attachment };
}

/** Where a file lives. Composed as on the server: person and name. */
export const attachmentUrl = (subjectId: string, name: string): string =>
  `${PORTFOLIO_BASE_URL}/portfolios/${subjectId}/attachments/${name}`;
