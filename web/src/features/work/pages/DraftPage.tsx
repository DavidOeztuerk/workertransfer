import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Checkbox from "@mui/material/Checkbox";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Delete as DeleteIcon, Visibility as ViewIcon } from "@mui/icons-material";
import { useNavigate, useParams } from "react-router-dom";

import { LetterSheet } from "../../../shared/components/LetterSheet";
import type { Failure } from "../../../shared/api/errors";
import { getCompanyProfile } from "../../company/api/companies";
import { getPostalAddress } from "../../person/api/civilIdentity";
import { recipientFromCompany, senderFromSession } from "../lib/letterHead";
import { remember } from "../lib/letterContext";
import { writingEnded, writingStart } from "../lib/writingClock";
import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { SignInRequired } from "../../person/components/SignInRequired";
import {
  deleteDocument,
  getMyCurriculumVitae,
  getMyDocuments,
  getOwnDocumentContent,
  UPLOAD_CASES,
  uploadDocument,
  type DocumentKind,
  type DocumentRecordV1,
} from "../../person/api/resume";
import { WritingProgress } from "../components/WritingProgress";
import { getJob } from "../api/jobs";
import {
  type DraftStep,
  draftStepErrorKey,
  emptyLetterHintKey,
  writingKey,
} from "../lib/draftErrors";
import { failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type DraftError,
  type DraftStatus,
  addComment,
  approveDraft,
  chooseAttachments,
  deleteDraft,
  getDraft,
  reviseDraft,
  sendDraft,
  updateDraft,
  writeDraft,
} from "../api/applications";

const STATUS_COLOUR: Record<
  DraftStatus,
  "default" | "primary" | "warning" | "info" | "error"
> = {
  open: "default",
  generating: "warning",
  review: "info",
  needs_changes: "warning",
  approved: "primary",
  sent: "default",
  failed: "error",
};

/** What can be uploaded beside the CV here. The words are the wire's. */
type EnclosureKind = Exclude<DocumentKind, "lebenslauf">;

