import { request } from "../../../core/api/client";
import { COMPANIES_BASE_URL } from "../../../env";

/**
 * Only a company's display name — this area needs no more.
 *
 * `/consents` uses it to resolve the tenant UUID of a release to a name.
 * Keeping the name in the ledger would mean holding a copy that goes stale the
 * moment a company renames itself — and the ledger manages capabilities, not
 * companies.
 *
 * `null` means "no profile retrievable". The page then writes "A company" and
 * invents no name.
 */
export interface CompanyName {
  tenant_id: string;
  display_name: string;
}

export async function getCompanyName(
  tenantId: string,
  signal?: AbortSignal
): Promise<string | null> {
  const answer = await request<CompanyName>(
    COMPANIES_BASE_URL,
    `/companies/${tenantId}/profile`,
    { signal }
  );
  if (!answer.ok) return null;
  const name = answer.value?.display_name;
  return typeof name === "string" && name !== "" ? name : null;
}
