import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { API_BASE_URL } from "../../../env";

export type ErasureResult = { ok: true } | { ok: false; error: ApiError };

/**
 * <c>POST /account/erasure</c> — for oneself only, and without a reason.
 *
 * <strong>No reason field, and that is not an omission.</strong> Demanding a
 * justification from someone who wants to leave is a lever against them. So
 * this call sends an empty body, and there is nothing that could be added here
 * without breaking the promise.
 */
export async function requestErasure(signal?: AbortSignal): Promise<ErasureResult> {
  const answer = await request<void>(
    API_BASE_URL,
    "/account/erasure",
    { method: "POST", signal },
    "errors.deletionNotAccepted"
  );

  return answer.ok ? { ok: true } : { ok: false, error: answer.error };
}
