// Cookie-based auth client for the identity-service. The backend sets HTTP-only
// `access` and `refresh` cookies on login (POST /auth/login) and authorises via
// the `access` cookie on GET /me; the browser jar is carried with
// `credentials: "include"`. LoginResult is a discriminated union (never throws on
// a bad credential — the caller renders the German message) so the UI stays out
// of the exception-control-flow business.

import { API_BASE_URL } from "../env";

export { API_BASE_URL };

export type LoginResult = { ok: true } | { ok: false; message: string };

export interface LoginInput {
  email: string;
  password: string;
}

export interface MeResponse {
  user_id: string;
  // The user's own address. Needed to show the company domain that would be
  // derived from it — the server derives it either way, this is only display.
  email: string | null;
  // null while acting as a person. A tenant is a company (ADR-0017) and only
  // becomes active after switching into one the user is a member of.
  tenant_id: string | null;
  roles: readonly string[];
}

export interface RegisterInput {
  email: string;
  password: string;
  displayName: string;
}

export type RegisterResult = { ok: true } | { ok: false; message: string };
// `expired` is its own case so the UI can offer "resend" instead of a dead end.
export type VerifyResult = { ok: true } | { ok: false; expired: boolean; message: string };
export type CreateCompanyResult =
  | { ok: true; company: Company }
  | { ok: false; message: string };

export interface Company {
  id: string;
  name: string;
  domain: string;
}

export interface Membership extends Company {
  role: string;
}

// Mirrors the server list. Only decides whether the entry point is OFFERED —
// the refusal itself always comes from the server (422), never from here.
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

export async function login(input: LoginInput): Promise<LoginResult> {
  const res = await fetch(`${API_BASE_URL}/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: input.email,
      password: input.password,
    }),
  });
  if (res.ok) {
    return { ok: true };
  }
  let message = "Anmeldung fehlgeschlagen";
  try {
    const body = (await res.json()) as { detail?: string };
    if (typeof body.detail === "string" && body.detail.length > 0) message = body.detail;
  } catch {
    // keep default German message
  }
  return { ok: false, message };
}

export async function fetchMe(): Promise<MeResponse | null> {
  const res = await fetch(`${API_BASE_URL}/me`, { credentials: "include" });
  if (!res.ok) return null;
  return (await res.json()) as MeResponse;
}

// Idempotent by design on the backend (204 even without a refresh cookie), so a
// failed call still leaves the caller free to drop its cached session.
export async function logout(): Promise<void> {
  await fetch(`${API_BASE_URL}/auth/logout`, { method: "POST", credentials: "include" });
}

async function detail(res: Response, fallback: string): Promise<string> {
  try {
    const body = (await res.json()) as { detail?: string };
    if (typeof body.detail === "string" && body.detail.length > 0) return body.detail;
  } catch {
    // keep the German fallback
  }
  return fallback;
}

export async function registerUser(input: RegisterInput): Promise<RegisterResult> {
  const res = await fetch(`${API_BASE_URL}/auth/register`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: input.email,
      password: input.password,
      display_name: input.displayName,
    }),
  });
  // A known address answers 201 too — the server sends the real owner a warning
  // instead of telling us they exist. So there is no "already taken" branch here.
  if (res.ok) return { ok: true };
  return { ok: false, message: await detail(res, "Registrierung fehlgeschlagen") };
}

export async function verifyEmail(token: string): Promise<VerifyResult> {
  const res = await fetch(`${API_BASE_URL}/auth/verify-email`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ token }),
  });
  if (res.ok) return { ok: true };
  const expired = res.status === 410;
  return {
    ok: false,
    expired,
    message: expired
      ? "Dieser Bestätigungslink ist abgelaufen."
      : "Dieser Bestätigungslink ist ungültig.",
  };
}

// Always resolves: the endpoint answers 202 whether or not anything was sent,
// so there is nothing for the caller to distinguish.
export async function resendVerification(email: string): Promise<void> {
  await fetch(`${API_BASE_URL}/auth/resend-verification`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email }),
  });
}

export async function createCompany(name: string): Promise<CreateCompanyResult> {
  // No domain in the body — the server derives it from the confirmed address.
  const res = await fetch(`${API_BASE_URL}/companies`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ name }),
  });
  if (res.ok) return { ok: true, company: (await res.json()) as Company };
  return { ok: false, message: await detail(res, "Unternehmen konnte nicht angelegt werden") };
}

export async function listCompanies(): Promise<Membership[]> {
  const res = await fetch(`${API_BASE_URL}/me/companies`, { credentials: "include" });
  if (!res.ok) return [];
  return (await res.json()) as Membership[];
}

export async function switchCompany(companyId: string): Promise<boolean> {
  const res = await fetch(`${API_BASE_URL}/auth/company/${companyId}`, {
    method: "POST",
    credentials: "include",
  });
  return res.ok;
}
