import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Link from "@mui/material/Link";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useActor } from "../../work/lib/session";
import { useAsync } from "../../work/lib/useAsync";
import {
  type Outcome,
  MAX_HOURS,
  MIN_HOURS,
  MIN_NOTICE_DAYS,
  type Case,
  evaluate,
  getCompanyCases,
  STEP_CASES,
  setTask,
} from "../../work/api/assessments";
import { failureKey } from "../../../shared/api/errors";

/**
 * `/company/assessments` — set work samples and answer them.
 *
 * <strong>Three promises, and all three are visible here</strong> (ADR-0042):
 *
 * 1. The feedback has a text field and <em>no mark</em>. There is no star bar,
 *    no scale and no second, internal field — where there were two, the truth
 *    would stand in the second.
 * 2. <strong>The reject button is locked as long as no text stands
 *    there.</strong> The server refuses a rejection without a reason anyway;
 *    this form says so beforehand instead of letting someone try it out.
 *    Rejecting and giving a reason are one step.
 * 3. The effort is a required field with an upper bound of eight hours, and the
 *    hint below it says why.
 *
 * <strong>This list shows only who has released right now.</strong> Whoever
 * revokes drops out — at the next call, not the one after. That is why there is
 * no total: through the difference to the length it would reveal how many
 * people withdrew the view (ADR-0026).
 */
