import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { failureKey, type MessageKey } from "../../../shared/api/errors";
import { useAsync } from "../lib/useAsync";
import {
  SOURCE_CASES,
  SOURCE_KINDS,
  type Source,
  type SourceKind,
  type SourceMode,
  createSource,
  deleteSource,
  fetchSource,
  listSources,
  outcomeKey,
  setSourceMode,
} from "../api/sources";

const KIND_KEYS: Record<SourceKind, MessageKey> = {
  greenhouse: "sources.kindGreenhouse",
  lever: "sources.kindLever",
  personio: "sources.kindPersonio",
  feed: "sources.kindFeed",
};

const KEY_HINTS: Record<SourceKind, MessageKey> = {
  greenhouse: "sources.keyHintGreenhouse",
  lever: "sources.keyHintLever",
  personio: "sources.keyHintPersonio",
  feed: "sources.keyHintFeed",
};

/**
 * Where the company's adverts come from (ADR-0052) — its ATS board or its own
 * feed, on <c>/company/jobs</c>.
 *
 * <strong>The code is the proof, and the card says so before anything is
 * fetched.</strong> A board name proves nothing; the code written into one
 * advert at the ATS does. Until the board carries it, "fetch" brings nothing in
 * and the outcome says why.
 */
export function JobSources({ onFetched }: { onFetched: () => void }) {
  const { t, i18n } = useTranslation();
  const sources = useAsync((signal) => listSources(signal), []);
  const [kind, setKind] = useState<SourceKind>("greenhouse");
  const [key, setKey] = useState("");
  const [mode, setMode] = useState<SourceMode>("draft");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<MessageKey | null>(null);
  const [report, setReport] = useState<string | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);

  const list = sources.data?.ok ? sources.data.sources : [];

  async function run(step: () => Promise<{ ok: boolean; error?: Parameters<typeof failureKey>[0] }>) {
    setBusy(true);
    setError(null);
    setReport(null);
    const result = await step();
    setBusy(false);
    if (!result.ok && result.error !== undefined) setError(failureKey(result.error, "errors.stepFailed", SOURCE_CASES));
    sources.reload();
    return result.ok;
  }

  async function add() {
    if (await run(() => createSource(kind, key.trim(), mode))) setKey("");
  }

  async function fetchNow(source: Source) {
    await run(async () => {
      const result = await fetchSource(source.id);
      if (result.ok) {
        const { added, changed, closed } = result.fetched;
        setReport(t("sources.fetched", { added, changed, closed }));
        onFetched();
      }
      return result;
    });
  }

  return (
    <Card component="section" aria-label={t("sources.title")} sx={{ mt: 3 }}>
      <CardContent sx={{ display: "grid", gap: 2 }}>
        <Box>
          <Typography variant="h3">{t("sources.title")}</Typography>
          <Typography variant="body2" color="text.secondary">
            {t("sources.lead")}
          </Typography>
        </Box>

        {sources.data !== null && !sources.data.ok ? (
          <Alert severity="error">{t(failureKey(sources.data.error, "errors.listNotLoaded"))}</Alert>
        ) : null}
        {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}
        {report !== null ? <Alert severity="success">{report}</Alert> : null}

        {list.map((source) => (
          <Card key={source.id} component="article" variant="outlined" aria-label={`${t(KIND_KEYS[source.kind])} ${source.key}`}>
            <CardContent sx={{ display: "grid", gap: 1.5 }}>
              <Box>
                <Typography variant="h4">{t(KIND_KEYS[source.kind])}</Typography>
                <Typography variant="body2" sx={{ wordBreak: "break-all" }}>
                  {source.key}
                </Typography>
              </Box>

              {source.proven_at === null && source.code !== undefined ? (
                <Alert severity="info">
                  <Typography variant="body2">{t("sources.codeLead")}</Typography>
                  <Typography
                    component="code"
                    aria-label={t("sources.codeLabel")}
                    sx={{ display: "block", my: 1, fontFamily: "monospace", fontSize: "1.1rem", fontWeight: 700 }}
                  >
                    {source.code}
                  </Typography>
                  <Typography variant="body2">{t("sources.codeAfter")}</Typography>
                </Alert>
              ) : null}

              <Typography variant="body2" color="text.secondary">
                {source.fetched_at === null
                  ? t("sources.neverFetched")
                  : t("sources.lastFetched", {
                      date: new Date(source.fetched_at).toLocaleString(i18n.language),
                      outcome: t(outcomeKey(source.outcome)),
                    })}
              </Typography>

              <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, alignItems: "center" }}>
                <TextField
                  select
                  size="small"
                  label={t("sources.mode")}
                  value={source.mode}
                  disabled={busy}
                  onChange={(event) => void run(() => setSourceMode(source.id, event.target.value as SourceMode))}
                  sx={{ minWidth: 220 }}
                >
                  <MenuItem value="draft">{t("sources.modeDraft")}</MenuItem>
                  <MenuItem value="direct">{t("sources.modeDirect")}</MenuItem>
                </TextField>
                <Button variant="contained" size="small" disabled={busy} onClick={() => void fetchNow(source)}>
                  {source.proven_at === null ? t("sources.checkCode") : t("sources.fetchNow")}
                </Button>
                {removing === source.id ? (
                  <Button
                    color="error"
                    size="small"
                    disabled={busy}
                    onClick={() => void run(() => deleteSource(source.id)).then(() => setRemoving(null))}
                  >
                    {t("sources.removeConfirm")}
                  </Button>
                ) : (
                  <Button size="small" disabled={busy} onClick={() => setRemoving(source.id)}>
                    {t("sources.remove")}
                  </Button>
                )}
              </Box>
            </CardContent>
          </Card>
        ))}

        <Box
          component="form"
          aria-label={t("sources.addTitle")}
          onSubmit={(event) => {
            event.preventDefault();
            void add();
          }}
          sx={{ display: "grid", gap: 1.5 }}
        >
          <Typography variant="h4">{t("sources.addTitle")}</Typography>
          <TextField
            select
            size="small"
            label={t("sources.kind")}
            value={kind}
            onChange={(event) => setKind(event.target.value as SourceKind)}
          >
            {SOURCE_KINDS.map((value) => (
              <MenuItem key={value} value={value}>
                {t(KIND_KEYS[value])}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            size="small"
            label={kind === "feed" ? t("sources.keyFeed") : t("sources.keyBoard")}
            helperText={t(KEY_HINTS[kind])}
            value={key}
            onChange={(event) => setKey(event.target.value)}
          />
          <TextField
            select
            size="small"
            label={t("sources.mode")}
            helperText={t("sources.modeHint")}
            value={mode}
            onChange={(event) => setMode(event.target.value as SourceMode)}
          >
            <MenuItem value="draft">{t("sources.modeDraft")}</MenuItem>
            <MenuItem value="direct">{t("sources.modeDirect")}</MenuItem>
          </TextField>
          <Box>
            <Button type="submit" variant="outlined" disabled={busy || key.trim().length === 0}>
              {t("sources.add")}
            </Button>
          </Box>
        </Box>
      </CardContent>
    </Card>
  );
}
