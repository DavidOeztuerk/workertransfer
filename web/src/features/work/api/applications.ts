// Client für applications-service.
//
// Die Bewerbung trägt keine Profildaten — nur eine `subject_id`. Wer Profil,
// Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
// dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
// einen zweiten Filter, und der weicht irgendwann vom ersten ab.

import { request } from "../../../core/api/client";
import { APPLICATIONS_BASE_URL } from "../../../env";
import {
  type Deutung,
  type Fehlschlag,
  deuten,
} from "../../../shared/api/fehler";

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
  Record<number, Deutung<BewerbungFehler>>
> = {
  0: { reason: "offline", titel: "fehler.keineVerbindung" },
  401: {
    reason: "unauthenticated",
    titel: "fehler.kontoZumBewerben",
    text: "fehler.anmelden",
  },
  404: { reason: "gone", titel: "fehler.stelleGeschlossen" },
  // Weder abgelehnt noch angenommen: wir wissen es gerade nicht, und eine
  // Absage, die niemand ausgesprochen hat, wäre die schlimmere Antwort.
  503: {
    reason: "unavailable",
    titel: "fehler.dienstSchweigt",
    text: "fehler.spaeterErneut",
  },
};

async function schreiben(path: string, body?: unknown): Promise<BewerbungErgebnis> {
  const answer = await request<Application>(
    APPLICATIONS_BASE_URL,
    path,
    { method: "POST", body },
    "fehler.bewerbungNichtGesendet"
  );
  if (answer.ok) return { ok: true, application: answer.value };
  // `409` behält den Satz des Servers: der Zustand passt nicht, und die Person
  // soll nicht am Formular nach dem Fehler suchen.
  if (answer.error.status === 409) {
    return { ok: false, reason: "already", error: answer.error };
  }
  return deuten<BewerbungFehler>(answer.error, SCHREIBFEHLER, "invalid");
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

async function list(path: string, signal?: AbortSignal): Promise<ListenErgebnis> {
  const answer = await request<Application[]>(
    APPLICATIONS_BASE_URL,
    path,
    { signal },
    "fehler.listeNichtGeladen"
  );
  if (answer.ok) return { ok: true, applications: answer.value ?? [] };
  // Kein Konto bzw. kein aktives Unternehmen: ein behebbarer Zustand, kein
  // Fehler, den man melden müsste.
  if (answer.error.status === 401 || answer.error.status === 403) {
    return { ok: true, applications: [] };
  }
  return deuten<"fehlgeschlagen" | "offline">(
    answer.error,
    { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
    "fehlgeschlagen"
  );
}

export function listMyApplications(signal?: AbortSignal): Promise<ListenErgebnis> {
  return list("/applications/me", signal);
}

export function listApplicationsForJob(
  jobId: string,
  signal?: AbortSignal
): Promise<ListenErgebnis> {
  return list(`/jobs/${jobId}/applications`, signal);
}
