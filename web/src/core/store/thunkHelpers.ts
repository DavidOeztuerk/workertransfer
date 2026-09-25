import { createAsyncThunk } from "@reduxjs/toolkit";

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
  title: string;
  detail: string;
  correlationId?: string;
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
