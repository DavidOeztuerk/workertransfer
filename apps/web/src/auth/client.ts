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
  tenantId: string;
}

export interface MeResponse {
  user_id: string;
  tenant_id: string;
  roles: readonly string[];
}

export async function login(input: LoginInput): Promise<LoginResult> {
  const res = await fetch(`${API_BASE_URL}/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      email: input.email,
      password: input.password,
      tenant_id: input.tenantId,
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
