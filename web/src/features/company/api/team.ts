// A company's team: members and invitations.
//
// The invitation token appears nowhere here. It stands neither in the answer to
// inviting nor in the list of open invitations — an invitation is accepted in
// `features/auth`, and there it comes from the URL the mail wrote it into.

import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";

/** Ordered: member < recruiter < admin (ADR-0048). */
export type Role = "admin" | "recruiter" | "member";

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

export type MembersResult =
  | { ok: true; members: CompanyMember[] }
  | Failure<"failed">;
export type InvitationsResult =
  | { ok: true; invitations: Invitation[] }
  | Failure<"failed">;
export type InviteResult =
  | { ok: true; invitation: Invitation }
  | Failure<"not-admin" | "not-yours" | "invalid" | "offline">;
export type RemoveResult =
  | { ok: true }
  | Failure<"last-admin" | "not-admin" | "offline">;

/**
 * The team. A `404` means "not yours", not "broken".
 *
 * The server deliberately answers `404` instead of `403`, so nobody can ask
 * which companies exist. The UI turns that into a calm empty state instead of
 * an error message that explains nothing.
 */
export async function listMembers(
  tenantId: string,
  signal?: AbortSignal
): Promise<MembersResult> {
  const answer = await request<CompanyMember[]>(
    API_BASE_URL,
    `/companies/${tenantId}/members`,
    { signal },
    "errors.teamNotLoaded"
  );
  if (answer.ok) return { ok: true, members: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, members: [] };
  return interpretError<"failed">(
    answer.error,
    { 0: { reason: "failed", titleKey: "errors.noConnection" } },
    "failed"
  );
}

export async function listInvitations(
  tenantId: string,
  signal?: AbortSignal
): Promise<InvitationsResult> {
  const answer = await request<Invitation[]>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations`,
    { signal },
    "errors.invitationsNotLoaded"
  );
  if (answer.ok) return { ok: true, invitations: answer.value ?? [] };
  if (answer.error.status === 404) return { ok: true, invitations: [] };
  return interpretError<"failed">(
    answer.error,
    { 0: { reason: "failed", titleKey: "errors.noConnection" } },
    "failed"
  );
}

export async function inviteMember(
  tenantId: string,
  email: string,
  role: Role
): Promise<InviteResult> {
  const answer = await request<Invitation>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations`,
    // No company in the body: it stands in the path and is checked against the
    // caller's membership.
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
): Promise<{ ok: true } | Failure<"failed">> {
  // `204` without a body — `request()` returns `undefined` for it instead of
  // running the parser into an error that would look like a server error.
  const answer = await request<void>(
    API_BASE_URL,
    `/companies/${tenantId}/invitations/${invitationId}`,
    { method: "DELETE" },
    "errors.invitationNotWithdrawn"
  );
  if (answer.ok) return { ok: true };
  return interpretError<"failed">(
    answer.error,
    { 0: { reason: "failed", titleKey: "errors.noConnection" } },
    "failed"
  );
}

/**
 * Remove a member — or oneself.
 *
 * `409` does not mean "you may not" but "not this member, not now": the last
 * administrator cannot leave. Keeping that apart is the difference between
 * "find someone with more rights" and "make somebody administrator first".
 */
export async function removeMember(
  tenantId: string,
  memberId: string
): Promise<RemoveResult> {
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
