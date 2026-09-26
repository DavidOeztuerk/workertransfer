import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { type Case, getMyCases, submitSolution } from "../api/assessments";

/**
 * `/assessments` — the work samples that were set for a human being.
 *
 * <strong>The effort stands in front, and it stands large.</strong> A work
 * sample without a stated effort is a task whose price one only learns after
 * paying it (ADR-0042 §3). That is why "4 hours" on this page is no side note
 * but stands beside the heading.
 *
 * <strong>There is no decline button, and that is deliberate.</strong> Whoever
 * does not want to does nothing; the deadline passes. A polite decline button
 * would be kinder to the company and would create the record ADR-0042 §3
 * forbids — "declined three times" is a fact about a human being, and it would
 * come about from individually reasonable clicks. The sentence at the top says
 * so expressly, so that nobody guesses whether a missing button is an accident.
 *
 * <strong>And the evaluation stands here, always.</strong> Also on a
 * rejection, also after somebody revoked their visibility: this list does not
 * ask the ledger. A judgement the judged never reads is exactly what this
 * platform does not build.
 */
export function AssessmentsPage() {
  const { t } = useTranslation();
  const { signedIn, loading, subjectId } = useActor();

  const cases = useAsync((signal) => getMyCases(signal), [subjectId], signedIn);

  if (loading) {
    return (
      <PageShell title={t("assessments.title")} narrow>
        <LoadingBlock label={t("assessments.loading")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return (
      <PageShell title={t("assessments.title")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("errors.signIn")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("assessments.title")} narrow>
      <Typography sx={{ mb: 1 }}>{t("assessments.lead")}</Typography>

      <Alert
        // Not dismissible and no tooltip: whoever does not want to do a task
        // should read this without having to look for it.
        severity="info"
        sx={{ mb: 3 }}
      >
        {t("assessments.noDecline")}
      </Alert>

      {cases.pending && <LoadingBlock label={t("assessments.loading")} />}

      {cases.data?.ok === false && (
        <Alert severity="warning">{t("errors.ledgerSilent")}</Alert>
      )}

      {cases.data?.ok === true && cases.data.items.length === 0 && (
        <EmptyBlock title={t("assessments.empty")} hint={t("assessments.emptyHint")} />
      )}

      <Stack spacing={2}>
        {cases.data?.ok === true &&
          cases.data.items.map((caseItem) => (
            <MyCaseCard
              key={caseItem.id}
              caseItem={caseItem}
              done={() => cases.reload()}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** One card per case — task, effort, one's own solution, feedback. */
function MyCaseCard({ caseItem, done }: { caseItem: Case; done: () => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState("");
  const [url, setUrl] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setBusy(true);
    const result = await submitSolution(caseItem.id, text, url.trim() || null);
    setBusy(false);

    if (result.ok) {
      setError(null);
      done();
      return;
    }

    setError(result.error.detail || result.error.title);
  }

  const open = caseItem.state === "set";
  const empty = text.trim().length === 0 && url.trim().length === 0;

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap", gap: 1 }}>
          <Chip label={t(`assessments.state_${caseItem.state}`)} size="small" />
          <Chip
            // The effort, BESIDE the state and not in the small print.
            label={t("assessments.effort", { stunden: caseItem.hours })}
            size="small"
            color="primary"
            variant="outlined"
          />
          <Chip
            label={t("assessments.deadline", { datum: formatDate(caseItem.due_at) })}
            size="small"
            variant="outlined"
          />
        </Stack>

        <Typography variant="h3" sx={{ mb: 1 }}>
          {caseItem.title}
        </Typography>

        <Typography sx={{ mb: 2, whiteSpace: "pre-wrap" }}>{caseItem.task}</Typography>

        {caseItem.submission !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">{t("assessments.yourSolution")}</Typography>
            {caseItem.submission.text.length > 0 && (
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>
                {caseItem.submission.text}
              </Typography>
            )}
            {caseItem.submission.url !== null && (
              <Link href={caseItem.submission.url} rel="noreferrer noopener" target="_blank">
                {caseItem.submission.url}
              </Link>
            )}
          </>
        )}

        {caseItem.evaluation !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Alert
              // THE EVALUATION. It stands here as soon as it exists — on an
              // acceptance as on a rejection, and regardless of what is released
              // right now (ADR-0042 §2).
              severity={caseItem.evaluation.outcome === "accepted" ? "success" : "info"}
              sx={{ mb: 1 }}
            >
              {t(`assessments.outcome_${caseItem.evaluation.outcome}`)}
            </Alert>
            <Typography variant="subtitle2">{t("assessments.feedback")}</Typography>
            <Typography sx={{ whiteSpace: "pre-wrap" }}>{caseItem.evaluation.text}</Typography>
          </>
        )}

        {open && (
          <>
            <Divider sx={{ my: 2 }} />

            {error !== null && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {error}
              </Alert>
            )}

            <Stack spacing={2}>
              <TextField
                label={t("assessments.solutionText")}
                helperText={t("assessments.solutionHint")}
                value={text}
                onChange={(event) => setText(event.target.value)}
                multiline
                minRows={3}
                fullWidth
              />
              <TextField
                label={t("assessments.solutionLink")}
                helperText={t("assessments.solutionLinkHint")}
                value={url}
                onChange={(event) => setUrl(event.target.value)}
                fullWidth
              />
              <Stack direction="row">
                <Button
                  variant="contained"
                  disabled={busy || empty}
                  onClick={() => void submit()}
                >
                  {t("assessments.submit")}
                </Button>
              </Stack>
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** A date as it stands. The language comes from the session, not from here. */
function formatDate(raw: string): string {
  return new Date(raw).toLocaleDateString();
}

export default AssessmentsPage;
