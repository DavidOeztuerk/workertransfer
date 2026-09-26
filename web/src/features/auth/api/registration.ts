import { API_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";
import { i18n } from "../../../core/i18n/i18n";
import type { OccupationalField } from "../../../shared/lib/occupationalFields";

/**
 * Register, confirm, send again — the three ways into an account.
 *
 * They stand here and not in the `authSlice`: none of them changes the
 * session. Whoever registers is <em>not</em> signed in afterwards but
 * `pending`; whoever confirms has an active account and still has to sign in.
 * A thunk that writes nothing into the state would be mere ceremony.
 */

export interface RegistrationInput {
  email: string;
  password: string;
  displayName: string;
  givenName?: string;
  familyName?: string;
  /**
   * Set means: a company registers here.
   *
   * The company only comes into being with the confirmation of the address —
   * an unconfirmed address proves no domain (ADR-0019). Until then the SERVER
   * remembers the intent, not the browser: confirmation mails are often opened
   * on another device.
   */
  companyName?: string;
  /** Optional (ADR-0039). Not set means "not stated". */
  occupationalField?: OccupationalField;
}

export type RegistrationResult = { ok: true } | { ok: false; message: string };

/**
 * The success case has THREE forms, not one: confirmed · confirmed with a
 * company · confirmed without a company, with the reason. Without the third
 * the page shows "all good" while half the intent has evaporated.
 */
export type VerificationResult =
  | { ok: true; company?: string; companyError?: string }
  | { ok: false; expired: boolean; message: string };

export type ResendResult = { ok: true } | { ok: false };

/**
 * Mirrors the server's list. Decides only whether the path is OFFERED — the
 * refusal itself is always spoken by the server (422), never by this file.
 */
const PUBLIC_EMAIL_DOMAINS = new Set([
  "aol.com",
  "freenet.de",
  "gmail.com",
  "googlemail.com",
  "gmx.at",
  "gmx.ch",
  "gmx.de",
  "gmx.net",
  "hotmail.com",
  "icloud.com",
  "mail.com",
  "me.com",
  "outlook.com",
  "proton.me",
  "protonmail.com",
  "t-online.de",
  "web.de",
  "yahoo.com",
  "yahoo.de",
  "yandex.com",
  "zoho.com",
]);

export function emailDomain(email: string): string {
  return email.split("@")[1]?.trim().toLowerCase() ?? "";
}

export function isPublicEmailDomain(email: string): boolean {
  return PUBLIC_EMAIL_DOMAINS.has(emailDomain(email));
}

/**
 * Create an account.
 *
 * <strong>A known address answers 201 as well.</strong> The server sends the
 * real owner a warning instead of telling the asker that it exists — that is
 * why there is no "already taken" branch here, and none may be added. It would
 * be an enumeration channel.
 */
export async function register(input: RegistrationInput): Promise<RegistrationResult> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/auth/register",
    {
      method: "POST",
      body: {
        email: input.email,
        password: input.password,
        // snake_case, because the wire demands it: the server binds
        // `display_name`. With `displayName` the input does not arrive —
        // measured, and the defect looked like an empty form.
        display_name: input.displayName,
        ...(input.givenName ? { given_name: input.givenName } : {}),
        ...(input.familyName ? { family_name: input.familyName } : {}),
        // Only send it along when there is an intent: `company_name` is optional
        // in the contract, and a `null` in the body would be a statement nobody
        // made.
        ...(input.companyName !== undefined
          ? { company_name: input.companyName }
          : {}),
        // Only send it along when one was chosen.
        ...(input.occupationalField !== undefined
          ? { occupational_field: input.occupationalField }
          : {}),
      },
    },
    "errors.registrationFailed"
  );

  return answer.ok ? { ok: true } : { ok: false, message: answer.error.detail };
}

interface VerificationBody {
  company?: string;
  company_error?: string;
}

/**
 * Confirm the address — and learn what became of the company.
 *
 * A body that cannot be read does <strong>not</strong> make the confirmation
 * invalid: it has long happened on the server, and turning a 200 into a
 * failure afterwards would be the wrong answer. `request` returns `ok: true`
 * with an undefined value for that.
 */
export async function verifyEmail(token: string): Promise<VerificationResult> {
  const answer = await request<VerificationBody>(
    API_BASE_URL,
    "/auth/verify-email",
    { method: "POST", body: { token } },
    "errors.verificationLinkInvalid"
  );

  if (answer.ok) {
    const body = answer.value ?? {};
    return {
      ok: true,
      ...(typeof body.company === "string" ? { company: body.company } : {}),
      ...(typeof body.company_error === "string"
        ? { companyError: body.company_error }
        : {}),
    };
  }

  // 410 is a case of its own, so the page can offer "send a new link" instead
  // of leading into a dead end. An INVALID link does not become valid on the
  // second attempt either — there is nothing to offer there.
  const expired = answer.error.status === 410;
  return {
    ok: false,
    expired,
    message: expired
      ? i18n.t("errors.verificationLinkExpired")
      : i18n.t("errors.verificationLinkInvalid"),
  };
}

/**
 * Request a new confirmation link.
 *
 * <strong>A non-2xx counts as a failure too.</strong> The old version caught
 * only the transport error; a `429` from the brake (`/auth/resend-verification`
 * is throttled to 3/min) produced the promise "on its way" while nothing was on
 * its way.
 *
 * This is no enumeration channel: the answer is the same for known and unknown
 * addresses (202), and a 429 speaks about the origin of the request, not about
 * the address in the body.
 */
export async function resendVerification(email: string): Promise<ResendResult> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/auth/resend-verification",
    { method: "POST", body: { email } },
    "errors.mailNotRequested"
  );
  return answer.ok ? { ok: true } : { ok: false };
}
