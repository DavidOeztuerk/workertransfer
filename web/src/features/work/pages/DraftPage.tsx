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

import { Briefbogen } from "../../../shared/components/Briefbogen";
import { getCompanyProfile } from "../../company/api/companies";
import { ladeAnschrift } from "../../person/api/zivilidentitaet";
import { empfaengerAusFirma, kopfAusSitzung } from "../lib/briefkopf";
import { merke } from "../lib/kontext";
import { schreibbeginn, schreibende } from "../lib/schreibuhr";
import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { AnmeldungNoetig } from "../../person/components/AnmeldungNoetig";
import {
  deleteDocument,
  getMyDocuments,
  holeEigenenInhalt,
  ladeMeinen,
  uploadDocument,
  type UnterlageV1,
} from "../../person/api/resume";
import { Schreibfortschritt } from "../components/Schreibfortschritt";
import { getJob } from "../api/jobs";
import { leerBriefHinweisSchluessel, schreibschluessel } from "../lib/schreibfehler";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
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

const STATUS_FARBE: Record<
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

export function DraftPage() {
  const { t } = useTranslation();
  const { signedIn, laedt: sitzungLaedt, session } = useHandelnder();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [fehler, setFehler] = useState<string | null>(null);
  const [hinweis, setHinweis] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState<string | null>(null);
  const [kommentar, setKommentar] = useState("");
  const [zitat, setZitat] = useState("");
  const [bearbeiten, setBearbeiten] = useState(false);
  const [betreff, setBetreff] = useState("");
  const [text, setText] = useState("");
  const [gewaehlt, setGewaehlt] = useState<string[]>([]);
  const [datei, setDatei] = useState<File | null>(null);
  const [dateiname, setDateiname] = useState("");
  const [dateiart, setDateiart] = useState<"zeugnis" | "zertifikat" | "sonstiges">("zeugnis");
  const [uploading, setUploading] = useState<string | null>(null);
  const [zuLoeschen, setZuLoeschen] = useState<string | null>(null);

  const entwurf = useAsync(
    (signal) => getDraft(id!, signal),
    [id],
    signedIn && !!id,
  );

  const e = entwurf.data?.ok ? entwurf.data.draft : null;
  const schreibt = e?.status === "generating";

  useEffect(() => {
    if (!e || e.status !== "generating") return;
    const timer = window.setInterval(() => entwurf.poll(), 1000);
    return () => window.clearInterval(timer);
  }, [e?.status, e?.id]);

  useEffect(() => {
    if (!e) return;
    if (e.status === "generating") schreibbeginn(e.id, e.writing_started_at);
    else schreibende(e.id);
  }, [e?.id, e?.status, e?.writing_started_at]);

  const unterlagen = useAsync(
    (signal) => getMyDocuments(signal),
    [session?.userId],
    signedIn && e !== null && !schreibt,
  );
  const lebenslauf = useAsync(
    () => merke(`resume:${session?.userId ?? ""}`, () => ladeMeinen()),
    [session?.userId],
    signedIn && e !== null && !schreibt,
  );
  const anschrift = useAsync(
    () => merke(`anschrift:${session?.userId ?? ""}`, () => ladeAnschrift()),
    [session?.userId],
    signedIn && !!session?.userId,
  );

  const stelle = useAsync(
    () => merke(`job:${e!.job_id}`, () => getJob(e!.job_id)),
    [e?.job_id],
    e !== null,
  );
  const firma = useAsync(
    () =>
      merke(`firma:${stelle.data!.tenant_id}`, () =>
        getCompanyProfile(stelle.data!.tenant_id),
      ),
    [stelle.data?.tenant_id],
    stelle.data !== null && !schreibt,
  );

  useEffect(() => {
    if (!e) return;
    setGewaehlt(Array.isArray(e.documents) ? e.documents : []);
    setBetreff(e.subject ?? "");
    setText(e.body ?? "");
  }, [e?.id, e?.subject, e?.body, e?.documents]);

  useEffect(() => {
    if (!e || e.status === "generating" || e.shares_resume === true) return;
    void chooseAttachments(e.id, true, Array.isArray(e.documents) ? e.documents : []).then(
      () => entwurf.reload(),
    );
  }, [e?.id, e?.shares_resume, e?.status]);

  useEffect(() => {
    if (!e || schreibt || !unterlagen.data?.ok) return;
    const cvIds = unterlagen.data.value
      .filter((d) => d.kind === "lebenslauf")
      .map((d) => d.id);
    if (cvIds.length === 0) return;
    const bisher = Array.isArray(e.documents) ? e.documents : [];
    if (cvIds.every((id) => bisher.includes(id))) return;
    const next = [...new Set([...bisher, ...cvIds])];
    void chooseAttachments(e.id, true, next).then(() => entwurf.reload());
  }, [e?.id, e?.status, unterlagen.data, schreibt]);

  if (sitzungLaedt) {
    return (
      <PageShell title={t("entwurfs.titel")} narrow>
        <LoadingBlock label={t("entwurfs.laden")} />
      </PageShell>
    );
  }

  if (!signedIn || !id) {
    return <AnmeldungNoetig titel={t("entwurfs.titel")} satz="entwurfs.anmelden" />;
  }

  if (entwurf.pending && !e) {
    return (
      <PageShell title={t("entwurfs.titel")} narrow>
        <LoadingBlock label={t("entwurfs.laden")} />
      </PageShell>
    );
  }

  if (entwurf.data !== null && !entwurf.data.ok) {
    return (
      <PageShell title={t("entwurfs.titel")} narrow>
        {entwurf.data.reason === "unavailable" ? (
          <Alert severity="info">{t(schreibschluessel(entwurf.data.error.detail))}</Alert>
        ) : (
          <Alert severity="error">{entwurf.data.error.detail}</Alert>
        )}
      </PageShell>
    );
  }

  if (!e) {
    return (
      <PageShell title={t("entwurfs.titel")} narrow>
        <EmptyBlock
          title={t("entwurfs.nichtGefunden")}
          hint={t("entwurfs.nichtGefundenHinweis")}
        />
      </PageShell>
    );
  }

  const offen = e.comments.filter((c) => !c.resolved);
  const erledigt = e.comments.filter((c) => c.resolved);
  const leer = e.body.trim() === "";
  const stand: DraftStatus = e.status;
  const canWrite = (e.status === "generating" || e.status === "failed") && !schreibt;
  const canRevise = e.status === "needs_changes" && offen.length > 0 && !leer && !schreibt;
  const canApprove = e.status === "review" && offen.length === 0 && !leer && !schreibt;
  const canSend = e.status === "approved" && !schreibt;
  const canDelete = e.status !== "sent";
  const canEdit = !leer && e.status !== "sent" && e.status !== "generating" && !schreibt;
  const canSelfWrite = leer && e.status === "failed" && !schreibt;
  const canComment = !leer && e.status !== "generating" && e.status !== "sent" && !schreibt;
  const titel = stelle.data?.title ?? (e.subject || t("entwurfs.titel"));
  const entwurfsId = e.id;

  async function lauf(
    name: string,
    aktion: () => Promise<{ ok: boolean; error?: { detail: string }; reason?: string }>,
  ) {
    setLaeuft(name);
    setFehler(null);
    setHinweis(null);
    const result = await aktion();
    setLaeuft(null);
    if (!result.ok) {
      const detail = result.error?.detail ?? "";
      if (name === "write" || name === "revise" || result.reason === "unavailable") {
        setHinweis(t(schreibschluessel(detail)));
      } else setFehler(detail);
    } else if (name !== "write" && name !== "revise") {
      /* write/revise laufen im Dienst weiter — der Stand kommt per GET. */
    }
    entwurf.reload();
  }

  async function handleUpload() {
    if (!datei) return;
    const name = dateiname.trim() || datei.name.replace(/\.[^/.]+$/, "");
    setUploading(name);
    setFehler(null);
    const result = await uploadDocument(datei, name, dateiart);
    setUploading(null);
    if (!result.ok) {
      setFehler(result.error.detail);
      return;
    }
    setDatei(null);
    setDateiname("");
    const cvIds = (unterlagen.data?.ok ? unterlagen.data.value : [])
      .filter((d) => d.kind === "lebenslauf")
      .map((d) => d.id);
    const next = [...new Set([...gewaehlt, result.value.id, ...cvIds])];
    setGewaehlt(next);
    unterlagen.reload();
    void lauf("attachments", () => chooseAttachments(entwurfsId, true, next));
  }

  async function handlePreview(docId: string) {
    const result = await holeEigenenInhalt(docId);
    if (!result.ok) {
      setFehler(result.error.detail);
      return;
    }
    const win = window.open(result.value.url, "_blank");
    if (!win) URL.revokeObjectURL(result.value.url);
  }

  async function handleDeleteDocument() {
    if (zuLoeschen === null) return;
    const id = zuLoeschen;
    setZuLoeschen(null);
    const result = await deleteDocument(id);
    if (!result.ok) {
      setFehler(result.error.detail);
      return;
    }
    const next = gewaehlt.filter((x) => x !== id);
    setGewaehlt(next);
    unterlagen.reload();
    void lauf("attachments", () => chooseAttachments(entwurfsId, true, next));
  }

  return (
    <PageShell
      title={titel}
      lead={t(`entwurfs.standHinweis.${stand}`)}
      actions={
        <Button variant="outlined" onClick={() => navigate("/applications/drafts")}>
          {t("entwurfs.zurListe")}
        </Button>
      }
    >
      {fehler && !schreibt ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setFehler(null)}>
          {fehler}
        </Alert>
      ) : null}
      {hinweis && !schreibt ? (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setHinweis(null)}>
          {hinweis}
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
                {e.subject || t("entwurfs.ohneBetreff")}
              </Typography>
              <Chip
                label={t(`entwurfs.status.${stand}`)}
                color={STATUS_FARBE[stand]}
                size="small"
              />
              <Typography variant="body2" color="text.secondary">
                {t("entwurfs.fassung", { version: e.version })}
              </Typography>
            </Box>

            {!leer && stand === "failed" && !schreibt ? (
              <Alert severity="info" sx={{ mb: 2 }}>
                {t(schreibschluessel(e.error))}
              </Alert>
            ) : null}

            {schreibt ? (
              <Schreibfortschritt seit={schreibbeginn(e.id, e.writing_started_at)} />
            ) : null}

            {bearbeiten ? (
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
                <TextField
                  label={t("entwurfs.betreff")}
                  value={betreff ?? ""}
                  onChange={(ev) => setBetreff(ev.target.value)}
                />
                <TextField
                  label={t("entwurfs.text")}
                  value={text ?? ""}
                  onChange={(ev) => setText(ev.target.value)}
                  multiline
                  minRows={10}
                />
                <Box sx={{ display: "flex", gap: 1 }}>
                  <Button
                    variant="contained"
                    disabled={laeuft === "save"}
                    onClick={() =>
                      void lauf("save", () => updateDraft(e.id, betreff, text)).then(() =>
                        setBearbeiten(false),
                      )
                    }
                  >
                    {t("allgemein.speichern")}
                  </Button>
                  <Button onClick={() => setBearbeiten(false)}>{t("allgemein.abbrechen")}</Button>
                </Box>
              </Box>
            ) : leer && !schreibt ? (
              <EmptyBlock
                title={t("entwurfs.leerBrief")}
                hint={t(leerBriefHinweisSchluessel(e.error))}
                action={
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", justifyContent: "center" }}>
                    {canWrite ? (
                      <Button
                        variant="contained"
                        disabled={laeuft !== null}
                        onClick={() => void lauf("write", () => writeDraft(e.id))}
                        startIcon={
                          laeuft === "write" ? (
                            <CircularProgress color="inherit" size={16} />
                          ) : null
                        }
                      >
                        {t("entwurfs.generieren")}
                      </Button>
                    ) : null}
                    {canSelfWrite ? (
                      <Button variant="outlined" onClick={() => setBearbeiten(true)}>
                        {t("entwurfs.selbstSchreiben")}
                      </Button>
                    ) : null}
                  </Box>
                }
              />
            ) : (
              <Briefbogen
                betreff={e.subject}
                text={e.body}
                leerHinweis={schreibt ? t("entwurfs.schreibtJetzt") : t("entwurfs.leerBrief")}
                kopf={kopfAusSitzung(session, anschrift.data)}
                empfaenger={empfaengerAusFirma(firma.data, stelle.data)}
              />
            )}

            {leer || bearbeiten || schreibt ? null : (
              <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap", mt: 2 }}>
                {canWrite ? (
                  <Button
                    variant="contained"
                    disabled={laeuft !== null}
                    onClick={() => void lauf("write", () => writeDraft(e.id))}
                    startIcon={
                      laeuft === "write" ? <CircularProgress color="inherit" size={16} /> : null
                    }
                  >
                    {t("entwurfs.nochmal")}
                  </Button>
                ) : null}
                {canRevise ? (
                  <Button
                    variant="contained"
                    disabled={laeuft !== null}
                    onClick={() => void lauf("revise", () => reviseDraft(e.id))}
                  >
                    {t("entwurfs.ueberarbeiten")}
                  </Button>
                ) : null}
                {canApprove ? (
                  <Button
                    variant="contained"
                    disabled={laeuft !== null}
                    onClick={() => void lauf("approve", () => approveDraft(e.id))}
                  >
                    {t("entwurfs.freigeben")}
                  </Button>
                ) : null}
                {canSend ? (
                  <Button
                    variant="contained"
                    disabled={laeuft !== null}
                    onClick={() => void lauf("send", () => sendDraft(e.id))}
                  >
                    {t("entwurfs.senden")}
                  </Button>
                ) : null}
                {canEdit ? (
                  <Button variant="outlined" onClick={() => setBearbeiten(true)}>
                    {t("entwurfs.bearbeiten")}
                  </Button>
                ) : null}
                {canDelete ? (
                  <Button
                    color="error"
                    disabled={laeuft !== null}
                    onClick={() =>
                      void lauf("delete", () => deleteDraft(e.id)).then(() =>
                        navigate("/applications/drafts"),
                      )
                    }
                  >
                    {t("allgemein.zurueckziehen")}
                  </Button>
                ) : null}
              </Box>
            )}
            {leer && !bearbeiten && !schreibt && canDelete ? (
              <Box sx={{ display: "flex", justifyContent: "center", mt: 2 }}>
                <Button
                  color="error"
                  disabled={laeuft !== null}
                  onClick={() =>
                    void lauf("delete", () => deleteDraft(e.id)).then(() =>
                      navigate("/applications/drafts"),
                    )
                  }
                >
                  {t("allgemein.zurueckziehen")}
                </Button>
              </Box>
            ) : null}
            {e.status === "review" || e.status === "approved" ? (
              <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                {t("entwurfs.schritte")}
              </Typography>
            ) : null}
          </CardContent>
        </Card>

        <Box sx={{ display: "grid", gap: 3 }}>
          <Card>
            <CardContent>
              <Typography variant="h5" sx={{ mb: 2 }}>
                {t("entwurfs.beilagen")}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {t("entwurfs.lebenslaufImmer")}
              </Typography>
              {(() => {
                const alle = unterlagen.data?.ok ? unterlagen.data.value : [];
                const cvDateien = alle.filter((doc) => doc.kind === "lebenslauf");
                const beilagen = alle.filter((doc) => doc.kind !== "lebenslauf");
                const hatFormular = lebenslauf.data?.ok && lebenslauf.data.value !== null;
                const hatCv = cvDateien.length > 0;
                const cvIds = cvDateien.map((d) => d.id);
                return (
              <>
              {hatCv ? (
                <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mb: 2 }}>
                  {cvDateien.map((doc) => (
                    <Box
                      key={doc.id}
                      sx={{ display: "flex", alignItems: "center", gap: 0.5, flexWrap: "wrap" }}
                    >
                      <FormControlLabel
                        sx={{ flex: 1, mr: 0, minWidth: 0 }}
                        control={<Checkbox checked disabled />}
                        label={`${doc.name} · ${t("entwurfs.lebenslaufGehtMit")}`}
                      />
                      <IconButton
                        size="small"
                        onClick={() => void handlePreview(doc.id)}
                        aria-label={t("lebenslauf.dateiAnsehen")}
                      >
                        <ViewIcon />
                      </IconButton>
                    </Box>
                  ))}
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { weg: "datei" } })}
                    >
                      {t("entwurfs.lebenslaufAnpassen")}
                    </Button>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { weg: "datei" } })}
                    >
                      {t("entwurfs.lebenslaufErsetzen")}
                    </Button>
                  </Box>
                </Box>
              ) : (
                <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mb: 2 }}>
                  <Typography variant="body2" color="text.secondary">
                    {hatFormular ? t("entwurfs.lebenslaufVorhanden") : t("entwurfs.lebenslaufFehlt")}
                  </Typography>
                  <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
                    <Button
                      size="small"
                      variant={hatFormular ? "outlined" : "contained"}
                      onClick={() => navigate("/resume", { state: { weg: "manuell" } })}
                    >
                      {hatFormular ? t("entwurfs.lebenslaufAnpassen") : t("entwurfs.lebenslaufAnlegen")}
                    </Button>
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => navigate("/resume", { state: { weg: "datei" } })}
                    >
                      {t("entwurfs.lebenslaufHochladen")}
                    </Button>
                  </Box>
                </Box>
              )}
              <Box sx={{ display: "flex", flexDirection: "column", gap: 0.5, mt: 2 }}>
                {beilagen.map((doc: UnterlageV1) => (
                  <Box
                    key={doc.id}
                    sx={{ display: "flex", alignItems: "center", gap: 0.5, flexWrap: "wrap" }}
                  >
                    <FormControlLabel
                      sx={{ flex: 1, mr: 0, minWidth: 0 }}
                      control={
                        <Checkbox
                          checked={gewaehlt.includes(doc.id)}
                          onChange={(_, an) => {
                            const next = an
                              ? [...new Set([...gewaehlt, doc.id, ...cvIds])]
                              : [...new Set([...gewaehlt.filter((x) => x !== doc.id), ...cvIds])];
                            setGewaehlt(next);
                            void lauf("attachments", () =>
                              chooseAttachments(e.id, true, next),
                            );
                          }}
                        />
                      }
                      label={doc.name}
                    />
                    <IconButton
                      size="small"
                      onClick={() => void handlePreview(doc.id)}
                      aria-label={t("lebenslauf.dateiAnsehen")}
                    >
                      <ViewIcon />
                    </IconButton>
                    <IconButton
                      size="small"
                      color="error"
                      onClick={() => setZuLoeschen(doc.id)}
                      disabled={laeuft !== null || schreibt}
                      aria-label={t("lebenslauf.unterlageLoeschen")}
                    >
                      <DeleteIcon />
                    </IconButton>
                  </Box>
                ))}
                {unterlagen.data?.ok && beilagen.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">
                    {t("entwurfs.keineUnterlagen")}
                  </Typography>
                ) : null}
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5, mt: 2 }}>
                <Button
                  size="small"
                  variant="outlined"
                  component="label"
                  disabled={uploading !== null || schreibt}
                  sx={{ alignSelf: "flex-start" }}
                >
                  {t("lebenslauf.unterlageHinzufuegen")}
                  <input
                    type="file"
                    hidden
                    accept="application/pdf,image/png,image/jpeg"
                    onChange={(ev) => {
                      const gewaehltDatei = ev.target.files?.[0] ?? null;
                      ev.target.value = "";
                      setDatei(gewaehltDatei);
                      if (gewaehltDatei) {
                        setDateiname(gewaehltDatei.name.replace(/\.[^/.]+$/, ""));
                        setFehler(null);
                      }
                    }}
                  />
                </Button>
                {datei ? (
                  <>
                    <Typography variant="body2" color="text.secondary">
                      {datei.name}
                    </Typography>
                    <TextField
                      size="small"
                      label={t("lebenslauf.unterlageName")}
                      value={dateiname}
                      onChange={(ev) => setDateiname(ev.target.value)}
                    />
                    <TextField
                      select
                      size="small"
                      label={t("lebenslauf.unterlageArt")}
                      value={dateiart}
                      onChange={(ev) =>
                        setDateiart(ev.target.value as "zeugnis" | "zertifikat" | "sonstiges")
                      }
                    >
                      <MenuItem value="zeugnis">{t("lebenslauf.unterlageArtZeugnis")}</MenuItem>
                      <MenuItem value="zertifikat">{t("lebenslauf.unterlageArtZertifikat")}</MenuItem>
                      <MenuItem value="sonstiges">{t("lebenslauf.unterlageArtSonstiges")}</MenuItem>
                    </TextField>
                    <Button
                      variant="contained"
                      size="small"
                      onClick={() => void handleUpload()}
                      disabled={uploading !== null}
                      sx={{ alignSelf: "flex-start" }}
                    >
                      {t("lebenslauf.unterlageHochladen")}
                    </Button>
                  </>
                ) : null}
                {uploading ? (
                  <Alert severity="info" role="status">
                    {t("lebenslauf.hochladenLaeuft", { name: uploading })}
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
                {t("entwurfs.anmerkungenTitel")}
              </Typography>
              {canComment ? (
                <>
                  <TextField
                    multiline
                    minRows={3}
                    fullWidth
                    label={t("entwurfs.kommentarHinzufuegen")}
                    placeholder={t("entwurfs.kommentarPlatzhalter")}
                    value={kommentar}
                    onChange={(ev) => setKommentar(ev.target.value)}
                    sx={{ mb: 1 }}
                  />
                  {zitat ? (
                    <Alert severity="info" sx={{ mb: 1 }}>
                      {t("entwurfs.markiertesZitat")} „{zitat}“
                    </Alert>
                  ) : null}
                  <Button
                    variant="contained"
                    disabled={!kommentar.trim() || laeuft !== null}
                    onClick={() => {
                      const markierung = window.getSelection()?.toString().trim() ?? "";
                      const quote = markierung || zitat;
                      setZitat(quote);
                      void lauf("comment", () => addComment(e.id, kommentar.trim(), quote)).then(
                        () => {
                          setKommentar("");
                          setZitat("");
                        },
                      );
                    }}
                  >
                    {t("entwurfs.kommentieren")}
                  </Button>
                </>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  {t("entwurfs.kommentarErstNachText")}
                </Typography>
              )}

              {offen.map((c) => (
                <Box key={c.id} sx={{ mt: 2, p: 1.5, border: 1, borderColor: "divider" }}>
                  {c.quote ? (
                    <Typography variant="body2" sx={{ fontStyle: "italic", mb: 0.5 }}>
                      „{c.quote}“
                    </Typography>
                  ) : null}
                  <Typography>{c.text}</Typography>
                </Box>
              ))}
              {erledigt.map((c) => (
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
        open={zuLoeschen !== null}
        onClose={() => setZuLoeschen(null)}
        aria-labelledby="entwurf-unterlage-loeschen-titel"
        aria-describedby="entwurf-unterlage-loeschen-text"
      >
        <DialogTitle id="entwurf-unterlage-loeschen-titel">
          {t("lebenslauf.unterlageLoeschenTitel")}
        </DialogTitle>
        <DialogContent>
          <DialogContentText id="entwurf-unterlage-loeschen-text">
            {t("lebenslauf.unterlageLoeschenBestaetigen")}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setZuLoeschen(null)}>
            {t("allgemein.abbrechen")}
          </Button>
          <Button
            color="error"
            variant="contained"
            onClick={() => void handleDeleteDocument()}
          >
            {t("lebenslauf.unterlageLoeschen")}
          </Button>
        </DialogActions>
      </Dialog>
    </PageShell>
  );
}
