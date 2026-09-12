import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";
import { Navigate, useNavigate } from "react-router-dom";

import { ErrorBlock } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { AuthCard } from "../components/AuthCard";
import { AuthModeTabs } from "../components/AuthModeTabs";
import { errorCleared } from "../store/authSlice";
import { login } from "../store/authThunks";

/**
 * Anmelden.
 *
 * <strong>Kein Feld für ein Unternehmen.</strong> Ein Mandant ist ein
 * Unternehmen, und eine natürliche Person hat keines (ADR-0017). Das Token aus
 * dieser Anmeldung trägt <em>keinen</em> `tenant`-Anspruch; wer für ein
 * Unternehmen handeln will, wählt es danach aus, und der Server prüft die
 * Mitgliedschaft (ADR-0018). Jemanden hier eine Mandanten-UUID tippen zu
 * lassen war beides: falsch und unbenutzbar.
 */
export function LoginPage() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const navigate = useNavigate();

  const status = useAppSelector((state) => state.auth.status);
  const pending = useAppSelector((state) => state.auth.pending);
  const fehler = useAppSelector((state) => state.auth.error);

  const [email, setEmail] = useState("");
  const [passwort, setPasswort] = useState("");

  /**
   * Der Rückfalltext für einen Fehlschlag OHNE `ApiError`.
   *
   * Der `login`-Thunk `unwrap()`t intern `loadSession`. Scheitert dieser Teil,
   * ist `action.payload` undefiniert, der Slice setzt `error = null` — und der
   * Knopf tut sichtbar nichts. Ohne diesen Zustand wäre die Anmeldung an genau
   * der Stelle stumm, an der jemand nicht weiterkommt.
   */
  const [stummerFehlschlag, setStummerFehlschlag] = useState<string | null>(
    null,
  );

  // Wer schon angemeldet ist, hat auf dem Anmeldeformular nichts verloren.
  // Greift auch nach einer erfolgreichen Anmeldung, weil der Status dann
  // umspringt — das `navigate` unten ist der ausdrückliche, diese Zeile der
  // stille Weg zum selben Ziel.
  if (status === "authenticated") return <Navigate to="/overview" replace />;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setStummerFehlschlag(null);
    dispatch(errorCleared());

    const result = await dispatch(login({ email, password: passwort }));

    if (login.fulfilled.match(result)) {
      if (result.payload !== null) {
        // Hier hing früher der Rückweg zur gemerkten Stelle, und er war
        // stillgelegt. Am 12.09.2026 ist die andere Hälfte gefallen:
        // `features/work/lib/intent.ts` schrieb eine Absicht nach
        // localStorage, die niemand je las — ein Eintrag mit 24 Stunden
        // Lebensdauer im Browser einer Person, ohne Einlösung (PBI-8.3).
        //
        // Wer den Rückweg will, baut BEIDE Hälften in einem Zug: das Merken
        // beim Bewerben-Knopf und das Einlösen hier. Nur eine davon ist genau
        // der Zustand, der gerade beseitigt wurde.
        void navigate("/overview", { replace: true });
        return;
      }
      // Angemeldet, aber die Sitzung kam leer zurück. Das ist kein Erfolg, und
      // stillschweigend auf der Seite stehen zu bleiben erklärt es niemandem.
      setStummerFehlschlag(t("anmeldung.sitzungUnlesbar"));
      return;
    }

    // `payload` gesetzt heisst: der Slice zeigt den Fehler des Servers. Nur der
    // andere Fall braucht hier einen eigenen Satz.
    if (result.payload === undefined) {
      setStummerFehlschlag(t("anmeldung.fehlgeschlagen"));
    }
  }

  return (
    <AuthCard
      title={t("anmeldung.titel")}
      lead={t("anmeldung.lead")}
    >
      <AuthModeTabs current="login" />

      <Box
        component="form"
        onSubmit={submit}
        noValidate
        sx={{ display: "grid", gap: 2 }}
      >
        <TextField
          label={t("anmeldung.email")}
          type="email"
          // Ohne `autoComplete` kann kein Passwortmanager füllen — der Browser
          // mahnt es in der Konsole selbst an.
          autoComplete="username"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
        />
        <TextField
          label={t("anmeldung.passwort")}
          type="password"
          autoComplete="current-password"
          value={passwort}
          onChange={(event) => setPasswort(event.target.value)}
          required
        />

        {/* Der Fehlertext kommt vom Server und wird nie erfunden. */}
        {fehler !== null ? <ErrorBlock error={fehler} /> : null}
        {stummerFehlschlag !== null ? (
          <Alert severity="error">{stummerFehlschlag}</Alert>
        ) : null}

        <Button
          type="submit"
          variant="contained"
          size="large"
          disabled={pending}
        >
          {pending ? t("anmeldung.laeuft") : t("anmeldung.knopf")}
        </Button>
      </Box>
    </AuthCard>
  );
}
