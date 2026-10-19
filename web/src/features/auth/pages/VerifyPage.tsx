import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { AuthCard } from "../components/AuthCard";
import { useTranslation } from "react-i18next";
import {
  type VerificationResult,
  resendVerification,
  verifyEmail,
} from "../api/registration";

type VerifyState =
  | { phase: "running" }
  | { phase: "done"; company?: string; companyError?: string }
  | { phase: "failed"; expired: boolean; message: string };

/**
 * Exactly one call per token — module-wide, not per mount of the component.
 *
 * The confirmation token is single-use: the second call does not spend it, it
 * fails with "invalid". And because both answers write into the same state,
 * the one arriving last decides what the person sees — in the bad case
 * "confirmation failed" while their account was just activated. In the E2E run
 * that was 2 of 120 calls.
 *
 * A `useRef` is NOT enough for that: it dies with the component, and a second
 * mount is exactly the case (HMR, StrictMode, reload, a router mounting anew).
 * That is why the <strong>promise</strong> is remembered, not only the fact —
 * so the second mount gets the same result as the first instead of none.
 */
const attempts = new Map<string, Promise<VerificationResult>>();

function verifyOnce(token: string): Promise<VerificationResult> {
  const running = attempts.get(token);
  if (running !== undefined) return running;
  const started = verifyEmail(token);
  attempts.set(token, started);
  return started;
}

export function VerifyPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";

  const [state, setState] = useState<VerifyState>({ phase: "running" });

  useEffect(() => {
    if (token === "") {
      setState({
        phase: "failed",
        expired: false,
        message: t("verify.linkMissing"),
      });
      return;
    }
    void verifyOnce(token).then((result) => {
      setState(
        result.ok
          ? { phase: "done", ...result }
          : {
              phase: "failed",
              expired: result.expired,
              message: result.message,
            },
      );
    });
  }, [token, t]);

  if (state.phase === "running") {
    return (
      <AuthCard
        title={t("verify.inProgressTitle")}
        lead={t("verify.lead")}
      >
        <Typography role="status" color="text.secondary">
          {t("verify.inProgressText")}
        </Typography>
      </AuthCard>
    );
  }

  if (state.phase === "done") {
    return (
      // The heading is the same for ALL three successes, and it is true in all
      // three: what is confirmed is the EMAIL. What became of the company is a
      // second statement below it.
      //
      // It also stays literally "E-Mail bestätigt", because the E2E helper
      // checks exactly that (`exact: true`) — a soft pattern otherwise also hit
      // "Wird bestätigt…", and the test was satisfied while the confirmation
      // was still running.
      //
      // The third outcome — account confirmed, company refused — needs its own
      // branch below; without it the page reads green while half the intent was
      // dropped, and nobody learns why no company is there later. There is no
      // second attempt: the token is spent and there is no "create a company"
      // button.
      <AuthCard
        title={t("verify.doneTitle")}
        lead={
          state.company !== undefined
            ? t("verify.doneWithCompany", { name: state.company })
            : t("verify.doneWithoutCompany")
        }
      >
        <Box sx={{ display: "grid", gap: 2 }}>
          {state.companyError === "domain_already_claimed" ? (
            <Alert severity="warning">
              {t("verify.domainTaken")}
            </Alert>
          ) : state.companyError !== undefined ? (
            <Alert severity="warning">
              {t("verify.companyFailed")}
            </Alert>
          ) : null}

          <Typography variant="body2">
            <Link component={RouterLink} to="/login">
              {t("verify.toLogin")}
            </Link>
          </Typography>
        </Box>
      </AuthCard>
    );
  }

  return (
    <Failed expired={state.expired} message={state.message} />
  );
}

/**
 * The failure — and a new link only for an EXPIRED one.
 *
 * An invalid link does not become valid on the second attempt either; there
 * would be nothing to offer there but a dead end with a button.
 */
function Failed({
  expired,
  message,
}: {
  expired: boolean;
  message: string;
}) {
  const { t } = useTranslation();
  const [email, setEmail] = useState("");
  const [running, setRunning] = useState(false);
  const [sent, setSent] = useState(false);
  const [failed, setFailed] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setRunning(true);
    setFailed(false);
    const result = await resendVerification(email);
    setRunning(false);
    if (result.ok) setSent(true);
    else setFailed(true);
  }

  return (
    <AuthCard
      title={t("verify.failedTitle")}
      lead={t("verify.lead")}
    >
      <Box sx={{ display: "grid", gap: 2 }}>
        <Alert severity="error">{message}</Alert>

        {expired ? (
          <Box
            component="form"
            onSubmit={submit}
            noValidate
            sx={{ display: "grid", gap: 2 }}
          >
            <TextField
              label={t("verify.email")}
              type="email"
              autoComplete="username"
              helperText={t("verify.emailHint")}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            <Button type="submit" variant="contained" disabled={running}>
              {running
                ? t("verify.newLinkSending")
                : t("verify.newLink")}
            </Button>

            {failed ? (
              <Alert severity="error">
                {t("verify.newLinkFailed")}
              </Alert>
            ) : null}
            {sent ? (
              <Alert
                // A confirmation must not interrupt; MUI's Alert would set role="alert".
                role="status"
                severity="success"
              >
                {t("verify.newLinkSent")}
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </Box>
    </AuthCard>
  );
}
