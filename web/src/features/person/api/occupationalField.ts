import type { ApiError } from "../../../core/store/thunkHelpers";
import { request } from "../../../core/api/client";
import { API_BASE_URL } from "../../../env";
import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";

/**
 * Sets the occupational field of one's own account. `null` means remove. No id
 * in the body — the server writes only into the signed-in account.
 */
export async function saveOccupationalField(
  field: OccupationalFieldChoice,
): Promise<{ ok: true } | { ok: false; error: ApiError }> {
  const answer = await request<unknown>(
    API_BASE_URL,
    "/account/occupational-field",
    { method: "PUT", body: { occupational_field: field } },
    "errors.occupationNotSaved",
  );

  return answer.ok ? { ok: true } : { ok: false, error: answer.error };
}
