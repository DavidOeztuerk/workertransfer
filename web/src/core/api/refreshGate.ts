/**
 * One flight for `POST /auth/refresh`.
 *
 * The access JWT dies after fifteen minutes; the refresh cookie (path `/auth`,
 * httpOnly) carries on. Without this gate twenty parallel 401s would start
 * twenty rotations — Girder has reuse grace, but one flight is enough.
 *
 * Tokens live in the cookie. No bearer and no storage stand here.
 */

const AUTH_PATHS_WITHOUT_REFRESH = [
  "/auth/login",
  "/auth/register",
  "/auth/refresh",
  "/auth/verify-email",
  "/auth/resend-verification",
  "/auth/logout",
];

/** Auth paths whose 401 IS the refusal and not an expired access JWT. */
export function skipsRefresh(path: string): boolean {
  const bare = path.split("?")[0] ?? path;
  return AUTH_PATHS_WITHOUT_REFRESH.some((known) => bare === known);
}

let inflight: Promise<boolean> | null = null;

/**
 * Rotates the cookies once. Several callers share the same promise.
 *
 * `true` means: the server set new cookies. `false`: dead or network — the
 * server then deletes the refresh cookie so that `/auth/session` does not say
 * `renewable` for ever.
 */
export function refreshOnce(baseUrl: string): Promise<boolean> {
  if (inflight === null) {
    inflight = (async () => {
      try {
        const response = await fetch(`${baseUrl}/auth/refresh`, {
          method: "POST",
          credentials: "include",
        });
        return response.ok;
      } catch {
        return false;
      } finally {
        inflight = null;
      }
    })();
  }
  return inflight;
}

/** For tests only: otherwise a flight from the previous case hangs on. */
export function resetForTests(): void {
  inflight = null;
}
