// Base URLs of the services. Vite injects the VITE_ variables (typed by
// vite/client); they are optional, so under noUncheckedIndexedAccess the
// literal access is `string | undefined`.
//
// One variable per service instead of one gateway: the services run on ports
// of their own and have databases of their own (ADR-0004). A shared prefix
// would suggest a merger that does not exist.
//
// The key strings (`API_BASE_URL` …), the `VITE_*` names and `__WT_CONFIG__`
// are a contract with `public/config.js` and the Helm chart, which writes that
// file — renaming one here leaves the chart's value unread.

/**
 * The fallback takes the page's host, not `127.0.0.1`.
 *
 * Cookies treat `localhost` and `127.0.0.1` as different hosts, but ignore
 * ports. A fixed fallback on `127.0.0.1` therefore yields no connection errors
 * for a page on `localhost` but something worse: requests without a session
 * cookie that silently answer 401. That is exactly how the E2E journey once
 * failed — the service ran, the token was valid, and still nobody was signed
 * in.
 */
function fallback(port: number): string {
  const host = typeof window === "undefined" ? "127.0.0.1" : window.location.hostname;
  return `http://${host}:${port}`;
}

/**
 * Runtime configuration, set by `public/config.js` BEFORE the module bundle.
 *
 * It exists because of a property of `vite build`: the `VITE_` variables are
 * inserted into the bundle at BUILD time. A built image therefore carries its
 * URLs baked in — and could not be the same in two environments. For docker
 * compose that does not matter (the dev server runs there and reads the
 * environment at start), for a Helm chart it does: there "one artifact, many
 * environments" is the whole idea (ADR-0028).
 *
 * The default in the repository is an empty object and so changes nothing.
 * Only the chart lays a real version over it.
 */
type RuntimeConfig = Partial<Record<string, string>>;

function runtime(key: string): string | undefined {
  if (typeof window === "undefined") return undefined;
  const config = (window as { __WT_CONFIG__?: RuntimeConfig }).__WT_CONFIG__;
  return config?.[key];
}

/**
 * Three steps, in this order: runtime value, build-time value, port fallback.
 *
 * The order is the actual statement. The runtime value wins because it alone
 * knows where the bundle is actually being served right now.
 */
function resolve(key: string, raw: string | undefined, port: number): string {
  const live = runtime(key);
  if (typeof live === "string" && live.length > 0) return live;
  return typeof raw === "string" && raw.length > 0 ? raw : fallback(port);
}

export const API_BASE_URL = resolve("API_BASE_URL", import.meta.env.VITE_API_BASE_URL, 8001);
export const CONSENT_BASE_URL = resolve(
  "CONSENT_BASE_URL",
  import.meta.env.VITE_CONSENT_BASE_URL,
  8002
);
export const PROFILE_BASE_URL = resolve(
  "PROFILE_BASE_URL",
  import.meta.env.VITE_PROFILE_BASE_URL,
  8003
);
export const RESUME_BASE_URL = resolve(
  "RESUME_BASE_URL",
  import.meta.env.VITE_RESUME_BASE_URL,
  8004
);
export const PORTFOLIO_BASE_URL = resolve(
  "PORTFOLIO_BASE_URL",
  import.meta.env.VITE_PORTFOLIO_BASE_URL,
  8005
);
export const JOBS_BASE_URL = resolve("JOBS_BASE_URL", import.meta.env.VITE_JOBS_BASE_URL, 8006);
export const APPLICATIONS_BASE_URL = resolve(
  "APPLICATIONS_BASE_URL",
  import.meta.env.VITE_APPLICATIONS_BASE_URL,
  8007
);
export const COMPANIES_BASE_URL = resolve(
  "COMPANIES_BASE_URL",
  import.meta.env.VITE_COMPANIES_BASE_URL,
  8008
);
export const TRANSFER_BASE_URL = resolve(
  "TRANSFER_BASE_URL",
  import.meta.env.VITE_TRANSFER_BASE_URL,
  8009
);
export const GITHUB_BASE_URL = resolve(
  "GITHUB_BASE_URL",
  import.meta.env.VITE_GITHUB_BASE_URL,
  // 8011, not 8010 — 8010 belongs to notification-service. The offset came
  // when that service was added and went unnoticed because NOBODY else uses
  // it: the UI does not talk to notification-service at all, the notification
  // settings live at identity. Whoever opened /github sent their account to
  // the wrong service.
  8011
);
export const SCOUT_BASE_URL = resolve(
  "SCOUT_BASE_URL",
  import.meta.env.VITE_SCOUT_BASE_URL,
  8012
);
export const ADVISOR_BASE_URL = resolve(
  "ADVISOR_BASE_URL",
  import.meta.env.VITE_ADVISOR_BASE_URL,
  8013
);

export const ASSESSMENT_BASE_URL = resolve(
  "ASSESSMENT_BASE_URL",
  import.meta.env.VITE_ASSESSMENT_BASE_URL,
  8014
);

export const MESSAGES_BASE_URL = resolve(
  "MESSAGES_BASE_URL",
  import.meta.env.VITE_MESSAGES_BASE_URL,
  8015
);

/**
 * A setting without a fallback: runtime value, then build-time value — empty
 * means the feature is off. Used by the file pickers (ADR-0054), whose keys an
 * operator registers; without them no button appears.
 */
function optional(key: string, raw: string | undefined): string {
  const live = runtime(key);
  if (typeof live === "string" && live.length > 0) return live;
  return typeof raw === "string" ? raw : "";
}

/** Google Drive through the Picker, `drive.file` only (ADR-0054). */
export const GOOGLE_PICKER = {
  clientId: optional("GOOGLE_PICKER_CLIENT_ID", import.meta.env.VITE_GOOGLE_PICKER_CLIENT_ID),
  apiKey: optional("GOOGLE_PICKER_API_KEY", import.meta.env.VITE_GOOGLE_PICKER_API_KEY),
  appId: optional("GOOGLE_PICKER_APP_ID", import.meta.env.VITE_GOOGLE_PICKER_APP_ID),
};

/** Dropbox through the Chooser — an app key, no OAuth (ADR-0054). */
export const DROPBOX_APP_KEY = optional("DROPBOX_APP_KEY", import.meta.env.VITE_DROPBOX_APP_KEY);
