import { useState } from "react";

import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import CircularProgress from "@mui/material/CircularProgress";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { useAsync } from "../lib/useAsync";
import { getCompanyBySlug } from "../api/companies";
import { remoteLabel, searchJobs } from "../../work/api/jobs";
import { startDrafts } from "../../work/lib/startDrafts";
import { useActor } from "../lib/session";
import { Trans, useTranslation } from "react-i18next";

/**
 * <c>/careers/&lt;slug&gt;</c> — a company's public page.
 *
 * <strong>Readable without an account</strong>, like the job list itself: an
 * advert one only sees after signing in misses exactly those it is meant for.
 *
 * <strong>"Not retrievable right now" is not "does not exist".</strong> Both
 * cases stand apart here, each with a sentence of its own — otherwise someone
 * reads that their company does not exist because a service is silent right
 * now.
 *
 * <strong>"Apply" is a button and not a link.</strong> It pointed at
 * <c>/jobs/:id/apply</c>, and that address had lately become a mere switch: it
 * created a draft and redirected. An address without a view is a stopover
 * nobody is meant to see — and it carried a dead form with release boxes that
 * nobody reached after it. The operation now stands in
 * <c>work/lib/startDrafts</c>, in ONE place, and this button calls exactly the
 * same one as the button on the job list.
 */
export function CareerPage() {
  const { slug } = useParams();
  const requestedSlug = slug ?? "";

  const { t } = useTranslation();
  const navigate = useNavigate();
  const { signedIn } = useActor();
  const [runningJobId, setRunningJobId] = useState<string | null>(null);
  const [applyError, setApplyError] = useState<string | null>(null);

  async function apply(jobId: string) {
    if (!signedIn) {
      void navigate("/login");
      return;
    }
    setRunningJobId(jobId);
    setApplyError(null);
    try {
      const start = await startDrafts([jobId]);
      if (!start.ok) {
        setApplyError(start.error === null ? null : t(start.error));
        return;
      }
      const first = start.drafts[0];
      if (first) void navigate(`/applications/drafts/${first.id}`);
    } finally {
      setRunningJobId(null);
    }
  }

  const company = useAsync(
    (signal) => getCompanyBySlug(requestedSlug, signal),
    [requestedSlug],
    requestedSlug !== "",
  );

  const profile = company.data?.ok ? company.data.profile : undefined;
  const tenantId = profile?.tenant_id;

  const jobs = useAsync(
    (signal) =>
      searchJobs({ company: tenantId as string }, 1, 50, signal),
    [tenantId],
    tenantId !== undefined,
  );

  if (company.pending) {
    return (
      <PageShell title={t("careers.title")} narrow>
        <Card>
          <CardContent>
            <LoadingBlock label={t("careers.loading")} />
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (
    company.data?.ok === false &&
    company.data.reason === "unavailable"
  ) {
    return (
      <PageShell title={t("careers.title")} narrow>
        <Alert severity="warning">
          {t(failureKey(company.data.error, "errors.pageUnavailable"))}{" "}
          {t("careers.unavailable")}
        </Alert>
      </PageShell>
    );
  }

  if (profile === undefined) {
    return (
      <PageShell title={t("careers.noCompanyTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="careers.noCompany"
                components={{ 1: <Link component={RouterLink} to="/jobs" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const result = jobs.data;
  const list = result?.ok ? result.items : [];
  const fetchFailed =
    result !== null && result !== undefined && !result.ok;

  return (
    <PageShell title={profile.display_name} narrow>
      {profile.website !== null ? (
        <Box sx={{ mb: 3 }}>
          <Link href={profile.website} target="_blank" rel="noreferrer noopener">
            {profile.website}
          </Link>
        </Box>
      ) : null}

      {profile.about !== "" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              {t("careers.aboutUs")}
            </Typography>
            <Typography>{profile.about}</Typography>
          </CardContent>
        </Card>
      ) : null}

      {profile.locations.length > 0 || profile.benefits.length > 0 ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            {profile.locations.length > 0 ? (
              <Typography sx={{ mb: profile.benefits.length > 0 ? 1.5 : 0 }}>
                {t("careers.locations", {
                  orte: profile.locations.join(", "),
                })}
              </Typography>
            ) : null}
            {profile.benefits.length > 0 ? (
              <Box component="ul" sx={{ pl: 2.5, m: 0 }}>
                {profile.benefits.map((benefit: string) => (
                  <Typography component="li" key={benefit}>
                    {benefit}
                  </Typography>
                ))}
              </Box>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("careers.openJobs")}
          </Typography>

          {applyError !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {applyError}
            </Alert>
          ) : null}

          {jobs.pending ? (
            <LoadingBlock label={t("careers.jobsLoading")} />
          ) : null}

          {fetchFailed ? (
            <Alert severity="warning">
              {t("careers.jobsUnavailable")}
            </Alert>
          ) : null}

          {!jobs.pending && !fetchFailed && list.length === 0 ? (
            <EmptyBlock title={t("careers.empty")} />
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
              {list.map((job) => (
                <Card key={job.id} component="li" variant="outlined">
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
                      <Typography variant="h4">{job.title}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        {job.location !== ""
                          ? job.location
                          : t("careers.locationMissing")}{" "}
                        · {remoteLabel(job.remote)}
                      </Typography>
                    </Box>
                    <Button
                      type="button"
                      variant="contained"
                      size="small"
                      sx={{ flexShrink: 0 }}
                      disabled={runningJobId !== null}
                      onClick={() => void apply(job.id)}
                      startIcon={
                        runningJobId === job.id ? (
                          <CircularProgress color="inherit" size={16} />
                        ) : null
                      }
                    >
                      {t("careers.apply")}
                    </Button>
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
