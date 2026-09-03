import { API_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";
import { i18n } from "../../../core/i18n/i18n";

/**
 * Registrieren, bestätigen, erneut senden — die drei Wege in ein Konto.
 *
 * Sie stehen hier und nicht im `authSlice`: keiner von ihnen verändert die
 * Sitzung. Wer sich registriert, ist danach <em>nicht</em> angemeldet, sondern
 * `pending`; wer bestätigt, hat ein aktives Konto und muss sich trotzdem noch
 * anmelden. Ein Thunk, der nichts in den Zustand schreibt, wäre nur Zeremonie.
 */

export interface RegistrierEingabe {
  email: string;
  password: string;
  anzeigename: string;
  /**
   * Gesetzt heisst: hier registriert sich ein Unternehmen.
   *
   * Das Unternehmen entsteht erst mit der Bestätigung der Adresse — eine
   * unbestätigte Adresse beweist keine Domain (ADR-0019). Bis dahin merkt sich
   * der SERVER die Absicht, nicht der Browser: Bestätigungsmails werden oft auf
   * einem anderen Gerät geöffnet.
   */
  unternehmensname?: string;
}

export type RegistrierErgebnis = { ok: true } | { ok: false; meldung: string };

/**
 * Der Erfolgsfall hat DREI Ausprägungen, nicht eine: bestätigt · bestätigt mit
 * Unternehmen · bestätigt ohne Unternehmen samt Grund. Ohne die dritte zeigt
 * die Seite „alles gut", während die halbe Absicht verpufft ist.
 */
export type BestaetigungsErgebnis =
  | { ok: true; unternehmen?: string; unternehmenFehler?: string }
  | { ok: false; abgelaufen: boolean; meldung: string };

export type VersandErgebnis = { ok: true } | { ok: false };

/**
 * Spiegelt die Liste des Servers. Entscheidet nur, ob der Weg ANGEBOTEN wird —
 * die Absage selbst spricht immer der Server aus (422), nie diese Datei.
 */
const OEFFENTLICHE_DOMAINS = new Set([
  "aol.com",
  "freenet.de",
  "gmail.com",
  "googlemail.com",
  "gmx.at",
  "gmx.ch",
  "gmx.de",
  "gmx.net",
  "hotmail.com",
  "icloud.com",
  "mail.com",
  "me.com",
  "outlook.com",
  "proton.me",
  "protonmail.com",
  "t-online.de",
  "web.de",
  "yahoo.com",
  "yahoo.de",
  "yandex.com",
  "zoho.com",
]);

export function emailDomain(email: string): string {
  return email.split("@")[1]?.trim().toLowerCase() ?? "";
}

export function isPublicEmailDomain(email: string): boolean {
  return OEFFENTLICHE_DOMAINS.has(emailDomain(email));
}

/**
 * Ein Konto anlegen.
 *
 * <strong>Eine bekannte Adresse antwortet ebenfalls 201.</strong> Der Server
 * schickt der echten Inhaberin eine Warnung, statt dem Fragenden zu verraten,
 * dass es sie gibt — deshalb gibt es hier keinen „schon vergeben"-Zweig, und es
 * darf auch keiner dazukommen. Er wäre ein Aufzählungskanal.
 */
export async function registriere(eingabe: RegistrierEingabe): Promise<RegistrierErgebnis> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/auth/register",
    {
      method: "POST",
      body: {
        email: eingabe.email,
        password: eingabe.password,
        // snake_case, weil der Draht es so verlangt: der Server bindet
        // `display_name`. Mit `displayName` kommt die Eingabe nicht herein —
        // gemessen, und der Fehler sah aus wie ein leeres Formular.
        display_name: eingabe.anzeigename,
        // Nur mitschicken, wenn es eine Absicht gibt: `company_name` ist im
        // Vertrag optional, und ein `null` im Rumpf wäre eine Aussage, die
        // niemand gemacht hat.
        ...(eingabe.unternehmensname !== undefined
          ? { company_name: eingabe.unternehmensname }
          : {}),
      },
    },
    "fehler.registrierungFehlgeschlagen"
  );

  return answer.ok ? { ok: true } : { ok: false, meldung: answer.error.detail };
}

interface BestaetigungsRumpf {
  company?: string;
  company_error?: string;
}

/**
 * Die Adresse bestätigen — und erfahren, was aus dem Unternehmen wurde.
 *
 * Ein Rumpf, der sich nicht lesen lässt, macht die Bestätigung <strong>nicht</strong>
 * ungültig: sie ist serverseitig längst passiert, und ein 200 nachträglich in
 * ein Scheitern zu drehen wäre die falsche Auskunft. `request` liefert dafür
 * `ok: true` mit undefiniertem Wert.
 */
export async function bestaetigeEmail(token: string): Promise<BestaetigungsErgebnis> {
  const answer = await request<BestaetigungsRumpf>(
    API_BASE_URL,
    "/auth/verify-email",
    { method: "POST", body: { token } },
    "fehler.bestaetigungslinkUngueltig"
  );

  if (answer.ok) {
    const body = answer.value ?? {};
    return {
      ok: true,
      ...(typeof body.company === "string" ? { unternehmen: body.company } : {}),
      ...(typeof body.company_error === "string"
        ? { unternehmenFehler: body.company_error }
        : {}),
    };
  }

  // 410 ist ein eigener Fall, damit die Seite „neuen Link senden" anbieten kann
  // statt in eine Sackgasse zu führen. Ein UNGÜLTIGER Link wird auch beim
  // zweiten Versuch nicht gültig — dort gibt es nichts anzubieten.
  const abgelaufen = answer.error.status === 410;
  return {
    ok: false,
    abgelaufen,
    meldung: abgelaufen
      ? i18n.t("fehler.bestaetigungslinkAbgelaufen")
      : i18n.t("fehler.bestaetigungslinkUngueltig"),
  };
}

/**
 * Einen neuen Bestätigungslink anfordern.
 *
 * <strong>Auch ein Nicht-2xx gilt als Fehlschlag.</strong> Die alte Fassung fing
 * nur den Transportfehler ab; ein `429` von der Bremse (`/auth/resend-verification`
 * ist auf 3/min gedrosselt) erzeugte die Zusage „ist unterwegs", obwohl nichts
 * unterwegs war.
 *
 * Das ist kein Aufzählungskanal: die Antwort ist für bekannte und unbekannte
 * Adressen dieselbe (202), und ein 429 spricht über die Herkunft der Anfrage,
 * nicht über die Adresse im Rumpf.
 */
export async function sendeBestaetigungErneut(email: string): Promise<VersandErgebnis> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/auth/resend-verification",
    { method: "POST", body: { email } },
    "fehler.mailNichtAngefordert"
  );
  return answer.ok ? { ok: true } : { ok: false };
}
