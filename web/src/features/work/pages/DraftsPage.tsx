import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";
import { useLocation, useNavigate } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { SignInRequired } from "../../person/components/SignInRequired";
import { WritingProgress } from "../components/WritingProgress";
import { getJob } from "../api/jobs";
import { remember } from "../lib/letterContext";
import { writingStart } from "../lib/writingClock";
import { writingKey } from "../lib/draftErrors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Draft,
  type DraftStatus,
  deleteDraft,
  getDrafts,
  writeDraft,
} from "../api/applications";

const STATUS_COLOUR: Record<
  DraftStatus,
  "default" | "primary" | "warning" | "info" | "error"
> = {
  generating: "warning",
  review: "info",
  needs_changes: "warning",
  approved: "primary",
  sent: "default",
  failed: "error",
};

/**
 * The router state the job list hands over after a bulk start. Not the wire and
 * not stored — it travels between two pages of this area and is named in both.
 */
export type DraftsRouteState = { writing?: "ok" | "noProvider" } | null;

export function DraftsPage() {
  const { t } = useTranslation();
  const { signedIn, loading: sessionLoading } = useActor();
  const location = useLocation();
  const navigate = useNavigate();
  const writingState = (location.state as DraftsRouteState)?.writing ?? null;
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [titles, setTitles] = useState<Record<string, string>>({});
  const [running, setRunning] = useState<string | null>(null);

  function clearMessages() {
    setError(null);
    setSuccess(null);
    if (writingState !== null) {
      navigate(".", { replace: true, state: {} });
    }
  }

  const drafts = useAsync((signal) => getDrafts(signal), [], signedIn);
  const list = drafts.data?.ok ? drafts.data.drafts : [];
  const jobIds = list.map((d) => d.job_id).join(",");
  const anyWriting = list.some((d) => d.status === "generating");

  useEffect(() => {
    if (!anyWriting) return;
    const timer = window.setInterval(() => drafts.poll(), 2000);
    return () => window.clearInterval(timer);
  }, [anyWriting]);

  useEffect(() => {
    if (!signedIn || jobIds === "") return;
    const ids = [...new Set(jobIds.split(","))];
    let gone = false;
    void Promise.all(ids.map((id) => remember(`job:${id}`, () => getJob(id)))).then((jobs) => {
      if (gone) return;
      const next: Record<string, string> = {};
      ids.forEach((id, i) => {
        if (jobs[i]) next[id] = jobs[i]!.title;
      });
      setTitles(next);
    });
    return () => {
      gone = true;
    };
  }, [signedIn, jobIds]);

  if (sessionLoading) {
    return (
      <PageShell title={t("drafts.title")} narrow>
        <LoadingBlock label={t("drafts.loading")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return <SignInRequired title={t("drafts.title")} sentenceKey="drafts.signInRequired" />;
  }

  async function retry(draft: Draft) {
    clearMessages();
    const result = await writeDraft(draft.id);
    if (!result.ok) {
      setError(t(writingKey(result.error.detail)));
      return;
    }
    navigate(`/applications/drafts/${draft.id}`);
  }

  async function remove(id: string) {
    clearMessages();
    setRunning(id);
    const result = await deleteDraft(id);
    setRunning(null);
    if (!result.ok) setError(result.error.detail);
    drafts.reload();
  }

  return (
    <PageShell title={t("drafts.title")} lead={t("drafts.lead")}>
      {writingState === "noProvider" && running === null ? (
        <Alert
          severity="info"
          sx={{ mb: 2 }}
          onClose={() => navigate(".", { replace: true, state: {} })}
        >
          {t("drafts.writingFailed")}
        </Alert>
      ) : null}
      {error && running === null ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      ) : null}
      {success && running === null ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setSuccess(null)}>
          {success}
        </Alert>
      ) : null}

      {drafts.pending && list.length === 0 ? (
        <LoadingBlock label={t("drafts.loading")} />
      ) : null}
      {drafts.data !== null && !drafts.data.ok ? (
        <Alert severity="error">{drafts.data.error.detail}</Alert>
      ) : null}
      {drafts.data?.ok && list.length === 0 ? (
        <EmptyBlock
          title={t("drafts.emptyTitle")}
          hint={t("drafts.emptyHint")}
          action={
            <Button onClick={() => navigate("/jobs")} variant="contained">
              {t("applications.emptyButton")}
            </Button>
          }
        />
      ) : null}
      {list.length > 0 ? (
        <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 2 }}>
          {list.map((draft) => {
            const status: DraftStatus = draft.status;
            const openCount = draft.comments.filter((c) => !c.resolved).length;
            const hint = status === "failed" ? t(writingKey(draft.error)) : null;
            return (
              <Card key={draft.id} component="li" variant="outlined">
                <CardContent
                  sx={{
                    display: "flex",
                    flexDirection: { xs: "column", sm: "row" },
                    justifyContent: "space-between",
                    gap: 2,
                  }}
                >
                  <Box sx={{ minWidth: 0, flex: 1 }}>
                    <Typography variant="h4">
                      {titles[draft.job_id] ?? t("drafts.jobUnknown")}
                    </Typography>
                    <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", mt: 0.5, mb: 1 }}>
                      <Chip
                        size="small"
                        label={t(`drafts.status.${status}`)}
                        color={STATUS_COLOUR[status]}
                      />
                      <Typography variant="body2" color="text.secondary">
                        {t("drafts.version", { version: draft.version })}
                      </Typography>
                      {openCount > 0 ? (
                        <Typography variant="body2" color="text.secondary">
                          {t("drafts.comments", { count: openCount })}
                        </Typography>
                      ) : null}
                    </Box>
                    {status === "review" ? (
                      <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                        {t("drafts.writingDone")}
                      </Typography>
                    ) : status === "generating" ? (
                      <WritingProgress
                        since={writingStart(draft.id, draft.writing_started_at)}
                      />
                    ) : hint ? (
                      <Alert severity="info" sx={{ mt: 1.5 }}>
                        {hint}
                      </Alert>
                    ) : null}
                  </Box>
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", flexShrink: 0, alignItems: "flex-start" }}>
                    <Button
                      size="small"
                      variant="contained"
                      onClick={() => navigate(`/applications/drafts/${draft.id}`)}
                    >
                      {t("drafts.open")}
                    </Button>
                    {draft.status === "failed" ? (
                      <Button size="small" onClick={() => void retry(draft)}>
                        {t("drafts.retry")}
                      </Button>
                    ) : null}
                    {draft.status !== "sent" ? (
                      <Button
                        size="small"
                        color="error"
                        disabled={running !== null}
                        onClick={() => void remove(draft.id)}
                      >
                        {t("common.withdraw")}
                      </Button>
                    ) : null}
                  </Box>
                </CardContent>
              </Card>
            );
          })}
        </Box>
      ) : null}
    </PageShell>
  );
}
