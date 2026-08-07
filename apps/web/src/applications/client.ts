// Client für applications-service.
//
// Die Bewerbung trägt keine Profildaten — nur eine subject_id. Wer Profil,
// Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
// dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
// einen zweiten Filter, und der weicht irgendwann vom ersten ab.

import { APPLICATIONS_BASE_URL } from "../env";

export type ApplicationStatus =
  | "submitted"
  | "reviewing"
  | "rejected"
  | "withdrawn"
  | "hired";

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

export interface ApplicationInput {
  job_id: string;
  message: string;
  shares_resume: boolean;
  shares_portfolio: boolean;
}

export type ApplyResult =
  | { ok: true; application: Application }
  | {
      ok: false;
      reason: "unauthenticated" | "gone" | "already" | "unavailable" | "invalid" | "offline";
      message: string;
    };

export type ListResult = { ok: true; applications: Application[] } | { ok: false; message: string };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${APPLICATIONS_BASE_URL}${path}`, { credentials: "include", ...init });
}

async function write(path: string, init: RequestInit): Promise<ApplyResult> {
  let res: Response;
  try {
    res = await send(path, init);
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Zum Bewerben brauchst du ein Konto. Bitte melde dich an.",
    };
  }
  if (res.status === 404) {
    return {
      ok: false,
      reason: "gone",
      message: "Diese Stelle ist nicht mehr offen.",
    };
  }
  if (res.status === 409) {
    // Der Zustand passt nicht — das ist etwas anderes als ein Formularfehler,
    // und die Person soll nicht am Formular suchen.
    return {
      ok: false,
      reason: "already",
      message: await problemMessage(res, "Das geht in diesem Zustand nicht."),
    };
  }
  if (res.status === 503) {
    // Weder abgelehnt noch angenommen: wir wissen es gerade nicht, und eine
    // Absage, die niemand ausgesprochen hat, wäre die schlimmere Antwort.
    return {
      ok: false,
      reason: "unavailable",
      message: "Ein beteiligter Dienst antwortet gerade nicht. Bitte später erneut versuchen.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Die Bewerbung konnte nicht abgeschickt werden."),
    };
  }
  return { ok: true, application: (await res.json()) as Application };
}

export function apply(input: ApplicationInput): Promise<ApplyResult> {
  return write("/applications", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(input),
  });
}

export function withdrawApplication(applicationId: string): Promise<ApplyResult> {
  return write(`/applications/${applicationId}/withdraw`, { method: "POST" });
}

export function advanceApplication(
  applicationId: string,
  status: "reviewing" | "rejected" | "hired"
): Promise<ApplyResult> {
  return write(`/applications/${applicationId}/status`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ status }),
  });
}

async function list(path: string): Promise<ListResult> {
  try {
    const res = await send(path);
    if (res.status === 401 || res.status === 403) {
      // Kein Konto bzw. kein aktives Unternehmen: ein behebbarer Zustand, kein
      // Fehler, den man melden müsste.
      return { ok: true, applications: [] };
    }
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Liste ließ sich nicht laden.") };
    }
    return { ok: true, applications: (await res.json()) as Application[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

export function listMyApplications(): Promise<ListResult> {
  return list("/applications/me");
}

export function listApplicationsForJob(jobId: string): Promise<ListResult> {
  return list(`/jobs/${jobId}/applications`);
}
