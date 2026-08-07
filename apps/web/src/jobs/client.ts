// Client für jobs-service.
//
// Anders als alle bisherigen: die Suche und die Einzelansicht brauchen KEINE
// Anmeldung. Eine Ausschreibung, die man nur angemeldet sieht, ist keine
// Ausschreibung. `credentials: "include"` schadet dabei nicht — ist ein Cookie
// da, wird es mitgeschickt, und ist keines da, antwortet der Server trotzdem.

import { JOBS_BASE_URL } from "../env";

export type RemoteMode = "none" | "hybrid" | "full";
export type EmploymentType = "full_time" | "part_time" | "contract" | "internship";
export type JobStatus = "draft" | "published" | "closed";

export interface Job {
  id: string;
  tenant_id: string;
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  /** Was die Stelle verlangt — die Liste, gegen die im Browser abgeglichen wird. */
  skills: string[];
  status: JobStatus;
  published_at: string | null;
  updated_at: string;
}

export interface JobInput {
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string[];
}

export interface SearchFilters {
  q?: string;
  /** Nur die Stellen eines Unternehmens — für die Karriere-Seite. */
  company?: string;
  /** Seitengröße; der Server deckelt sie ohnehin bei 50. */
  limit?: number;
  location?: string;
  remote?: RemoteMode | "";
  employment?: EmploymentType | "";
  cursor?: string;
}

export type SearchResult =
  | { ok: true; items: Job[]; nextCursor: string | null }
  | { ok: false; message: string };

export type JobResult =
  | { ok: true; job: Job }
  | { ok: false; reason: "not-found" | "no-company" | "conflict" | "invalid" | "offline"; message: string };

export type OwnJobsResult = { ok: true; jobs: Job[] } | { ok: false; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      if (Array.isArray(detail)) {
        const first: unknown = detail[0];
        if (typeof first === "object" && first !== null) {
          const msg = (first as { msg?: unknown }).msg;
          if (typeof msg === "string") return msg;
        }
      }
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${JOBS_BASE_URL}${path}`, { credentials: "include", ...init });
}

async function write(path: string, init: RequestInit): Promise<JobResult> {
  let res: Response;
  try {
    res = await send(path, init);
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Ausschreiben kann nur, wer für ein Unternehmen handelt. Wechsle oben darauf.",
    };
  }
  if (res.status === 404) {
    return { ok: false, reason: "not-found", message: "Diese Ausschreibung gibt es nicht." };
  }
  if (res.status === 409) {
    // Die Eingabe ist in Ordnung, der Zustand passt nicht — das ist etwas
    // anderes als ein Formularfehler, und die Seite sagt es anders.
    return {
      ok: false,
      reason: "conflict",
      message: await problemMessage(res, "Das geht in diesem Zustand nicht."),
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Die Ausschreibung konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, job: (await res.json()) as Job };
}

export async function searchJobs(filters: SearchFilters = {}): Promise<SearchResult> {
  const params = new URLSearchParams();
  // Leere Filter gar nicht erst senden: `remote=` würde der Server als Filter
  // auf einen leeren Wert lesen und nichts finden.
  if (filters.q) params.set("q", filters.q);
  if (filters.location) params.set("location", filters.location);
  if (filters.remote) params.set("remote", filters.remote);
  if (filters.employment) params.set("employment", filters.employment);
  if (filters.company) params.set("company", filters.company);
  if (filters.limit !== undefined) params.set("limit", String(filters.limit));
  if (filters.cursor) params.set("cursor", filters.cursor);
  const query = params.toString();
  try {
    const res = await send(`/jobs${query === "" ? "" : `?${query}`}`);
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Suche ist fehlgeschlagen.") };
    }
    const body = (await res.json()) as { items?: Job[]; next_cursor?: string | null };
    return { ok: true, items: body.items ?? [], nextCursor: body.next_cursor ?? null };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

export async function getJob(jobId: string): Promise<Job | null> {
  try {
    const res = await send(`/jobs/${jobId}`);
    if (!res.ok) return null;
    return (await res.json()) as Job;
  } catch {
    return null;
  }
}

export async function listOwnJobs(): Promise<OwnJobsResult> {
  try {
    const res = await send("/companies/me/jobs");
    if (res.status === 403) {
      // Kein aktives Unternehmen ist ein behebbarer Zustand, kein Fehler.
      return { ok: true, jobs: [] };
    }
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Liste ließ sich nicht laden.") };
    }
    return { ok: true, jobs: (await res.json()) as Job[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

const asJson = (input: JobInput): RequestInit => ({
  headers: { "content-type": "application/json" },
  // Kein tenant_id: das Unternehmen steht im Token und wird gegen die
  // Mitgliedschaft geprüft. Was der Client nicht senden kann, kann er nicht
  // fälschen.
  body: JSON.stringify(input),
});

export function createJob(input: JobInput): Promise<JobResult> {
  return write("/jobs", { method: "POST", ...asJson(input) });
}

export function updateJob(jobId: string, input: JobInput): Promise<JobResult> {
  return write(`/jobs/${jobId}`, { method: "PUT", ...asJson(input) });
}

export function publishJob(jobId: string): Promise<JobResult> {
  return write(`/jobs/${jobId}/publish`, { method: "POST" });
}

export function closeJob(jobId: string): Promise<JobResult> {
  return write(`/jobs/${jobId}/close`, { method: "POST" });
}

/**
 * Die eigene Anzeige verständlicher formulieren lassen.
 *
 * Der Unternehmens-Agent — und er sagt über niemanden etwas. Er arbeitet an
 * einem Text, den das Unternehmen selbst verfasst hat.
 */
export type JobDraftResult =
  | { ok: true; draft: string }
  | { ok: false; message: string };

export async function draftJobText(input: {
  title: string;
  description: string;
  location: string;
  skills: string[];
  wish: string;
}): Promise<JobDraftResult> {
  try {
    const res = await send("/jobs/draft", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(input),
    });
    if (res.status === 403) {
      return { ok: false, message: "Dafür musst du für ein Unternehmen handeln." };
    }
    if (!res.ok) {
      return {
        ok: false,
        message: "Die Formulierungshilfe ist gerade nicht verfügbar. Dein Text bleibt unverändert.",
      };
    }
    return { ok: true, draft: ((await res.json()) as { draft: string }).draft };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
