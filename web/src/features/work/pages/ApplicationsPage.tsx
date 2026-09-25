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
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Application,
  type ApplicationStatus,
  listMyApplications,
  withdrawApplication,
} from "../api/applications";

/** Der Stand als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const STAND: Record<ApplicationStatus, string> = {
  submitted: "applications.statusSubmitted",
  reviewing: "applications.statusReviewing",
  rejected: "applications.statusRejected",
  withdrawn: "applications.statusWithdrawn",
  hired: "applications.statusHired",
};

/** Läuft die Bewerbung noch — also sieht das Unternehmen gerade etwas? */
const running = (current: ApplicationStatus) =>
  current === "submitted" || current === "reviewing";

/**
 * Was mit dieser Bewerbung geöffnet wurde.
 *
 * Das Profil ist immer dabei — ohne es gäbe es nichts zu lesen. Lebenslauf und
 * Arbeiten nur, wenn die Person sie beim Bewerben ausdrücklich mitgegeben hat.
 */
function freigegeben(
  bewerbung: Application,
  t: (key: string) => string,
): string {
  const parts = [t("applications.partProfile")];
  if (bewerbung.shares_resume) parts.push(t("applications.partResume"));
  if (bewerbung.shares_portfolio) parts.push(t("applications.partPortfolio"));
  return parts.join(", ");
}

/**
 * <c>/applications</c> — die eigenen Bewerbungen.
 *
 * <strong>Eine Bewerbung öffnet Daten, und die Seite sagt welche.</strong>
 * Solange sie läuft, sieht das Unternehmen das Profil und was sonst freigegeben
 * wurde; wird sie zurückgezogen, ist der Zugriff sofort zu. Der Vorgang bleibt
 * beim Unternehmen als das stehen, was er war — zurückziehen löscht keine
 * Geschichte, es schliesst nur den Zugang.
 *
 * <strong>Nur laufende bekommen „Zurückziehen".</strong> Bei einer abgelehnten
 * oder bereits zurückgezogenen wäre der Knopf eine Handlung ohne Wirkung.
 */
export function ApplicationsPage() {
  const { t } = useTranslation();
  const { signedIn, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuftGerade, setLaeuftGerade] = useState(false);

  const bewerbungen = useAsync(
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

  async function zurueckziehen(id: string) {
    setLaeuftGerade(true);
    const result = await withdrawApplication(id);
    setLaeuftGerade(false);

    setFehler(result.ok ? null : result.error.detail);
    bewerbungen.reload();
  }

  const result = bewerbungen.data;
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
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card>
        <CardContent>
          {bewerbungen.pending ? (
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
              {list.map((bewerbung: Application) => (
                <Card key={bewerbung.id} component="li" variant="outlined">
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
                        {t(STAND[bewerbung.status])}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        {running(bewerbung.status)
                          ? t("applications.released", {
                              teile: freigegeben(bewerbung, t),
                            })
                          : t("applications.noLongerVisible")}
                      </Typography>
                    </Box>

                    {running(bewerbung.status) ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void zurueckziehen(bewerbung.id)}
                        disabled={laeuftGerade}
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
