import { API_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";
import { createAppThunk } from "../../../core/store/thunkHelpers";
import { readOccupationalField } from "../../../shared/lib/occupationalFields";
import type { Membership, Session, SessionWireState } from "../types/session";
import { announceSignedOut } from "../lib/sessionBroadcast";

interface SessionBody {
  user_id: string;
  email: string;
  tenant_id: string | null;
  language: string;
  display_name: string;
  given_name?: string | null;
  family_name?: string | null;
  occupational_field?: string | null;
}

interface SessionResponse {
  user: SessionBody | null;
  state?: SessionWireState;
}

interface MembershipBody {
  id: string;
  name: string;
  role: string;
}

const toSession = (body: SessionBody): Session => ({
  userId: body.user_id,
  email: body.email,
  tenantId: body.tenant_id,
  language: body.language,
  displayName: body.display_name ?? "",
  givenName: body.given_name ?? "",
  familyName: body.family_name ?? "",
  occupationalField: readOccupationalField(body.occupational_field),
});

/**
 * The session at start-up.
 *
 * `GET /auth/session` answers 200 even WITHOUT a token — the UI's question is
 * "am I signed in?", and a 401 would be no answer to that but an error. That is
 * why `null` is a valid result here and not a rejection.
 */
export const loadSession = createAppThunk<Session | null>(
  "auth/loadSession",
  async (_arg, { rejectWithValue, dispatch }) => {
    const answer = await request<SessionResponse>(
      API_BASE_URL,
      "/auth/session",
      { skipRefresh: true },
      "errors.sessionNotChecked"
    );
    if (!answer.ok) return rejectWithValue(answer.error);

    // Access dead, refresh present: renew deliberately, do not draw as signed
    // out. `GET /auth/session` answers 200 with user:null — without this branch
    // every page would be the sign-in prompt after 15 minutes.
    if (answer.value?.state === "renewable") {
      const refreshed = await dispatch(refreshSession());
      if (refreshed.meta.requestStatus !== "fulfilled" || !refreshed.payload) {
        return null;
      }
      const again = await request<SessionResponse>(
        API_BASE_URL,
        "/auth/session",
        { skipRefresh: true },
        "errors.sessionNotChecked"
      );
      if (!again.ok) return rejectWithValue(again.error);
      return again.value?.user ? toSession(again.value.user) : null;
    }

    return answer.value?.user ? toSession(answer.value.user) : null;
  }
);

/**
 * Rotates the httpOnly cookies. No body, no token in the JSON.
 *
 * `skipRefresh`: a 401 here IS the refusal, no reason for a second attempt —
 * otherwise the server deletes the dead refresh cookie in a loop.
 */
export const refreshSession = createAppThunk<boolean>(
  "auth/refreshSession",
  async (_arg, { rejectWithValue }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/auth/refresh",
      { method: "POST", skipRefresh: true },
      "errors.sessionNotChecked"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return true;
  }
);

export const login = createAppThunk<Session | null, { email: string; password: string }>(
  "auth/login",
  async (input, { rejectWithValue, dispatch }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/auth/login",
      { method: "POST", body: { email: input.email, password: input.password } },
      "errors.signInFailed"
    );
    if (!answer.ok) return rejectWithValue(answer.error);

    // The token lies in the httpOnly cookie; who is signed in stands in /me.
    const session = await dispatch(loadSession()).unwrap();
    return session;
  }
);

export const logout = createAppThunk<void>("auth/logout", async (_arg, { rejectWithValue }) => {
  const answer = await request<void>(
    API_BASE_URL,
    "/auth/logout",
    { method: "POST" },
    "errors.signOutFailed"
  );
  if (!answer.ok) return rejectWithValue(answer.error);
  announceSignedOut();
});

export const loadMemberships = createAppThunk<Membership[]>(
  "auth/loadMemberships",
  async (_arg, { rejectWithValue }) => {
    const answer = await request<MembershipBody[]>(
      API_BASE_URL,
      "/me/companies",
      {},
      "errors.companiesNotLoaded"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return answer.value ?? [];
  }
);

/**
 * Act for a company.
 *
 * The client NAMES the company, the server DECIDES: it checks the membership
 * and only then writes the tenant into the token (ADR-0018). That is why the
 * session is read again afterwards instead of set locally — what would stand
 * here would be the browser's claim about its own rights.
 */
export const actForCompany = createAppThunk<Session | null, string>(
  "auth/actForCompany",
  async (tenantId, { rejectWithValue, dispatch }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      `/auth/company/${tenantId}`,
      { method: "POST" },
      "errors.switchFailed"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return await dispatch(loadSession()).unwrap();
  }
);

/**
 * Write the chosen language to the account.
 *
 * <strong>Only for the signed in, and a failure is swallowed.</strong> The UI
 * already switched when the person clicked; turning it back because the server
 * is not answering right now would be the worse of two answers. What is lost is
 * the language of the next MAIL — unpleasant, but no reason to make the page
 * jump back.
 */
export const saveLanguage = createAppThunk<null, string>(
  "auth/saveLanguage",
  async (language, { rejectWithValue }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/account/language",
      { method: "PUT", body: { language: language } },
      "errors.settingsNotSaved"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return null;
  }
);
