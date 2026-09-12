import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { RESUME_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";
import { deuten } from "../../../shared/api/fehler";

/**
 * Der Lebenslauf — strenger als das Profil, und das ist der ganze Entwurf.
 *
 * Ein Profil ist ein Aushang; ein Lebenslauf nennt echte Arbeitgeber mit Daten
 * — genau das, was ein <em>jetziger</em> Arbeitgeber nicht sehen darf. Deshalb
 * gibt es hier <strong>keinen öffentlichen Schalter</strong>: eine Firma FRAGT,
 * die Person antwortet, und die Freigabe gilt für diese eine Firma.
 */
export interface Station {
  employer: string;
  title: string;
  started_on: string;
  /** `null` heisst „läuft noch" — nicht „unbekannt". */
  ended_on: string | null;
  description: string;
  /**
   * Womit dort gearbeitet wurde — von der Person selbst genannt.
   *
   * Sie machen die Station nicht durchsuchbar: ein Lebenslauf ist einzeln
   * freigegeben (ADR-0020). Suchbar wird eine Fähigkeit erst im Profil, und
   * dorthin kommt sie mit einem Klick.
   */
  technologies: string[];
}

export interface Ausbildung {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
  /** Fehlt bei älteren Einträgen — dann berufliche Ausbildung. */
  kind?: "schule" | "ausbildung";
}

export interface Lebenslauf {
  subject_id: string;
  positions: Station[];
  education: Ausbildung[];
  updated_at: string;
  template: "schlicht" | "klassisch" | "modern";
}

export interface Lebenslaufeingabe {
  positions: Station[];
  education: Ausbildung[];
}

export type Unterlagenart = "zeugnis" | "zertifikat" | "sonstiges" | "lebenslauf";

export interface UnterlageV1 {
  id: string;
  name: string;
  kind: Unterlagenart;
  content_type: string;
  size_bytes: number;
  uploaded_at: string;
}

/**
 * Was in einer eigenen Unterlage gelesen wurde — oder dass noch nie gelesen
 * wurde.
 *
 * <strong>Drei Zustände, und sie dürfen nie zu zweien werden.</strong>
 * `read_at === null` heisst „noch nie gelesen"; `has_text === false` heisst
 * „gelesen, aber es war kein Text darin" (ein abfotografierter Meisterbrief ist
 * ein Bild); eine leere `terms` bei `has_text === true` heisst „gelesen, kein
 * bekanntes Wort gefunden". Die drei zusammenzuziehen hiesse, jemandem
 * stillschweigend zu sagen, in seinem Meisterbrief stehe nichts — ADR-0022 §3.
 *
 * `terms` ist ein <strong>Beleg</strong> und keine Nennung: eine Aussage über
 * dieses Dokument. Suchbar wird ein Wort erst, wenn die Person es ins Profil
 * tippt und <em>speichert</em> (ADR-0033).
 */
export interface Unterlagenfund {
  document_id: string;
  name: string;
  read_at: string | null;
  has_text: boolean;
  terms: string[];
}

/**
 * Was zuletzt gelesen wurde — <strong>ohne zu lesen</strong>.
 *
 * Die Profilseite fragt das beim Laden, und sie darf das, weil dabei nichts
 * geschieht: keine Datei wird geöffnet, kein Erkenner gerufen, keine Zeile
 * geschrieben. Wer hier das Lesen anhängt, macht aus dem Öffnen einer Seite den
 * Hintergrundlauf, den ADR-0004 ausschliesst.
 */
export async function ladeUnterlagenfunde(
  signal?: AbortSignal
): Promise<Antwort<Unterlagenfund[]>> {
  const answer = await request<Unterlagenfund[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents/terms",
    { signal },
    "fehler.unterlagenNichtGeladen"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/**
 * <strong>Der Knopf.</strong> Erst hier wird gelesen.
 *
 * Ein Mensch drückt ihn, und nur dann öffnet der Dienst die Dateien. Kein
 * Auslesen beim Hochladen, kein Nachtlauf über die Ablage: einmal auf Bitte
 * hinsehen ist etwas anderes als dauerhaft hinterhersehen (ADR-0004).
 */
export async function leseUnterlagen(
  signal?: AbortSignal
): Promise<Antwort<Unterlagenfund[]>> {
  const answer = await request<Unterlagenfund[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents/read",
    { method: "POST", signal },
    "fehler.unterlagenNichtGelesen"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

export type Anfragestand = "PENDING" | "GRANTED" | "DECLINED";

/**
 * Eine Anfrage einer Firma.
 *
 * <strong>Die Anfrage ist nicht die Erlaubnis.</strong> `GRANTED` heisst „wurde
 * einmal erteilt", nicht „gilt jetzt" — deshalb hat eine Anfrage weder ein
 * Aktiv-Kennzeichen noch einen Widerrufszeitpunkt. Nach einer Rücknahme bleibt
 * sie `GRANTED` und das Lesen kommt trotzdem leer zurück. Das ist der Entwurf,
 * kein Fehler.
 */
export interface Lebenslaufanfrage {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: Anfragestand;
  created_at: string;
  answered_at?: string | null;
  active?: boolean | null;
}

export type Antwort<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * Der eigene Lebenslauf.
 *
 * <strong>Drei Ausgänge, nicht zwei.</strong> `wert: null` heisst „noch keiner
 * geschrieben" — das ist eine Antwort und kein Fehler, denn `404` ist hier der
 * Normalfall für jeden, der gerade erst angefangen hat. Ein Transportfehler ist
 * etwas anderes und kommt als `error` zurück. Die beiden zusammenzulegen hiesse,
 * jemandem ein leeres Formular zu zeigen, obwohl sein Lebenslauf nur gerade
 * nicht abrufbar ist — und der nächste Speichern-Klick überschriebe ihn.
 */
export async function ladeMeinen(signal?: AbortSignal): Promise<Antwort<Lebenslauf | null>> {
  const answer = await request<Lebenslauf>(
    RESUME_BASE_URL,
    "/resumes/me",
    { signal },
    "fehler.lebenslaufNichtAbrufbar"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

export async function speichereMeinen(
  eingabe: Lebenslaufeingabe,
  signal?: AbortSignal
): Promise<Antwort<Lebenslauf>> {
  const answer = await request<Lebenslauf>(
    RESUME_BASE_URL,
    "/resumes/me",
    { method: "PUT", body: eingabe, signal },
    "fehler.lebenslaufNichtGespeichert"
  );

  return answer.ok
    ? { ok: true, value: answer.value as Lebenslauf }
    : { ok: false, error: answer.error };
}

export async function getMyDocuments(signal?: AbortSignal): Promise<Antwort<UnterlageV1[]>> {
  const answer = await request<UnterlageV1[]>(
    RESUME_BASE_URL,
    "/resumes/me/documents",
    { signal },
    "fehler.unterlagenNichtGeladen"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/** Upload einer Unterlage. */
export async function uploadDocument(
  file: File,
  name: string,
  kind: Unterlagenart,
  signal?: AbortSignal
): Promise<Antwort<UnterlageV1>> {
  const formData = new FormData();
  formData.append("file", file);
  formData.append("name", name);
  formData.append("kind", kind);

  const answer = await request<UnterlageV1>(
    RESUME_BASE_URL,
    "/resumes/me/documents",
    { method: "POST", body: formData, signal },
    "fehler.unterlageNichtHochgeladen"
  );

  if (answer.ok) return { ok: true, value: answer.value };
  const gedeutet = deuten<"invalid" | "too-large" | "too-many" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      413: { reason: "too-large", titel: "fehler.dateiZuGross" },
      415: { reason: "invalid", titel: "fehler.dateiNichtAngenommen" },
      422: { reason: "too-many", titel: "fehler.dateiNichtAngenommen" },
    },
    "invalid"
  );
  return { ok: false, error: gedeutet.error };
}

export interface Unterlageninhalt {
  url: string;
  contentType: string;
}

/** Inhalt einer eigenen Unterlage als Objekt-URL — anzeigen, nicht herunterladen. */
export async function holeEigenenInhalt(
  documentId: string,
  signal?: AbortSignal
): Promise<Antwort<Unterlageninhalt>> {
  return holeInhalt(`/resumes/me/documents/${documentId}/content`, signal);
}

/** Inhalt einer fremden Unterlage — Ledger-geprüft. */
export async function holeFremdenInhalt(
  subjectId: string,
  documentId: string,
  signal?: AbortSignal
): Promise<Antwort<Unterlageninhalt>> {
  return holeInhalt(`/resumes/${subjectId}/documents/${documentId}/content`, signal);
}

async function holeInhalt(
  path: string,
  signal?: AbortSignal
): Promise<Antwort<Unterlageninhalt>> {
  try {
    const response = await fetch(`${RESUME_BASE_URL}${path}`, {
      credentials: "include",
      signal,
    });
    if (!response.ok) {
      return {
        ok: false,
        error: {
          status: response.status,
          title: i18n.t("fehler.unterlagenNichtGeladen"),
          detail: i18n.t("fehler.unterlagenNichtGeladen"),
        },
      };
    }
    const blob = await response.blob();
    return {
      ok: true,
      value: { url: URL.createObjectURL(blob), contentType: blob.type },
    };
  } catch {
    return {
      ok: false,
      error: {
        status: 0,
        title: i18n.t("fehler.keineVerbindungKurz"),
        detail: i18n.t("fehler.dienstNichtErreichbar"),
      },
    };
  }
}

/** Sichtbarer Lebenslauf einer Person — 404 heißt verborgen oder nicht vorhanden. */
export async function ladeSichtbaren(
  subjectId: string,
  signal?: AbortSignal
): Promise<Antwort<Lebenslauf | null>> {
  const answer = await request<Lebenslauf>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}`,
    { signal },
    "fehler.lebenslaufNichtAbrufbar"
  );
  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

export async function ladeSichtbareUnterlagen(
  subjectId: string,
  signal?: AbortSignal
): Promise<Antwort<UnterlageV1[]>> {
  const answer = await request<UnterlageV1[]>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}/documents`,
    { signal },
    "fehler.unterlagenNichtGeladen"
  );
  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/** Diese Datei ist der Lebenslauf. Die vorige Lebenslauf-Datei wird zur Beilage. */
export async function alsLebenslauf(
  documentId: string,
  signal?: AbortSignal
): Promise<Antwort<void>> {
  const answer = await request<void>(
    RESUME_BASE_URL,
    `/resumes/me/documents/${documentId}/as-cv`,
    { method: "PUT", signal },
    "fehler.unterlageNichtHochgeladen"
  );

  return answer.ok
    ? { ok: true, value: undefined }
    : { ok: false, error: answer.error };
}

/** Löschen einer Unterlage. */
export async function deleteDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<Antwort<void>> {
  const answer = await request<void>(
    RESUME_BASE_URL,
    `/resumes/me/documents/${documentId}`,
    { method: "DELETE", signal },
    "fehler.unterlageNichtGeloescht"
  );

  return answer.ok
    ? { ok: true, value: undefined }
    : { ok: false, error: answer.error };
}

/** Vorlage setzen. */
export async function setTemplate(
  template: "schlicht" | "klassisch" | "modern",
  signal?: AbortSignal
): Promise<Antwort<{ template: string }>> {
  const answer = await request<{ template: string }>(
    RESUME_BASE_URL,
    "/resumes/me/template",
    { method: "PUT", body: { template }, signal },
    "fehler.vorlageNichtGesetzt"
  );

  return answer.ok
    ? { ok: true, value: answer.value }
    : { ok: false, error: answer.error };
}

/** Die Anfragen, die an mich gestellt wurden. */
export async function ladeMeineAnfragen(
  signal?: AbortSignal
): Promise<Antwort<Lebenslaufanfrage[]>> {
  const answer = await request<Lebenslaufanfrage[]>(
    RESUME_BASE_URL,
    "/resumes/me/requests",
    { signal },
    "fehler.anfragenNichtAbrufbar"
  );

  return answer.ok
    ? { ok: true, value: answer.value ?? [] }
    : { ok: false, error: answer.error };
}

/**
 * Eine Anfrage beantworten oder eine Freigabe zurücknehmen.
 *
 * <strong>503 ist hier eine eigene Aussage</strong> und darf nicht als
 * gewöhnlicher Fehler durchgereicht werden: der Ledger hat nicht geantwortet,
 * es wurde also NICHTS geändert. Wer daraus „fehlgeschlagen" macht, lässt offen,
 * ob die Rücknahme vielleicht doch griff.
 */
async function handeln(path: string, signal?: AbortSignal): Promise<Antwort<Lebenslaufanfrage>> {
  const answer = await request<Lebenslaufanfrage>(
    RESUME_BASE_URL,
    path,
    { method: "POST", signal },
    "fehler.anfrageNichtBeantwortet"
  );

  if (!answer.ok && answer.error.status === 503) {
    return {
      ok: false,
      error: {
        ...answer.error,
        detail: i18n.t("fehler.ledgerSchweigtOhneAenderung"),
      },
    };
  }

  return answer.ok
    ? { ok: true, value: answer.value as Lebenslaufanfrage }
    : { ok: false, error: answer.error };
}

export const beantworten = (id: string, grant: boolean, signal?: AbortSignal) =>
  handeln(`/resumes/requests/${id}/${grant ? "grant" : "decline"}`, signal);

export const zuruecknehmen = (id: string, signal?: AbortSignal) =>
  handeln(`/resumes/requests/${id}/revoke`, signal);
