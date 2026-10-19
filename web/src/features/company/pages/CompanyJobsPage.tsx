import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { ADMIN_CASES, failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Job,
  closeJob,
  listOwnJobs,
  publishJob,
} from "../../work/api/jobs";

/** The state as a catalogue key — the wording lies in the catalogues. */
const STATUS_KEYS: Record<string, string> = {
  draft: "companyJobs.statusDraft",
  published: "companyJobs.statusPublished",
  closed: "companyJobs.statusClosed",
};

/**
 * <c>/company/jobs</c> — one's own adverts.
 *
 * <strong>Three states, and the middle one is public.</strong> A draft is seen
 * by nobody but the company; published it is visible to everyone, even without
 * an account; closed stays closed. A closed advert cannot be reopened — whoever
 * advertises anew writes a new advert.
 *
 * <strong>The form lies on an address of its own</strong>
 * (<c>/company/jobs/new</c>), and this list visibly links to it. An E2E journey
 * still looks for it here today; that is the journey's defect and not the
 * UI's, but the link must be findable for that reason.
 */
export function CompanyJobsPage() {
  const { t } = useTranslation();
  const { forCompany } = useActor();
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const jobs = useAsync(
    (signal) => listOwnJobs(signal),
    [forCompany],
    forCompany,
  );

  if (!forCompany) {
    return (
      <PageShell title={t("companyJobs.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("companyJobs.companyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function toggle(id: string, publish: boolean) {
    setRunning(true);
    const result = publish
      ? await publishJob(id)
      : await closeJob(id);
    setRunning(false);
    // Publishing and closing demand an `admin` (PBI-2); a 409 means the advert
    // already stands otherwise. A catalogue text, never the server's sentence.
    setError(
      result.ok
        ? null
        : t(
            failureKey(result.error, "errors.jobNotSaved", [
              ...ADMIN_CASES,
              { status: 409, key: "companyJobs.stateMovedOn" },
            ]),
          ),
    );
    jobs.reload();
  }

  const result = jobs.data;
  const list = result?.ok ? result.jobs : [];

  return (
    <PageShell
      title={t("companyJobs.title")}
      narrow
      lead={t("companyJobs.lead")}
      actions={
        <Button
          component={RouterLink}
          to="/company/jobs/new"
          variant="contained"
        >
          {t("companyJobs.new")}
        </Button>
      }
    >
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      <Card>
        <CardContent>
          {jobs.pending ? (
            <LoadingBlock
              // Its own branch, before error and empty: the empty sentence needs
              // a successful answer, so without this branch the card stayed
              // completely empty while loading.
              label={t("companyJobs.loading")}
            />
          ) : null}

          {result !== null && !result.ok ? (
            <Alert severity="error">
              {t(failureKey(result.error, "errors.listNotLoaded"))}
            </Alert>
          ) : null}

          {result?.ok && list.length === 0 ? (
            <EmptyBlock
              title={t("companyJobs.empty")}
              action={
                <Button
                  component={RouterLink}
                  to="/company/jobs/new"
                  variant="contained"
                >
                  {t("companyJobs.new")}
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
              {list.map((job: Job) => (
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
                        {t(STATUS_KEYS[job.status] ?? job.status)}
                      </Typography>
                    </Box>

                    {job.status !== "closed" ? (
                      <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
                        {job.status === "draft" ? (
                          <Button
                            variant="contained"
                            size="small"
                            onClick={() => void toggle(job.id, true)}
                            disabled={running}
                          >
                            {t("companyJobs.publish")}
                          </Button>
                        ) : null}
                        <Button
                          variant="text"
                          size="small"
                          onClick={() => void toggle(job.id, false)}
                          disabled={running}
                        >
                          {t("companyJobs.close")}
                        </Button>
                      </Box>
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