export function DraftPage() {
  const { t } = useTranslation();
  const { signedIn, loading: sessionLoading, session } = useActor();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const [hint, setHint] = useState<string | null>(null);
  const [running, setRunning] = useState<string | null>(null);
  const [comment, setComment] = useState("");
  const [quote, setQuote] = useState("");
  const [editing, setEditing] = useState(false);
  const [subject, setSubject] = useState("");
  const [text, setText] = useState("");
  const [chosen, setChosen] = useState<string[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [fileName, setFileName] = useState("");
  const [fileKind, setFileKind] = useState<EnclosureKind>("zeugnis");
  const [uploading, setUploading] = useState<string | null>(null);
  const [toDelete, setToDelete] = useState<string | null>(null);

  const draftFetch = useAsync(
    (signal) => getDraft(id!, signal),
    [id],
    signedIn && !!id,
  );

  const draft = draftFetch.data?.ok ? draftFetch.data.draft : null;
  const writing = draft?.status === "generating";

  useEffect(() => {
    if (!draft || draft.status !== "generating") return;
    const timer = window.setInterval(() => draftFetch.poll(), 1000);
    return () => window.clearInterval(timer);
  }, [draft?.status, draft?.id]);

  useEffect(() => {
    if (!draft) return;
    if (draft.status === "generating") writingStart(draft.id, draft.writing_started_at);
    else writingEnded(draft.id);
  }, [draft?.id, draft?.status, draft?.writing_started_at]);

  const documents = useAsync(
    (signal) => getMyDocuments(signal),
    [session?.userId],
    signedIn && draft !== null && !writing,
  );
  const curriculumVitae = useAsync(
    () => remember(`resume:${session?.userId ?? ""}`, () => getMyCurriculumVitae()),
    [session?.userId],
    signedIn && draft !== null && !writing,
  );
  const address = useAsync(
    () => remember(`address:${session?.userId ?? ""}`, () => getPostalAddress()),
    [session?.userId],
    signedIn && !!session?.userId,
  );

  const job = useAsync(
    () => remember(`job:${draft!.job_id}`, () => getJob(draft!.job_id)),
    [draft?.job_id],
    draft !== null,
  );
  const company = useAsync(
    () =>
      remember(`company:${job.data!.tenant_id}`, () =>
        getCompanyProfile(job.data!.tenant_id),
      ),
    [job.data?.tenant_id],
    job.data !== null && !writing,
  );

  useEffect(() => {
    if (!draft) return;
    setChosen(Array.isArray(draft.documents) ? draft.documents : []);
    setSubject(draft.subject ?? "");
    setText(draft.body ?? "");
  }, [draft?.id, draft?.subject, draft?.body, draft?.documents]);

  useEffect(() => {
    if (!draft || draft.status === "generating" || draft.shares_resume === true) return;
    void chooseAttachments(
      draft.id,
      true,
      Array.isArray(draft.documents) ? draft.documents : [],
    ).then(() => draftFetch.reload());
  }, [draft?.id, draft?.shares_resume, draft?.status]);

  useEffect(() => {
    if (!draft || writing || !documents.data?.ok) return;
    const cvIds = documents.data.value
      .filter((d) => d.kind === "lebenslauf")
      .map((d) => d.id);
    if (cvIds.length === 0) return;
    const current = Array.isArray(draft.documents) ? draft.documents : [];
    if (cvIds.every((id) => current.includes(id))) return;
    const next = [...new Set([...current, ...cvIds])];
    void chooseAttachments(draft.id, true, next).then(() => draftFetch.reload());
  }, [draft?.id, draft?.status, documents.data, writing]);

  if (sessionLoading) {
    return (
      <PageShell title={t("drafts.title")} narrow>
        <LoadingBlock label={t("drafts.loading")} />
      </PageShell>
    );
  }

  if (!signedIn || !id) {
    return <SignInRequired title={t("drafts.title")} sentenceKey="drafts.signInRequired" />;
  }

  if (draftFetch.pending && !draft) {
    return (
      <PageShell title={t("drafts.title")} narrow>
        <LoadingBlock label={t("drafts.loading")} />
      </PageShell>
    );
  }

  if (draftFetch.data !== null && !draftFetch.data.ok) {
    return (
      <PageShell title={t("drafts.title")} narrow>
        {draftFetch.data.reason === "unavailable" ? (
          <Alert severity="info">{t(writingKey(draftFetch.data.error.detail))}</Alert>
        ) : (
          <Alert severity="error">
            {t(failureKey(draftFetch.data.error, "errors.draftNotLoaded"))}
          </Alert>
        )}
      </PageShell>
    );
  }

  if (!draft) {
    return (
      <PageShell title={t("drafts.title")} narrow>
        <EmptyBlock
          title={t("drafts.notFound")}
          hint={t("drafts.notFoundHint")}
        />
      </PageShell>
    );
  }

  const openComments = draft.comments.filter((c) => !c.resolved);
  const resolvedComments = draft.comments.filter((c) => c.resolved);
  const empty = draft.body.trim() === "";
  const status: DraftStatus = draft.status;
  const canWrite =
    (draft.status === "open" || draft.status === "generating" || draft.status === "failed") && !writing;
  const canRevise =
    draft.status === "needs_changes" && openComments.length > 0 && !empty && !writing;
  const canApprove =
    draft.status === "review" && openComments.length === 0 && !empty && !writing;
  const canSend = draft.status === "approved" && !writing;
  const canDelete = draft.status !== "sent";
  const canEdit = !empty && draft.status !== "sent" && draft.status !== "generating" && !writing;
  const canSelfWrite = empty && (draft.status === "failed" || draft.status === "open") && !writing;
  const canComment = !empty && draft.status !== "generating" && draft.status !== "sent" && !writing;
  const title = job.data?.title ?? (draft.subject || t("drafts.title"));
  const draftId = draft.id;

  /**
   * One step on the draft, and what its failure says.
   *
   * <strong>Never the server's `detail` itself.</strong> `write` and `revise`
   * (and every silent provider) are about the writing help and read as a hint;
   * every other step reads as a catalogue text chosen by status and — where
   * the person has to act — by the sentence (`lib/draftErrors.ts`). The raw
   * detail stood here until 26.09.2026, and since applications-service answers
   * in English a German person read English.
   */
  async function run(
    step: "write" | "revise" | DraftStep,
    action: () => Promise<{ ok: true } | Failure<DraftError>>,
  ) {
    setRunning(step);
    setError(null);
    setHint(null);
    const result = await action();
    setRunning(null);
    if (!result.ok) {
      if (step === "write" || step === "revise" || result.reason === "unavailable") {
        setHint(t(writingKey(result.error.detail)));
      } else {
        setError(t(draftStepErrorKey(step, result.error)));
      }
    }
    // write/revise keep running in the service — the state arrives by GET.
    draftFetch.reload();
  }

  async function handleUpload() {
    if (!file) return;
    const name = fileName.trim() || file.name.replace(/\.[^/.]+$/, "");
    setUploading(name);
    setError(null);
    const result = await uploadDocument(file, name, fileKind);
    setUploading(null);
    if (!result.ok) {
      // 0/413/415/422 carry their keys from `uploadDocument`; every other
      // status reads as the step's own sentence, never resume-service's.
      setError(t(failureKey(result.error, "errors.documentNotUploaded", UPLOAD_CASES)));
      return;
    }
    setFile(null);
    setFileName("");
    const cvIds = (documents.data?.ok ? documents.data.value : [])
      .filter((d) => d.kind === "lebenslauf")
      .map((d) => d.id);
    const next = [...new Set([...chosen, result.value.id, ...cvIds])];
    setChosen(next);
    documents.reload();
    void run("attachments", () => chooseAttachments(draftId, true, next));
  }

  async function handlePreview(docId: string) {
    const result = await getOwnDocumentContent(docId);
    if (!result.ok) {
      setError(t(failureKey(result.error, "errors.documentsNotLoaded")));
      return;
    }
    const win = window.open(result.value.url, "_blank");
    if (!win) URL.revokeObjectURL(result.value.url);
  }

  async function handleDeleteDocument() {
    if (toDelete === null) return;
    const id = toDelete;
    setToDelete(null);
    const result = await deleteDocument(id);
    if (!result.ok) {
      // resume-service's own sentence is English too; the step names itself.
      setError(
        t(result.error.status === 0 ? "errors.noConnection" : "errors.documentNotDeleted"),
      );
      return;
    }
    const next = chosen.filter((x) => x !== id);
    setChosen(next);
    documents.reload();
    void run("attachments", () => chooseAttachments(draftId, true, next));
  }

  return (
    <PageShell
      title={title}
      lead={t(`drafts.statusHint.${status}`)}
      actions={
        <Button variant="outlined" onClick={() => navigate("/applications/drafts")}>
          {t("drafts.backToList")}
        </Button>
      }
    >
      {error && !writing ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      ) : null}
      {hint && !writing ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setHint(null)}>
          {hint}
        </Alert>
      ) : null}

      <Box
        sx={{
          display: "grid",
          gap: 3,
          gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1.4fr) minmax(18rem, 0.8fr)" },
          alignItems: "start",
        }}
      >
        <Card>
          <CardContent>
            <Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 2, flexWrap: "wrap" }}>
              <Typography variant="h4" sx={{ flex: 1 }}>
                {draft.subject || t("drafts.noSubject")}
              </Typography>
              <Chip
                label={t(`drafts.status.${status}`)}
                color={STATUS_COLOUR[status]}
                size="small"
              />
              <Typography variant="body2" color="text.secondary">
                {t("drafts.version", { version: draft.version })}
              </Typography>
            </Box>

            {!empty && status === "failed" && !writing ? (
              <Alert severity="info" sx={{ mb: 2 }}>
                {t(writingKey(draft.error))}
              </Alert>
            ) : null}

            {writing ? (
              <WritingProgress since={writingStart(draft.id, draft.writing_started_at)} />
            ) : null}

            {editing ? (
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
                <TextField
                  label={t("drafts.subject")}
                  value={subject ?? ""}
                  onChange={(ev) => setSubject(ev.target.value)}
                />
                <TextField
                  label={t("drafts.text")}
                  value={text ?? ""}
                  onChange={(ev) => setText(ev.target.value)}
                  multiline
                  minRows={10}
                />
                <Box sx={{ display: "flex", gap: 1 }}>
                  <Button
                    variant="contained"
                    disabled={running === "save"}
                    onClick={() =>
                      void run("save", () => updateDraft(draft.id, subject, text)).then(() =>
                        setEditing(false),
                      )
                    }
                  >
                    {t("common.save")}
                  </Button>
                  <Button onClick={() => setEditing(false)}>{t("common.cancel")}</Button>
                </Box>
              </Box>
            ) : empty && !writing ? (
              <EmptyBlock
                title={t("drafts.emptyLetter")}
                hint={t(status === "open" ? "drafts.emptyLetterOpen" : emptyLetterHintKey(draft.error))}
                action={
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", justifyContent: "center" }}>
                    {canWrite ? (
                      <Button
                        variant="contained"
                        disabled={running !== null}
                        onClick={() => void run("write", () => writeDraft(draft.id))}
                        startIcon={
                          running === "write" ? (
                            <CircularProgress color="inherit" size={16} />
                          ) : null
                        }
                      >
                        {t("drafts.generate")}
                      </Button>
                    ) : null}
                    {canSelfWrite ? (
                      <Button variant="outlined" onClick={() => setEditing(true)}>
                        {t("drafts.writeYourself")}
                      </Button>
                    ) : null}
                  </Box>
                }
              />
            ) : (
              <>
                <LetterSheet
                  subject={draft.subject}
                  text={draft.body}
                  emptyHint={writing ? t("drafts.writingNow") : t("drafts.emptyLetter")}
                  sender={senderFromSession(session, address.data)}
                  recipient={recipientFromCompany(company.data, job.data)}
                  // The CV's template (P2); the company's folder shows the same.
                  template={curriculumVitae.data?.ok ? curriculumVitae.data.value?.template : undefined}
                />
                <Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 1 }}>
                  {t("drafts.templateFollows")}
                </Typography>
              </>
            )}

            {empty || editing || writing ? null : (
              <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", mt: 2 }}>
                {canWrite ? (
                  <Button
                    variant="contained"
                    disabled={running !== null}
                    onClick={() => void run("write", () => writeDraft(draft.id))}
                    startIcon={
                      running === "write" ? <CircularProgress color="inherit" size={16} /> : null
                    }
                  >
                    {t("drafts.retry")}
                  </Button>
                ) : null}
                {canRevise ? (
                  <Button
                    variant="contained"
                    disabled={running !== null}
                    onClick={() => void run("revise", () => reviseDraft(draft.id))}
                  >
                    {t("drafts.revise")}
                  </Button>
                ) : null}
                {canApprove ? (
                  <Button
                    variant="contained"
                    disabled={running !== null}
                    onClick={() => void run("approve", () => approveDraft(draft.id))}
                  >
                    {t("drafts.approve")}
                  </Button>
                ) : null}
                {canSend ? (
                  <Button
                    variant="contained"
                    disabled={running !== null}
                    onClick={() => void run("send", () => sendDraft(draft.id))}
                  >
                    {t("drafts.send")}
                  </Button>
                ) : null}
                {canEdit ? (
                  <Button variant="outlined" onClick={() => setEditing(true)}>
                    {t("drafts.edit")}
                  </Button>
                ) : null}
                {canDelete ? (
                  <Button
                    color="error"
                    disabled={running !== null}
                    onClick={() =>
                      void run("delete", () => deleteDraft(draft.id)).then(() =>
                        navigate("/applications/drafts"),
                      )
                    }
                  >
                    {t("common.withdraw")}
                  </Button>
                ) : null}
              </Box>
            )}
            {empty && !editing && !writing && canDelete ? (
              <Box sx={{ display: "flex", justifyContent: "center", mt: 2 }}>
                <Button
                  color="error"
                  disabled={running !== null}
                  onClick={() =>
                    void run("delete", () => deleteDraft(draft.id)).then(() =>
                      navigate("/applications/drafts"),
                    )
                  }
                >
                  {t("common.withdraw")}
                </Button>
              </Box>
            ) : null}
            {draft.status === "review" || draft.status === "approved" ? (
              <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                {t("drafts.steps")}
              </Typography>
            ) : null}
          </CardContent>
        </Card>

        <Box sx={{ display: "grid", gap: 3 }}>
          <Card>
            <CardContent>
              <Typography variant="h5" sx={{ mb: 2 }}>
                {t("drafts.enclosures")}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {t("drafts.resumeAlways")}
              </Typography>
              {(() => {
                const all = documents.data?.ok ? documents.data.value : [];
                const cvFiles = all.filter((doc) => doc.kind === "lebenslauf");
                const enclosures = all.filter((doc) => doc.kind !== "lebenslauf");
                const hasForm = curriculumVitae.data?.ok && curriculumVitae.data.value !== null;
                const hasCv = cvFiles.length > 0;
                const cvIds = cvFiles.map((d) => d.id);
                return (
              <>
              {hasCv ? (
                <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mb: 2 }}>
                  {cvFiles.map((doc) => (
                    <Box
                      key={doc.id}
                      sx={{ display: "flex", alignItems: "center", gap: 0.5, flexWrap: "wrap" }}
                    >
                      <FormControlLabel
                        sx={{ flex: 1, mr: 0, minWidth: 0 }}
                        control={<Checkbox checked disabled />}
                        label={`${doc.name} · ${t("drafts.resumeIncluded")}`}
                      />
                      <IconButton
                        size="small"
                        onClick={() => void handlePreview(doc.id)}
                        aria-label={t("resume.viewFile")}
                      >
                        <ViewIcon />
                      </IconButton>
                    </Box>
                  ))}
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { mode: "file" } })}
                    >
                      {t("drafts.resumeEdit")}
                    </Button>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { mode: "file" } })}
                    >
                      {t("drafts.resumeReplace")}
                    </Button>
                  </Box>
                </Box>
              ) : (
                <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mb: 2 }}>
                  <Typography variant="body2" color="text.secondary">
                    {hasForm ? t("drafts.resumePresent") : t("drafts.resumeMissing")}
                  </Typography>
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
                    <Button
                      size="small"
                      variant={hasForm ? "outlined" : "contained"}
                      onClick={() => navigate("/resume", { state: { mode: "manual" } })}
                    >
                      {hasForm ? t("drafts.resumeEdit") : t("drafts.resumeCreate")}
                    </Button>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { mode: "file" } })}
                    >
                      {t("drafts.resumeUpload")}
                    </Button>
                  </Box>
                </Box>
              )}
              <Box sx={{ display: "flex", flexDirection: "column", gap: 0.5, mt: 2 }}>
                {enclosures.map((doc: DocumentRecordV1) => (
                  <Box
                    key={doc.id}
                    sx={{ display: "flex", alignItems: "center", gap: 0.5, flexWrap: "wrap" }}
                  >
                    <FormControlLabel
                      sx={{ flex: 1, mr: 0, minWidth: 0 }}
                      control={
                        <Checkbox
                          checked={chosen.includes(doc.id)}
                          onChange={(_, checked) => {
                            const next = checked
                              ? [...new Set([...chosen, doc.id, ...cvIds])]
                              : [...new Set([...chosen.filter((x) => x !== doc.id), ...cvIds])];
                            setChosen(next);
                            void run("attachments", () =>
                              chooseAttachments(draft.id, true, next),
                            );
                          }}
                        />
                      }
                      label={doc.name}
                    />
                    <IconButton
                      size="small"
                      onClick={() => void handlePreview(doc.id)}
                      aria-label={t("resume.viewFile")}
                    >
                      <ViewIcon />
                    </IconButton>
                    <IconButton
                      size="small"
                      color="error"
                      onClick={() => setToDelete(doc.id)}
                      disabled={running !== null || writing}
                      aria-label={t("resume.documentDelete")}
                    >
                      <DeleteIcon />
                    </IconButton>
                  </Box>
                ))}
                {documents.data?.ok && enclosures.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">
                    {t("drafts.noDocuments")}
                  </Typography>
                ) : null}
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5, mt: 2 }}>
                <Button
                  size="small"
                  variant="outlined"
                  component="label"
                  disabled={uploading !== null || writing}
                  sx={{ alignSelf: "flex-start" }}
                >
                  {t("resume.addDocument")}
                  <input
                    type="file"
                    hidden
                    accept="application/pdf,image/png,image/jpeg"
                    onChange={(ev) => {
                      const picked = ev.target.files?.[0] ?? null;
                      ev.target.value = "";
                      setFile(picked);
                      if (picked) {
                        setFileName(picked.name.replace(/\.[^/.]+$/, ""));
                        setError(null);
                      }
                    }}
                  />
                </Button>
                {file ? (
                  <>
                    <Typography variant="body2" color="text.secondary">
                      {file.name}
                    </Typography>
                    <TextField
                      size="small"
                      label={t("resume.documentName")}
                      value={fileName}
                      onChange={(ev) => setFileName(ev.target.value)}
                    />
                    <TextField
                      select
                      size="small"
                      label={t("resume.documentKind")}
                      value={fileKind}
                      onChange={(ev) => setFileKind(ev.target.value as EnclosureKind)}
                    >
                      <MenuItem value="zeugnis">{t("resume.documentKindReference")}</MenuItem>
                      <MenuItem value="zertifikat">{t("resume.documentKindCertificate")}</MenuItem>
                      <MenuItem value="sonstiges">{t("resume.documentKindOther")}</MenuItem>
                    </TextField>
                    <Button
                      variant="contained"
                      size="small"
                      onClick={() => void handleUpload()}
                      disabled={uploading !== null}
                      sx={{ alignSelf: "flex-start" }}
                    >
                      {t("resume.documentUpload")}
                    </Button>
                  </>
                ) : null}
                {uploading ? (
                  <Alert severity="info" role="status">
                    {t("resume.uploading", { name: uploading })}
                  </Alert>
                ) : null}
              </Box>
              </>
                );
              })()}
            </CardContent>
          </Card>

          <Card>
            <CardContent>
              <Typography variant="h5" sx={{ mb: 2 }}>
                {t("drafts.commentsTitle")}
              </Typography>
              {canComment ? (
                <>
                  <TextField
                    multiline
                    minRows={3}
                    fullWidth
                    label={t("drafts.commentAdd")}
                    placeholder={t("drafts.commentPlaceholder")}
                    value={comment}
                    onChange={(ev) => setComment(ev.target.value)}
                    sx={{ mb: 1 }}
                  />
                  {quote ? (
                    <Alert severity="info" sx={{ mb: 1 }}>
                      {t("drafts.markedQuote")} {t("drafts.quoted", { quote })}
                    </Alert>
                  ) : null}
                  <Button
                    variant="contained"
                    disabled={!comment.trim() || running !== null}
                    onClick={() => {
                      const selection = window.getSelection()?.toString().trim() ?? "";
                      const marked = selection || quote;
                      setQuote(marked);
                      void run("comment", () => addComment(draft.id, comment.trim(), marked)).then(
                        () => {
                          setComment("");
                          setQuote("");
                        },
                      );
                    }}
                  >
                    {t("drafts.comment")}
                  </Button>
                </>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  {t("drafts.commentNeedsText")}
                </Typography>
              )}

              {openComments.map((c) => (
                <Box key={c.id} sx={{ mt: 2, p: 1.5, border: 1, borderColor: "divider" }}>
                  {c.quote ? (
                    <Typography variant="body2" sx={{ fontStyle: "italic", mb: 0.5 }}>
                      {t("drafts.quoted", { quote: c.quote })}
                    </Typography>
                  ) : null}
                  <Typography>{c.text}</Typography>
                </Box>
              ))}
              {resolvedComments.map((c) => (
                <Box
                  key={c.id}
                  sx={{ mt: 1, p: 1.5, opacity: 0.6, textDecoration: "line-through" }}
                >
                  <Typography>{c.text}</Typography>
                </Box>
              ))}
            </CardContent>
          </Card>
        </Box>
      </Box>

      <Dialog
        open={toDelete !== null}
        onClose={() => setToDelete(null)}
        aria-labelledby="draft-document-delete-title"
        aria-describedby="draft-document-delete-text"
      >
        <DialogTitle id="draft-document-delete-title">
          {t("resume.documentDeleteTitle")}
        </DialogTitle>
        <DialogContent>
          <DialogContentText id="draft-document-delete-text">
            {t("resume.documentDeleteConfirm")}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setToDelete(null)}>
            {t("common.cancel")}
          </Button>
          <Button
            color="error"
            variant="contained"
            onClick={() => void handleDeleteDocument()}
          >
            {t("resume.documentDelete")}
          </Button>
        </DialogActions>
      </Dialog>
    </PageShell>
  );
}
