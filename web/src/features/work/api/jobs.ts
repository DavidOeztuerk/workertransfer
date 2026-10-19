// Client for jobs-service.
//
// Unlike most: search and detail view need NO sign-in. An advert one can only
// see signed in is no advert. `credentials: "include"` does no harm here — if a
// cookie is there it is sent along, if there is none the server answers
// anyway. That is why this page too goes through `request()` and not around it.

import { request } from "../../../core/api/client";
import { JOBS_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import {
  type ErrorReading,
  type Failure,
  interpretError,
} from "../../../shared/api/errors";

export type RemoteMode = "none" | "hybrid" | "full";
export type EmploymentType = "full_time" | "part_time" | "contract" | "internship";
export type JobStatus = "draft" | "published" | "closed";

/**
 * The wire is snake_case, and these types mirror it instead of translating it.
 * A camelCase version in between was once a serious defect: the fields never
 * reached the server, and nobody saw it, because the form reported green
 * afterwards anyway.
 */
export interface Job {
  id: string;
  tenant_id: string;
  title: string;
  description: string;
  location: string;
  /**
   * The postal code. Empty when none was given.
   *
   * A field of its own and not part of `location`: the place name may be
   * ambiguous ("Neustadt" exists twenty times), the postal code is not — and
   * only it makes the radius search around a small place reliable (ADR-0032).
   */
  postal_code: string;
  remote: RemoteMode;
  employment: EmploymentType;
  /** What the job asks for — the list the browser compares against. */
  skills: string[];
  status: JobStatus;
  published_at: string | null;
  updated_at: string;
}

/**
 * What an advert looks like on the WIRE.
 *
 * <strong>Not the same as <c>Job</c>, and exactly that once broke it.</strong>
 * The service writes <c>remote_mode</c> and <c>employment_type</c>; the type
 * above is called <c>remote</c> and <c>employment</c>. As long as the answer
 * was cast straight to <c>Job</c>, both fields were silently
 * <c>undefined</c> — TypeScript does not look inside a JSON answer, and a cast
 * is a claim, not a check.
 *
 * It only became visible when the lookup table turned into a function:
 * `REMOTE_LABEL[undefined]` gives `undefined` and draws nothing,
 * `remoteLabel(undefined)` gives `job.remoteundefined` and stands on the card.
 * The same class of defect as with the four notification hops — a name that is
 * spelled differently on the two sides, and nobody notices.
 */
interface JobWire {
  id: string;
  tenant_id: string;
  title: string;
  description: string;
  location: string;
  postal_code: string;
  remote_mode: RemoteMode;
  employment_type: EmploymentType;
  skills: string[];
  status: JobStatus;
  published_at: string | null;
  updated_at: string;
}

/** From the wire into the shape the UI works with. */
function fromWire(wire: JobWire): Job {
  return {
    id: wire.id,
    tenant_id: wire.tenant_id,
    title: wire.title,
    description: wire.description,
    location: wire.location,
    postal_code: wire.postal_code ?? "",
    remote: wire.remote_mode,
    employment: wire.employment_type,
    skills: wire.skills ?? [],
    status: wire.status,
    published_at: wire.published_at,
    updated_at: wire.updated_at,
  };
}

export interface JobInput {
  title: string;
  description: string;
  location: string;
  postal_code: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string[];
}

export interface SearchFilters {
  q?: string;
  /** Only one company's jobs — for the career page. */
  company?: string;
  location?: string;
  remote?: RemoteMode | "";
  employment?: EmploymentType | "";
  /**
   * Skills searched for. The server expects them as a REPEATED parameter
   * (`?skill=a&skill=b`), not as a list in one — a comma-separated string
   * would be a second splitting rule beside the one that already exists.
   */
  skills?: string[];
  /**
   * "At most N kilometres from here."
   *
   * <strong>The radius goes out without coordinates too.</strong> Then
   * `location` is the centre, and the service resolves the place name with the
   * same table it uses to place the adverts — whoever types "Leipzig" and
   * chooses "50 km" needs neither GPS nor the permission for it.
   *
   * `lat` and `lon` go along only TOGETHER and are already ROUNDED (see
   * `useGeolocation`): they stand in an address line and thus in access logs.
   */
  lat?: number;
  lon?: number;
  radiusKm?: number;
}

export type SearchError = "offline" | "failed";
export type JobError = "not-found" | "no-company" | "conflict" | "invalid" | "offline";

export type SearchFailure = Failure<SearchError>;

export type SearchResult =
  | {
      ok: true;
      items: Job[];
      page: number;
      pageSize: number;
      totalItems: number;
      totalPages: number;
      /**
       * How many adverts the radius search could NOT judge because their place
       * is unknown. `null` when searched without a radius.
       *
       * The number must reach the screen. A place filter that silently leaves
       * things out delivers a result that looks complete and is not.
       */
      omitted: number | null;
    }
  | SearchFailure;

export type JobResult = { ok: true; job: Job } | Failure<JobError>;

export type OwnJobsResult = { ok: true; jobs: Job[] } | Failure<SearchError>;

/**
 * Values from the contract are no sentences for human beings.
 *
 * FUNCTIONS and not tables: a constant would be built when the module loads and
 * would then carry for ever the language that held at that moment.
 */
/** The allowed values, in display order — for select fields. */
export const REMOTE_MODES: RemoteMode[] = ["none", "hybrid", "full"];

export const EMPLOYMENT_TYPES: EmploymentType[] = [
  "full_time",
  "part_time",
  "contract",
  "internship",
];

export function remoteLabel(value: RemoteMode): string {
  const key = { none: "None", hybrid: "Hybrid", full: "Full" } as const;
  return i18n.t(`job.remote${key[value]}`);
}

export function employmentLabel(value: EmploymentType): string {
  const key = {
    full_time: "FullTime",
    part_time: "PartTime",
    contract: "Contract",
    internship: "Internship",
  } as const;
  return i18n.t(`job.employment${key[value]}`);
}

export function searchQuery(filters: SearchFilters, page = 1, pageSize?: number): string {
  const params = new URLSearchParams();
  // Do not send empty filters at all: the server would read `remote=` as a
  // filter on an empty value and find nothing.
  if (filters.q !== undefined && filters.q !== "") params.set("q", filters.q);
  if (filters.location !== undefined && filters.location !== "")
    params.set("location", filters.location);
  if (filters.remote !== undefined && filters.remote !== "") params.set("remote", filters.remote);
  if (filters.employment !== undefined && filters.employment !== "")
    params.set("employment", filters.employment);
  if (filters.company !== undefined && filters.company !== "")
    params.set("company", filters.company);
  // Repeated, one per skill — see `SearchFilters.skills`.
  for (const skill of filters.skills ?? []) params.append("skill", skill);
  // The radius alone is enough — without coordinates the place is the centre.
  if (filters.radiusKm !== undefined) {
    params.set("radius_km", String(filters.radiusKm));
  }
  // Coordinates only TOGETHER, though: half a position is none.
  if (filters.lat !== undefined && filters.lon !== undefined) {
    params.set("lat", String(filters.lat));
    params.set("lon", String(filters.lon));
  }
  if (page > 1) params.set("page", String(page));
  if (pageSize !== undefined) params.set("page_size", String(pageSize));
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}

/**
 * The nearest known place to a point.
 *
 * So that the searching person SEES from where the distance is measured — a
 * radius around an invisible point is an imposition. The point is the same
 * already rounded one that goes to the search (see `useGeolocation`), and the
 * answer comes from a table in the service, not from a third party.
 *
 * `null` means "nothing nearby" — then the place field stays empty instead of
 * claiming a place.
 */
export async function placeNear(
  lat: number,
  lon: number,
  signal?: AbortSignal
): Promise<string | null> {
  const answer = await request<{ location?: string | null }>(
    JOBS_BASE_URL,
    `/jobs/place?lat=${lat}&lon=${lon}`,
    { signal }
  );

  return answer.ok ? (answer.value?.location ?? null) : null;
}

export async function searchJobs(
  filters: SearchFilters = {},
  page = 1,
  pageSize?: number,
  signal?: AbortSignal
): Promise<SearchResult> {
  const answer = await request<{
    items?: JobWire[];
    page?: number;
    page_size?: number;
    total_items?: number;
    total_pages?: number;
    omitted?: number;
  }>(
    JOBS_BASE_URL,
    `/jobs${searchQuery(filters, page, pageSize)}`,
    { signal },
    "errors.searchFailed"
  );
  if (!answer.ok) {
    return interpretError<SearchError>(
      answer.error,
      { 0: { reason: "offline", titleKey: "errors.noConnection" } },
      "failed"
    );
  }
  return {
    ok: true,
    items: (answer.value?.items ?? []).map(fromWire),
    // The SERVER's values and not those of the request: at `page_size=100000`
    // it caps, and the bar must draw what was really delivered.
    page: answer.value?.page ?? page,
    pageSize: answer.value?.page_size ?? (pageSize ?? 12),
    totalItems: answer.value?.total_items ?? 0,
    totalPages: answer.value?.total_pages ?? 1,
    // `?? null` and not `?? 0`: the field is MISSING when searched without a
    // radius, and a 0 would claim that nothing was left out. That is a
    // different sentence from "nobody asked for that".
    omitted: answer.value?.omitted ?? null,
  };
}

/**
 * A single job, or `null`.
 *
 * `null` covers "withdrawn" and "never existed" together — which of the two
 * applies is a statement about the company that nobody can expect from us.
 */
export async function getJob(jobId: string, signal?: AbortSignal): Promise<Job | null> {
  const answer = await request<JobWire>(JOBS_BASE_URL, `/jobs/${jobId}`, { signal });
  return answer.ok && answer.value !== undefined ? fromWire(answer.value) : null;
}

export async function listOwnJobs(signal?: AbortSignal): Promise<OwnJobsResult> {
  const answer = await request<JobWire[]>(
    JOBS_BASE_URL,
    "/companies/me/jobs",
    { signal },
    "errors.listNotLoaded"
  );
  if (answer.ok) return { ok: true, jobs: (answer.value ?? []).map(fromWire) };
  // No active company is a state one can fix, not an error. The page does not
  // ask without a company anyway, but the answer stays the same as before: an
  // empty list, no message.
  if (answer.error.status === 403) return { ok: true, jobs: [] };
  return interpretError<SearchError>(
    answer.error,
    { 0: { reason: "offline", titleKey: "errors.noConnection" } },
    "failed"
  );
}

const WRITE_ERRORS: Partial<Record<number, ErrorReading<JobError>>> = {
  0: { reason: "offline", titleKey: "errors.noConnection" },
  403: {
    reason: "no-company",
    titleKey: "errors.onlyCompanyPublishes",
    detailKey: "errors.chooseCompany",
  },
  404: { reason: "not-found", titleKey: "errors.jobMissing" },
};

async function saveJob(
  path: string,
  method: "POST" | "PUT",
  body?: unknown
): Promise<JobResult> {
  const answer = await request<JobWire>(
    JOBS_BASE_URL,
    path,
    { method, body },
    "errors.jobNotSaved"
  );
  if (answer.ok) return { ok: true, job: fromWire(answer.value) };
  // `409` stays with the server's sentence: the input is fine, the state does
  // not fit — that is something other than a form error, and the service knows
  // better what is not possible right now.
  if (answer.error.status === 409) {
    return { ok: false, reason: "conflict", error: answer.error };
  }
  return interpretError<JobError>(answer.error, WRITE_ERRORS, "invalid");
}

// No `tenant_id` in the body: the company stands in the token and is checked
// against the membership. What the client cannot send, it cannot forge.
export function createJob(input: JobInput): Promise<JobResult> {
  return saveJob("/jobs", "POST", input);
}

export function updateJob(jobId: string, input: JobInput): Promise<JobResult> {
  return saveJob(`/jobs/${jobId}`, "PUT", input);
}

export function publishJob(jobId: string): Promise<JobResult> {
  return saveJob(`/jobs/${jobId}/publish`, "POST");
}

export function closeJob(jobId: string): Promise<JobResult> {
  return saveJob(`/jobs/${jobId}/close`, "POST");
}

/**
 * Have one's own advert phrased more clearly (ADR-0024).
 *
 * The company agent — and it says nothing about anybody. It works on a text the
 * company wrote itself. Nothing is stored: neither the request nor the answer.
 */
export type JobDraftResult = { ok: true; draft: string } | Failure<"no-company" | "unavailable">;

export async function draftJobText(input: {
  title: string;
  description: string;
  location: string;
  skills: string[];
  wish: string;
}): Promise<JobDraftResult> {
  const answer = await request<{ draft: string }>(
    JOBS_BASE_URL,
    "/jobs/draft",
    { method: "POST", body: input },
    "errors.draftHelpUnavailableLong"
  );
  if (answer.ok) return { ok: true, draft: answer.value?.draft ?? "" };
  return interpretError<"no-company" | "unavailable">(
    answer.error,
    {
      403: { reason: "no-company", titleKey: "errors.actForCompanyRequired" },
    },
    "unavailable"
  );
}
