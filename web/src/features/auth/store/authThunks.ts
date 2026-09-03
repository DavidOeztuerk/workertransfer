import { API_BASE_URL } from "../../../env";
import { request } from "../../../core/api/client";
import { createAppThunk } from "../../../core/store/thunkHelpers";
import type { Membership, Session } from "../types/session";

interface SessionBody {
  user_id: string;
  email: string;
  tenant_id: string | null;
  language: string;
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
  async (_arg, { rejectWithValue }) => {
    const answer = await request<{ user: SessionBody | null }>(
      API_BASE_URL,
      "/auth/session",
      {},
      "fehler.sitzungNichtGeprueft"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return answer.value?.user ? toSession(answer.value.user) : null;
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
    const sitzung = await dispatch(loadSession()).unwrap();
    return sitzung;
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
  async (sprache, { rejectWithValue }) => {
    const answer = await request<unknown>(
      API_BASE_URL,
      "/account/language",
      { method: "PUT", body: { language: sprache } },
      "fehler.einstellungenNichtGespeichert"
    );
    if (!answer.ok) return rejectWithValue(answer.error);
    return null;
  }
);
