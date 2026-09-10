import { createSlice } from "@reduxjs/toolkit";
import type { PayloadAction } from "@reduxjs/toolkit";

import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";

import type { ApiError } from "../../../core/store/thunkHelpers";
import type { Membership, Session, SessionStatus } from "../types/session";
import { actForCompany, loadMemberships, loadSession, login, logout } from "./authThunks";

interface AuthState {
  status: SessionStatus;
  session: Session | null;
  memberships: Membership[];
  /** Der letzte Fehler einer BEWUSSTEN Handlung — nie der der Hintergrundprüfung. */
  error: ApiError | null;
  pending: boolean;
}

const initialState: AuthState = {
  status: "unknown",
  session: null,
  memberships: [],
  error: null,
  pending: false,
};

const authSlice = createSlice({
  name: "auth",
  initialState,
  reducers: {
    /** Räumt die Fehlermeldung weg, sobald jemand das Formular wieder anfasst. */
    errorCleared(state) {
      state.error = null;
    },

    /**
     * Die Sitzung ist auf dem Server schon weg — der Speicher zieht nach.
     *
     * Nicht dasselbe wie `logout`: das *bittet* den Server abzumelden. Nach
     * einer angenommenen Kontolöschung ist jede Sitzung bereits widerrufen, ein
     * `POST /auth/logout` bekäme 401, `logout.fulfilled` liefe nie, und der
     * Speicher bliebe auf „angemeldet" stehen. Genau das war zu sehen: die
     * Löschseite sagte „Du bist abgemeldet", während der Kopf weiter das
     * Konto-Menü zeigte.
     */
    /** Das Berufsfeld hat sich geändert — die Navigation zieht sofort nach. */
    berufsfeldGesetzt(state, action: PayloadAction<BerufsfeldWahl>) {
      if (state.session !== null) {
        state.session = { ...state.session, berufsfeld: action.payload };
      }
    },

    sitzungBeendet(state) {
      state.session = null;
      state.memberships = [];
      state.status = "anonymous";
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      // Die Sitzungsprüfung setzt NIE `error`: sie läuft im Hintergrund, und
      // ein roter Kasten für etwas, das niemand angestossen hat, erschreckt
      // ohne Anlass. Sie setzt nur den Status.
      .addCase(loadSession.fulfilled, (state, action) => {
        state.session = action.payload;
        state.status = action.payload ? "authenticated" : "anonymous";
      })
      .addCase(loadSession.rejected, (state) => {
        state.session = null;
        state.status = "anonymous";
      })

      .addCase(login.pending, (state) => {
        state.pending = true;
        state.error = null;
      })
      .addCase(login.fulfilled, (state, action) => {
        state.pending = false;
        state.session = action.payload;
        state.status = action.payload ? "authenticated" : "anonymous";
      })
      .addCase(login.rejected, (state, action) => {
        state.pending = false;
        state.error = action.payload ?? null;
      })

      .addCase(logout.fulfilled, (state) => {
        state.session = null;
        state.memberships = [];
        state.status = "anonymous";
      })

      .addCase(loadMemberships.fulfilled, (state, action) => {
        state.memberships = action.payload;
      })

      .addCase(actForCompany.fulfilled, (state, action) => {
        state.session = action.payload;
      })
      .addCase(actForCompany.rejected, (state, action) => {
        state.error = action.payload ?? null;
      });
  },
});

export const { berufsfeldGesetzt, errorCleared, sitzungBeendet } = authSlice.actions;
export default authSlice.reducer;
