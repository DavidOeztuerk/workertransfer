import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { GITHUB_BASE_URL } from "../../../env";

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

export type Antwort<T> = { ok: true; wert: T } | { ok: false; error: ApiError };

/** Die eigene Verbindung. `null` heisst: noch keine genannt. */
export async function ladeMeine(signal?: AbortSignal): Promise<Antwort<Verbindung | null>> {
  const antwort = await request<Verbindung>(
    GITHUB_BASE_URL,
    "/github/me",
    { signal },
    "Die Verbindung ist gerade nicht abrufbar."
  );

  if (antwort.ok) return { ok: true, wert: antwort.value ?? null };
  if (antwort.error.status === 404) return { ok: true, wert: null };
  return { ok: false, error: antwort.error };
}

async function handeln(
  pfad: string,
  optionen: Parameters<typeof request>[2],
  fallback: string
): Promise<Antwort<Verbindung>> {
  const antwort = await request<Verbindung>(GITHUB_BASE_URL, pfad, optionen, fallback);

  return antwort.ok
    ? { ok: true, wert: antwort.value as Verbindung }
    : { ok: false, error: antwort.error };
}

/** Ein Konto NENNEN — verbunden ist es damit noch nicht. */
export const nenneKonto = (login: string, signal?: AbortSignal) =>
  handeln(
    "/github/me",
    { method: "POST", body: { login }, signal },
    "Das Konto konnte nicht angenommen werden."
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
    "Der Nachweis konnte nicht geprüft werden."
  );

/**
 * Die Repositories neu holen.
 *
 * <strong>Nur auf Auslösung.</strong> Es läuft kein Abgleich im Hintergrund:
 * eine Plattform, die dauerhaft hinterhersieht, tut etwas anderes als eine, die
 * einmal auf Bitte hinsieht.
 */
export const holeNeu = (signal?: AbortSignal) =>
  handeln("/github/me/refresh", { method: "POST", signal }, "Es konnte nichts geholt werden.");

/** Die Verbindung trennen. */
export async function trenne(signal?: AbortSignal): Promise<boolean> {
  const antwort = await request<void>(
    GITHUB_BASE_URL,
    "/github/me",
    { method: "DELETE", signal },
    "Die Verbindung konnte nicht getrennt werden."
  );

  return antwort.ok;
}
