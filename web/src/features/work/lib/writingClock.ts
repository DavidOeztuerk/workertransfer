/**
 * When this writing began — across navigation.
 *
 * The server sends `writing_started_at`. If it is missing (an older draft, or
 * the worker died after a restart), the UI counts from the first sight of this
 * draft, not from every mount.
 */

/**
 * The sessionStorage prefix. The string stays German on purpose: it is stored
 * in real browsers, and renaming it would lose a running draft's start.
 */
const PREFIX = "wt.schreiben.";

export function writingStart(id: string, server?: string | null): string {
  try {
    if (server) {
      sessionStorage.setItem(PREFIX + id, server);
      return server;
    }
    const stored = sessionStorage.getItem(PREFIX + id);
    if (stored) return stored;
    const now = new Date().toISOString();
    sessionStorage.setItem(PREFIX + id, now);
    return now;
  } catch {
    return server ?? new Date().toISOString();
  }
}

export function writingEnded(id: string): void {
  try {
    sessionStorage.removeItem(PREFIX + id);
  } catch {
    /* sessionStorage may be missing */
  }
}
