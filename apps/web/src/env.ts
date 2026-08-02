// Basis-URLs der Dienste. Vite injiziert die VITE_-Variablen (deklariert in
// turbo.json globalEnv + vite/client types); sie sind optional, der literale
// Zugriff ist unter noUncheckedIndexedAccess also `string | undefined`.
//
// Je Dienst eine eigene Variable statt eines Gateways: die Dienste laufen auf
// eigenen Ports und haben eigene Datenbanken (ADR-0004). Ein gemeinsames
// Präfix würde jetzt eine Zusammenlegung suggerieren, die es nicht gibt.

function resolve(raw: string | undefined, fallback: string): string {
  return typeof raw === "string" && raw.length > 0 ? raw : fallback;
}

export const API_BASE_URL = resolve(import.meta.env.VITE_API_BASE_URL, "http://127.0.0.1:8001");
export const CONSENT_BASE_URL = resolve(
  import.meta.env.VITE_CONSENT_BASE_URL,
  "http://127.0.0.1:8002"
);
export const PROFILE_BASE_URL = resolve(
  import.meta.env.VITE_PROFILE_BASE_URL,
  "http://127.0.0.1:8003"
);
export const RESUME_BASE_URL = resolve(
  import.meta.env.VITE_RESUME_BASE_URL,
  "http://127.0.0.1:8004"
);
