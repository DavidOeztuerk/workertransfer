import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";

/**
 * Who is acting right now.
 *
 * `tenantId === null` means "acting as a person", NOT "missing" (ADR-0017). On
 * a transfer market the person without a company is the normal case, and
 * mixing the two states would be exactly the defect that later shows a company
 * view to someone who has none.
 */
export interface Session {
  userId: string;
  email: string;
  tenantId: string | null;
  /**
   * The language that stands on the ACCOUNT.
   *
   * It travels along so the choice survives the browser: whoever chose German
   * on an English machine sees German again after signing in on another
   * device. Local storage knows nothing about that.
   */
  language: string;
  /**
   * The name the person appears under.
   *
   * For the initials in the header. It could not be derived from the address:
   * `max.werber@…` would give "M" and not "MW".
   */
  displayName: string;
  /** Legal given name, or empty. Never in the token, never to the AI as an address. */
  givenName: string;
  /** Legal family name, or empty. */
  familyName: string;
  /**
   * Which working world the person stands in, or `null` for "not stated".
   * Only what the UI offers follows from it (ADR-0039).
   */
  occupationalField: OccupationalFieldChoice;
}

/**
 * What `GET /auth/session` says about the state — independent of the user object.
 *
 * `renewable` means: access dead, refresh cookie present. The UI must then call
 * `POST /auth/refresh`, or the session looks dead after 15 minutes.
 */
export type SessionWireState = "active" | "renewable" | "anonymous";

/** A company someone may act for. */
export interface Membership {
  id: string;
  name: string;
  role: string;
}

/**
 * The session state knows THREE cases, not two.
 *
 * `unknown` is the state before the first answer and must not be drawn as
 * "signed out": otherwise the sign-in prompt briefly flashes on every page load
 * although the person is signed in.
 */
export type SessionStatus = "unknown" | "authenticated" | "anonymous";
