import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { i18n } from "../../../core/i18n/i18n";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { type ServerCase, failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type MyConversation,
  advanceStage,
  agree,
  endAsPerson,
  getMandate,
  getMyConversations,
  saveMandate,
  withdrawStage,
} from "../api/advisor";

/** A number from a text field, or `null` for "said nothing". */
function toNumber(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed.length === 0) return null;
  const value = Number.parseInt(trimmed, 10);
  return Number.isNaN(value) ? null : value;
}

/** A comma list as domains, without empty entries. */
function domains(raw: string): string[] {
  return raw
    .split(",")
    .map((entry) => entry.trim().toLowerCase())
    .filter((entry) => entry.length > 0);
}

/**
 * <c>/advisor</c> — one's own mandate and one's own conversations.
 *
 * <strong>There is no visibility switch here.</strong> Who may see you, you
 * decide on the profile page and in every conversation on its own; the mandate
 * is four statements and nothing else (ADR-0037). A switch at this place would
 * be a second truth beside the ledger — and it diverges at the first
 * revocation.
 *
 * <strong>The stage stands on every conversation, and it comes from the
 * server.</strong> It is read fresh from the ledger with every request. That is
 * why this page reloads after every release instead of counting the number on
 * by itself: what holds right now only the ledger knows.
 *
 * <strong>And the warning at the exclusion field is no courtesy.</strong>
 * Whoever excludes a company loses "visible to all companies" — that is the
 * price of the ledger knowing no negation, and the person must read it before,
 * not notice it after.
 */

/**
 * The rules a mandate can break, as the person reads them — never the server's
 * sentence (advisor-service answers in English). Each pattern matches a
 * sentence in advisor-service's `Mandate.cs`, which points back here; what none
 * knows reads as `errors.mandateNotSaved`.
 */
const MANDATE_CASES: readonly ServerCase[] = [
  { status: 422, pattern: /entry date is a month/i, key: "advisor.entryMonthInvalid" },
  { status: 422, pattern: /workload is between/i, key: "advisor.workloadOutOfRange" },
  { status: 422, pattern: /salary is not negative/i, key: "advisor.salaryNegative" },
  { status: 422, pattern: /lower bound of a salary range/i, key: "advisor.salaryRangeReversed" },
  { status: 422, pattern: /at most \d+ excluded companies/i, key: "advisor.tooManyExclusions" },
  { status: 422, pattern: /excluded company is named by its domain/i, key: "advisor.exclusionByDomain" },
];

