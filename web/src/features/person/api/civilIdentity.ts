import type { ApiError } from "../../../core/store/thunkHelpers";
import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";

/**
 * A 2xx without a body is no confirmation of what was stored. It reads as the
 * step's own failure — there is no sentence of the server's to show anyway.
 */
const EMPTY_ANSWER: ApiError = { status: 500, title: "", detail: "" };

/** Postal address for applications — a template for the letterhead, never for search, never for AI. */
export interface PostalAddress {
  line1: string;
  line2: string;
  postalCode: string;
  city: string;
  country: string;
  phone: string;
}

interface PostalAddressWire {
  line1: string;
  line2: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
}

const toView = (wire: PostalAddressWire): PostalAddress => ({
  line1: wire.line1 ?? "",
  line2: wire.line2 ?? "",
  postalCode: wire.postal_code ?? "",
  city: wire.city ?? "",
  country: wire.country ?? "DE",
  phone: wire.phone ?? "",
});

export const EMPTY_POSTAL_ADDRESS: PostalAddress = {
  line1: "",
  line2: "",
  postalCode: "",
  city: "",
  country: "DE",
  phone: "",
};

export async function getPostalAddress(signal?: AbortSignal): Promise<PostalAddress | null> {
  const answer = await request<PostalAddressWire>(
    API_BASE_URL,
    "/account/address",
    { signal },
    "errors.settingsUnavailable",
  );
  return answer.ok && answer.value !== undefined ? toView(answer.value) : null;
}

export async function savePostalAddress(
  address: PostalAddress,
): Promise<{ ok: true; address: PostalAddress } | { ok: false; error: ApiError }> {
  const answer = await request<PostalAddressWire>(
    API_BASE_URL,
    "/account/address",
    {
      method: "PUT",
      body: {
        line1: address.line1,
        line2: address.line2,
        postal_code: address.postalCode,
        city: address.city,
        country: address.country,
        phone: address.phone,
      },
    },
    "errors.settingsNotSaved",
  );
  if (!answer.ok || answer.value === undefined) {
    return { ok: false, error: answer.ok ? EMPTY_ANSWER : answer.error };
  }
  return { ok: true, address: toView(answer.value) };
}

export async function saveLegalName(
  givenName: string,
  familyName: string,
): Promise<{ ok: true } | { ok: false; error: ApiError }> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/name",
    {
      method: "PUT",
      body: { given_name: givenName, family_name: familyName },
    },
    "errors.settingsNotSaved",
  );
  return answer.ok ? { ok: true } : { ok: false, error: answer.error };
}
