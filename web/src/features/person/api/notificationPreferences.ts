import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { API_BASE_URL } from "../../../env";

/**
 * The notification preferences at notification-service.
 *
 * Eight switches, all ON by default: whoever does not learn that they were
 * asked has no choice, only the appearance of one.
 *
 * `profile_discovered` is the youngest (ADR-0033) and the only one that is
 * about a search rather than a case: "your profile was discovered by a
 * company". It can be unsubscribed SEPARATELY, and that is the reason for its
 * own kind — it is the only information somebody gets about their own
 * visibility, and whoever does not want it should not have to switch off
 * everything else with it.
 *
 * The field names are the wire and therefore snake_case — not this file's
 * taste.
 */
export interface NotificationPreferences {
  resume_request: boolean;
  market_request: boolean;
  application_update: boolean;
  transfer_update: boolean;
  application_received: boolean;
  profile_discovered: boolean;
  /**
   * "A company has opened a conversation" (ADR-0037).
   *
   * <strong>The last two were missing here</strong> — the server has known them
   * since advisor-service, this interface did not. That had no consequence as
   * long as nobody could switch them off; but as soon as an eighth switch is
   * added, a `PUT` without these fields would silently write them back to
   * `true`, because the contract defaults them that way. Two lines, and the
   * switch does what it says again.
   */
  advisor_conversation: boolean;
  /** "Something moved on your work sample" (ADR-0042). */
  assessment_update: boolean;
}

/** What holds as long as nobody has set anything. */
export const ALL_ON: NotificationPreferences = {
  resume_request: true,
  market_request: true,
  application_update: true,
  transfer_update: true,
  application_received: true,
  profile_discovered: true,
  advisor_conversation: true,
  assessment_update: true,
};

const PATH = "/me/notification-preferences";

/**
 * What is set. `null` means: not retrievable.
 *
 * The endpoint always answers `200` with the defaults, even if nothing was ever
 * saved — that promise comes from the server. `null` therefore really means
 * "we do not know", and the page must then claim no switch position.
 */
export async function getNotificationPreferences(
  signal?: AbortSignal
): Promise<NotificationPreferences | null> {
  const answer = await request<NotificationPreferences>(
    API_BASE_URL,
    PATH,
    { signal },
    "errors.settingsUnavailable"
  );

  return answer.ok ? (answer.value ?? null) : null;
}

export type NotificationPreferencesSaveResult =
  | { ok: true; choice: NotificationPreferences }
  | { ok: false; error: ApiError };

/** Saves all of them at once — the endpoint knows no partial update. */
export async function saveNotificationPreferences(
  choice: NotificationPreferences,
  signal?: AbortSignal
): Promise<NotificationPreferencesSaveResult> {
  const answer = await request<NotificationPreferences>(
    API_BASE_URL,
    PATH,
    { method: "PUT", body: choice, signal },
    "errors.settingsNotSaved"
  );

  return answer.ok ? { ok: true, choice: answer.value ?? choice } : { ok: false, error: answer.error };
}
