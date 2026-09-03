// Client für jobs-service.
//
// Anders als die meisten: Suche und Einzelansicht brauchen KEINE Anmeldung.
// Eine Ausschreibung, die man nur angemeldet sieht, ist keine Ausschreibung.
// `credentials: "include"` schadet dabei nicht — ist ein Cookie da, wird es
// mitgeschickt, ist keines da, antwortet der Server trotzdem. Deshalb geht
// auch diese Seite über `request()` und nicht an ihm vorbei.

import { request } from "../../../core/api/client";
import { JOBS_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import {
  type Deutung,
  type Fehlschlag,
  deuten,
} from "../../../shared/api/fehler";

export type RemoteMode = "none" | "hybrid" | "full";
export type EmploymentType = "full_time" | "part_time" | "contract" | "internship";
export type JobStatus = "draft" | "published" | "closed";

/**
 * Der Draht ist snake_case, und diese Typen bilden ihn ab, statt ihn zu
 * übersetzen. Eine camelCase-Fassung dazwischen war schon einmal ein schwerer
 * Fehler: die Felder kamen beim Server nicht an, und niemand sah es, weil das
 * Formular danach trotzdem grün meldete.
 */
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
}

export type SucheFehler = "offline" | "fehlgeschlagen";
export type JobFehler = "not-found" | "no-company" | "conflict" | "invalid" | "offline";

export type SucheFehlschlag = Fehlschlag<SucheFehler>;

export type SucheErgebnis =
  | { ok: true; items: Job[]; nextCursor: string | null }
  | SucheFehlschlag;

export type JobErgebnis = { ok: true; job: Job } | Fehlschlag<JobFehler>;

export type EigeneJobsErgebnis = { ok: true; jobs: Job[] } | Fehlschlag<SucheFehler>;

/**
 * Werte aus dem Vertrag sind keine Sätze für Menschen.
 *
 * FUNKTIONEN und keine Tabellen: eine Konstante entstünde beim Laden des
 * Moduls und trüge dann für immer die Sprache, die in diesem Augenblick galt.
 */
/** Die erlaubten Werte, in Anzeigereihenfolge — für Auswahlfelder. */
export const REMOTE_MODES: RemoteMode[] = ["none", "hybrid", "full"];

export const EMPLOYMENT_TYPES: EmploymentType[] = [
  "full_time",
  "part_time",
  "contract",
  "internship",
];

export function remoteLabel(wert: RemoteMode): string {
  const schluessel = { none: "None", hybrid: "Hybrid", full: "Full" } as const;
  return i18n.t(`stelle.remote${schluessel[wert]}`);
}

export function employmentLabel(wert: EmploymentType): string {
  const schluessel = {
    full_time: "FullTime",
    part_time: "PartTime",
    contract: "Contract",
    internship: "Internship",
  } as const;
  return i18n.t(`stelle.employment${schluessel[wert]}`);
}

export function suchAnfrage(filters: SearchFilters, cursor?: string): string {
  const params = new URLSearchParams();
  // Leere Filter gar nicht erst senden: `remote=` würde der Server als Filter
  // auf einen leeren Wert lesen und nichts finden.
  if (filters.q !== undefined && filters.q !== "") params.set("q", filters.q);
  if (filters.location !== undefined && filters.location !== "")
    params.set("location", filters.location);
  if (filters.remote !== undefined && filters.remote !== "") params.set("remote", filters.remote);
  if (filters.employment !== undefined && filters.employment !== "")
    params.set("employment", filters.employment);
  if (filters.company !== undefined && filters.company !== "")
    params.set("company", filters.company);
  if (filters.limit !== undefined) params.set("limit", String(filters.limit));
  if (cursor !== undefined && cursor !== "") params.set("cursor", cursor);
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}

