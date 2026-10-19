import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { failureKey, type MessageKey } from "../../../shared/api/errors";
import {
  type Conversation,
  type Message,
  type ProcessKind,
  DRAFT_CASES,
  askAdvisor,
  discardDraft,
  getConversation,
  releaseDraft,
  requestDraft,
  sendMessage,
} from "../api/messages";

/**
 * One conversation per application (ADR-0047), the same panel for both sides.
 *
 * <strong>A draft goes nowhere until a human being presses "Freigeben"</strong>
 * (VISION-FRAGEN D2). It stands in an editable field, marked as a draft, and
 * the other side never learns of it. There is no auto-send, no "send all".
 *
 * <strong>The hint about health data stands above the field, always</strong>
 * (E2, Art. 9 DSGVO) — not only when a model is involved: a message is stored,
 * and a model may read it later when somebody asks for a draft.
 */
export function ConversationPanel({
  reference,
  kind = "application",
}: {
  reference: string;
  kind?: ProcessKind;
}) {
  const { t } = useTranslation();
  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<MessageKey | null>(null);
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [advice, setAdvice] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      const result = await getConversation(reference, signal, kind);
      if (signal?.aborted) return;
      setLoading(false);
      if (result.ok) {
        setConversation(result.conversation);
      } else {
        setError(failureKey(result.error, "errors.conversationNotLoaded"));
      }
    },
    [reference, kind]
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  async function send() {
    setBusy(true);
    const result = await sendMessage(reference, text, kind);
    setBusy(false);
    if (result.ok) {
      setText("");
      setError(null);
      await load();
    } else {
      setError(failureKey(result.error, "errors.messageNotSent"));
    }
  }

  async function draft() {
    setBusy(true);
    const result = await requestDraft(reference, kind);
    setBusy(false);
    if (result.ok) {
      setError(null);
      await load();
    } else {
      setError(failureKey(result.error, "errors.draftNotCreated", DRAFT_CASES));
    }
  }

  async function advise() {
    setBusy(true);
    const result = await askAdvisor(reference, kind);
    setBusy(false);
    if (result.ok) {
      setAdvice(result.advice);
      setError(null);
    } else {
      setError(failureKey(result.error, "errors.adviceNotGiven", DRAFT_CASES));
    }
  }

  if (loading) return <LoadingBlock label={t("messages.loading")} />;

  if (conversation === null) {
    return <Alert severity="error">{t(error ?? "errors.conversationNotLoaded")}</Alert>;
  }

  const sent = conversation.messages.filter((message) => message.state === "sent");
  const drafts = conversation.messages.filter((message) => message.state === "draft" && message.own);

  return (
    <Box sx={{ display: "grid", gap: 2 }}>
      {sent.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          {t("messages.empty")}
        </Typography>
      ) : (
        <Box
          component="ol"
          aria-label={t("messages.listLabel")}
          sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 1.5 }}
        >
          {sent.map((message) => (
            <SentMessage key={message.id} message={message} />
          ))}
        </Box>
      )}

      {drafts.map((message) => (
        <DraftCard
          key={message.id}
          message={message}
          onDone={async (failure) => {
            setError(failure);
            await load();
          }}
        />
      ))}

      {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}

      <Alert severity="info">{t("messages.noHealthData")}</Alert>

      <TextField
        label={t("messages.compose")}
        multiline
        minRows={3}
        value={text}
        onChange={(event) => setText(event.target.value)}
        slotProps={{ htmlInput: { maxLength: 4000 } }}
      />
      <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
        <Button variant="contained" onClick={() => void send()} disabled={busy || text.trim() === ""}>
          {t("messages.send")}
        </Button>
        <Button variant="outlined" onClick={() => void draft()} disabled={busy}>
          {t("messages.requestDraft")}
        </Button>
        {conversation.side === "person" ? (
          // The advisor works for the person only (ADR-0049, D4) — the company
          // side has no such button and the server refuses it.
          <Button variant="text" onClick={() => void advise()} disabled={busy}>
            {t("messages.askAdvisor")}
          </Button>
        ) : null}
      </Box>
      {advice !== null ? (
        <Alert severity="info" role="note" aria-label={t("messages.adviceLabel")}>
          <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", mb: 1 }}>
            {advice}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {t("messages.adviceNote")}{" "}
            <Link component={RouterLink} to="/checklists">
              {t("footer.checklists")}
            </Link>
          </Typography>
        </Alert>
      ) : null}
      <Typography variant="body2" color="text.secondary">
        {t("messages.draftHint")}
      </Typography>
    </Box>
  );
}

function SentMessage({ message }: { message: Message }) {
  const { t } = useTranslation();
  const at = message.sent_at ?? message.created_at;

  return (
    <Box
      component="li"
      sx={{
        justifySelf: message.own ? "end" : "start",
        maxWidth: "85%",
        bgcolor: message.own ? "action.selected" : "action.hover",
        borderRadius: 2,
        px: 2,
        py: 1.25,
      }}
    >
      <Typography variant="caption" color="text.secondary" component="p">
        {message.own ? t("messages.you") : t(message.side === "company" ? "messages.company" : "messages.person")}
        {" · "}
        {new Date(at).toLocaleString()}
      </Typography>
      <Typography sx={{ whiteSpace: "pre-wrap" }}>{message.text}</Typography>
    </Box>
  );
}

/**
 * A draft of the own side. Released as it stands in the field — changed or
 * not; the words are then the releaser's.
 */
function DraftCard({
  message,
  onDone,
}: {
  message: Message;
  onDone: (failure: MessageKey | null) => Promise<void>;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState(message.text);
  const [busy, setBusy] = useState(false);

  async function release() {
    setBusy(true);
    const result = await releaseDraft(message.id, text);
    setBusy(false);
    await onDone(result.ok ? null : failureKey(result.error, "errors.messageNotSent"));
  }

  async function discard() {
    setBusy(true);
    const ok = await discardDraft(message.id);
    setBusy(false);
    await onDone(ok ? null : "errors.requestFailed");
  }

  return (
    <Box
      sx={{ border: 1, borderColor: "warning.main", borderStyle: "dashed", borderRadius: 2, p: 2, display: "grid", gap: 1.5 }}
      aria-label={t("messages.draftLabel")}
      role="group"
    >
      <Box sx={{ display: "flex", gap: 1, alignItems: "center" }}>
        <Chip size="small" color="warning" label={t("messages.draftChip")} />
        <Typography variant="body2" color="text.secondary">
          {t("messages.draftNotSent")}
        </Typography>
      </Box>
      <TextField
        label={t("messages.draftText")}
        multiline
        minRows={3}
        value={text}
        onChange={(event) => setText(event.target.value)}
        slotProps={{ htmlInput: { maxLength: 4000 } }}
      />
      <Box sx={{ display: "flex", gap: 1.5 }}>
        <Button variant="contained" onClick={() => void release()} disabled={busy || text.trim() === ""}>
          {t("messages.release")}
        </Button>
        <Button variant="text" onClick={() => void discard()} disabled={busy}>
          {t("messages.discard")}
        </Button>
      </Box>
    </Box>
  );
}
