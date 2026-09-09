import { API_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";
import { createAppThunk } from "../../../core/store/thunkHelpers";
import type { Membership, Session, SessionWireState } from "../types/session";

interface SessionBody {
  user_id: string;
  email: string;
  tenant_id: string | null;
  language: string;
  display_name: string;
  given_name?: string | null;
  family_name?: string | null;
}

interface SessionAntwort {
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
});

/**
 * Die Sitzung beim Start.
 *
 * `GET /auth/session` antwortet 200 auch OHNE Token — die Frage der Oberfläche
 * lautet "bin ich angemeldet?", und ein 401 wäre darauf keine Antwort, sondern
 * ein Fehler. Deshalb ist `null` hier ein gültiges Ergebnis und keine Ablehnung.
 */
export const loadSession = createAppThunk<Session | null>(
  "auth/loadSession",
  async (_arg, { rejectWithValue, dispatch }) => {
    const answer = await request<SessionAntwort>(
      API_BASE_URL,
      "/auth/session",
      { ohneErneuerung: true },
      "fehler.sitzungNichtGeprueft"
    );
    if (!answer.ok) return rejectWithValue(answer.error);

    // Access tot, Refresh da: bewusst erneuern, nicht als abgemeldet zeichnen.
    // `GET /auth/session` antwortet 200 mit user:null — ohne diesen Zweig
    // wäre nach 15 Minuten jede Seite die Anmeldeaufforderung.
    if (answer.value?.state === "renewable") {
      const erneuert = await dispatch(erneuern());
      if (erneuert.meta.requestStatus !== "fulfilled" || !erneuert.payload) {
        return null;
      }
      const erneut = await request<SessionAntwort>(
        API_BASE_URL,
        "/auth/session",
        { ohneErneuerung: true },
        "fehler.sitzungNichtGeprueft"
      );
      if (!erneut.ok) return rejectWithValue(erneut.error);
      return erneut.value?.user ? toSession(erneut.value.user) : null;
    }

    return answer.value?.user ? toSession(answer.value.user) : null;
  }
);

/**
 * Rotiert die httpOnly-Cookies. Kein Rumpf, kein Token im JSON.
 *
 * `ohneErneuerung`: ein 401 hier IST die Absage, kein Anlass für einen zweiten
 * Versuch — der Server löscht den toten Refresh-Cookie sonst in einer Schleife.
 */
export const erneuern = createAppThunk<boolean>(
  "auth/erneuern",
  async (_arg, { rejectWithValue }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/auth/refresh",
      { method: "POST", ohneErneuerung: true },
      "fehler.sitzungNichtGeprueft"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return true;
  }
);

export const login = createAppThunk<Session | null, { email: string; password: string }>(
  "auth/login",
  async (eingabe, { rejectWithValue, dispatch }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/auth/login",
      { method: "POST", body: { email: eingabe.email, password: eingabe.password } },
      "fehler.anmeldungFehlgeschlagen"
    );
    if (!answer.ok) return rejectWithValue(answer.error);

    // Das Token liegt im httpOnly-Cookie; wer angemeldet ist, steht in /me.
    const session = await dispatch(loadSession()).unwrap();
    return session;
  }
);

export const logout = createAppThunk<void>("auth/logout", async (_arg, { rejectWithValue }) => {
  const answer = await request<void>(
    API_BASE_URL,
    "/auth/logout",
    { method: "POST" },
    "fehler.abmeldungFehlgeschlagen"
  );
  if (!answer.ok) return rejectWithValue(answer.error);
});

export const loadMemberships = createAppThunk<Membership[]>(
  "auth/loadMemberships",
  async (_arg, { rejectWithValue }) => {
    const answer = await request<MembershipBody[]>(
      API_BASE_URL,
      "/me/companies",
      {},
      "fehler.firmenNichtGeladen"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return answer.value ?? [];
  }
);

/**
 * Für eine Firma handeln.
 *
 * Der Client NENNT die Firma, der Server ENTSCHEIDET: er prüft die
 * Mitgliedschaft und schreibt den Mandanten erst dann ins Token (ADR-0018).
 * Deshalb wird die Sitzung danach neu gelesen statt lokal gesetzt — was hier
 * stünde, wäre eine Behauptung des Browsers über seine eigenen Rechte.
 */
export const actForCompany = createAppThunk<Session | null, string>(
  "auth/actForCompany",
  async (tenantId, { rejectWithValue, dispatch }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      `/auth/company/${tenantId}`,
      { method: "POST" },
      "fehler.wechselFehlgeschlagen"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return await dispatch(loadSession()).unwrap();
  }
);

/**
 * Die gewählte Sprache ans Konto schreiben.
 *
 * <strong>Nur für Angemeldete, und ein Fehlschlag wird geschluckt.</strong> Die
 * Oberfläche hat schon umgeschaltet, als der Mensch geklickt hat; sie
 * zurückzudrehen, weil der Server gerade nicht antwortet, wäre die schlechtere
 * von zwei Antworten. Was verlorengeht, ist die Sprache der nächsten MAIL —
 * unangenehm, aber kein Grund, die Seite zurückspringen zu lassen.
 */
export const spracheSpeichern = createAppThunk<null, string>(
  "auth/spracheSpeichern",
  async (language, { rejectWithValue }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/account/language",
      { method: "PUT", body: { language: language } },
      "fehler.einstellungenNichtGespeichert"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return null;
  }
);
