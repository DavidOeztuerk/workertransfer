/**
 * Ein Flug für `POST /auth/refresh`.
 *
 * Access-JWT stirbt nach fünfzehn Minuten; der Refresh-Cookie (Pfad `/auth`,
 * httpOnly) trägt weiter. Ohne diese Klammer würden zwanzig parallele 401
 * zwanzig Rotationen anstoßen — Girder hat Reuse-Grace, aber ein Flug reicht.
 *
 * Tokens liegen im Cookie. Hier steht kein Bearer und kein Speicher.
 */

const AUTH_OHNE_ERNEUERUNG = [
  "/auth/login",
  "/auth/register",
  "/auth/refresh",
  "/auth/verify-email",
  "/auth/resend-verification",
  "/auth/logout",
];

/** Auth-Pfade, deren 401 die Absage IST und kein abgelaufenes Access-JWT. */
export function brauchtKeineErneuerung(path: string): boolean {
  const pfad = path.split("?")[0] ?? path;
  return AUTH_OHNE_ERNEUERUNG.some((bekannt) => pfad === bekannt);
}

let inflight: Promise<boolean> | null = null;

/**
 * Rotiert die Cookies einmal. Mehrere Aufrufer teilen dasselbe Versprechen.
 *
 * `true` heisst: der Server hat neue Cookies gesetzt. `false`: tot oder Netz —
 * der Server löscht dann den Refresh-Cookie, damit `/auth/session` nicht
 * ewig `renewable` sagt.
 */
export function erneuereEinmal(baseUrl: string): Promise<boolean> {
  if (inflight === null) {
    inflight = (async () => {
      try {
        const antwort = await fetch(`${baseUrl}/auth/refresh`, {
          method: "POST",
          credentials: "include",
        });
        return antwort.ok;
      } catch {
        return false;
      } finally {
        inflight = null;
      }
    })();
  }
  return inflight;
}

/** Nur für Tests: sonst hängt ein Flug aus dem vorigen Fall. */
export function zuruecksetzenFuerTests(): void {
  inflight = null;
}
