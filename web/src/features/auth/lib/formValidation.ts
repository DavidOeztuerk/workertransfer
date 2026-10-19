/**
 * What the registration form can already see in the browser.
 *
 * The browser decides nothing — the refusal is spoken by the server (422).
 * Needed because the form carries `noValidate`, which makes `type="email"`
 * ineffective.
 */

/**
 * The minimum length as the server knows it (`PasswordPolicy`). Deliberately
 * twice: the server stays the authority, a deviation only costs a hint at the
 * wrong time.
 */
export const PASSWORD_MIN_LENGTH = 12;

/** The upper bound from bcrypt: 72 BYTES, not characters — "ä" is two. */
export const PASSWORD_MAX_BYTES = 72;

/** How many bytes this password takes in UTF-8. */
export function passwordBytes(password: string): number {
  return new TextEncoder().encode(password).length;
}

/**
 * Does this look like a deliverable address? Deliberately generous — it is
 * meant to catch the typo, not to rebuild RFC 5321.
 */
export function looksLikeAddress(email: string): boolean {
  const trimmed = email.trim();

  if (trimmed.length === 0 || trimmed.length > 254) return false;
  if (/\s/.test(trimmed)) return false;

  const parts = trimmed.split("@");
  if (parts.length !== 2) return false;

  const local = parts[0] ?? "";
  const domain = parts[1] ?? "";
  if (local.length === 0 || domain.length === 0) return false;

  const dot = domain.lastIndexOf(".");
  return dot > 0 && dot < domain.length - 1;
}

/** The catalogue key for what is wrong with the address — or `null`. */
export function emailError(email: string): string | null {
  // An empty field is not an error yet.
  if (email.trim().length === 0) return null;

  return looksLikeAddress(email) ? null : "register.emailInvalid";
}

/** The catalogue key for what is wrong with the password — or `null`. */
export function passwordError(password: string): string | null {
  if (password.length === 0) return null;
  if (password.length < PASSWORD_MIN_LENGTH) return "register.passwordTooShort";
  if (passwordBytes(password) > PASSWORD_MAX_BYTES) return "register.passwordTooLong";

  return null;
}
