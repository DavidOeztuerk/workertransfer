// Basis-URLs der Dienste. Vite injiziert die VITE_-Variablen (deklariert in
// turbo.json globalEnv + vite/client types); sie sind optional, der literale
// Zugriff ist unter noUncheckedIndexedAccess also `string | undefined`.
//
// Je Dienst eine eigene Variable statt eines Gateways: die Dienste laufen auf
// eigenen Ports und haben eigene Datenbanken (ADR-0004). Ein gemeinsames
// Präfix würde eine Zusammenlegung suggerieren, die es nicht gibt.

/**
 * Der Ersatz nimmt den Host der Seite, nicht `127.0.0.1`.
 *
 * Cookies unterscheiden `localhost` und `127.0.0.1` als verschiedene Hosts,
 * Ports dagegen ignorieren sie. Ein fester Ersatz auf `127.0.0.1` liefert
 * deshalb bei einer Seite auf `localhost` keine Verbindungsfehler, sondern
 * etwas Schlimmeres: Anfragen ohne Sitzungscookie, die wortlos mit 401
 * antworten. Genau so ist die E2E-Reise einmal gescheitert — der Dienst lief,
 * das Token war gültig, und trotzdem war niemand angemeldet.
 */
function fallback(port: number): string {
  const host = typeof window === "undefined" ? "127.0.0.1" : window.location.hostname;
  return `http://${host}:${port}`;
}

function resolve(raw: string | undefined, port: number): string {
  return typeof raw === "string" && raw.length > 0 ? raw : fallback(port);
}

export const API_BASE_URL = resolve(import.meta.env.VITE_API_BASE_URL, 8001);
export const CONSENT_BASE_URL = resolve(import.meta.env.VITE_CONSENT_BASE_URL, 8002);
export const PROFILE_BASE_URL = resolve(import.meta.env.VITE_PROFILE_BASE_URL, 8003);
export const RESUME_BASE_URL = resolve(import.meta.env.VITE_RESUME_BASE_URL, 8004);
export const PORTFOLIO_BASE_URL = resolve(import.meta.env.VITE_PORTFOLIO_BASE_URL, 8005);
export const JOBS_BASE_URL = resolve(import.meta.env.VITE_JOBS_BASE_URL, 8006);
export const APPLICATIONS_BASE_URL = resolve(
  import.meta.env.VITE_APPLICATIONS_BASE_URL,
  8007
);
export const COMPANIES_BASE_URL = resolve(import.meta.env.VITE_COMPANIES_BASE_URL, 8008);
