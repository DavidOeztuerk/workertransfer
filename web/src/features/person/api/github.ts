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
  /**
   * Welche Sprachen im Repository vorkommen — die MENGE, nie ihr Anteil.
   *
   * Ohne Bytes und ohne Prozente: genau daraus rechnete das gelöschte Paket
   * sein „Können" (ADR-0022 §2). Leer, wenn der Dienst ohne GitHub-Token läuft
   * — dann steht nur die Hauptsprache zur Verfügung.
   */
  languages: string[];
  /**
   * Die Topics, die der Besitzer selbst am Repository gesetzt hat.
   *
   * Der stärkste Beleg von allen, weil er eine NENNUNG ist: „kubernetes" hat
   * ein Mensch dorthin geschrieben, kein Zähler abgeleitet.
   */
  topics: string[];
}

export interface Verbindung {
  subject_id: string;
  /**
   * `null`: noch kein Konto genannt.
   *
   * Wer über GitHub anmeldet, nennt keins — GitHub meldet es. Bis der
   * Rücksprung da ist, gibt es hier nichts zu zeigen.
   */
  login: string | null;
  verified: boolean;
  /** Nur in der eigenen Ansicht gefüllt: die Zeile für den Gist. */
  challenge_description: string | null;
  fetched_at: string | null;
  repositories: Repository[];
  /**
   * Konnten fuer JEDES Repository die Sprachen geholt werden?
   *
   * `false` heisst: bei einigen steht nur die Hauptsprache, weil GitHubs
   * Ratenlimit den Aufruf je Repository begrenzt. Die Seite MUSS das sagen —
   * eine unvollstaendige Menge, die sich nicht als solche zu erkennen gibt,
   * ist die stillschweigende Vollstaendigkeit aus ADR-0022 §3: „Python" laese
   * sich dann wie „nur Python".
   */
  languages_complete: boolean;
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
/**
 * Bietet dieser Server die Anmeldung über GitHub an?
 *
 * Eine Aussage über die EINRICHTUNG, nicht über einen Menschen. Sie wird beim
 * Betrachten der Seite geholt, weil die Seite vorher nicht weiss, ob sie einen
 * Knopf oder die Gist-Anleitung zeigen soll — und weil sie, anders als
 * `anmeldungBeginnen`, nichts anlegt.
 */
export async function anmeldungMoeglich(signal?: AbortSignal): Promise<boolean> {
  const answer = await request<{ available?: boolean }>(
    GITHUB_BASE_URL,
    "/github/oauth",
    { signal }
  );

  return answer.ok && answer.value?.available === true;
}

/**
 * Wohin der Browser für die Anmeldung bei GitHub geht.
 *
 * NUR AUF KLICK. Der Aufruf legt eine Verbindung an, wenn es noch keine gibt —
 * beim blossen Betrachten der Seite entstünde sonst eine Zeile für jeden, der
 * einmal hereingeschaut hat.
 *
 * `null` heisst: keine Anmeldung eingerichtet. Dann bleibt der Gist der Weg.
 */
export async function anmeldungBeginnen(
  signal?: AbortSignal
): Promise<string | null> {
  const answer = await request<{ url?: string | null }>(
    GITHUB_BASE_URL,
    "/github/me/oauth/start",
    { method: "POST", signal }
  );

  return answer.ok ? (answer.value?.url ?? null) : null;
}

/** Die Rückkehr von GitHub: Einmalcode und Zustand einlösen. */
export const anmeldungAbschliessen = (
  code: string,
  state: string,
  signal?: AbortSignal
) =>
  handeln(
    "/github/me/oauth/finish",
    { method: "POST", body: { code, state }, signal },
    "fehler.nachweisNichtGeprueft"
  );

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
