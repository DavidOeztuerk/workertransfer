// Mannschaft eines Unternehmens: Mitglieder und Einladungen.
//
// Der Einladungs-Token taucht hier nirgends auf. Er steht weder in der Antwort
// aufs Einladen noch in der Liste der offenen Einladungen — angenommen wird
// eine Einladung in `features/auth`, und dort kommt er aus der URL, in die ihn
// die Mail geschrieben hat.

import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "../../../shared/api/fehler";

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
  | Fehlschlag<"fehlgeschlagen">;
export type EinladungenErgebnis =
  | { ok: true; invitations: Invitation[] }
  | Fehlschlag<"fehlgeschlagen">;
export type EinladenErgebnis =
  | { ok: true; invitation: Invitation }
  | Fehlschlag<"not-admin" | "not-yours" | "invalid" | "offline">;
export type EntfernenErgebnis =
  | { ok: true }
  | Fehlschlag<"last-admin" | "not-admin" | "offline">;

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
    "fehler.mannschaftNichtGeladen"
  );
  if (answer.ok) return { ok: true, members: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, members: [] };
  return deuten<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titel: "fehler.keineVerbindung" } },
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
    "fehler.einladungenNichtGeladen"
  );
  if (answer.ok) return { ok: true, invitations: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, invitations: [] };
  return deuten<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titel: "fehler.keineVerbindung" } },
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
    "fehler.einladungNichtAngelegt"
  );
  if (answer.ok) return { ok: true, invitation: answer.value };
  return deuten<"not-admin" | "not-yours" | "invalid" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      403: {
        reason: "not-admin",
        titel: "fehler.nurAdminEinladen",
      },
      404: { reason: "not-yours", titel: "fehler.fuerDieseFirmaNichtEinladen" },
    },
    "invalid"
  );
}

export async function withdrawInvitation(
  tenantId: string,
  invitationId: string
): Promise<{ ok: true } | Fehlschlag<"fehlgeschlagen">> {
  // `204` ohne Rumpf — `request()` gibt dafür `undefined` zurück, statt den
  // Parser in einen Fehler laufen zu lassen, der wie ein Serverfehler aussähe.
  const answer = await request<void>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations/${invitationId}`,
    { method: "DELETE" },
    "fehler.einladungNichtZurueckgezogen"
  );
  if (answer.ok) return { ok: true };
  return deuten<"fehlgeschlagen">(
    answer.error,
    { 0: { reason: "fehlgeschlagen", titel: "fehler.keineVerbindung" } },
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
    "fehler.mitgliedNichtEntfernt"
  );
  if (answer.ok) return { ok: true };
  return deuten<"last-admin" | "not-admin" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      409: {
        reason: "last-admin",
        titel: "fehler.mindestensEinAdmin",
        text: "fehler.zuerstAdminMachen",
      },
      403: {
        reason: "not-admin",
        titel: "fehler.nurAdminEntfernen",
      },
    },
    "offline"
  );
}
