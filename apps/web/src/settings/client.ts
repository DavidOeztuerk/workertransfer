// Benachrichtigungs-Einstellungen beim identity-service.
//
// Vier Schalter, alle standardmäßig an: wer nicht erfährt, dass gefragt wurde,
// hat keine Wahl, sondern nur den Anschein einer.

import { API_BASE_URL } from "../env";

export interface NotificationPreferences {
  resume_request: boolean;
  market_request: boolean;
  application_update: boolean;
  transfer_update: boolean;
}

export const ALL_ON: NotificationPreferences = {
  resume_request: true,
  market_request: true,
  application_update: true,
  transfer_update: true,
};

export type PreferencesResult =
  | { ok: true; preferences: NotificationPreferences }
  | { ok: false; message: string };

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${API_BASE_URL}${path}`, { credentials: "include", ...init });
}

/**
 * Bei einem Fehler die Voreinstellung, nicht „alles aus".
 *
 * Ein Netzfehler ist keine Abbestellung. Zeigte die Seite hier vier
 * ausgeschaltete Schalter, würde beim nächsten Speichern genau das
 * geschrieben — und die Person hätte sich abgemeldet, ohne es zu wollen.
 */
export async function getNotificationPreferences(): Promise<NotificationPreferences> {
  try {
    const res = await send("/me/notification-preferences");
    if (!res.ok) return { ...ALL_ON };
    return (await res.json()) as NotificationPreferences;
  } catch {
    return { ...ALL_ON };
  }
}

export async function saveNotificationPreferences(
  preferences: NotificationPreferences
): Promise<PreferencesResult> {
  let res: Response;
  try {
    res = await send("/me/notification-preferences", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(preferences),
    });
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
  if (res.status === 401) {
    return { ok: false, message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an." };
  }
  if (!res.ok) {
    return { ok: false, message: "Die Einstellungen konnten nicht gespeichert werden." };
  }
  return { ok: true, preferences: (await res.json()) as NotificationPreferences };
}
