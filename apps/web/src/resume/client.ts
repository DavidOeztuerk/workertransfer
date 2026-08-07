// Client für resume-service.
//
// Anders als beim Profil gibt es hier keinen Sichtbarkeitsschalter: der
// Lebenslauf wird einem einzelnen Unternehmen auf dessen Anfrage hin
// freigegeben. Die Oberfläche baut deshalb nie einen Capability-String — sie
// nennt eine Anfrage-ID, und der Server weiß, was daraus folgt. Ein Test hält
// fest, dass "resume.visibility" in keinem Anfragekörper vorkommt.

import { RESUME_BASE_URL } from "../env";

export interface PositionInput {
  employer: string;
  title: string;
  started_on: string;
  /** `null` heißt „läuft noch" — nicht „unbekannt". */
  ended_on: string | null;
  description: string;
}

export interface EducationInput {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
}

export interface Resume {
  subject_id: string;
  positions: PositionInput[];
  education: EducationInput[];
  updated_at: string;
}

export interface ResumeInput {
  positions: PositionInput[];
  education: EducationInput[];
}

export type RequestStatus = "PENDING" | "GRANTED" | "DECLINED";

export interface ResumeRequest {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: RequestStatus;
  created_at: string;
  answered_at?: string | null;
  /**
   * Was gerade gilt — frisch aus dem Ledger, kann von `status` abweichen.
   * Nach einem Widerruf bleibt `GRANTED` stehen und `active` fällt auf `false`.
   */
  active?: boolean | null;
}

export type SaveResult =
  | { ok: true; resume: Resume }
  | { ok: false; reason: "unauthenticated" | "invalid" | "offline"; message: string };

export type RequestResult =
  | { ok: true; request: ResumeRequest }
  | {
      ok: false;
      reason: "already-asked" | "no-company" | "not-available" | "unavailable" | "offline";
      message: string;
    };

export type RequestListResult =
  | { ok: true; requests: ResumeRequest[] }
  | { ok: false; message: string };

export type ResumeResult = { ok: true; resume: Resume | null } | { ok: false; message: string };

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
      const title = (body as { title?: unknown }).title;
      if (typeof title === "string") return title;
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${RESUME_BASE_URL}${path}`, { credentials: "include", ...init });
}

export async function getMyResume(): Promise<Resume | null> {
  try {
    const res = await send("/resumes/me");
    if (!res.ok) return null;
    const body: unknown = await res.json();
    return body === null ? null : (body as Resume);
  } catch {
    return null;
  }
}

export async function saveMyResume(input: ResumeInput): Promise<SaveResult> {
  let res: Response;
  try {
    res = await send("/resumes/me", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      // Genau die zwei Felder des Vertrags.
      body: JSON.stringify({ positions: input.positions, education: input.education }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Der Lebenslauf konnte nicht gespeichert werden."),
    };
  }
  return { ok: true, resume: (await res.json()) as Resume };
}

/**
 * Ein Unternehmen fragt nach einem Lebenslauf.
 *
 * Die Gründe sind unterscheidbar, weil die Oberfläche auf jeden anders
 * antwortet: „schon gefragt" ist endgültig, „kein Unternehmen aktiv" ist
 * behebbar, „nicht verfügbar" sagt bewusst nicht, ob es die Person gibt.
 */
export async function requestResume(subjectId: string): Promise<RequestResult> {
  let res: Response;
  try {
    res = await send(`/resumes/${subjectId}/requests`, { method: "POST" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 409) {
    return {
      ok: false,
      reason: "already-asked",
      message: "Ihr habt diese Person bereits gefragt.",
    };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "no-company",
      message: "Lebensläufe fragen nur Unternehmen an. Wechsle oben auf ein Unternehmen.",
    };
  }
  if (res.status === 404) {
    return {
      ok: false,
      reason: "not-available",
      message: "Diese Person ist gerade nicht anfragbar.",
    };
  }
  if (res.status === 503) {
    return {
      ok: false,
      reason: "unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht. Bitte später erneut versuchen.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "offline",
      message: await problemMessage(res, "Die Anfrage konnte nicht gestellt werden."),
    };
  }
  return { ok: true, request: (await res.json()) as ResumeRequest };
}

async function post(path: string): Promise<RequestResult> {
  let res: Response;
  try {
    res = await send(path, { method: "POST" });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 503) {
    return {
      ok: false,
      reason: "unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht — es wurde nichts geändert.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "offline",
      message: await problemMessage(res, "Die Anfrage konnte nicht beantwortet werden."),
    };
  }
  return { ok: true, request: (await res.json()) as ResumeRequest };
}

export function answerRequest(requestId: string, grant: boolean): Promise<RequestResult> {
  // Zwei Pfade statt eines Endpunkts mit Flag: erteilen und ablehnen sind
  // verschiedene Handlungen, und im Protokoll des Ledgers sollen sie das auch
  // bleiben.
  return post(`/resumes/requests/${requestId}/${grant ? "grant" : "decline"}`);
}

export function revokeAccess(requestId: string): Promise<RequestResult> {
  return post(`/resumes/requests/${requestId}/revoke`);
}

export async function listMyRequests(): Promise<RequestListResult> {
  return listRequests("/resumes/me/requests");
}

export async function listCompanyRequests(): Promise<RequestListResult> {
  return listRequests("/resumes/requests");
}

async function listRequests(path: string): Promise<RequestListResult> {
  try {
    const res = await send(path);
    if (res.status === 503) {
      // Nicht als leere Liste zeigen: das wäre die Behauptung, niemand habe
      // gefragt oder freigegeben — und das weiß gerade niemand.
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Liste ließ sich nicht laden.") };
    }
    return { ok: true, requests: (await res.json()) as ResumeRequest[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

/**
 * Der Lebenslauf einer anderen Person.
 *
 * `404` wird zu `resume: null`, weil „gibt es nicht" und „nicht freigegeben"
 * für die Oberfläche derselbe Fall sind — genau so, wie der Server sie
 * ununterscheidbar hält. `503` bleibt ein eigener Zustand: nichts zeigen ist
 * etwas anderes als „hat keinen".
 */
export async function getResume(subjectId: string): Promise<ResumeResult> {
  try {
    const res = await send(`/resumes/${subjectId}`);
    if (res.status === 404) return { ok: true, resume: null };
    if (res.status === 503) {
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) {
      return {
        ok: false,
        message: await problemMessage(res, "Der Lebenslauf ließ sich nicht laden."),
      };
    }
    return { ok: true, resume: (await res.json()) as Resume };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
