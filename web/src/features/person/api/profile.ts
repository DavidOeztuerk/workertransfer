import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { PROFILE_BASE_URL } from "../../../env";
import { PROFILE_VISIBILITY, type ConsentResult, isGranted, setGranted } from "./consent";
import { i18n } from "../../../core/i18n/i18n";

/**
 * profile-service und der Consent-Ledger — zwei Dienste, weil es zwei
 * Wahrheiten sind.
 *
 * Was im Profil steht, gehört profile-service; ob es jemand sehen darf, gehört
 * dem Ledger (ADR-0013). Die Oberfläche zeigt beides nebeneinander, aber sie
 * schreibt an zwei Stellen — und ein Schalter, der nur den Ledger anfasst, ist
 * genau richtig so. Es gibt <strong>kein</strong> Sichtbarkeitsfeld im Profil
 * (ADR-0020); eines wäre eine zweite Wahrheit, und der Client setzte sie.
 */
export interface Profile {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
  updated_at: string;
}

export interface ProfileInput {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string[];
}

/**
 * „Noch keins angelegt" und „nicht ladbar" sind verschiedene Antworten.
 *
 * Der alte Client gab bei <em>jedem</em> Nicht-2xx `null` zurück, und `null`
 * heisst dort „noch keins". Die Seite zeigte dann ein leeres Formular über einer
 * gescheiterten Abfrage — wer darin etwas tippte und speicherte, überschrieb
 * Überschrift, Text, Ort und Fähigkeiten mit leer.
 */
export type ProfileLoad = { ok: true; profile: Profile | null } | { ok: false; error: ApiError };

export type SaveResult = { ok: true; profile: Profile } | { ok: false; error: ApiError };

export async function getMyProfile(signal?: AbortSignal): Promise<ProfileLoad> {
  const answer = await request<Profile | null>(
    PROFILE_BASE_URL,
    "/profiles/me",
    { signal },
    "fehler.eigenesProfilNichtGeladen"
  );
  // 404 heisst hier „noch keins" — bei der EIGENEN Ressource gibt es die
  // Doppeldeutigkeit „versteckt oder nicht vorhanden" nicht, die ADR-0020 für
  // fremde Profile herstellt.
  if (!answer.ok) {
    if (answer.error.status === 404) return { ok: true, profile: null };
    return { ok: false, error: answer.error };
  }
  return { ok: true, profile: answer.value ?? null };
}

export async function saveMyProfile(input: ProfileInput): Promise<SaveResult> {
  const answer = await request<Profile>(
    PROFILE_BASE_URL,
    "/profiles/me",
    {
      method: "PUT",
      // Genau die fünf Felder des Vertrags, in der Schreibweise des Drahtes.
      // `remote_ok` und nicht `remoteOk`: an genau dieser Stelle ging das
      // Häkchen „Remote möglich" einmal still verloren.
      body: {
        headline: input.headline,
        bio: input.bio,
        location: input.location,
        remote_ok: input.remote_ok,
        skills: input.skills,
      },
    },
    "fehler.profilNichtGespeichert"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, profile: answer.value };
}

/**
 * Ein Entwurf für den EIGENEN Profiltext (ADR-0024).
 *
 * Der Text geht an einen fremden Anbieter — das steht an dem Knopf, der das
 * auslöst, und nicht in einer Datenschutzerklärung. Gespeichert wird nichts:
 * der Entwurf lebt im Formular, bis die Person selbst speichert.
 *
 * Mitgeschickt wird nur ihr Wunsch. Der Zusammenhang (Überschrift, Text,
 * Fähigkeiten) kommt serverseitig aus dem gespeicherten Profil — was der Client
 * nicht senden kann, kann er nicht in einen fremden Dienst schleusen.
 */
export type DraftResult =
  | { ok: true; draft: string }
  | { ok: false; message: string };

export async function draftProfileText(wish: string): Promise<DraftResult> {
  const answer = await request<{ draft: string }>(
    PROFILE_BASE_URL,
    "/profiles/me/draft",
    { method: "POST", body: { wish } },
    "fehler.entwurfNichtVerfuegbar"
  );
  if (!answer.ok) {
    // ÜBERSETZT, nicht durchgereicht. `message` ist hier ein fertiger Satz für
    // die Oberfläche und kein Schlüssel — anders als in den `deuten`-Tabellen,
    // die `deuten()` selbst auflöst. Der Unterschied ist einmal übersehen
    // worden, und dann stand `fehler.entwurfNichtVerfuegbarLang` wörtlich im
    // Warnkasten: i18next gibt einen Schlüssel unverändert zurück, wenn ihn
    // niemand nachschlägt.
    if (answer.error.status === 401) {
      return { ok: false, message: i18n.t("fehler.sitzungAbgelaufenLang") };
    }
    // 503 heisst „nicht eingerichtet ODER Anbieter still" — von aussen dasselbe,
    // und beides heisst „später noch einmal", nicht „falsch gemacht". Gemeldet
    // wird die Art des Fehlschlags, nie der Inhalt.
    return { ok: false, message: i18n.t("fehler.entwurfNichtVerfuegbarLang") };
  }
  return { ok: true, draft: answer.value?.draft ?? "" };
}

/** `null` heisst: der Ledger hat nicht geantwortet (siehe `isGranted`). */
export function getVisibility(subjectId: string, signal?: AbortSignal): Promise<boolean | null> {
  return isGranted(subjectId, PROFILE_VISIBILITY, signal);
}

export function setVisibility(subjectId: string, granted: boolean): Promise<ConsentResult> {
  return setGranted(
    subjectId,
    PROFILE_VISIBILITY,
    granted,
    i18n.t("fehler.widerrufProfilEinstellungen")
  );
}
