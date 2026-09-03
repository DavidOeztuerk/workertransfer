import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { Membership } from "../types/session";

export type Einladungsergebnis =
  | { ok: true; mitgliedschaft: Membership }
  | { ok: false; brauchtKonto: boolean; meldung: string };

/**
 * Eine Einladung annehmen.
 *
 * <strong>Die Einladung gilt für die Adresse, an die sie ging.</strong> Ein
 * weitergeleiteter Link öffnet nichts — der Server prüft das, und ein `401`
 * heisst hier deshalb nicht „abgelaufen", sondern „du bist gerade als jemand
 * anderes angemeldet, oder als niemand".
 *
 * Die drei Ausgänge werden getrennt gehalten, weil sie zu drei verschiedenen
 * nächsten Schritten führen: anmelden, eine neue Einladung erbitten, oder
 * nichts.
 */
export async function nimmAn(token: string, signal?: AbortSignal): Promise<Einladungsergebnis> {
  const antwort = await request<Membership>(
    API_BASE_URL,
    "/invitations/accept",
    { method: "POST", body: { token }, signal },
    "fehler.einladungNichtAngenommen"
  );

  if (antwort.ok) {
    return { ok: true, mitgliedschaft: antwort.value as Membership };
  }

  if (antwort.error.status === 401) {
    return {
      ok: false,
      brauchtKonto: true,
      meldung: "fehler.eingeladeneAdresse",
    };
  }

  if (antwort.error.status === 400) {
    return { ok: false, brauchtKonto: false, meldung: antwort.error.detail };
  }

  return {
    ok: false,
    brauchtKonto: false,
    meldung: "fehler.einladungUngueltig",
  };
}
