import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey, type MessageKey } from "../../../shared/api/errors";
import { type Job, PERIOD_KEY, salaryText, searchJobs } from "../api/jobs";

/**
 * <c>/salaries</c> — what adverts pay (VISION-FRAGEN B5).
 *
 * <strong>Every figure names its source, and none is computed.</strong> The
 * list shows each advert's own range — title, place, range — and no average,
 * no median, no "you are worth": a figure the page computed would be an
 * opinion in the costume of a statistic, and twenty adverts are no market
 * (ADR-0022's reasoning, applied to jobs). Whoever reads the list sees the
 * spread and where each number came from.
 *
 * <strong>The official statistic is named and not yet connected.</strong>
 * Destatis GENESIS is the source the catalogue chose; its interface needs an
 * account, and what it answers has not been measured here. Until it is, the
 * page links the published tables instead of pretending to quote them.
 */
export function SalariesPage() {
  const { t, i18n } = useTranslation();
  const [term, setTerm] = useState("");
  const [jobs, setJobs] = useState<Job[] | null>(null);
  const [error, setError] = useState<MessageKey | null>(null);
  const [busy, setBusy] = useState(false);

  async function search() {
    setBusy(true);
    const result = await searchJobs({ q: term.trim(), withSalary: true }, 1, 50);
    setBusy(false);
    if (result.ok) {
      setJobs(result.items);
      setError(null);
    } else {
      setError(failureKey(result.error, "errors.searchFailed"));
    }
  }

  return (
    <PageShell title={t("salaries.title")} lead={t("salaries.lead")} narrow>
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void search();
            }}
            sx={{ display: "flex", gap: 1.5, flexWrap: "wrap", alignItems: "flex-start" }}
          >
            <TextField
              label={t("salaries.term")}
              helperText={t("salaries.termHint")}
              value={term}
              onChange={(event) => setTerm(event.target.value)}
            />
            <Button type="submit" variant="contained" disabled={busy || term.trim() === ""}>
              {t("salaries.search")}
            </Button>
          </Box>
        </CardContent>
      </Card>

      {busy ? <LoadingBlock label={t("salaries.loading")} /> : null}
      {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}

      {jobs !== null && !busy ? (
        jobs.length === 0 ? (
          <EmptyBlock title={t("salaries.none")} hint={t("salaries.noneHint")} />
        ) : (
          <Card sx={{ mb: 3 }}>
            <CardContent>
              <Typography variant="h2" sx={{ mb: 0.5 }}>
                {t("salaries.platformTitle")}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {t("salaries.platformSource", { count: jobs.length, date: new Date().toLocaleDateString(i18n.language) })}
              </Typography>
              <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 1.5 }}>
                {jobs.map((job) =>
                  job.salary !== null ? (
                    <Box component="li" key={job.id}>
                      <Typography variant="h4">{job.title}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        {job.location !== "" ? `${job.location} · ` : ""}
                        {salaryText(job.salary, i18n.language, (period) => t(PERIOD_KEY[period]))}
                      </Typography>
                    </Box>
                  ) : null,
                )}
              </Box>
            </CardContent>
          </Card>
        )
      ) : null}

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 0.5 }}>
            {t("salaries.officialTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            {t("salaries.officialNotConnected")}
          </Typography>
          <Link
            href="https://www.destatis.de/DE/Themen/Arbeit/Verdienste/Verdienste-Branche-Berufe/_inhalt.html"
            target="_blank"
            rel="noopener noreferrer"
          >
            {t("salaries.officialLink")}
          </Link>
        </CardContent>
      </Card>
    </PageShell>
  );
}
