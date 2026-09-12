import { describe, expect, it } from "vitest";

import * as env from "./env";

/**
 * Jede Basisadresse zeigt auf den Port ihres Dienstes.
 *
 * Der Anlass: `GITHUB_BASE_URL` fiel auf **8010** zurück — den Port von
 * notification-service. Wer `/github` öffnete, schickte seinen Kontonamen an
 * den falschen Dienst und bekam eine Fehlermeldung, die nach einem abgelehnten
 * Konto aussah.
 *
 * Unbemerkt blieb es, weil der Nachbar niemandem fehlt: die Oberfläche redet
 * mit notification-service überhaupt nicht (die Benachrichtigungs-Einstellungen
 * liegen bei identity). Ein Versatz um eins traf deshalb nur einen einzigen
 * Aufrufer, und den erst auf einer Seite, die kein Unit-Test öffnet.
 *
 * Die Tabelle steht hier ausgeschrieben und wird nicht aus dem Code abgeleitet
 * — sonst prüfte sie sich selbst. Sie ist dieselbe wie in CLAUDE.md und in
 * `docker-compose.yml`; ein neuer Dienst gehört an alle drei Stellen.
 */
const PORTS: [string, number][] = [
  ["API_BASE_URL", 8001],
  ["CONSENT_BASE_URL", 8002],
  ["PROFILE_BASE_URL", 8003],
  ["RESUME_BASE_URL", 8004],
  ["PORTFOLIO_BASE_URL", 8005],
  ["JOBS_BASE_URL", 8006],
  ["APPLICATIONS_BASE_URL", 8007],
  ["COMPANIES_BASE_URL", 8008],
  ["TRANSFER_BASE_URL", 8009],
  ["GITHUB_BASE_URL", 8011],
];

describe("die Basisadressen", () => {
  it.each(PORTS)("%s zeigt auf %i", (name, port) => {
    expect(env[name as keyof typeof env]).toBe(`http://localhost:${port}`);
  });

  /**
   * Zwei Dienste dürfen sich keinen Port teilen — ein Tippfehler wäre sonst
   * nicht „falscher Port", sondern „zwei Aufrufer, ein Ziel", und das fällt
   * noch später auf.
   */
  it("vergibt keinen Port zweimal", () => {
    const adressen = PORTS.map(([name]) => env[name as keyof typeof env]);

    expect(new Set(adressen).size).toBe(adressen.length);
  });
});
