import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { GITHUB_BASE_URL } from "../../../env";
import type { Fehlerschluessel } from "../../../shared/api/fehler";

/**
 * Die eigene, nachgewiesene GitHub-Verbindung.
 *
 * <strong>Belege, keine Noten.</strong> Was von hier kommt, sind öffentliche
 * Repositories mit Link — nachprüfbar, mit Herkunft, ohne Zwischenrechnung. Es
 * gibt keine Punktzahl und keine Rangfolge, und es darf auch keine geben:
 * „Anforderung rein, Belege raus" ist die Richtung, „Mensch rein, Zahl raus"
 * wäre die verbotene.
 */
export interface Repository {
  name: string;
  description: string;
  language: string | null;
  stars: number;
  url: string;
  pushed_at: string | null;
}

export interface Verbindung {
  subject_id: string;
  login: string;
  verified: boolean;
  /** Nur in der eigenen Ansicht gefüllt: die Zeile für den Gist. */
  challenge_description: string | null;
  fetched_at: string | null;
  repositories: Repository[];
}

export type Antwort<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/** Die eigene Verbindung. `null` heisst: noch keine genannt. */
export async function ladeMeine(signal?: AbortSignal): Promise<Antwort<Verbindung | null>> {
  const answer = await request<Verbindung>(
    GITHUB_BASE_URL,
    "/github/me",
    { signal },
    "fehler.verbindungNichtAbrufbar"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

async function handeln(
  path: string,
  optionen: Parameters<typeof request>[2],
  fallback: Fehlerschluessel
): Promise<Antwort<Verbindung>> {
  const answer = await request<Verbindung>(GITHUB_BASE_URL, path, optionen, fallback);

  return answer.ok
    ? { ok: true, value: answer.value as Verbindung }
    : { ok: false, error: answer.error };
}

/** Ein Konto NENNEN — verbunden ist es damit noch nicht. */
export const nenneKonto = (login: string, signal?: AbortSignal) =>
  handeln(
    "/github/me",
    { method: "POST", body: { login }, signal },
    "fehler.kontoNichtAngenommen"
  );

/**
 * Den Nachweis prüfen.
 *
 * Ohne Nachweis wäre ein Feld „mein GitHub-Name" eine Einladung, sich mit
 * fremder Arbeit zu schmücken. Der Beweis läuft über einen öffentlichen Gist —
 * dieselbe Form wie der Domain-Nachweis bei Unternehmen.
 */
export const pruefeNachweis = (signal?: AbortSignal) =>
  handeln(
    "/github/me/verify",
    { method: "POST", signal },
    "fehler.nachweisNichtGeprueft"
  );

/**
 * Die Repositories neu holen.
 *
 * <strong>Nur auf Auslösung.</strong> Es läuft kein Abgleich im Hintergrund:
 * eine Plattform, die dauerhaft hinterhersieht, tut etwas anderes als eine, die
 * einmal auf Bitte hinsieht.
 */
export const holeNeu = (signal?: AbortSignal) =>
  handeln("/github/me/refresh", { method: "POST", signal }, "fehler.nichtsGeholt");

/** Die Verbindung trennen. */
export async function trenne(signal?: AbortSignal): Promise<boolean> {
  const answer = await request<void>(
    GITHUB_BASE_URL,
    "/github/me",
    { method: "DELETE", signal },
    "fehler.verbindungNichtGetrennt"
  );

  return answer.ok;
}
