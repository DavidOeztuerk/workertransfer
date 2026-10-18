import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";
import { Navigate, useNavigate } from "react-router-dom";

import { ErrorBlock } from "../../../shared/components/ui";
import type { ServerCase } from "../../../shared/api/errors";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { AuthCard } from "../components/AuthCard";
import { AuthModeTabs } from "../components/AuthModeTabs";
import { ExternalSignIn } from "../components/ExternalSignIn";
import { errorCleared } from "../store/authSlice";
import { login } from "../store/authThunks";


/**
 * A refused sign-in, by status alone: 401 is "these credentials" (the server
 * does not say which half — one answer for an unknown address and a wrong
 * password), 403 an address not yet confirmed, 429 the gateway's brake.
 */
const SIGN_IN_CASES: readonly ServerCase[] = [
  { status: 401, key: "login.invalidCredentials" },
  { status: 403, key: "login.notConfirmed" },
  { status: 429, key: "errors.tooManyAttempts" },
];

/**
 * Sign in.
 *
 * <strong>No field for a company.</strong> A tenant is a company, and a natural
 * person has none (ADR-0017). The token from this sign-in carries <em>no</em>
 * `tenant` claim; whoever wants to act for a company picks it afterwards, and
 * the server checks the membership (ADR-0018). Making someone type a tenant
 * UUID here was both: wrong and unusable.
 */
export function LoginPage() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const navigate = useNavigate();

  const status = useAppSelector((state) => state.auth.status);
  const pending = useAppSelector((state) => state.auth.pending);
  // The failure comes from the server; what it SAYS comes from the catalogue,
  // by status (`SIGN_IN_CASES`) — identity-service answers in English.
  const error = useAppSelector((state) => state.auth.error);

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  /**
   * The fallback text for a failure WITHOUT an `ApiError`.
   *
   * The `login` thunk `unwrap()`s `loadSession` internally. If that part fails,
   * `action.payload` is undefined, the slice sets `error = null` — and the
   * button visibly does nothing. Without this state the sign-in would be silent
   * at exactly the place where someone cannot get on.
   */
  const [silentFailure, setSilentFailure] = useState<string | null>(
    null,
  );

  // Whoever is already signed in has no business on the sign-in form. Also
  // applies after a successful sign-in, because the status then flips — the
  // `navigate` below is the explicit, this line the quiet way to the same goal.
  if (status === "authenticated") return <Navigate to="/overview" replace />;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSilentFailure(null);
    dispatch(errorCleared());

    const result = await dispatch(login({ email, password }));

    if (login.fulfilled.match(result)) {
      if (result.payload !== null) {
        // The way back to the remembered job used to hang here, and it was
        // shut down. On 12.09.2026 the other half fell: `features/work/lib/
        // intent.ts` wrote an intent to localStorage that nobody ever read — an
        // entry with a 24-hour lifetime in a person's browser, never redeemed
        // (PBI-8.3).
        //
        // Whoever wants the way back builds BOTH halves in one go: remembering
        // at the apply button and redeeming here. Only one of them is exactly
        // the state that was just removed.
        void navigate("/overview", { replace: true });
        return;
      }
      // Signed in, but the session came back empty. That is no success, and
      // silently staying on the page explains it to nobody.
      setSilentFailure(t("login.sessionUnreadable"));
      return;
    }

    // `payload` set means: the slice shows the server's error. Only the other
    // case needs a sentence of its own here.
    if (result.payload === undefined) {
      setSilentFailure(t("login.failed"));
    }
  }

  return (
    <AuthCard
      title={t("login.title")}
      lead={t("login.lead")}
    >
      <AuthModeTabs current="login" />

      <Box
        component="form"
        onSubmit={submit}
        noValidate
        sx={{ display: "grid", gap: 2 }}
      >
        <TextField
          label={t("login.email")}
          type="email"
          // Without `autoComplete` no password manager can fill in — the browser
          // itself complains about it in the console.
          autoComplete="username"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
        />
        <TextField
          label={t("login.password")}
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />

        {error !== null ? (
          <ErrorBlock error={error} fallback="login.failed" cases={SIGN_IN_CASES} />
        ) : null}
        {silentFailure !== null ? (
          <Alert severity="error">{silentFailure}</Alert>
        ) : null}

        <Button
          type="submit"
          variant="contained"
          size="large"
          disabled={pending}
        >
          {pending ? t("login.inProgress") : t("login.button")}
        </Button>
      </Box>

      <ExternalSignIn />
    </AuthCard>
  );
}
