import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";

/** Bewerbungsanschrift — Vorlage für den Briefkopf, nie für die Suche, nie für KI. */
export interface Anschrift {
  line1: string;
  line2: string;
  postalCode: string;
  city: string;
  country: string;
  phone: string;
}

interface Draht {
  line1: string;
  line2: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
}

const zuAnsicht = (draht: Draht): Anschrift => ({
  line1: draht.line1 ?? "",
  line2: draht.line2 ?? "",
  postalCode: draht.postal_code ?? "",
  city: draht.city ?? "",
  country: draht.country ?? "DE",
  phone: draht.phone ?? "",
});

export const LEERE_ANSCHRIFT: Anschrift = {
  line1: "",
  line2: "",
  postalCode: "",
  city: "",
  country: "DE",
  phone: "",
};

export async function ladeAnschrift(signal?: AbortSignal): Promise<Anschrift | null> {
  const answer = await request<Draht>(
    API_BASE_URL,
    "/account/address",
    { signal },
    "fehler.einstellungenNichtAbrufbar",
  );
  return answer.ok && answer.value !== undefined ? zuAnsicht(answer.value) : null;
}

export async function speichereAnschrift(
  anschrift: Anschrift,
): Promise<{ ok: true; anschrift: Anschrift } | { ok: false; detail: string }> {
  const answer = await request<Draht>(
    API_BASE_URL,
    "/account/address",
    {
      method: "PUT",
      body: {
        line1: anschrift.line1,
        line2: anschrift.line2,
        postal_code: anschrift.postalCode,
        city: anschrift.city,
        country: anschrift.country,
        phone: anschrift.phone,
      },
    },
    "fehler.einstellungenNichtGespeichert",
  );
  if (!answer.ok || answer.value === undefined) {
    return { ok: false, detail: answer.ok ? "" : answer.error.detail };
  }
  return { ok: true, anschrift: zuAnsicht(answer.value) };
}

export async function speichereKlarname(
  givenName: string,
  familyName: string,
): Promise<{ ok: true } | { ok: false; detail: string }> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/name",
    {
      method: "PUT",
      body: { given_name: givenName, family_name: familyName },
    },
    "fehler.einstellungenNichtGespeichert",
  );
  return answer.ok ? { ok: true } : { ok: false, detail: answer.error.detail };
}
