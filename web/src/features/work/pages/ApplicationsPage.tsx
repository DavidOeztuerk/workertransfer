import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import Link from "@mui/material/Link";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Application,
  type ApplicationStatus,
  listMyApplications,
  withdrawApplication,
} from "../api/applications";

/** The status as a catalogue key — the wording lies in the catalogues. */
const STATUS_KEY: Record<ApplicationStatus, string> = {
  submitted: "applications.statusSubmitted",
  reviewing: "applications.statusReviewing",
  rejected: "applications.statusRejected",
  withdrawn: "applications.statusWithdrawn",
  hired: "applications.statusHired",
};

/** Is the application still running — that is, does the company see something right now? */
const running = (current: ApplicationStatus) =>
  current === "submitted" || current === "reviewing";

/**
 * What was opened with this application.
 *
 * The profile is always included — without it there would be nothing to read.
 * CV and work only if the person expressly gave them along when applying.
 */
function releasedParts(
  application: Application,
  t: (key: string) => string,
): string {
  const parts = [t("applications.partProfile")];
  if (application.shares_resume) parts.push(t("applications.partResume"));
  if (application.shares_portfolio) parts.push(t("applications.partPortfolio"));
  return parts.join(", ");
}

/**
 * <c>/applications</c> — one's own applications.
 *
 * <strong>An application opens data, and the page says which.</strong> While
 * it runs, the company sees the profile and whatever else was released; once it
 * is withdrawn, access is closed immediately. The case stays with the company
 * as what it was — withdrawing deletes no history, it only closes the access.
 *
 * <strong>Only running ones get "Zurückziehen".</strong> On a rejected or
 * already withdrawn one the button would be an act without effect.
 */
export function ApplicationsPage() {
  const { t } = useTranslation();
  const { signedIn, subjectId } = useActor();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const applications = useAsync(
    (signal) => listMyApplications(signal),
    [subjectId],
    signedIn,
  );

  if (!signedIn) {
    return (
      <PageShell title={t("applications.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="applications.signInRequired"
                components={{ 1: <Link component={RouterLink} to="/login" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function withdraw(id: string) {
    setBusy(true);
    const result = await withdrawApplication(id);
    setBusy(false);

    setError(result.ok ? null : result.error.detail);
    applications.reload();
  }

  const result = applications.data;
  const list = result?.ok ? result.applications : [];

  return (
    <PageShell
      title={t("applications.title")}
      narrow
      lead={t("applications.lead")}
    >
      <Typography variant="body2" sx={{ mb: 2 }}>
        <Link component={RouterLink} to="/applications/drafts">
          {t("applications.toDrafts")}
        </Link>
      </Typography>
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      <Card>
        <CardContent>
          {applications.pending ? (
            <LoadingBlock
              // Its own branch, before error and empty: without it no branch
              // matched while the list was in flight and the card stayed empty.
              label={t("applications.loading")}
            />
          ) : null}

          {result !== null && !result.ok ? (
            <Alert severity="error">{result.error.detail}</Alert>
          ) : null}

          {result?.ok && list.length === 0 ? (
            <EmptyBlock
              title={t("applications.emptyTitle")}
              action={
                <Button component={RouterLink} to="/jobs" variant="contained">
                  {t("applications.emptyButton")}
                </Button>
              }
            />
          ) : null}

          {list.length > 0 ? (
            <Box
              component="ul"
              sx={{
                display: "flex",
                flexDirection: "column",
                gap: 1.5,
                listStyle: "none",
                p: 0,
                m: 0,
              }}
            >
              {list.map((application: Application) => (
                <Card key={application.id} component="li" variant="outlined">
                  <CardContent
                    sx={{
                      display: "flex",
                      flexDirection: { xs: "column", sm: "row" },
                      justifyContent: "space-between",
                      alignItems: { xs: "flex-start", sm: "center" },
                      gap: 2,
                    }}
                  >
                    <Box>
                      <Typography variant="h4">
                        {t(STATUS_KEY[application.status])}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        {running(application.status)
                          ? t("applications.released", {
                              teile: releasedParts(application, t),
                            })
                          : t("applications.noLongerVisible")}
                      </Typography>
                    </Box>

                    {running(application.status) ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void withdraw(application.id)}
                        disabled={busy}
                        sx={{ flexShrink: 0 }}
                      >
                        {t("common.withdraw")}
                      </Button>
                    ) : null}
                  </CardContent>
                </Card>
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}
