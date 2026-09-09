import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { Membership } from "../types/session";
import { i18n } from "../../../core/i18n/i18n";

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
// `meldung` ist ein FERTIGER SATZ, kein Katalogschlüssel: die Seite zeigt ihn
// unverändert an, und ein Schlüssel stünde dort wörtlich — i18next gibt einen
// zurück, den niemand nachschlägt. Genau das ist einmal passiert, an der
// Formulierungshilfe, und die E2E-Reise hat es gefunden.
export async function nimmAn(token: string, signal?: AbortSignal): Promise<Einladungsergebnis> {
  const answer = await request<Membership>(
    API_BASE_URL,
    "/invitations/accept",
    { method: "POST", body: { token }, signal },
    "fehler.einladungNichtAngenommen"
  );

  if (answer.ok) {
    return { ok: true, mitgliedschaft: answer.value as Membership };
  }

  if (answer.error.status === 401) {
    return {
      ok: false,
      brauchtKonto: true,
      meldung: i18n.t("fehler.eingeladeneAdresse"),
    };
  }

  if (answer.error.status === 400) {
    return { ok: false, brauchtKonto: false, meldung: answer.error.detail };
  }

  return {
    ok: false,
    brauchtKonto: false,
    meldung: i18n.t("fehler.einladungUngueltig"),
  };
}
