import { request } from "../../../core/api/client";
import { COMPANIES_BASE_URL } from "../../../env";

/**
 * Nur der Anzeigename eines Unternehmens — mehr braucht dieser Bereich nicht.
 *
 * `/consents` löst damit die Tenant-UUID einer Freigabe zu einem Namen auf. Den
 * Namen im Ledger zu führen hiesse, eine Kopie zu halten, die veraltet, sobald
 * ein Unternehmen sich umbenennt — und der Ledger verwaltet Fähigkeiten, keine
 * Unternehmen.
 *
 * `null` heisst „kein Profil abrufbar". Die Seite schreibt dann „Ein
 * Unternehmen" und erfindet keinen Namen.
 */
export interface CompanyName {
  tenant_id: string;
  display_name: string;
}

export async function getCompanyName(
  tenantId: string,
  signal?: AbortSignal
): Promise<string | null> {
  const antwort = await request<CompanyName>(
    COMPANIES_BASE_URL,
    `/companies/${tenantId}/profile`,
    { signal }
  );
  if (!antwort.ok) return null;
  const name = antwort.value?.display_name;
  return typeof name === "string" && name !== "" ? name : null;
}
