import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { sessionEnded } from "../../auth/store/authSlice";
import { SignInRequired } from "../components/SignInRequired";
import { requestErasure } from "../api/erasure";
import { failureKey } from "../../../shared/api/errors";
import { announceSignedOut } from "../../auth/lib/sessionBroadcast";

/** Only the numbers — the wording lives in the catalogues. */
const WHAT_DISAPPEARS = [1, 2, 3, 4, 5, 6, 7, 8] as const;

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

/**
 * <c>/delete-account</c> — where the promise is made to a person.
 *
 * <strong>Everything important stands BEFORE the button</strong>, and the most
 * uncomfortable sentence is said out loud: even the application through which
 * somebody was <em>hired</em> disappears from the company's list. An erasure
 * promise whose exceptions one learns only afterwards is none.
 *
 * <strong>Two steps, inline, no dialog.</strong> A dialog would take away the
 * page that says what disappears — and exactly that should still be readable
 * at the second click. The second button is worded differently from the first,
 * so that nobody clicks the same thing twice.
 *
 * <strong>No typed word, no waiting period, no re-entered password.</strong>
 * Whoever wants to delete may. Every additional hurdle would be a brake against
 * the person, not for them.
 *
 * <strong>The trap that has already snapped shut here:</strong> on success the
 * session is gone, so the shell redraws the page without a principal. The
 * accepted state must therefore be checked BEFORE the sign-in prompt —
 * otherwise the person reads "Please sign in" right after deleting.
 */
export function DeleteAccountPage() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const status = useAppSelector((state) => state.auth.status);
  const companies = useAppSelector((state) => state.auth.memberships);

  const [asking, setAsking] = useState(false);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);

  // FIRST. See above — after the erasure there is no principal any more.
  if (accepted) {
    return (
      <PageShell title={t("deleteAccount.title")} narrow>
        <Card>
          <CardContent>
            <Alert
              // A confirmation, not a failure: a red box here would read as if
              // something had gone wrong.
              severity="info"
              sx={{ mb: 2 }}
              role="status"
            >
              <strong>{t("deleteAccount.acceptedTitle")}</strong>{" "}
              <Trans
                i18nKey="deleteAccount.acceptedText"
                components={{ 1: <strong /> }}
              />
            </Alert>
            <Typography variant="body2" color="text.secondary">
              {t("deleteAccount.acceptedWhy")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (status !== "authenticated") {
    return (
      <SignInRequired
        title={t("deleteAccount.title")}
        sentenceKey="deleteAccount.signInRequired"
      />
    );
  }

  async function erase() {
    setRunning(true);
    const result = await requestErasure();
    setRunning(false);

    if (result.ok) {
      setError(null);
      setAccepted(true);

      // The server has already revoked every session — the store must follow,
      // otherwise the header keeps showing the account menu while this page
      // says "you are signed out". Two statements about the same thing, and the
      // more visible one is the wrong one.
      //
      // NOT `logout()`: that asks the server, and it answers a revoked session
      // with 401 — the store would stay as it is.
      dispatch(sessionEnded());
      announceSignedOut();
    } else {
      setError(t(failureKey(result.error, "errors.deletionNotAccepted")));
    }
  }

  return (
    <PageShell
      title={t("deleteAccount.title")}
      narrow
      lead={t("deleteAccount.lead")}
    >
      <Section title={t("deleteAccount.deletedTitle")}>
        <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
          {WHAT_DISAPPEARS.map((number) => (
            <Typography component="li" key={number} sx={{ mb: 0.5 }}>
              {t(`deleteAccount.deleted${number}`)}
            </Typography>
          ))}
        </Box>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="deleteAccount.nothingRemains"
            components={[<strong key="a" />, <strong key="b" />]}
          />
        </Typography>
      </Section>

      <Section title={t("deleteAccount.remainsTitle")}>
        <Typography variant="body2" color="text.secondary">
          <Trans i18nKey="deleteAccount.remainsText" components={{ 1: <em /> }} />
        </Typography>
      </Section>

      {companies.length > 0 ? (
        <Section title={t("deleteAccount.companiesTitle")}>
          <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
            {companies.map((company) => (
              <Typography component="li" key={company.id} sx={{ mb: 0.5 }}>
                <strong>{company.name}</strong>{" "}
                {company.role === "admin" ? (
                  <Trans
                    i18nKey="deleteAccount.companyAdmin"
                    components={{ 1: <em />, 3: <strong /> }}
                  />
                ) : (
                  t("deleteAccount.companyMember")
                )}
              </Typography>
            ))}
          </Box>
          <Typography variant="body2" color="text.secondary">
            <Trans
              i18nKey="deleteAccount.companiesDoNotBlock"
              components={{ 1: <strong /> }}
            />
          </Typography>
        </Section>
      ) : null}

      <Section title={t("deleteAccount.processTitle")}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          <Trans
            i18nKey="deleteAccount.processText"
            components={{ 1: <strong />, 3: <strong /> }}
          />
        </Typography>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="deleteAccount.processExport"
            components={{ 1: <Link component={RouterLink} to="/my-data" /> }}
          />
        </Typography>
      </Section>

      <Card>
        <CardContent>
          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          {asking ? (
            <>
              <Alert severity="warning" sx={{ mb: 2 }}>
                {t("deleteAccount.lastQuestion")}
              </Alert>
              <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
                <Button
                  variant="contained"
                  color="error"
                  onClick={() => void erase()}
                  disabled={running}
                >
                  {running ? t("deleteAccount.inProgress") : t("deleteAccount.confirm")}
                </Button>
                <Button
                  variant="text"
                  onClick={() => {
                    setAsking(false);
                    setError(null);
                  }}
                  disabled={running}
                >
                  {t("common.cancel")}
                </Button>
              </Box>
            </>
          ) : (
            <Button
              variant="outlined"
              color="error"
              onClick={() => setAsking(true)}
            >
              {t("deleteAccount.button")}
            </Button>
          )}
        </CardContent>
      </Card>
    </PageShell>
  );
}
