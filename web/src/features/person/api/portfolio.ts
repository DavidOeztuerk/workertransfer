import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { PORTFOLIO_BASE_URL } from "../../../env";
import { i18n } from "../../../core/i18n/i18n";

/**
 * Die Arbeitsproben — ein Schaufenster mit eigener Freigabe.
 *
 * <strong>Getrennt vom Profil, und das ist Absicht:</strong> jemand kann
 * ansprechbar sein, ohne seine Arbeiten zu zeigen. Zwei Freigaben, zwei
 * Entscheidungen.
 */
export interface Arbeit {
  title: string;
  summary: string;
  /** `null` heisst „kein Link", nicht „leerer Link". */
  url: string | null;
  role: string;
  year: number | null;
  /** Name einer hochgeladenen Datei — vom Server vergeben, nie selbst gewählt. */
  attachment: string | null;
}

export interface Schaufenster {
  subject_id: string;
  items: Arbeit[];
  updated_at: string;
}

export type Antwort<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * Das eigene Schaufenster.
 *
 * Wie beim Lebenslauf drei Ausgänge: `wert: null` heisst „noch nichts
 * eingetragen" (ein `404`, der Normalfall), ein Transportfehler ist etwas
 * anderes. Zusammengelegt zeigte die Seite eine leere Liste, obwohl die Arbeiten
 * nur gerade nicht abrufbar sind — und das nächste Speichern löschte sie.
 */
export async function ladeMeines(signal?: AbortSignal): Promise<Antwort<Schaufenster | null>> {
  const answer = await request<Schaufenster>(
    PORTFOLIO_BASE_URL,
    "/portfolios/me",
    { signal },
    "fehler.arbeitenNichtAbrufbar"
  );

  if (answer.ok) return { ok: true, value: answer.value ?? null };
  if (answer.error.status === 404) return { ok: true, value: null };
  return { ok: false, error: answer.error };
}

/**
 * Speichert das GANZE Feld.
 *
 * Der Dienst kennt keine Teiländerung: eine Arbeit hat keine eigene Kennung,
 * ihre Adresse ist ihre Stelle in der Liste. Wer eine ändert, schickt alle.
 */
export async function speichereMeines(
  arbeiten: Arbeit[],
  signal?: AbortSignal
): Promise<Antwort<Schaufenster>> {
  const answer = await request<Schaufenster>(
    PORTFOLIO_BASE_URL,
    "/portfolios/me",
    { method: "PUT", body: { items: arbeiten }, signal },
    "fehler.arbeitenNichtGespeichert"
  );

  return answer.ok
    ? { ok: true, value: answer.value as Schaufenster }
    : { ok: false, error: answer.error };
}

export interface Anhang {
  name: string;
  content_type: string;
  size: number;
}

/**
 * Eine Datei anhängen.
 *
 * <strong>Der lokale Dateiname wandert nicht mit.</strong> Der Server vergibt
 * den Namen; die Oberfläche zeigt deshalb nur, <em>dass</em> eine Datei hängt
 * und wohin sie zeigt. Ein angezeigter lokaler Name wäre eine Behauptung über
 * etwas, das der Server gar nicht kennt.
 *
 * Eigener Aufruf statt <c>request</c>: hier geht ein <c>FormData</c> über die
 * Leitung, kein JSON, und der Browser muss die Grenzmarkierung selbst setzen.
 */
export async function haengeAn(file: File, signal?: AbortSignal): Promise<Antwort<Anhang>> {
  const body = new FormData();
  body.append("file", file);

  let answer: Response;
  try {
    answer = await fetch(`${PORTFOLIO_BASE_URL}/portfolios/me/attachments`, {
      method: "POST",
      credentials: "include",
      body: body,
      signal,
    });
  } catch {
    return {
      ok: false,
      error: {
        status: 0,
        title: i18n.t("fehler.keineVerbindungKurz"),
        detail: i18n.t("fehler.keineVerbindung"),
      },
    };
  }

  if (!answer.ok) {
    return {
      ok: false,
      error: {
        status: answer.status,
        title: i18n.t("fehler.dateiAbgelehnt"),
        detail: i18n.t("fehler.dateiNichtAngenommen"),
      },
    };
  }

  return { ok: true, value: (await answer.json()) as Anhang };
}

/** Wo eine Datei liegt. Zusammengesetzt wie serverseitig: Person und Name. */
export const anhangUrl = (subjectId: string, name: string): string =>
  `${PORTFOLIO_BASE_URL}/portfolios/${subjectId}/attachments/${name}`;
