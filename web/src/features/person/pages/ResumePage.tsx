import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardActionArea from "@mui/material/CardActionArea";
import CardContent from "@mui/material/CardContent";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Divider from "@mui/material/Divider";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Delete as DeleteIcon, Description as DocIcon, Visibility as ViewIcon } from "@mui/icons-material";

import {
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { useLocation } from "react-router-dom";
import { SignInRequired } from "../components/SignInRequired";
import { parseSkills } from "../lib/skills";
import { useAsync } from "../lib/useAsync";
import { CurriculumVitaeSheet } from "../components/resume/CurriculumVitaeSheet";
import { getMyProfile } from "../api/profile";
import {
  type CurriculumVitaeTemplate,
  type DocumentKind,
  type DocumentRecordV1,
  type Education,
  type Position,
  type ResumeRequest,
  answerResumeRequest,
  deleteDocument,
  getMyCurriculumVitae,
  getMyDocuments,
  getMyResumeRequests,
  getOwnDocumentContent,
  markAsCurriculumVitae,
  revokeResumeRequest,
  saveMyCurriculumVitae,
  setTemplate,
  UPLOAD_CASES,
  uploadDocument,
} from "../api/resume";
import { failureKey } from "../../../shared/api/errors";

/**
 * How the CV is kept: not chosen yet, typed in by hand, or as an uploaded
 * file. The draft page links here with `{ mode: "file" }` or
 * `{ mode: "manual" }` in the router state.
 */
type ResumeMode = "choice" | "manual" | "file";

function modeFromState(value: string | null): ResumeMode {
  return value === "manual" || value === "file" ? value : "choice";
}

/** The kinds an enclosure may have — the CV file is chosen on its own path. */
type EnclosureKind = Exclude<DocumentKind, "lebenslauf">;

const EMPTY_POSITION: Position = {
  employer: "",
  title: "",
  started_on: "",
  ended_on: null,
  description: "",
  technologies: [],
};

const EMPTY_EDUCATION: Education = {
  institution: "",
  qualification: "",
  started_on: "",
  ended_on: null,
};

const TEMPLATES = ["schlicht", "klassisch", "modern"] as const;

/** Empty means "still running", not "unknown" — hence `null` and not `""`. */
function normaliseEnd(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === "" ? null : trimmed;
}

export function ResumePage() {
  const { t } = useTranslation();
  const location = useLocation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const curriculumVitae = useAsync(
    (signal) => getMyCurriculumVitae(signal),
    [session?.userId],
    session !== null,
  );
  const requests = useAsync(
    (signal) => getMyResumeRequests(signal),
    [session?.userId],
    session !== null,
  );
  /*
   * THE PROFILE, ONLY FOR THE PREVIEW.
   *
   * Headline, text, location and skills stand in the profile, not in the CV —
   * but the sheet needs them, because the company sees them there. Without this
   * call the preview would show a different sheet from the recipient's, and
   * that is exactly what it must not do.
   */
  const ownProfile = useAsync(
    (signal) => getMyProfile(signal),
    [session?.userId],
    session !== null,
  );

  const ownDocuments = useAsync(
    (signal) => getMyDocuments(signal),
    [session?.userId],
    session !== null,
  );

  const [rows, setRows] = useState<Position[]>([]);
  const [apprenticeships, setApprenticeships] = useState<Education[]>([]);
  const [schools, setSchools] = useState<Education[]>([]);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [template, setTemplateState] = useState<CurriculumVitaeTemplate>("schlicht");
  const [uploading, setUploading] = useState<string | null>(null);
  const [documentName, setDocumentName] = useState("");
  const [documentKind, setDocumentKind] = useState<EnclosureKind>("zeugnis");
  const [chosenFile, setChosenFile] = useState<File | null>(null);
  const [toDelete, setToDelete] = useState<string | null>(null);
  const [mode, setMode] = useState<ResumeMode>(() =>
    modeFromState((location.state as { mode?: string } | null)?.mode ?? null),
  );
  const [cvFile, setCvFile] = useState<File | null>(null);
  const [cvName, setCvName] = useState("");

  // The form follows the loaded state — but only when there really is one. On
  // a loading error it stays empty AND the page says why; a silent empty form
  // would be the invitation to overwrite the CV.
  useEffect(() => {
    if (curriculumVitae.value?.ok && curriculumVitae.value.value !== null) {
      setRows(
        curriculumVitae.value.value.positions.map((position) => ({ ...position })),
      );
      const education = curriculumVitae.value.value.education ?? [];
      setApprenticeships(education.filter((entry) => entry.kind !== "schule").map((e) => ({ ...e })));
      setSchools(education.filter((entry) => entry.kind === "schule").map((e) => ({ ...e })));
      if (curriculumVitae.value.value.template) {
        setTemplateState(curriculumVitae.value.value.template);
      }
    }
  }, [curriculumVitae.value]);

  const documentList: DocumentRecordV1[] =
    ownDocuments.value?.ok ? ownDocuments.value.value : [];
  const cvDocuments = documentList.filter((doc) => doc.kind === "lebenslauf");
  const enclosures = documentList.filter((doc) => doc.kind !== "lebenslauf");

  useEffect(() => {
    if (mode !== "choice") return;
    if (cvDocuments.length > 0) {
      setMode("file");
      return;
    }
    if (curriculumVitae.value?.ok && curriculumVitae.value.value !== null) {
      setMode("manual");
    }
  }, [mode, cvDocuments.length, curriculumVitae.value]);

  useEffect(() => {
    if (mode !== "manual") return;
    setRows((previous) => (previous.length === 0 ? [{ ...EMPTY_POSITION }] : previous));
  }, [mode]);

  if (status === "anonymous") {
    return (
      <SignInRequired
        title={t("resume.title")}
        sentenceKey="resume.signInRequired"
      />
    );
  }

  function change(index: number, part: Partial<Position>) {
    setRows((before) =>
      before.map((row, i) => (i === index ? { ...row, ...part } : row)),
    );
    setSaved(false);
  }

  function changeEducation(
    setter: typeof setApprenticeships,
    index: number,
    part: Partial<Education>,
  ) {
    setter((before) => before.map((row, i) => (i === index ? { ...row, ...part } : row)));
    setSaved(false);
  }

  async function save() {
    setRunning(true);
    const result = await saveMyCurriculumVitae({
      positions: rows.filter(
        (row) =>
          row.employer.trim() !== "" && row.title.trim() !== "" && row.started_on.trim() !== "",
      ),
      education: [
        ...apprenticeships
          .filter((row) => row.institution.trim() !== "" && row.started_on.trim() !== "")
          .map((row) => ({ ...row, kind: "ausbildung" as const })),
        ...schools
          .filter((row) => row.institution.trim() !== "" && row.started_on.trim() !== "")
          .map((row) => ({ ...row, kind: "schule" as const })),
      ],
    });
    setRunning(false);

    if (result.ok) {
      setError(null);
      setSaved(true);
      curriculumVitae.set({ ok: true, value: result.value });
    } else {
      setError(t(failureKey(result.error, "errors.resumeNotSaved")));
      setSaved(false);
    }
  }

  async function answerRequest(id: string, grant: boolean) {
    setRunning(true);
    const result = await answerResumeRequest(id, grant);
    setRunning(false);
    if (!result.ok) setError(t(failureKey(result.error, "errors.requestNotAnswered")));
    requests.again();
  }

  async function revokeRequest(id: string) {
    setRunning(true);
    const result = await revokeResumeRequest(id);
    setRunning(false);
    if (!result.ok) setError(t(failureKey(result.error, "errors.consentNotChanged")));
    requests.again();
  }

  async function handleTemplateChange(newTemplate: CurriculumVitaeTemplate) {
    setRunning(true);
    const result = await setTemplate(newTemplate);
    setRunning(false);
    if (!result.ok) setError(t(failureKey(result.error, "errors.templateNotSet")));
    else setTemplateState(newTemplate);
  }

  async function handleUpload(kind: DocumentKind, file: File, name: string) {
    const named = name.trim() || file.name.replace(/\.[^/.]+$/, "");
    setUploading(named);
    const result = await uploadDocument(file, named, kind);
    setUploading(null);

    if (!result.ok) setError(t(failureKey(result.error, "errors.documentNotUploaded", UPLOAD_CASES)));
    else {
      setError(null);
      setChosenFile(null);
      setDocumentName("");
      setCvFile(null);
      setCvName("");
      ownDocuments.again();
    }
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

  async function handleMarkAsCurriculumVitae(id: string) {
    const result = await markAsCurriculumVitae(id);
    if (!result.ok) setError(t(failureKey(result.error, "errors.stepFailed")));
    else {
      setError(null);
      setMode("file");
      ownDocuments.again();
    }
  }

  async function handleDeleteDocument() {
    if (toDelete === null) return;
    const id = toDelete;
    setToDelete(null);
    const result = await deleteDocument(id);
    if (!result.ok) setError(t(failureKey(result.error, "errors.documentNotDeleted")));
    else {
      setError(null);
      ownDocuments.again();
    }
  }

  return (
    <PageShell
      title={t("resume.title")}
      narrow
      lead={t("resume.lead")}
    >
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("resume.requests")}
          </Typography>

          {requests.loading ? (
            <LoadingBlock label={t("resume.requestsLoading")} />
          ) : null}

          {requests.value && !requests.value.ok ? (
            <ErrorBlock error={requests.value.error} fallback="errors.requestsUnavailable" />
          ) : null}

          {requests.value?.ok && requests.value.value.length === 0 ? (
            <EmptyBlock title={t("resume.requestsEmpty")} />
          ) : null}

          {requests.value?.ok && requests.value.value.length > 0 ? (
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
              {requests.value.value.map((resumeRequest) => (
                <RequestRow
                  key={resumeRequest.id}
                  resumeRequest={resumeRequest}
                  locked={running}
                  onAnswer={(grant) =>
                    void answerRequest(resumeRequest.id, grant)
                  }
                  onRevoke={() => void revokeRequest(resumeRequest.id)}
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      {mode === "choice" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.pathChoiceTitle")}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              {t("resume.pathChoiceLead")}
            </Typography>
            <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" } }}>
              <Card variant="outlined">
                <CardActionArea
                  // A clickable card is not a button: `<Card onClick>` has no
                  // role, no tabIndex and no keyboard path, so the choice was
                  // visible and unreachable. CardActionArea renders a real
                  // <button> and takes focus, Enter and Space with it.
                  onClick={() => {
                    setMode("manual");
                    setRows((previous) => (previous.length === 0 ? [{ ...EMPTY_POSITION }] : previous));
                  }}
                >
                <CardContent>
                  <Typography variant="h4">{t("resume.pathManual")}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    {t("resume.pathManualHint")}
                  </Typography>
                </CardContent>
                </CardActionArea>
              </Card>
              <Card variant="outlined">
                <CardActionArea onClick={() => setMode("file")}>
                  <CardContent>
                    <Typography variant="h4">{t("resume.pathFile")}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t("resume.pathFileHint")}
                    </Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            </Box>
          </CardContent>
        </Card>
      ) : (
        <Box sx={{ mb: 2 }}>
          <Button
            variant="text"
            onClick={() => {
              const next = mode === "file" ? "manual" : "file";
              setMode(next);
              if (next === "manual") {
                setRows((previous) => (previous.length === 0 ? [{ ...EMPTY_POSITION }] : previous));
              }
            }}
          >
            {mode === "file"
              ? t("resume.switchToManual")
              : t("resume.switchToFile")}
          </Button>
        </Box>
      )}

      {mode === "file" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.cvFile")}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              {t("resume.fileLead")}
            </Typography>
            <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
              <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
                {t("resume.addDocument")}
                <input
                  type="file"
                  accept=".png,.jpg,.jpeg,.pdf"
                  hidden
                  onChange={(event) => {
                    const file = event.target.files?.[0] ?? null;
                    event.target.value = "";
                    setCvFile(file);
                    if (file) {
                      setCvName(file.name.replace(/\.[^/.]+$/, ""));
                      setError(null);
                    }
                  }}
                />
              </Button>
              {cvFile ? (
                <>
                  <Typography variant="body2" color="text.secondary">
                    {cvFile.name}
                  </Typography>
                  <TextField
                    label={t("resume.documentName")}
                    value={cvName}
                    onChange={(event) => setCvName(event.target.value)}
                  />
                  <Button
                    variant="contained"
                    onClick={() => {
                      if (cvFile) void handleUpload("lebenslauf", cvFile, cvName);
                    }}
                    disabled={uploading !== null}
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
            {cvDocuments.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                {t("resume.noDocuments")}
              </Typography>
            ) : (
              <Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
                {cvDocuments.map((doc) => (
                  <Card key={doc.id} variant="outlined">
                    <CardContent sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                      <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                        {doc.content_type.startsWith("image/") ? <ViewIcon /> : <DocIcon />}
                        <Box>
                          <Typography variant="body1">{doc.name}</Typography>
                          <Typography variant="body2" color="text.secondary">
                            {t("resume.documentKindResume")} · {Math.round(doc.size_bytes / 1024)} KB
                          </Typography>
                        </Box>
                      </Box>
                      <Box sx={{ display: "flex", gap: 0.5 }}>
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
                          disabled={running}
                          aria-label={t("resume.documentDelete")}
                        >
                          <DeleteIcon />
                        </IconButton>
                      </Box>
                    </CardContent>
                  </Card>
                ))}
              </Box>
            )}
          </CardContent>
        </Card>
      ) : null}

      {mode === "file" ? (
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.documents")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.documentsLead")}
          </Typography>
          <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
            <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
              {t("resume.addDocument")}
              <input
                type="file"
                accept=".png,.jpg,.jpeg,.pdf"
                hidden
                onChange={(event) => {
                  const file = event.target.files?.[0] ?? null;
                  // Otherwise onChange does not fire when the same file is
                  // chosen again — after deleting and uploading again the
                  // button then seems dead, without an error.
                  event.target.value = "";
                  setChosenFile(file);
                  if (file) {
                    setDocumentName(file.name.replace(/\.[^/.]+$/, ""));
                    setError(null);
                  }
                }}
              />
            </Button>
            {chosenFile ? (
              <>
                <Typography variant="body2" color="text.secondary">
                  {chosenFile.name}
                </Typography>
                <TextField
                  label={t("resume.documentName")}
                  value={documentName}
                  onChange={(event) => setDocumentName(event.target.value)}
                />
                <TextField
                  select
                  label={t("resume.documentKind")}
                  value={documentKind}
                  onChange={(event) =>
                    setDocumentKind(event.target.value as EnclosureKind)
                  }
                >
                  <MenuItem value="zeugnis">{t("resume.documentKindReference")}</MenuItem>
                  <MenuItem value="zertifikat">{t("resume.documentKindCertificate")}</MenuItem>
                  <MenuItem value="sonstiges">{t("resume.documentKindOther")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (chosenFile) void handleUpload(documentKind, chosenFile, documentName);
                  }}
                  disabled={uploading !== null}
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

          {ownDocuments.loading ? (
            <LoadingBlock label={t("resume.documentsLoading")} />
          ) : null}

          {ownDocuments.value && !ownDocuments.value.ok ? (
            <ErrorBlock error={ownDocuments.value.error} fallback="errors.documentsNotLoaded" />
          ) : null}

          {ownDocuments.value?.ok && enclosures.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("resume.noDocuments")}</Typography>
          ) : null}

          {enclosures.length > 0 && (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mt: 1 }}>
              {enclosures.map((doc) => (
                <Card key={doc.id} variant="outlined">
                  <CardContent sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                    <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                      {doc.content_type.startsWith("image/") ? <ViewIcon /> : <DocIcon />}
                      <Box>
                        <Typography variant="body1">{doc.name}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {t(
                            doc.kind === "zeugnis"
                              ? "resume.documentKindReference"
                              : doc.kind === "zertifikat"
                                ? "resume.documentKindCertificate"
                                : "resume.documentKindOther",
                          )}{" "}
                          · {Math.round(doc.size_bytes / 1024)} KB · {t("resume.uploadedOn", { datum: new Date(doc.uploaded_at).toLocaleDateString("de-DE") })}
                        </Typography>
                      </Box>
                    </Box>
                    <Box sx={{ display: "flex", gap: 0.5, alignItems: "center" }}>
                      {doc.kind !== "lebenslauf" ? (
                        <Button size="small" onClick={() => void handleMarkAsCurriculumVitae(doc.id)}>
                          {t("drafts.useAsResume")}
                        </Button>
                      ) : null}
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
                        disabled={running}
                        aria-label={t("resume.documentDelete")}
                      >
                        <DeleteIcon />
                      </IconButton>
                    </Box>
                  </CardContent>
                </Card>
              ))}
            </Box>
          )}
        </CardContent>
      </Card>
      ) : null}

      {mode === "manual" ? (
      <>
      <Box
        component="form"
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
      {curriculumVitae.loading ? (
        <LoadingBlock label={t("resume.loading")} />
      ) : null}

      {curriculumVitae.value && !curriculumVitae.value.ok ? (
        <ErrorBlock error={curriculumVitae.value.error} fallback="errors.resumeUnavailable" />
      ) : null}

      {saved ? (
        <Alert severity="success" sx={{ mb: 2 }} role="status">
          {t("resume.saved")}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.experience")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.experienceLead")}
          </Typography>
          {rows.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("resume.positionNumber", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setRows((before) => before.filter((_, i) => i !== index));
                    setSaved(false);
                  }}
                  aria-label={t("resume.removeEntry")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("resume.employer")}
                  value={row.employer}
                  onChange={(e) => change(index, { employer: e.target.value })}
                />
                <TextField
                  label={t("resume.jobTitle")}
                  value={row.title}
                  onChange={(e) => change(index, { title: e.target.value })}
                />
                <TextField
                  label={t("resume.from")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => change(index, { started_on: e.target.value })}
                  helperText={t("resume.fromHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.to")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => change(index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("resume.toHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.description")}
                  value={row.description}
                  onChange={(e) => change(index, { description: e.target.value })}
                  multiline
                  minRows={2}
                />
                <TextField
                  label={t("resume.technologies")}
                  helperText={t("resume.technologiesHint")}
                  value={row.technologies.join(", ")}
                  onChange={(e) => change(index, { technologies: parseSkills(e.target.value) })}
                />
              </Box>
              <Divider sx={{ mt: 3 }} />
            </Box>
          ))}
          <Button
            variant="outlined"
            onClick={() => setRows((before) => [...before, { ...EMPTY_POSITION }])}
          >
            {t("resume.addPosition")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.training")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.trainingLead")}
          </Typography>
          {apprenticeships.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("resume.trainingNumber", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setApprenticeships((before) => before.filter((_, i) => i !== index));
                    setSaved(false);
                  }}
                  aria-label={t("resume.removeEntry")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("resume.institution")}
                  value={row.institution}
                  onChange={(e) => changeEducation(setApprenticeships, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("resume.qualification")}
                  value={row.qualification}
                  onChange={(e) => changeEducation(setApprenticeships, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("resume.from")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => changeEducation(setApprenticeships, index, { started_on: e.target.value })}
                  helperText={t("resume.fromHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.to")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => changeEducation(setApprenticeships, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("resume.toHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
              </Box>
              <Divider sx={{ mt: 3 }} />
            </Box>
          ))}
          <Button
            variant="outlined"
            onClick={() => setApprenticeships((before) => [...before, { ...EMPTY_EDUCATION }])}
          >
            {t("resume.addTraining")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.school")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.schoolLead")}
          </Typography>
          {schools.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("resume.schoolNumber", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setSchools((before) => before.filter((_, i) => i !== index));
                    setSaved(false);
                  }}
                  aria-label={t("resume.removeEntry")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("resume.schoolName")}
                  value={row.institution}
                  onChange={(e) => changeEducation(setSchools, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("resume.schoolQualification")}
                  value={row.qualification}
                  onChange={(e) => changeEducation(setSchools, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("resume.from")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => changeEducation(setSchools, index, { started_on: e.target.value })}
                  helperText={t("resume.fromHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.to")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => changeEducation(setSchools, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("resume.toHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
              </Box>
              <Divider sx={{ mt: 3 }} />
            </Box>
          ))}
          <Button
            variant="outlined"
            onClick={() => setSchools((before) => [...before, { ...EMPTY_EDUCATION }])}
          >
            {t("resume.addSchool")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>{t("resume.template")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{t("resume.templateLead")}</Typography>
          <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr 1fr" } }}>
            {TEMPLATES.map((templ) => (
              <Card
                key={templ}
                variant="outlined"
                sx={{
                  borderColor: template === templ ? "primary.main" : "divider",
                  borderWidth: template === templ ? 2 : 1,
                }}
              >
                <CardActionArea
                  // A real <button> here too, same reason as at the path choice.
                  onClick={() => void handleTemplateChange(templ)}
                  aria-pressed={template === templ}
                >
                  <CardContent>
                    <Typography variant="h4">
                      {t(`resume.template${templ.charAt(0).toUpperCase()}${templ.slice(1)}`)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t(`resume.template${templ.charAt(0).toUpperCase()}${templ.slice(1)}Hint`)}
                    </Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            ))}
          </Box>

          <Divider sx={{ my: 3 }} />
          <Typography variant="h4" sx={{ mb: 0.5 }}>
            {t("resume.preview")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.previewLead")}
          </Typography>
          <CurriculumVitaeSheet
            // The preview IS the promise (ADR-0035: the same rendering the
            // person sees, nothing rendered differently for the recipient), so
            // it is the SAME component as in the application folder — two
            // renderings would be two truths. Fed from the RUNNING form and not
            // from the saved state, so a typed position shows before saving.
            template={template}
            data={{
              headline: ownProfile.value?.ok
                ? (ownProfile.value.profile?.headline ?? "")
                : "",
              bio: ownProfile.value?.ok ? (ownProfile.value.profile?.bio ?? "") : "",
              location: ownProfile.value?.ok
                ? (ownProfile.value.profile?.location ?? "")
                : "",
              skills: ownProfile.value?.ok ? (ownProfile.value.profile?.skills ?? []) : [],
              positions: rows.filter(
                (row) =>
                  row.employer.trim() !== ""
                  && row.title.trim() !== ""
                  && row.started_on.trim() !== "",
              ),
              education: [
                ...apprenticeships
                  .filter(
                    (row) => row.institution.trim() !== "" && row.started_on.trim() !== "",
                  )
                  .map((row) => ({ ...row, kind: "ausbildung" as const })),
                ...schools
                  .filter(
                    (row) => row.institution.trim() !== "" && row.started_on.trim() !== "",
                  )
                  .map((row) => ({ ...row, kind: "schule" as const })),
              ],
            }}
          />
        </CardContent>
      </Card>

      <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap", mb: 3 }}>
        <Button type="submit" variant="contained" disabled={running}>
          {running ? t("common.saving") : t("common.save")}
        </Button>
      </Box>
      </Box>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("resume.documents")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("resume.documentsLead")}
          </Typography>
          <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
            <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
              {t("resume.addDocument")}
              <input
                type="file"
                accept=".png,.jpg,.jpeg,.pdf"
                hidden
                onChange={(event) => {
                  const file = event.target.files?.[0] ?? null;
                  event.target.value = "";
                  setChosenFile(file);
                  if (file) {
                    setDocumentName(file.name.replace(/\.[^/.]+$/, ""));
                    setError(null);
                  }
                }}
              />
            </Button>
            {chosenFile ? (
              <>
                <Typography variant="body2" color="text.secondary">{chosenFile.name}</Typography>
                <TextField
                  label={t("resume.documentName")}
                  value={documentName}
                  onChange={(event) => setDocumentName(event.target.value)}
                />
                <TextField
                  select
                  label={t("resume.documentKind")}
                  value={documentKind}
                  onChange={(event) =>
                    setDocumentKind(event.target.value as EnclosureKind)
                  }
                >
                  <MenuItem value="zeugnis">{t("resume.documentKindReference")}</MenuItem>
                  <MenuItem value="zertifikat">{t("resume.documentKindCertificate")}</MenuItem>
                  <MenuItem value="sonstiges">{t("resume.documentKindOther")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (chosenFile) void handleUpload(documentKind, chosenFile, documentName);
                  }}
                  disabled={uploading !== null}
                >
                  {t("resume.documentUpload")}
                </Button>
              </>
            ) : null}
          </Box>
          {enclosures.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("resume.noDocuments")}</Typography>
          ) : (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
              {enclosures.map((doc) => (
                <Card key={doc.id} variant="outlined">
                  <CardContent sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                    <Typography variant="body1">{doc.name}</Typography>
                    <Box>
                      <IconButton size="small" onClick={() => void handlePreview(doc.id)} aria-label={t("resume.viewFile")}>
                        <ViewIcon />
                      </IconButton>
                      <IconButton size="small" color="error" onClick={() => setToDelete(doc.id)} aria-label={t("resume.documentDelete")}>
                        <DeleteIcon />
                      </IconButton>
                    </Box>
                  </CardContent>
                </Card>
              ))}
            </Box>
          )}
        </CardContent>
      </Card>
      </>
      ) : null}

      <Dialog
        open={toDelete !== null}
        onClose={() => setToDelete(null)}
        aria-labelledby="document-delete-title"
        aria-describedby="document-delete-text"
      >
        <DialogTitle id="document-delete-title">
          {t("resume.documentDeleteTitle")}
        </DialogTitle>
        <DialogContent>
          <DialogContentText id="document-delete-text">
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

