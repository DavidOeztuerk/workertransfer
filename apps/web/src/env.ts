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

/**
 * Laufzeitkonfiguration, gesetzt von `public/config.js` VOR dem Modulbündel.
 *
 * Sie existiert wegen einer Eigenschaft von `vite build`: die `VITE_`-Variablen
 * werden beim BAUEN in das Bündel eingesetzt. Ein gebautes Image trägt seine
 * URLs also fest in sich — und könnte in zwei Umgebungen nicht dasselbe sein.
 * Für docker compose ist das gleichgültig (dort läuft der Dev-Server, der die
 * Umgebung beim Start liest), für ein Helm-Chart nicht: dort ist "ein Artefakt,
 * viele Umgebungen" die ganze Idee (ADR-0028).
 *
 * Die Voreinstellung im Repository ist ein leeres Objekt, ändert also nichts.
 * Erst das Chart legt eine echte Fassung darüber.
 */
type RuntimeConfig = Partial<Record<string, string>>;

function runtime(key: string): string | undefined {
  if (typeof window === "undefined") return undefined;
  const config = (window as { __WT_CONFIG__?: RuntimeConfig }).__WT_CONFIG__;
  return config?.[key];
}

/**
 * Drei Stufen, in dieser Reihenfolge: Laufzeitwert, Bauzeitwert, Port-Rückfall.
 *
 * Die Reihenfolge ist die eigentliche Aussage. Der Laufzeitwert gewinnt, weil
 * er als Einziger weiss, wo das Bündel gerade tatsächlich ausgeliefert wird.
 */
function resolve(key: string, raw: string | undefined, port: number): string {
  const live = runtime(key);
  if (typeof live === "string" && live.length > 0) return live;
  return typeof raw === "string" && raw.length > 0 ? raw : fallback(port);
}

export const API_BASE_URL = resolve("API_BASE_URL", import.meta.env.VITE_API_BASE_URL, 8001);
export const CONSENT_BASE_URL = resolve(
  "CONSENT_BASE_URL",
  import.meta.env.VITE_CONSENT_BASE_URL,
  8002
);
export const PROFILE_BASE_URL = resolve(
  "PROFILE_BASE_URL",
  import.meta.env.VITE_PROFILE_BASE_URL,
  8003
);
export const RESUME_BASE_URL = resolve(
  "RESUME_BASE_URL",
  import.meta.env.VITE_RESUME_BASE_URL,
  8004
);
export const PORTFOLIO_BASE_URL = resolve(
  "PORTFOLIO_BASE_URL",
  import.meta.env.VITE_PORTFOLIO_BASE_URL,
  8005
);
export const JOBS_BASE_URL = resolve("JOBS_BASE_URL", import.meta.env.VITE_JOBS_BASE_URL, 8006);
export const APPLICATIONS_BASE_URL = resolve(
  "APPLICATIONS_BASE_URL",
  import.meta.env.VITE_APPLICATIONS_BASE_URL,
  8007
);
export const COMPANIES_BASE_URL = resolve(
  "COMPANIES_BASE_URL",
  import.meta.env.VITE_COMPANIES_BASE_URL,
  8008
);
export const TRANSFER_BASE_URL = resolve(
  "TRANSFER_BASE_URL",
  import.meta.env.VITE_TRANSFER_BASE_URL,
  8009
);
export const GITHUB_BASE_URL = resolve(
  "GITHUB_BASE_URL",
  import.meta.env.VITE_GITHUB_BASE_URL,
  8010
);
