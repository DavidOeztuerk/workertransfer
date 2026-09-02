import { createAsyncThunk } from "@reduxjs/toolkit";

import type { AppDispatch, RootState } from "./store";

/**
 * Was schiefgehen kann, in EINER Gestalt — der auf dem Draht.
 *
 * Die Dienste antworten durchgehend mit RFC-9457-Problemdokumenten, und jedes
 * trägt eine `correlationId`. Die wird hier bewusst mitgeführt: wer sich
 * beschwert, soll eine Kennung nennen können, mit der die Zeile über alle
 * Dienste auffindbar ist.
 */
export interface ApiError {
  status: number;
  title: string;
  detail: string;
  correlationId?: string;
}

/**
 * Der typisierte Thunk. Jeder Feature-Thunk geht hierüber, damit `rejectValue`
 * überall dieselbe Gestalt hat — sonst prüft jede Komponente einen anderen
 * Fehler, und einer davon wird vergessen.
 */
export const createAppThunk = createAsyncThunk.withTypes<{
  state: RootState;
  dispatch: AppDispatch;
  rejectValue: ApiError;
}>();
