import { createSlice } from "@reduxjs/toolkit";
import type { PayloadAction } from "@reduxjs/toolkit";

import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";

import type { ApiError } from "../../../core/store/thunkHelpers";
import type { Membership, Session, SessionStatus } from "../types/session";
import { actForCompany, loadMemberships, loadSession, login, logout } from "./authThunks";

interface AuthState {
  status: SessionStatus;
  session: Session | null;
  memberships: Membership[];
  /** The last error of a DELIBERATE action — never that of the background check. */
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
    /** Clears the error message as soon as someone touches the form again. */
    errorCleared(state) {
      state.error = null;
    },

    /** The occupational field changed — the navigation follows at once. */
    occupationalFieldSet(state, action: PayloadAction<OccupationalFieldChoice>) {
      if (state.session !== null) {
        state.session = { ...state.session, occupationalField: action.payload };
      }
    },

    /**
     * The session is already gone on the server — the store follows.
     *
     * Not the same as `logout`: that *asks* the server to sign out. After an
     * accepted account erasure every session is already revoked, a
     * `POST /auth/logout` would get 401, `logout.fulfilled` would never run, and
     * the store would stay on "signed in". Exactly that was seen: the erasure
     * page said "You are signed out" while the header kept showing the account
     * menu.
     */
    sessionEnded(state) {
      state.session = null;
      state.memberships = [];
      state.status = "anonymous";
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      // The session check NEVER sets `error`: it runs in the background, and a
      // red box for something nobody started alarms without cause. It sets
      // only the status.
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

export const { errorCleared, occupationalFieldSet, sessionEnded } = authSlice.actions;
export default authSlice.reducer;
