import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { Membership } from "../types/session";
import { i18n } from "../../../core/i18n/i18n";
import { failureKey } from "../../../shared/api/errors";

export type InvitationResult =
  | { ok: true; membership: Membership }
  | { ok: false; needsAccount: boolean; message: string };

/**
 * Accept an invitation.
 *
 * <strong>The invitation holds for the address it went to.</strong> A
 * forwarded link opens nothing — the server checks that, and a `401` here
 * therefore does not mean "expired" but "you are signed in as somebody else
 * right now, or as nobody".
 *
 * The three outcomes are kept apart because they lead to three different next
 * steps: sign in, ask for a new invitation, or nothing.
 */
// `message` is a FINISHED SENTENCE, not a catalogue key: the page shows it
// unchanged, and a key would stand there literally — i18next hands back one
// nobody looks up. Exactly that happened once, at the drafting help, and the
// E2E journey found it.
export async function acceptInvitation(
  token: string,
  signal?: AbortSignal,
): Promise<InvitationResult> {
  const answer = await request<Membership>(
    API_BASE_URL,
    "/invitations/accept",
    { method: "POST", body: { token }, signal },
    "errors.invitationNotAccepted"
  );

  if (answer.ok) {
    return { ok: true, membership: answer.value as Membership };
  }

  if (answer.error.status === 401) {
    return {
      ok: false,
      needsAccount: true,
      message: i18n.t("errors.invitedAddress"),
    };
  }

  // 400 carries two cases the person answers differently: an expired
  // invitation (ask for a new one) and one issued for another address (sign in
  // with that one). The sentences are identity-service's (`Invitation.cs`), and
  // they point back here; neither is shown.
  if (answer.error.status === 400) {
    return {
      ok: false,
      needsAccount: false,
      message: i18n.t(
        failureKey(answer.error, "errors.invitationInvalid", [
          { status: 400, pattern: /has expired/i, key: "invitation.expired" },
          { status: 400, pattern: /different email address/i, key: "errors.invitedAddress" },
        ]),
      ),
    };
  }

  return {
    ok: false,
    needsAccount: false,
    message: i18n.t("errors.invitationInvalid"),
  };
}
