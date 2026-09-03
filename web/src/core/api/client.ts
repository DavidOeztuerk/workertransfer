import type { ApiError } from "../store/thunkHelpers";
import { i18n } from "../i18n/i18n";
import type { Fehlerschluessel } from "../../shared/api/fehler";

/**
 * Der eine Weg nach draussen.
 *
 * `fetch` und nicht axios: die Dienste antworten in einer Gestalt (RFC 9457),
 * die Sitzung hängt an einem httpOnly-Cookie, und mehr braucht es hier nicht.
 * Eine Bibliothek dazwischen würde vor allem eine zweite Fehlergestalt
 * mitbringen — und zwei Gestalten für "was ging schief" sind genau die
 * Abweichung, die später in der Oberfläche zugekleistert wird.
 *
 * `credentials: "include"` steht an EINER Stelle. Der Browser sieht das
 * Zugriffstoken nie (es ist httpOnly) und kann es nur als Cookie zurückgeben;
 * wer den Aufruf ohne diese Zeile schreibt, bekommt ein 401, das nach einer
 * abgelaufenen Sitzung aussieht und keines ist.
 */
export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
}

/** Was der Aufrufer bekommt: die Antwort ODER die Aussage, was fehlte. */
export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * Der Netzfehler als FUNKTION und nicht als Konstante: ein `const` entstünde
 * beim Laden des Moduls, also bevor jemand eine Sprache wählen konnte.
 */
function netzfehler(): ApiError {
  return {
    status: 0,
    title: i18n.t("fehler.keineVerbindungKurz"),
    detail: i18n.t("fehler.dienstNichtErreichbar"),
  };
}

/**
 * Liest ein Problemdokument aus, ohne ihm zu vertrauen.
 *
 * Ein Dienst kann auch mit einem leeren Rumpf oder mit HTML antworten (etwa ein
 * Proxy dazwischen). Dann steht hier ein brauchbarer Satz statt eines
 * Parserfehlers, der in der Oberfläche als weisse Seite ankäme.
 */
async function problem(
  response: Response,
  fallbackSchluessel: Fehlerschluessel
): Promise<ApiError> {
  // Übersetzt bei der ANTWORT, nicht beim Aufbau der Aufrufstelle: die
  // Vorgabewerte stehen in Signaturen und entstünden beim Laden des Moduls.
  const fallback = i18n.t(fallbackSchluessel);
  try {
    const body = (await response.json()) as Partial<ApiError> & { detail?: string };
    return {
      status: response.status,
      title: typeof body.title === "string" ? body.title : fallback,
      detail: typeof body.detail === "string" && body.detail !== "" ? body.detail : fallback,
      correlationId: typeof body.correlationId === "string" ? body.correlationId : undefined,
    };
  } catch {
    return { status: response.status, title: fallback, detail: fallback };
  }
}

/** Ein Aufruf. Wirft nicht — ein Statuscode ist eine Antwort, keine Störung. */
export async function request<T>(
  baseUrl: string,
  path: string,
  options: RequestOptions = {},
  fallbackMessage: Fehlerschluessel = "fehler.anfrageFehlgeschlagen"
): Promise<ApiResult<T>> {
  const { method = "GET", body, signal } = options;

  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      method,
      credentials: "include",
      signal,
      headers: body === undefined ? undefined : { "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    return { ok: false, error: netzfehler() };
  }

  if (!response.ok) {
    return { ok: false, error: await problem(response, fallbackMessage) };
  }

  // 204 und ein leerer Rumpf sind gültige Erfolge und dürfen den Parser nicht
  // in einen Fehler laufen lassen.
  if (response.status === 204 || response.headers.get("content-length") === "0") {
    return { ok: true, value: undefined as T };
  }

  try {
    return { ok: true, value: (await response.json()) as T };
  } catch {
    return { ok: true, value: undefined as T };
  }
}

/**
 * Für einen Thunk: gibt den Wert zurück oder wirft den `ApiError` in
 * `rejectWithValue`. Damit steht die Fehlerbehandlung einmal statt je Thunk.
 */
export async function unwrap<T>(
  result: Promise<ApiResult<T>>,
  reject: (error: ApiError) => never
): Promise<T> {
  const answer = await result;
  if (answer.ok) return answer.value;
  return reject(answer.error);
}
