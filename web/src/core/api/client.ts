import type { ApiError } from "../store/thunkHelpers";
import { i18n } from "../i18n/i18n";
import type { ErrorKey } from "../../shared/api/errors";
import { skipsRefresh, refreshOnce } from "./refreshGate";

/**
 * The one way out.
 *
 * `fetch` and not axios: the services answer in one shape (RFC 9457), the
 * session hangs on an httpOnly cookie, and nothing more is needed here. A
 * library in between would mainly bring a second error shape — and two shapes
 * for "what went wrong" are exactly the divergence that later gets papered
 * over in the UI.
 *
 * `credentials: "include"` stands in ONE place. The browser never sees the
 * access token (it is httpOnly) and can only return it as a cookie; whoever
 * writes the call without this line gets a 401 that looks like an expired
 * session and is not one.
 */
export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  signal?: AbortSignal;
  /**
   * No 401→refresh. For the refresh itself and for auth paths whose 401 is
   * the actual refusal (wrong password, dead refresh).
   */
  skipRefresh?: boolean;
}

/** What the caller gets: the answer OR the statement of what was missing. */
export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/**
 * The network error as a FUNCTION and not as a constant: a `const` would be
 * built when the module loads, i.e. before anybody could choose a language.
 */
function networkError(): ApiError {
  return {
    status: 0,
    title: i18n.t("errors.noConnectionShort"),
    detail: i18n.t("errors.serviceUnreachable"),
  };
}

/**
 * Reads a problem document without trusting it.
 *
 * A service may also answer with an empty body or with HTML (a proxy in
 * between, say). Then a usable sentence stands here instead of a parser error
 * that would reach the UI as a white page.
 */
async function problem(
  response: Response,
  fallbackKey: ErrorKey
): Promise<ApiError> {
  // Translated at the ANSWER, not when the call site is built: the defaults
  // stand in signatures and would be evaluated when the module loads.
  const fallback = i18n.t(fallbackKey);
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

type SessionExpiredHandler = () => void;
const expiredHandlers = new Set<SessionExpiredHandler>();

/**
 * Registers a listener that is called when a session has expired and could no
 * longer be renewed (e.g. a Docker restart or a dead refresh).
 */
export function onSessionExpired(handler: SessionExpiredHandler): () => void {
  expiredHandlers.add(handler);
  return () => {
    expiredHandlers.delete(handler);
  };
}

export function notifySessionExpired(): void {
  for (const handler of Array.from(expiredHandlers)) {
    try {
      handler();
    } catch {
      // An error in a handler must not break the HTTP flow.
    }
  }
}

/** One call. Never throws — a status code is an answer, not a fault. */
export async function request<T>(
  baseUrl: string,
  path: string,
  options: RequestOptions = {},
  fallbackMessage: ErrorKey = "errors.requestFailed"
): Promise<ApiResult<T>> {
  return send(baseUrl, path, options, fallbackMessage, false);
}

async function send<T>(
  baseUrl: string,
  path: string,
  options: RequestOptions,
  fallbackMessage: ErrorKey,
  alreadyRefreshed: boolean
): Promise<ApiResult<T>> {
  const { method = "GET", body, signal, skipRefresh } = options;

  let response: Response;
  try {
    const form = typeof FormData !== "undefined" && body instanceof FormData;
    response = await fetch(`${baseUrl}${path}`, {
      method,
      credentials: "include",
      signal,
      headers: body === undefined || form ? undefined : { "content-type": "application/json" },
      body: body === undefined ? undefined : form ? (body as FormData) : JSON.stringify(body),
    });
  } catch {
    return { ok: false, error: networkError() };
  }

  if (
    response.status === 401 &&
    !alreadyRefreshed &&
    skipRefresh !== true &&
    !skipsRefresh(path)
  ) {
    // Access JWT dead, refresh cookie maybe still there. One flight, repeat the
    // original once. Tokens stay in the cookie — no bearer, no storage.
    const refreshed = await refreshOnce(baseUrl);
    if (refreshed) {
      return send(baseUrl, path, options, fallbackMessage, true);
    }
    // Refresh failed: the session is definitely dead.
    notifySessionExpired();
  } else if (response.status === 401 && alreadyRefreshed) {
    // Even after a successful renewal the service answers 401.
    notifySessionExpired();
  }

  if (!response.ok) {
    return { ok: false, error: await problem(response, fallbackMessage) };
  }

  // 204 and an empty body are valid successes and must not run the parser
  // into an error.
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
 * For a thunk: returns the value or throws the `ApiError` into
 * `rejectWithValue`. That way error handling stands once instead of per thunk.
 */
export async function unwrap<T>(
  result: Promise<ApiResult<T>>,
  reject: (error: ApiError) => never
): Promise<T> {
  const answer = await result;
  if (answer.ok) return answer.value;
  return reject(answer.error);
}
