import { createAsyncThunk } from "@reduxjs/toolkit";

import type { ErrorKey } from "../../shared/api/errors";
import type { AppDispatch, RootState } from "./store";

/**
 * What can go wrong, in ONE shape — the one on the wire.
 *
 * The services answer with RFC 9457 problem documents throughout, and each one
 * carries a `correlationId`. It is carried along here on purpose: whoever
 * complains should be able to name an id that finds the line across every
 * service.
 */
export interface ApiError {
  status: number;
  /**
   * The server's words (or the client's fallback). English on the wire since
   * the backend translation — READ, never drawn: the UI draws a catalogue key
   * (`failureKey` in `shared/api/errors.ts`).
   */
  title: string;
  detail: string;
  correlationId?: string;
  /**
   * The keys `interpretError` chose by status. Absent where no table named the
   * status — then the site's own key applies, never `detail`.
   */
  titleKey?: ErrorKey;
  detailKey?: ErrorKey;
}

/**
 * The typed thunk. Every feature thunk goes through it so that `rejectValue`
 * has the same shape everywhere — otherwise each component checks a different
 * error, and one of them gets forgotten.
 */
export const createAppThunk = createAsyncThunk.withTypes<{
  state: RootState;
  dispatch: AppDispatch;
  rejectValue: ApiError;
}>();
