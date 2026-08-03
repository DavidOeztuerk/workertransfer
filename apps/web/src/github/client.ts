// Die GitHub-Verbindung, wie die Oberfläche sie benutzt.
//
// Was hier fehlt, ist die Aussage: keine Punktzahl, kein Rang, keine
// abgeleitete Eigenschaft (ADR-0022). Ein Repository ist ein Beleg mit einem
// Link.

import { GITHUB_BASE_URL } from "../env";

export interface GitHubRepository {
  name: string;
  description: string;
  language: string | null;
  stars: number;
  url: string;
  pushed_at: string | null;
}

export interface GitHubConnection {
  subject_id: string;
  login: string;
  verified: boolean;
  /** Nur in der eigenen Ansicht gefüllt: die Zeile für den Gist. */
  challenge_description: string | null;
  fetched_at: string | null;
  repositories: GitHubRepository[];
}

export type ConnectionResult =
  | { ok: true; connection: GitHubConnection }
  | {
      ok: false;
      reason: "not-proven" | "unavailable" | "invalid" | "offline";
      message: string;
    };

function send(path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${GITHUB_BASE_URL}${path}`, { credentials: "include", ...init });
}

async function toResult(res: Response): Promise<ConnectionResult> {
  if (res.status === 422) {
    return {
      ok: false,
      reason: "not-proven",
      message:
        "Kein öffentlicher Gist mit dieser Beschreibung gefunden. Angelegt? Öffentlich? Dann noch einmal versuchen.",
    };
  }
  if (res.status === 503) {
    return {
      ok: false,
      reason: "unavailable",
      // Ausdrücklich NICHT „Nachweis fehlt": das hieße, jemandem den Nachweis
      // abzusprechen, weil wir gerade nicht fragen konnten.
      message: "GitHub oder der Consent-Ledger antwortet gerade nicht. Bitte später erneut.",
    };
  }
  if (!res.ok) {
    return { ok: false, reason: "invalid", message: "Das hat nicht geklappt." };
  }
  return { ok: true, connection: (await res.json()) as GitHubConnection };
}

/** Die eigene Verbindung — auch die noch unbewiesene. */
export async function getMyGitHub(): Promise<GitHubConnection | null> {
  try {
    const res = await send("/github/me");
    if (!res.ok) return null;
    const body: unknown = await res.json();
    return body === null ? null : (body as GitHubConnection);
  } catch {
    return null;
  }
}

export async function connectGitHub(login: string): Promise<ConnectionResult> {
  try {
    return await toResult(
      await send("/github/me", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ login }),
      })
    );
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
}

export async function verifyGitHub(): Promise<ConnectionResult> {
  try {
    return await toResult(await send("/github/me/verify", { method: "POST" }));
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
}

export async function refreshGitHub(): Promise<ConnectionResult> {
  try {
    return await toResult(await send("/github/me/refresh", { method: "POST" }));
  } catch {
    return { ok: false, reason: "offline", message: "Keine Verbindung zum Server." };
  }
}

export async function disconnectGitHub(): Promise<boolean> {
  try {
    return (await send("/github/me", { method: "DELETE" })).ok;
  } catch {
    return false;
  }
}

/**
 * Die Verbindung einer anderen Person.
 *
 * `404` wird zu `null` — „gibt es nicht", „nicht bewiesen" und „nicht
 * freigegeben" sind für die Oberfläche derselbe Fall, genau wie der Server sie
 * ununterscheidbar hält.
 */
export async function getGitHub(
  subjectId: string
): Promise<{ ok: true; connection: GitHubConnection | null } | { ok: false; message: string }> {
  try {
    const res = await send(`/github/${subjectId}`);
    if (res.status === 404) return { ok: true, connection: null };
    if (res.status === 503) {
      return { ok: false, message: "Der Consent-Ledger antwortet gerade nicht." };
    }
    if (!res.ok) return { ok: false, message: "Die Verbindung ließ sich nicht laden." };
    return { ok: true, connection: (await res.json()) as GitHubConnection };
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }
}