export function AdvisorPage() {
  const { t } = useTranslation();
  const { signedIn, loading, subjectId } = useActor();

  const mandate = useAsync((signal) => getMandate(signal), [subjectId], signedIn);
  const conversations = useAsync(
    (signal) => getMyConversations(signal),
    [subjectId],
    signedIn,
  );

  const [entryMonth, setEntryMonth] = useState("");
  const [salaryFrom, setSalaryFrom] = useState("");
  const [salaryTo, setSalaryTo] = useState("");
  const [workload, setWorkload] = useState("");
  const [excluded, setExcluded] = useState("");
  const [adopted, setAdopted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // The loaded state fills the form EXACTLY ONCE. If that ran on every answer, a
  // reload would overwrite the number just typed — and a save would write it
  // back without anybody noticing.
  useEffect(() => {
    if (adopted || !mandate.data?.ok) return;
    const current = mandate.data.mandate;
    setEntryMonth(current.entry_month ?? "");
    setSalaryFrom(current.salary_min === null ? "" : String(current.salary_min));
    setSalaryTo(current.salary_max === null ? "" : String(current.salary_max));
    setWorkload(current.workload_percent === null ? "" : String(current.workload_percent));
    setExcluded(current.excluded_domains.join(", "));
    setAdopted(true);
  }, [adopted, mandate.data]);

  if (loading) {
    return (
      <PageShell title={t("advisor.title")} narrow>
        <LoadingBlock label={t("advisor.loading")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return (
      <PageShell title={t("advisor.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="advisor.signedInOnly"
                components={{ 1: <Link component={RouterLink} to="/login" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function save() {
    setBusy(true);
    const result = await saveMandate({
      entry_month: entryMonth.trim().length === 0 ? null : entryMonth.trim(),
      salary_min: toNumber(salaryFrom),
      salary_max: toNumber(salaryTo),
      workload_percent: toNumber(workload),
      excluded_domains: domains(excluded),
    });
    setBusy(false);

    if (result.ok) {
      setError(null);
      setSaved(true);
      mandate.reload();
      return;
    }

    setSaved(false);
    setError(t(failureKey(result.error, "errors.mandateNotSaved", MANDATE_CASES)));
  }

  async function moveStage(id: string, up: boolean, stage: number) {
    setBusy(true);
    const result = up ? await advanceStage(id, stage) : await withdrawStage(id, stage);
    setBusy(false);

    setError(result.ok ? null : t(failureKey(result.error, "errors.stageNotChanged")));
    // ALWAYS reload, also after a failure: what holds now, the ledger knows,
    // and not this page.
    conversations.reload();
  }

  async function step(id: string, what: "agree" | "end") {
    setBusy(true);
    const result = what === "agree" ? await agree(id) : await endAsPerson(id);
    setBusy(false);

    setError(result.ok ? null : t(failureKey(result.error, "errors.conversationStepFailed")));
    conversations.reload();
  }

  return (
    <PageShell title={t("advisor.title")} narrow>
      <Typography sx={{ mb: 3 }}>{t("advisor.lead")}</Typography>

      {error !== null && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>
            {t("advisor.mandateTitle")}
          </Typography>
          <Typography variant="body2" sx={{ mb: 2 }}>
            {t("advisor.mandateLead")}
          </Typography>

          {mandate.pending && <LoadingBlock label={t("advisor.loading")} />}

          {!mandate.pending && mandate.data !== null && !mandate.data.ok && (
            <Alert
              // No substitute form when the mandate cannot be read: whoever
              // touches something and saves silently erases their own entries.
              severity="warning"
            >
              {t("advisor.mandateUnavailable")}
            </Alert>
          )}

          {mandate.data?.ok === true && (
            <Stack spacing={2}>
              <TextField
                label={t("advisor.earliestStart")}
                helperText={t("advisor.earliestStartHint")}
                value={entryMonth}
                placeholder="2026-11"
                onChange={(event) => setEntryMonth(event.target.value)}
              />

              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField
                  label={t("advisor.salaryFrom")}
                  value={salaryFrom}
                  inputMode="numeric"
                  onChange={(event) => setSalaryFrom(event.target.value)}
                  fullWidth
                />
                <TextField
                  label={t("advisor.salaryTo")}
                  value={salaryTo}
                  inputMode="numeric"
                  onChange={(event) => setSalaryTo(event.target.value)}
                  fullWidth
                />
              </Stack>
              <Typography variant="caption">{t("advisor.salaryHint")}</Typography>

              <TextField
                label={t("advisor.workload")}
                helperText={t("advisor.workloadHint")}
                value={workload}
                inputMode="numeric"
                onChange={(event) => setWorkload(event.target.value)}
              />

              <TextField
                label={t("advisor.excluded")}
                helperText={t("advisor.excludedHint")}
                value={excluded}
                placeholder={t("advisor.excludedPlaceholder")}
                onChange={(event) => setExcluded(event.target.value)}
              />

              <Alert
                // Always visible and not only while typing: the price belongs
                // BEFORE the decision, not behind it.
                severity="info"
              >
                {t("advisor.excludedWarning")}
              </Alert>

              {saved && <Alert severity="success">{t("advisor.saved")}</Alert>}

              <Box>
                <Button variant="contained" onClick={save} disabled={busy}>
                  {busy ? t("advisor.saving") : t("advisor.save")}
                </Button>
              </Box>
            </Stack>
          )}
        </CardContent>
      </Card>

      <Typography variant="h2" sx={{ mb: 1 }}>
        {t("advisor.conversationsTitle")}
      </Typography>
      <Typography variant="body2" sx={{ mb: 2 }}>
        {t("advisor.conversationsLead")}
      </Typography>

      {conversations.pending && <LoadingBlock label={t("advisor.loading")} />}

      {conversations.data?.ok === false && (
        <Alert severity="warning">{t("errors.ledgerSilent")}</Alert>
      )}

      {conversations.data?.ok === true && conversations.data.items.length === 0 && (
        <EmptyBlock
          title={t("advisor.noConversations")}
          hint={t("advisor.noConversationsHint")}
        />
      )}

      <Stack spacing={2}>
        {conversations.data?.ok === true &&
          conversations.data.items.map((conversation) => (
            <ConversationCard
              key={conversation.id}
              conversation={conversation}
              busy={busy}
              onStage={(up, stage) => moveStage(conversation.id, up, stage)}
              onStep={(what) => step(conversation.id, what)}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/**
 * One card per conversation.
 *
 * <strong>The next step is exactly one button.</strong> Three buttons side by
 * side would make the choice look like a default; a stage is a standing, and
 * one walks it up.
 */
function ConversationCard({
  conversation,
  busy,
  onStage,
  onStep,
}: {
  conversation: MyConversation;
  busy: boolean;
  onStage: (up: boolean, stage: number) => void;
  onStep: (what: "agree" | "end") => void;
}) {
  const { t } = useTranslation();
  const next = conversation.stage + 1;
  const open = conversation.state === "talking" || conversation.state === "agreed";

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap" }}>
          <Chip label={t(`advisor.state_${conversation.state}`)} size="small" />
          <Chip
            label={t(`advisor.stage${conversation.stage}Short`)}
            size="small"
            color={conversation.stage > 0 ? "primary" : "default"}
            variant={conversation.stage > 0 ? "filled" : "outlined"}
          />
        </Stack>

        {conversation.note.length > 0 && (
          <Typography sx={{ mb: 1 }}>{conversation.note}</Typography>
        )}

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.openedOn", {
            datum: new Date(conversation.opened_at).toLocaleDateString(i18n.language),
          })}
        </Typography>

        <Typography variant="body2" sx={{ mb: 2 }}>
          {t(`advisor.stage${Math.min(conversation.stage + 1, 3)}`)}
        </Typography>

        <Divider sx={{ mb: 2 }} />

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.effectImmediate")}
        </Typography>

        {conversation.stage > 0 && (
          <Typography
            // "Withdraw everything" would promise too much: whoever set their
            // profile to "for all companies" stays visible through that, and the
            // stage does not fall to 0. That is right — a button in ONE
            // conversation must not flip a platform-wide switch — but it has to
            // be said here.
            variant="caption"
            sx={{ display: "block", mb: 1 }}
          >
            {t("advisor.backHint")}
          </Typography>
        )}

        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
          {open && next <= 3 && (
            <Button
              variant="contained"
              size="small"
              disabled={busy}
              onClick={() => onStage(true, next)}
            >
              {t("advisor.releaseStage", { nummer: next })}
            </Button>
          )}

          {conversation.stage > 0 && (
            <Button
              size="small"
              disabled={busy}
              // ONE button and not three: a stage is a standing and you walk it
              // down. `withdraw` takes everything from the named stage upwards —
              // from 3 leaves 2, from 1 leaves 0.
              onClick={() => onStage(false, conversation.stage)}
            >
              {conversation.stage === 1
                ? t("advisor.withdrawRelease")
                : t("advisor.backToStage", { nummer: conversation.stage - 1 })}
            </Button>
          )}

          {conversation.state === "talking" && (
            <Button size="small" disabled={busy} onClick={() => onStep("agree")}>
              {t("advisor.agree")}
            </Button>
          )}

          {open && (
            <Button size="small" color="error" disabled={busy} onClick={() => onStep("end")}>
              {t("advisor.end")}
            </Button>
          )}
        </Stack>

        {conversation.state === "agreed" && (
          <Alert severity="success" sx={{ mt: 2 }}>
            {t("advisor.agreed")}
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}

export default AdvisorPage;
