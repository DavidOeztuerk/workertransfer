// Client für applications-service.
//
// Die Bewerbung trägt keine Profildaten — nur eine `subject_id`. Wer Profil,
// Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
// dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
// einen zweiten Filter, und der weicht irgendwann vom ersten ab.

import { request } from "../../../core/api/client";
import { APPLICATIONS_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "./fehler";

export type ApplicationStatus = "submitted" | "reviewing" | "rejected" | "withdrawn" | "hired";

export interface Application {
  id: string;
  job_id: string;
  tenant_id: string;
  subject_id: string;
  message: string;
  shares_resume: boolean;
  shares_portfolio: boolean;
  status: ApplicationStatus;
  created_at: string;
  updated_at: string;
}

/** Genau die vier Felder des Vertrags — und alle vier in snake_case. */
export interface ApplicationInput {
  job_id: string;
  message: string;
  shares_resume: boolean;
  shares_portfolio: boolean;
}

export type BewerbungFehler =
  | "unauthenticated"
  | "gone"
  | "already"
  | "unavailable"
  | "invalid"
  | "offline";

export type BewerbungErgebnis =
  | { ok: true; application: Application }
  | Fehlschlag<BewerbungFehler>;

export type ListenErgebnis = { ok: true; applications: Application[] } | Fehlschlag<"fehlgeschlagen" | "offline">;

const SCHREIBFEHLER: Partial<
  Record<number, { reason: BewerbungFehler; title: string; detail?: string }>
> = {
  0: { reason: "offline", title: "Keine Verbindung zum Server." },
  401: {
    reason: "unauthenticated",
    title: "Zum Bewerben brauchst du ein Konto.",
    detail: "Bitte melde dich an.",
  },
  404: { reason: "gone", title: "Diese Stelle ist nicht mehr offen." },
  // Weder abgelehnt noch angenommen: wir wissen es gerade nicht, und eine
  // Absage, die niemand ausgesprochen hat, wäre die schlimmere Antwort.
  503: {
    reason: "unavailable",
    title: "Ein beteiligter Dienst antwortet gerade nicht.",
    detail: "Bitte später erneut versuchen.",
  },
};

async function schreiben(path: string, body?: unknown): Promise<BewerbungErgebnis> {
  const antwort = await request<Application>(
    APPLICATIONS_BASE_URL,
    path,
    { method: "POST", body },
    "Die Bewerbung konnte nicht abgeschickt werden."
  );
  if (antwort.ok) return { ok: true, application: antwort.value };
  // `409` behält den Satz des Servers: der Zustand passt nicht, und die Person
  // soll nicht am Formular nach dem Fehler suchen.
  if (antwort.error.status === 409) {
    return { ok: false, reason: "already", error: antwort.error };
  }
  return deuten<BewerbungFehler>(antwort.error, SCHREIBFEHLER, "invalid");
}

export function apply(input: ApplicationInput): Promise<BewerbungErgebnis> {
  return schreiben("/applications", input);
}

export function withdrawApplication(applicationId: string): Promise<BewerbungErgebnis> {
  return schreiben(`/applications/${applicationId}/withdraw`);
}

export function advanceApplication(
  applicationId: string,
  status: "reviewing" | "rejected" | "hired"
): Promise<BewerbungErgebnis> {
  return schreiben(`/applications/${applicationId}/status`, { status });
}

async function liste(path: string, signal?: AbortSignal): Promise<ListenErgebnis> {
  const antwort = await request<Application[]>(
    APPLICATIONS_BASE_URL,
    path,
    { signal },
    "Die Liste ließ sich nicht laden."
  );
  if (antwort.ok) return { ok: true, applications: antwort.value ?? [] };
  // Kein Konto bzw. kein aktives Unternehmen: ein behebbarer Zustand, kein
  // Fehler, den man melden müsste.
  if (antwort.error.status === 401 || antwort.error.status === 403) {
    return { ok: true, applications: [] };
  }
  return deuten<"fehlgeschlagen" | "offline">(
    antwort.error,
    { 0: { reason: "offline", title: "Keine Verbindung zum Server." } },
    "fehlgeschlagen"
  );
}

export function listMyApplications(signal?: AbortSignal): Promise<ListenErgebnis> {
  return liste("/applications/me", signal);
}

export function listApplicationsForJob(
  jobId: string,
  signal?: AbortSignal
): Promise<ListenErgebnis> {
  return liste(`/jobs/${jobId}/applications`, signal);
}
