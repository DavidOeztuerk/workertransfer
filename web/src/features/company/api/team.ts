// Mannschaft eines Unternehmens: Mitglieder und Einladungen.
//
// Der Einladungs-Token taucht hier nirgends auf. Er steht weder in der Antwort
// aufs Einladen noch in der Liste der offenen Einladungen — angenommen wird
// eine Einladung in `features/auth`, und dort kommt er aus der URL, in die ihn
// die Mail geschrieben hat.

import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";

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

export type MitgliederErgebnis =
  | { ok: true; members: CompanyMember[] }
  | Failure<"fehlgeschlagen">;
export type EinladungenErgebnis =
  | { ok: true; invitations: Invitation[] }
  | Failure<"fehlgeschlagen">;
export type EinladenErgebnis =
  | { ok: true; invitation: Invitation }
  | Failure<"not-admin" | "not-yours" | "invalid" | "offline">;
export type EntfernenErgebnis =
  | { ok: true }
  | Failure<"last-admin" | "not-admin" | "offline">;

/**
 * Die Mannschaft. Ein `404` heißt „nicht deins", nicht „kaputt".
 *
 * Der Server antwortet absichtlich `404` statt `403`, damit niemand erfragen
 * kann, welche Unternehmen es gibt. Die Oberfläche macht daraus einen ruhigen
 * leeren Zustand statt einer Fehlermeldung, die nichts erklärt.
 */
export async function listMembers(
  tenantId: string,
  signal?: AbortSignal
): Promise<MitgliederErgebnis> {
  const answer = await request<CompanyMember[]>(
    API_BASE_URL,
    `/companies/${tenantId}/members`,
    { signal },
    "errors.teamNotLoaded"
  );
  if (answer.ok) return { ok: true, members: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, members: [] };
  return interpretError<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titleKey: "errors.noConnection" } },
    "fehlgeschlagen"
  );
}

export async function listInvitations(
  tenantId: string,
  signal?: AbortSignal
): Promise<EinladungenErgebnis> {
  const answer = await request<Invitation[]>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations`,
    { signal },
    "errors.invitationsNotLoaded"
  );
  if (answer.ok) return { ok: true, invitations: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, invitations: [] };
  return interpretError<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titleKey: "errors.noConnection" } },
    "fehlgeschlagen"
  );
}

export async function inviteMember(
  tenantId: string,
  email: string,
  role: Role
): Promise<EinladenErgebnis> {
  const answer = await request<Invitation>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations`,
    // Kein Unternehmen im Rumpf: es steht im Pfad und wird gegen die
    // Mitgliedschaft des Aufrufers geprüft.
    { method: "POST", body: { email, role } },
    "errors.invitationNotCreated"
  );
  if (answer.ok) return { ok: true, invitation: answer.value };
  return interpretError<"not-admin" | "not-yours" | "invalid" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      403: {
        reason: "not-admin",
        titleKey: "errors.onlyAdminInvites",
      },
      404: { reason: "not-yours", titleKey: "errors.cannotInviteForCompany" },
    },
    "invalid"
  );
}

export async function withdrawInvitation(
  tenantId: string,
  invitationId: string
): Promise<{ ok: true } | Failure<"fehlgeschlagen">> {
  // `204` ohne Rumpf — `request()` gibt dafür `undefined` zurück, statt den
  // Parser in einen Fehler laufen zu lassen, der wie ein Serverfehler aussähe.
  const answer = await request<void>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations/${invitationId}`,
    { method: "DELETE" },
    "errors.invitationNotWithdrawn"
  );
  if (answer.ok) return { ok: true };
  return interpretError<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titleKey: "errors.noConnection" } },
    "fehlgeschlagen"
  );
}

/**
 * Ein Mitglied entfernen — oder sich selbst.
 *
 * `409` heißt nicht „du darfst nicht", sondern „nicht dieses Mitglied, nicht
 * jetzt": der letzte Administrator kann nicht gehen. Das getrennt zu halten ist
 * der Unterschied zwischen „such dir jemanden mit mehr Rechten" und „mach
 * vorher jemanden zum Administrator".
 */
export async function removeMember(
  tenantId: string,
  memberId: string
): Promise<EntfernenErgebnis> {
  const answer = await request<void>(
    API_BASE_URL,
    `/companies/${tenantId}/members/${memberId}`,
    { method: "DELETE" },
    "errors.memberNotRemoved"
  );
  if (answer.ok) return { ok: true };
  return interpretError<"last-admin" | "not-admin" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      409: {
        reason: "last-admin",
        titleKey: "errors.atLeastOneAdmin",
        detailKey: "errors.makeAdminFirst",
      },
      403: {
        reason: "not-admin",
        titleKey: "errors.onlyAdminRemoves",
      },
    },
    "offline"
  );
}