export function CompanyAssessmentsPage() {
  const { t } = useTranslation();
  const { signedIn, loading, forCompany, tenantId } = useActor();

  const cases = useAsync(
    (signal) => getCompanyCases(signal),
    [tenantId],
    signedIn && forCompany
  );

  if (loading) {
    return (
      <PageShell title={t("assessments.companyTitle")} narrow>
        <LoadingBlock label={t("assessments.loading")} />
      </PageShell>
    );
  }

  if (!signedIn || !forCompany) {
    return (
      <PageShell title={t("assessments.companyTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("assessments.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("assessments.companyTitle")} narrow>
      <Typography sx={{ mb: 3 }}>{t("assessments.companyLead")}</Typography>

      <TaskForm onDone={() => cases.reload()} />

      {cases.pending && <LoadingBlock label={t("assessments.loading")} />}

      {cases.data?.ok === false && (
        <Alert severity="warning" sx={{ mt: 3 }}>
          {cases.data.reason === "no-company"
            ? t("assessments.companyOnly")
            : t("errors.ledgerSilent")}
        </Alert>
      )}

      {cases.data?.ok === true && cases.data.items.length === 0 && (
        <EmptyBlock title={t("assessments.companyEmpty")} hint={t("assessments.companyEmptyHint")} />
      )}

      <Stack spacing={2} sx={{ mt: 3 }}>
        {cases.data?.ok === true &&
          cases.data.items.map((caseItem) => (
            <CompanyCaseCard
              key={caseItem.id}
              caseItem={caseItem}
              onDone={() => cases.reload()}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** Set a task — effort and deadline are required. */
function TaskForm({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation();
  const [recipient, setRecipient] = useState("");
  const [title, setTitle] = useState("");
  const [task, setTaskText] = useState("");
  const [hours, setHours] = useState("4");
  const [deadline, setDeadline] = useState("");
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submitTask() {
    setRunning(true);

    const result = await setTask({
      subject_id: recipient.trim(),
      title,
      task,
      hours: Number(hours),
      // The day ends in the evening: a deadline at midnight would take the
      // last day from the person without saying so.
      due_at: new Date(`${deadline}T23:59:00`).toISOString(),
    });

    setRunning(false);

    if (result.ok) {
      setError(null);
      setRecipient("");
      setTitle("");
      setTaskText("");
      setDeadline("");
      onDone();
      return;
    }

    setError(t(failureKey(result.error, "errors.taskNotSet", STEP_CASES)));
  }

  const complete =
    recipient.trim().length > 0 &&
    title.trim().length > 0 &&
    task.trim().length > 0 &&
    deadline.length > 0;

  return (
    <Card>
      <CardContent>
        <Typography variant="h3" sx={{ mb: 2 }}>
          {t("assessments.setTask")}
        </Typography>

        {error !== null && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Stack spacing={2}>
          <TextField
            label={t("assessments.recipient")}
            helperText={t("assessments.recipientHint")}
            value={recipient}
            onChange={(event) => setRecipient(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("assessments.headline")}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("assessments.task")}
            value={task}
            onChange={(event) => setTaskText(event.target.value)}
            multiline
            minRows={4}
            fullWidth
          />
          <TextField
            label={t("assessments.effortField")}
            // Required, with an upper bound, and the hint says WHY — not "please
            // fill in" but what eight hours mean.
            helperText={t("assessments.effortHint", { max: MAX_HOURS })}
            value={hours}
            onChange={(event) => setHours(event.target.value)}
            type="number"
            slotProps={{
              htmlInput: { min: MIN_HOURS, max: MAX_HOURS, step: 1 },
            }}
            fullWidth
          />
          <TextField
            label={t("assessments.deadlineField")}
            helperText={t("assessments.deadlineHint", { tage: MIN_NOTICE_DAYS })}
            value={deadline}
            onChange={(event) => setDeadline(event.target.value)}
            type="date"
            slotProps={{ inputLabel: { shrink: true } }}
            fullWidth
          />
          <Stack direction="row">
            <Button
              variant="contained"
              disabled={running || !complete}
              onClick={() => void submitTask()}
            >
              {t("assessments.setTaskButton")}
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}

/** One card per case — with the evaluation field as soon as something was submitted. */
function CompanyCaseCard({ caseItem, onDone }: { caseItem: Case; onDone: () => void }) {
  const { t } = useTranslation();
  const [outcome, setOutcome] = useState<Outcome>("accepted");
  const [text, setText] = useState("");
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submitEvaluation() {
    setRunning(true);
    const result = await evaluate(caseItem.id, outcome, text);
    setRunning(false);

    if (result.ok) {
      setError(null);
      onDone();
      return;
    }

    setError(t(failureKey(result.error, "errors.evaluationNotSent", STEP_CASES)));
  }

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap", gap: 1 }}>
          <Chip label={t(`assessments.state_${caseItem.state}`)} size="small" />
          <Chip
            label={t("assessments.effort", { stunden: caseItem.hours })}
            size="small"
            variant="outlined"
          />
        </Stack>

        <Typography variant="h3" sx={{ mb: 1 }}>
          {caseItem.title}
        </Typography>

        <Typography variant="body2" sx={{ mb: 2 }}>
          {caseItem.subject_id}
        </Typography>

        {caseItem.submission !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">{t("assessments.submitted")}</Typography>
            {caseItem.submission.text.length > 0 && (
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>
                {caseItem.submission.text}
              </Typography>
            )}
            {caseItem.submission.url !== null && (
              <Link
                // A reference, never embedded content: a stranger chose this
                // address, and neither the server nor the page fetches it.
                href={caseItem.submission.url}
                rel="noreferrer noopener"
                target="_blank"
              >
                {caseItem.submission.url}
              </Link>
            )}
          </>
        )}

        {caseItem.evaluation !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">
              {t(`assessments.outcome_${caseItem.evaluation.outcome}`)}
            </Typography>
            <Typography sx={{ whiteSpace: "pre-wrap" }}>{caseItem.evaluation.text}</Typography>
          </>
        )}

        {caseItem.state === "submitted" && (
          <>
            <Divider sx={{ my: 2 }} />

            {error !== null && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {error}
              </Alert>
            )}

            <Stack spacing={2}>
              <FormControl>
                <FormLabel>{t("assessments.outcomeQuestion")}</FormLabel>
                <RadioGroup
                  row
                  value={outcome}
                  onChange={(event) => setOutcome(event.target.value as Outcome)}
                >
                  <FormControlLabel
                    value="accepted"
                    control={<Radio />}
                    label={t("assessments.outcome_accepted")}
                  />
                  <FormControlLabel
                    value="rejected"
                    control={<Radio />}
                    label={t("assessments.outcome_rejected")}
                  />
                </RadioGroup>
              </FormControl>

              <TextField
                // ONE field, and it is the same one the person reads. No
                // internal note beside it, no mark, no scale (ADR-0042 §1).
                label={t("assessments.feedbackField")}
                helperText={t("assessments.feedbackHint")}
                value={text}
                onChange={(event) => setText(event.target.value)}
                multiline
                minRows={4}
                fullWidth
              />

              <Stack direction="row">
                <Button
                  variant="contained"
                  disabled={running || text.trim().length === 0}
                  onClick={() => void submitEvaluation()}
                >
                  {t("assessments.sendFeedback")}
                </Button>
              </Stack>
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default CompanyAssessmentsPage;
