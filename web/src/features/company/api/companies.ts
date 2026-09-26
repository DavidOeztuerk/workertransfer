// Client for companies-service.
//
// The public profile needs no sign-in — it is a company's self-presentation,
// and putting it behind a sign-in would contradict its purpose.

import { request } from "../../../core/api/client";
import { COMPANIES_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";
import { i18n } from "../../../core/i18n/i18n";

export interface CompanyProfile {
  tenant_id: string;
  /** The address of the careers page — assigned by the server, immutable. */
  slug: string;
  display_name: string;
  about: string;
  website: string | null;
  locations: string[];
  benefits: string[];
  line1: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
  updated_at: string;
}

export interface CompanyProfileInput {
  display_name: string;
  about: string;
  website: string | null;
  locations: string[];
  benefits: string[];
  line1?: string;
  postal_code?: string;
  city?: string;
  country?: string;
  phone?: string;
}

export type SaveResult =
  | { ok: true; profile: CompanyProfile }
  | Failure<"no-company" | "invalid" | "offline">;

export type OwnProfileResult =
  | { ok: true; profile: CompanyProfile | null }
  | Failure<"unavailable">;

export type SlugResult =
  | { ok: true; profile: CompanyProfile }
  | Failure<"not-found" | "unavailable">;

/**
 * A company's profile, or `null`.
 *
 * `null` means "has none yet" — then a job stays anonymous, and the UI simply
 * shows no company part. That is no error but a state the company brought
 * about itself. Filling it with "Unknown company" would be a statement nobody
 * made.
 */
export async function getCompanyProfile(
  tenantId: string,
  signal?: AbortSignal
): Promise<CompanyProfile | null> {
  const answer = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    `/companies/${tenantId}/profile`,
    { signal }
  );
  return answer.ok ? (answer.value ?? null) : null;
}

/**
 * One's own company profile — distinguishing "none yet" from "not
 * retrievable".
 *
 * `null` used to stand for both, and that was the most dangerous place of its
 * kind in the project: an empty form, the loading state over (the read did
 * succeed), and the page looked like "nothing entered yet". Whoever then typed
 * the display name and saved sent a `PUT` with an empty about, empty website,
 * empty locations and empty benefits — and overwrote what stood there before.
 *
 * `{ ok: true, profile: null }` means "there is none yet", `{ ok: false }`
 * means "we do not know". Only the first may show an empty form.
 */
export async function getOwnCompanyProfile(
  signal?: AbortSignal
): Promise<OwnProfileResult> {
  const answer = await request<CompanyProfile | null>(
    COMPANIES_BASE_URL,
    "/companies/me/profile",
    { signal },
    "errors.profileUnavailable"
  );
  if (answer.ok) return { ok: true, profile: answer.value ?? null };
  return interpretError<"unavailable">(
    answer.error,
    {
      0: { reason: "unavailable", titleKey: "errors.profileUnavailable" },
    },
    "unavailable"
  );
}

export async function saveCompanyProfile(
  input: CompanyProfileInput
): Promise<SaveResult> {
  const answer = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    "/companies/me/profile",
    // No `tenant_id`: the company stands in the token and is checked against
    // the membership.
    { method: "PUT", body: input },
    "errors.profileNotSaved"
  );
  if (answer.ok) return { ok: true, profile: answer.value };
  return interpretError<"no-company" | "invalid" | "offline">(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      403: { reason: "no-company", titleKey: "errors.chooseCompanyToActFor" },
    },
    "invalid"
  );
}

/**
 * A company's careers page, by its slug.
 *
 * `null` used to stand for "does not exist", for `500`, for `503` and for "no
 * network" — and the page turned that into "Unter dieser Adresse ist kein
 * Unternehmen hinterlegt."
 *
 * This is the only place of its kind that STRANGERS see: a company gives its
 * careers link to applicants, the service stumbles, and the recipient reads
 * that the company does not exist. A statement about a company nobody meant to
 * make.
 *
 * `not-found` is an answer, `unavailable` is a disruption.
 */
export async function getCompanyBySlug(
  slug: string,
  signal?: AbortSignal
): Promise<SlugResult> {
  const answer = await request<CompanyProfile>(
    COMPANIES_BASE_URL,
    `/companies/by-slug/${encodeURIComponent(slug)}`,
    { signal },
    "errors.pageUnavailable"
  );
  if (answer.ok && answer.value !== undefined && answer.value !== null) {
    return { ok: true, profile: answer.value };
  }
  if (answer.ok) {
    return {
      ok: false,
      reason: "unavailable",
      error: {
        status: 0,
        title: i18n.t("errors.pageUnavailable"),
        detail: i18n.t("errors.pageUnavailable"),
      },
    };
  }
  return interpretError<"not-found" | "unavailable">(
    answer.error,
    {
      0: { reason: "unavailable", titleKey: "errors.noConnection" },
      404: {
        reason: "not-found",
        titleKey: "errors.noCompanyAtAddress",
      },
    },
    "unavailable"
  );
}
