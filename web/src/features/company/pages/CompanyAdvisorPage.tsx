import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { useActor } from "../../work/lib/session";
import { useAsync } from "../../work/lib/useAsync";
import {
  type Conversation,
  endAsCompany,
  getCompanyConversations,
  handOver,
} from "../../work/api/advisor";

/**
 * <c>/company/advisor</c> — a company's conversations.
 *
 * <strong>What is missing here is missing.</strong> The fields of the higher
 * stages are not contained in the answer when they are not released — not
 * `null`, but not there at all. This page must build no hint from that: no "not
 * released yet", no lock, no grey line. Such a hint would reveal exactly what
 * the stage holds back — that there is something (ADR-0037).
 *
 * <strong>And a conversation at stage 0 does not stand in this list at
 * all.</strong> The server leaves it out; an empty line would be the same hint
 * in list form.
 */
export function CompanyAdvisorPage() {
  const { t } = useTranslation();
  const { signedIn, loading, forCompany, tenantId } = useActor();

  const conversations = useAsync(
    (signal) => getCompanyConversations(signal),
    [tenantId],
    signedIn && forCompany,
  );

  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [handedOver, setHandedOver] = useState(false);

  if (loading) {
    return (
      <PageShell title={t("advisor.companyTitle")} narrow>
        <LoadingBlock label={t("advisor.loading")} />
      </PageShell>
    );
  }

  if (!signedIn || !forCompany) {
    return (
      <PageShell title={t("advisor.companyTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("advisor.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function act(id: string, action: "hand-over" | "end") {
    setRunning(true);
    const result = action === "hand-over" ? await handOver(id) : await endAsCompany(id);
    setRunning(false);

    setError(result.ok ? null : t(failureKey(result.error, "errors.conversationStepFailed")));
    setHandedOver(result.ok && action === "hand-over");
    conversations.reload();
  }

  return (
    <PageShell title={t("advisor.companyTitle")} narrow>
      <Typography sx={{ mb: 3 }}>{t("advisor.companyLead")}</Typography>

      {error !== null && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {handedOver && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {t("advisor.handOverDone")}
        </Alert>
      )}

      {conversations.pending && <LoadingBlock label={t("advisor.loading")} />}

      {conversations.data?.ok === false && (
        <Alert severity="warning">
          {conversations.data.reason === "no-company"
            ? t("advisor.companyOnly")
            : t("errors.ledgerSilent")}
        </Alert>
      )}

      {conversations.data?.ok === true && conversations.data.items.length === 0 && (
        <EmptyBlock title={t("advisor.companyEmpty")} hint={t("advisor.companyEmptyHint")} />
      )}

      <Stack spacing={2}>
        {conversations.data?.ok === true &&
          conversations.data.items.map((conversation) => (
            <CompanyConversationCard
              key={conversation.id}
              conversation={conversation}
              running={running}
              act={(action) => act(conversation.id, action)}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** One card per conversation — with exactly the fields that arrived. */
function CompanyConversationCard({
  conversation,
  running,
  act,
}: {
  conversation: Conversation;
  running: boolean;
  act: (action: "hand-over" | "end") => void;
}) {
  const { t } = useTranslation();
  const open = conversation.state === "talking" || conversation.state === "agreed";

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap" }}>
          <Chip label={t(`advisor.state_${conversation.state}`)} size="small" />
          <Chip
            label={t("advisor.companyStage", {
              stand: t(`advisor.stage${conversation.stage}Short`),
            })}
            size="small"
            variant="outlined"
          />
        </Stack>

        {conversation.name !== undefined ? (
          <Typography variant="h3" sx={{ mb: 1 }}>
            {conversation.name}
          </Typography>
        ) : (
          // Without a name the id is shown as what it is — a case reference,
          // not a heading. No placeholder: "anonymous" or "not released yet"
          // would claim the person hid themselves, and ADR-0020 §1 wants the
          // name absent rather than explained.
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{ mb: 1, fontFamily: "monospace" }}
          >
            {conversation.subject_id}
          </Typography>
        )}

        {conversation.email !== undefined && (
          <Typography variant="body2" sx={{ mb: 1 }}>
            {conversation.email}
          </Typography>
        )}

        {conversation.note.length > 0 && (
          <Typography sx={{ mb: 1 }}>{conversation.note}</Typography>
        )}

        <Divider sx={{ my: 2 }} />

        <Stack spacing={0.5} sx={{ mb: 2 }}>
          {conversation.entry_month !== undefined && (
            <Typography
              // From stage 1. If the field is missing, NOTHING stands here — no
              // "not given", no "not released yet": from the outside the two are
              // indistinguishable, and that is exactly the promise.
              variant="body2"
            >
              {t("advisor.earliestStart")}: {conversation.entry_month}
            </Typography>
          )}
          {conversation.workload_percent !== undefined && (
            <Typography variant="body2">
              {t("advisor.workload")}: {conversation.workload_percent}
            </Typography>
          )}
          {(conversation.salary_min !== undefined || conversation.salary_max !== undefined) && (
            <Typography
              // From stage 2 — absent and not released stay indistinguishable,
              // as above.
              variant="body2"
            >
              {conversation.salary_min !== undefined && conversation.salary_max !== undefined
                ? t("advisor.salaryRange", {
                    von: conversation.salary_min,
                    bis: conversation.salary_max,
                  })
                : conversation.salary_min !== undefined
                  ? t("advisor.salaryFromOnly", { von: conversation.salary_min })
                  : t("advisor.salaryToOnly", { bis: conversation.salary_max })}
            </Typography>
          )}
        </Stack>

        <Typography variant="caption" sx={{ display: "block", mb: 1 }}>
          {t("advisor.handOverHint")}
        </Typography>

        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
          {conversation.state === "agreed" && (
            <Button
              variant="contained"
              size="small"
              disabled={running}
              onClick={() => act("hand-over")}
            >
              {t("advisor.handOver")}
            </Button>
          )}

          {open && (
            <Button size="small" color="error" disabled={running} onClick={() => act("end")}>
              {t("advisor.end")}
            </Button>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}

export default CompanyAdvisorPage;
