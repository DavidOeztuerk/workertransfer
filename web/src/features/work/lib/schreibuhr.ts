/**
 * Wann dieses Schreiben begann — über Navigation hinweg.
 *
 * Der Server schickt `writing_started_at`. Fehlt es (älterer Entwurf, oder
 * der Worker ist nach einem Neustart tot), zählt die Oberfläche ab dem
 * ersten Anblick dieses Entwurfs, nicht ab jedem Mount.
 */

const VOR = "wt.schreiben.";

export function schreibbeginn(id: string, server?: string | null): string {
  try {
    if (server) {
      sessionStorage.setItem(VOR + id, server);
      return server;
    }
    const da = sessionStorage.getItem(VOR + id);
    if (da) return da;
    const jetzt = new Date().toISOString();
    sessionStorage.setItem(VOR + id, jetzt);
    return jetzt;
  } catch {
    return server ?? new Date().toISOString();
  }
}

export function schreibende(id: string): void {
  try {
    sessionStorage.removeItem(VOR + id);
  } catch {
    /* sessionStorage kann fehlen */
  }
}
