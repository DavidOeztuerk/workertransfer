// Mannschaft eines Unternehmens: Mitglieder, Einladungen, Beitritt.
//
// Der Einladungs-Token taucht hier nur an einer Stelle auf — beim Annehmen,
// und dort kommt er aus der URL, in die ihn die Mail geschrieben hat. Die
// Oberfläche kennt ihn sonst nirgends: er steht weder in der Antwort aufs
// Einladen noch in der Liste der offenen Einladungen.

import { API_BASE_URL } from "../env";

export type Role = "admin" | "member";

export interface CompanyMember {
  user_id: string;
  display_name: string;
  role: Role;
}

export interface Invitation {
  id: string;
  email: string;
  role: Role;
  status: string;
  created_at: string;
  expires_at: string;
}

export interface Membership {
  id: string;
  name: string;
  domain: string;
  role: Role;
}

export type MemberListResult = { ok: true; members: CompanyMember[] } | { ok: false; message: string };
export type InvitationListResult =
  | { ok: true; invitations: Invitation[] }
  | { ok: false; message: string };
export type InviteResult =
  | { ok: true; invitation: Invitation }
  | { ok: false; reason: "not-admin" | "not-yours" | "invalid" | "offline"; message: string };
export type AcceptResult =
  | { ok: true; membership: Membership }
  | {
      ok: false;
      reason: "unauthenticated" | "refused" | "invalid" | "offline";
      message: string;
    };

async function problemMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body: unknown = await res.json();
    if (typeof body === "object" && body !== null) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === "string") return detail;
      const title = (body as { title?: unknown }).title;
      if (typeof title === "string") return title;
    }
  } catch {
    // Kein verwertbarer Körper — der Fallback sagt trotzdem etwas Wahres.
  }
  return fallback;
}

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${API_BASE_URL}${path}`, { credentials: "include", ...init });
}

/**
 * Die Mannschaft. Ein `404` heißt „nicht deins", nicht „kaputt".
 *
 * Der Server antwortet absichtlich `404` statt `403`, damit niemand erfragen
 * kann, welche Unternehmen es gibt. Die Oberfläche macht daraus einen ruhigen
 * leeren Zustand statt einer Fehlermeldung, die nichts erklärt.
 */
export async function listMembers(tenantId: string): Promise<MemberListResult> {
  try {
    const res = await send(`/companies/${tenantId}/members`);
    if (res.status === 404) return { ok: true, members: [] };
    if (!res.ok) {
      return { ok: false, message: await problemMessage(res, "Die Mannschaft ließ sich nicht laden.") };
    }
    return { ok: true, members: (await res.json()) as CompanyMember[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

export async function listInvitations(tenantId: string): Promise<InvitationListResult> {
  try {
    const res = await send(`/companies/${tenantId}/invitations`);
    if (res.status === 404) return { ok: true, invitations: [] };
    if (!res.ok) {
      return {
        ok: false,
        message: await problemMessage(res, "Die Einladungen ließen sich nicht laden."),
      };
    }
    return { ok: true, invitations: (await res.json()) as Invitation[] };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

export async function inviteMember(
  tenantId: string,
  email: string,
  role: Role
): Promise<InviteResult> {
  let res: Response;
  try {
    res = await send(`/companies/${tenantId}/invitations`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      // Kein Unternehmen im Körper: es steht im Pfad und wird gegen die
      // Mitgliedschaft des Aufrufers geprüft.
      body: JSON.stringify({ email, role }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 403) {
    return {
      ok: false,
      reason: "not-admin",
      message: "Einladen darf nur, wer Administrator dieses Unternehmens ist.",
    };
  }
  if (res.status === 404) {
    return {
      ok: false,
      reason: "not-yours",
      message: "Für dieses Unternehmen kannst du nicht einladen.",
    };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: await problemMessage(res, "Die Einladung ließ sich nicht anlegen."),
    };
  }
  return { ok: true, invitation: (await res.json()) as Invitation };
}

export async function withdrawInvitation(
  tenantId: string,
  invitationId: string
): Promise<{ ok: true } | { ok: false; message: string }> {
  try {
    const res = await send(`/companies/${tenantId}/invitations/${invitationId}`, {
      method: "DELETE",
    });
    // 204: kein Körper. Ihn zu parsen wäre ein Fehler, der wie ein Serverfehler
    // aussähe.
    if (res.status === 204) return { ok: true };
    return { ok: false, message: await problemMessage(res, "Die Einladung ließ sich nicht zurückziehen.") };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}

/**
 * Eine Einladung annehmen.
 *
 * Gesendet wird nur das Token. Die Adresse, gegen die es geprüft wird, holt der
 * Server aus der Datenbank — würde sie hier mitgeschickt, könnte man sich die
 * passende einfach aussuchen.
 */
export async function acceptInvitation(token: string): Promise<AcceptResult> {
  let res: Response;
  try {
    res = await send("/invitations/accept", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ token }),
    });
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return {
      ok: false,
      reason: "unauthenticated",
      message: "Bitte melde dich mit der eingeladenen Adresse an und öffne den Link erneut.",
    };
  }
  if (res.status === 400) {
    // Abgelaufen oder für jemand anderen ausgestellt — beides ist behebbar
    // (neu einladen lassen, richtiges Konto benutzen), und der Server sagt was.
    return { ok: false, reason: "refused", message: await problemMessage(res, "Abgelehnt.") };
  }
  if (!res.ok) {
    return {
      ok: false,
      reason: "invalid",
      message: "Diese Einladung ist nicht (mehr) gültig. Bitte lass dir eine neue schicken.",
    };
  }
  return { ok: true, membership: (await res.json()) as Membership };
}