/**
 * A request with its state.
 *
 * <strong>"Released" and "currently holds access" are two things.</strong>
 * `GRANTED` means "was granted once"; whether the release holds NOW is said by
 * `active`. Only whoever holds it is offered "Withdraw" — otherwise the page
 * would offer an action that no longer changes anything.
 */
function RequestRow({
  resumeRequest,
  locked,
  onAnswer,
  onRevoke,
}: {
  resumeRequest: ResumeRequest;
  locked: boolean;
  onAnswer: (grant: boolean) => void;
  onRevoke: () => void;
}) {
  const { t } = useTranslation();
  const open = resumeRequest.status === "PENDING";
  const holdsAccess = resumeRequest.status === "GRANTED" && resumeRequest.active === true;

  const current = open
    ? t("resume.statusOpen")
    : resumeRequest.status === "DECLINED"
      ? t("resume.statusDeclined")
      : t(
          holdsAccess
            ? "resume.statusReleased"
            : "resume.statusWithdrawn",
        );

  return (
    <Card component="li" variant="outlined">
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
          <Typography variant="h4">
            {t("resume.requestTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {current}
          </Typography>
        </Box>

        <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
          {open ? (
            <>
              <Button
                variant="contained"
                size="small"
                onClick={() => onAnswer(true)}
                disabled={locked}
              >
                {t("common.release")}
              </Button>
              <Button
                variant="text"
                size="small"
                onClick={() => onAnswer(false)}
                disabled={locked}
              >
                {t("common.decline")}
              </Button>
            </>
          ) : null}
          {holdsAccess ? (
            <Button
              variant="text"
              size="small"
              onClick={onRevoke}
              disabled={locked}
            >
              {t("common.withdraw")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}