export async function searchJobs(
  filters: SearchFilters = {},
  cursor?: string,
  signal?: AbortSignal
): Promise<SucheErgebnis> {
  const antwort = await request<{ items?: Job[]; next_cursor?: string | null }>(
    JOBS_BASE_URL,
    `/jobs${suchAnfrage(filters, cursor)}`,
    { signal },
    "fehler.sucheFehlgeschlagen"
  );
  if (!antwort.ok) {
    return deuten<SucheFehler>(
      antwort.error,
      { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
      "fehlgeschlagen"
    );
  }
  return {
    ok: true,
    items: antwort.value?.items ?? [],
    nextCursor: antwort.value?.next_cursor ?? null,
  };
}

/**
 * Eine einzelne Stelle, oder `null`.
 *
 * `null` deckt „zurückgezogen" und „gab es nie" gemeinsam ab — welcher der
 * beiden Fälle vorliegt, ist eine Aussage über das Unternehmen, die niemand von
 * uns erwarten kann.
 */
export async function getJob(jobId: string, signal?: AbortSignal): Promise<Job | null> {
  const antwort = await request<Job>(JOBS_BASE_URL, `/jobs/${jobId}`, { signal });
  return antwort.ok ? (antwort.value ?? null) : null;
}

export async function listOwnJobs(signal?: AbortSignal): Promise<EigeneJobsErgebnis> {
  const antwort = await request<Job[]>(
    JOBS_BASE_URL,
    "/companies/me/jobs",
    { signal },
    "fehler.listeNichtGeladen"
  );
  if (antwort.ok) return { ok: true, jobs: antwort.value ?? [] };
  // Kein aktives Unternehmen ist ein behebbarer Zustand, kein Fehler. Die Seite
  // fragt ohne Unternehmen ohnehin nicht, aber die Antwort bleibt dieselbe wie
  // zuvor: eine leere Liste, keine Meldung.
  if (antwort.error.status === 403) return { ok: true, jobs: [] };
  return deuten<SucheFehler>(
    antwort.error,
    { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
    "fehlgeschlagen"
  );
}

const SCHREIBFEHLER: Partial<Record<number, Deutung<JobFehler>>> =
  {
    0: { reason: "offline", titel: "fehler.keineVerbindung" },
    403: {
      reason: "no-company",
      titel: "fehler.nurFirmaAusschreiben",
      text: "fehler.firmaWaehlen",
    },
    404: { reason: "not-found", titel: "fehler.ausschreibungFehlt" },
  };

async function schreiben(
  path: string,
  method: "POST" | "PUT",
  body?: unknown
): Promise<JobErgebnis> {
  const antwort = await request<Job>(
    JOBS_BASE_URL,
    path,
    { method, body },
    "fehler.ausschreibungNichtGespeichert"
  );
  if (antwort.ok) return { ok: true, job: antwort.value };
  // `409` bleibt beim Satz des Servers: die Eingabe ist in Ordnung, der Zustand
  // passt nicht — das ist etwas anderes als ein Formularfehler, und der Dienst
  // weiß besser, was gerade nicht geht.
  if (antwort.error.status === 409) {
    return { ok: false, reason: "conflict", error: antwort.error };
  }
  return deuten<JobFehler>(antwort.error, SCHREIBFEHLER, "invalid");
}

// Kein `tenant_id` im Rumpf: das Unternehmen steht im Token und wird gegen die
// Mitgliedschaft geprüft. Was der Client nicht senden kann, kann er nicht
// fälschen.
export function createJob(input: JobInput): Promise<JobErgebnis> {
  return schreiben("/jobs", "POST", input);
}

export function updateJob(jobId: string, input: JobInput): Promise<JobErgebnis> {
  return schreiben(`/jobs/${jobId}`, "PUT", input);
}

export function publishJob(jobId: string): Promise<JobErgebnis> {
  return schreiben(`/jobs/${jobId}/publish`, "POST");
}

export function closeJob(jobId: string): Promise<JobErgebnis> {
  return schreiben(`/jobs/${jobId}/close`, "POST");
}

/**
 * Die eigene Anzeige verständlicher formulieren lassen (ADR-0024).
 *
 * Der Unternehmens-Agent — und er sagt über niemanden etwas. Er arbeitet an
 * einem Text, den das Unternehmen selbst verfasst hat. Nichts wird gespeichert:
 * weder die Bitte noch die Antwort.
 */
export type EntwurfErgebnis = { ok: true; draft: string } | Fehlschlag<"no-company" | "unavailable">;

export async function draftJobText(input: {
  title: string;
  description: string;
  location: string;
  skills: string[];
  wish: string;
}): Promise<EntwurfErgebnis> {
  const antwort = await request<{ draft: string }>(
    JOBS_BASE_URL,
    "/jobs/draft",
    { method: "POST", body: input },
    "fehler.entwurfNichtVerfuegbarLang"
  );
  if (antwort.ok) return { ok: true, draft: antwort.value?.draft ?? "" };
  return deuten<"no-company" | "unavailable">(
    antwort.error,
    {
      403: { reason: "no-company", titel: "fehler.fuerFirmaHandelnNoetig" },
    },
    "unavailable"
  );
}
