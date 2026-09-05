// Die GitHub-Belege einer Person, wie ein Unternehmen sie sieht.
//
// Was hier fehlt, ist die Aussage: keine Punktzahl, kein Rang, keine
// abgeleitete Eigenschaft (ADR-0022). Ein Repository ist ein Beleg mit einem
// Link — nachprüfbar, ohne Zwischenrechnung, und widersprechbar.
//
// ACHTUNG, gemeldeter Widerspruch: `GITHUB_BASE_URL` fällt in `src/env.ts` auf
// Port **8010** zurück; github-service läuft auf **8011** (8010 ist
// notification-service). `env.ts` liegt außerhalb dieses Territoriums und wurde
// nicht angefasst — steht in `docs/uebergabe/frontend-work-company.md`.

import { request } from "../../../core/api/client";
import { GITHUB_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "../../../shared/api/fehler";

export interface GitHubRepository {
  name: string;
  description: string;
  language: string | null;
  stars: number;
  url: string;
  pushed_at: string | null;
  /** Die MENGE der Sprachen, nie ihr Anteil (ADR-0022 §2). */
  languages: string[];
  /** Die Topics, die der Besitzer selbst gesetzt hat — eine Nennung. */
  topics: string[];
}

export interface GitHubConnection {
  subject_id: string;
  login: string;
  verified: boolean;
  fetched_at: string | null;
  repositories: GitHubRepository[];
}

/**
 * Die Verbindung einer anderen Person.
 *
 * `404` wird zu `null` — „gibt es nicht", „nicht bewiesen" und „nicht
 * freigegeben" sind für die Oberfläche derselbe Fall, genau wie der Server sie
 * ununterscheidbar hält.
 */
export async function getGitHub(
  subjectId: string,
  signal?: AbortSignal
): Promise<{ ok: true; connection: GitHubConnection | null } | Fehlschlag<"unavailable">> {
  const answer = await request<GitHubConnection>(
    GITHUB_BASE_URL,
    `/github/${subjectId}`,
    { signal },
    "fehler.verbindungNichtGeladen"
  );
  if (answer.ok) return { ok: true, connection: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, connection: null };
  return deuten<"unavailable">(
    answer.error,
    { 503: { reason: "unavailable", titel: "fehler.ledgerSchweigt" } },
    "unavailable"
  );
}
