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

import { type MessageKey, failureKey } from "../../../shared/api/errors";
import { useAsync } from "../lib/useAsync";
import { useCompanyProfiles } from "../lib/useCompanyProfiles";
import { closeEmployerRoute, getEmployerRoute, openEmployerRoute } from "../api/releases";

/**
 * "Do you work here?" on a company's careers page — the person's half of the
 * two consents (C1, ADR-0050).
 *
 * <strong>Naming the employer IS the consent</strong>, so what follows from it
 * stands before the button, in full: who sees that she works here, what her
 * employer learns and when, and that without the employer's own consent
 * nothing happens. Kept low on the page and folded: it must not read like an
 * invitation to an employee who only came to look at the jobs.
 */
export function EmployerRouteOffer({ companyId, companyName }: { companyId: string; companyName: string }) {
  const { t } = useTranslation();
  const [unfolded, setUnfolded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<MessageKey | null>(null);
  const route = useAsync((signal) => getEmployerRoute(signal), [companyId]);
  const current = route.data?.ok ? route.data.route : null;

  if (current === null) return null;

  if (current.employer_id === companyId && current.open) {
    return (
      <Alert severity="info" sx={{ mt: 3 }}>
        <Trans
          i18nKey="employerRoute.opened"
          values={{ company: companyName }}
          components={{ 1: <Link component={RouterLink} to="/market" /> }}
        />
      </Alert>
    );
  }

  async function open() {
    setBusy(true);
    const result = await openEmployerRoute(companyId);
    setBusy(false);
    setError(result.ok ? null : failureKey(result.error, "employerRoute.failed"));
    route.reload();
  }

  return (
    <Card component="section" aria-label={t("employerRoute.careerTitle")} sx={{ mt: 3 }}>
      <CardContent sx={{ display: "grid", gap: 1.5 }}>
        <Typography variant="h3">{t("employerRoute.careerTitle")}</Typography>
        {!unfolded ? (
          <Box>
            <Button variant="text" onClick={() => setUnfolded(true)}>
              {t("employerRoute.unfold")}
            </Button>
          </Box>
        ) : (
          <>
            <Typography variant="body2">{t("employerRoute.careerLead", { company: companyName })}</Typography>
            <Typography variant="body2" color="text.secondary">
              {t("employerRoute.careerWhat")}
            </Typography>
            {current.employer_id !== null && current.employer_id !== companyId ? (
              <Typography variant="body2" color="warning.main">
                {t("employerRoute.replaces")}
              </Typography>
            ) : null}
            {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}
            <Box>
              <Button variant="contained" disabled={busy} onClick={() => void open()}>
                {t("employerRoute.open")}
              </Button>
            </Box>
          </>
        )}
      </CardContent>
    </Card>
  );
}

/**
 * The route on the person's market page — whether it stands, to whom, and the
 * one button that closes it. Opening happens on the employer's careers page,
 * where she can see which company she names.
 */
export function EmployerRouteCard({ employed = true }: { employed?: boolean }) {
  const { t, i18n } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const route = useAsync((signal) => getEmployerRoute(signal), []);
  const current = route.data?.ok ? route.data.route : null;
  const names = useCompanyProfiles(current?.employer_id ? [current.employer_id] : []);

  if (current === null) return null;
  // Without an employer only an open route is worth showing — to close it.
  if (!employed && !(current.employer_id !== null && current.open)) return null;

  const company =
    current.employer_id === null ? "" : (names[current.employer_id]?.display_name ?? t("release.noProfile"));

  async function close() {
    setBusy(true);
    const ok = await closeEmployerRoute();
    setBusy(false);
    setFailed(!ok);
    route.reload();
  }

  return (
    <Card component="section" aria-label={t("employerRoute.title")} sx={{ mt: 3 }}>
      <CardContent sx={{ display: "grid", gap: 1.5 }}>
        <Typography variant="h2">{t("employerRoute.title")}</Typography>
        {current.employer_id === null ? (
          <Typography variant="body2" color="text.secondary">
            {t("employerRoute.none")}
          </Typography>
        ) : (
          <>
            <Typography variant="body2">
              {current.open
                ? t("employerRoute.openLine", {
                    company,
                    date: current.opened_at ? new Date(current.opened_at).toLocaleDateString(i18n.language) : "—",
                  })
                : t("employerRoute.closedLine", { company })}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t("employerRoute.closeHint")}
            </Typography>
            {failed ? <Alert severity="error">{t("employerRoute.failed")}</Alert> : null}
            <Box>
              <Button variant="outlined" disabled={busy} onClick={() => void close()}>
                {t("employerRoute.close")}
              </Button>
            </Box>
          </>
        )}
      </CardContent>
    </Card>
  );
}
