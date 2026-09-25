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
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { parseSkills } from "../lib/skills";
import { useAsync } from "../lib/useAsync";
import { Lebenslaufblatt } from "../components/lebenslauf/Lebenslaufblatt";
import { getMyProfile } from "../api/profile";
import {
  type Lebenslaufanfrage,
  type Ausbildung,
  type Station,
  beantworten,
  deleteDocument,
  getMyDocuments,
  holeEigenenInhalt,
  ladeMeinen,
  ladeMeineAnfragen,
  setTemplate,
  speichereMeinen,
  uploadDocument,
  zuruecknehmen,
  alsLebenslauf,
  type Unterlagenart,
  type UnterlageV1,
} from "../api/resume";

type Lebenslaufweg = "wahl" | "manuell" | "datei";

function wegAusSuche(wert: string | null): Lebenslaufweg {
  return wert === "manuell" || wert === "datei" ? wert : "wahl";
}

const LEERE_STATION: Station = {
  employer: "",
  title: "",
  started_on: "",
  ended_on: null,
  description: "",
  technologies: [],
};

const LEERE_BILDUNG: Ausbildung = {
  institution: "",
  qualification: "",
  started_on: "",
  ended_on: null,
};

const TEMPLATES = ["schlicht", "klassisch", "modern"] as const;

/** Leer heisst „läuft noch", nicht „unbekannt" — deshalb `null` und nicht `""`. */
function normaliseEnd(value: string): string | null {
  const getrimmt = value.trim();
  return getrimmt === "" ? null : getrimmt;
}

export function ResumePage() {
  const { t } = useTranslation();
  const ort = useLocation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const lebenslauf = useAsync(
    (signal) => ladeMeinen(signal),
    [session?.userId],
    session !== null,
  );
  const anfragen = useAsync(
    (signal) => ladeMeineAnfragen(signal),
    [session?.userId],
    session !== null,
  );
  /*
   * DAS PROFIL, NUR FÜR DIE VORSCHAU.
   *
   * Überschrift, Text, Ort und Fähigkeiten stehen im Profil, nicht im
   * Lebenslauf — das Blatt braucht sie aber, weil das Unternehmen sie dort
   * sieht. Ohne diesen Aufruf zeigte die Vorschau ein anderes Blatt als der
   * Empfänger, und genau das soll sie nicht.
   */
  const meinProfil = useAsync(
    (signal) => getMyProfile(signal),
    [session?.userId],
    session !== null,
  );

  const meineUnterlagen = useAsync(
    (signal) => getMyDocuments(signal),
    [session?.userId],
    session !== null,
  );

  const [rows, setZeilen] = useState<Station[]>([]);
  const [lehren, setLehren] = useState<Ausbildung[]>([]);
  const [schulen, setSchulen] = useState<Ausbildung[]>([]);
  const [saved, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);
  const [template, setTemplateState] = useState<"schlicht" | "klassisch" | "modern">("schlicht");
  const [uploading, setUploading] = useState<string | null>(null);
  const [dateiname, setDateiname] = useState("");
  const [dateiart, setDateiart] = useState<"zeugnis" | "zertifikat" | "sonstiges">("zeugnis");
  const [datei, setDatei] = useState<File | null>(null);
  const [zuLoeschen, setZuLoeschen] = useState<string | null>(null);
  const [weg, setWeg] = useState<Lebenslaufweg>(() =>
    wegAusSuche((ort.state as { weg?: string } | null)?.weg ?? null),
  );
  const [cvDatei, setCvDatei] = useState<File | null>(null);
  const [cvName, setCvName] = useState("");

  // Das Formular folgt dem geladenen Stand — aber nur, wenn wirklich einer da
  // ist. Bei einem Ladefehler bleibt es leer UND die Seite sagt warum; ein
  // stilles leeres Formular waere die Einladung, den Lebenslauf zu ueberschreiben.
  useEffect(() => {
    if (lebenslauf.value?.ok && lebenslauf.value.value !== null) {
      setZeilen(
        lebenslauf.value.value.positions.map((station) => ({ ...station })),
      );
      const bildung = lebenslauf.value.value.education ?? [];
      setLehren(bildung.filter((eintrag) => eintrag.kind !== "schule").map((e) => ({ ...e })));
      setSchulen(bildung.filter((eintrag) => eintrag.kind === "schule").map((e) => ({ ...e })));
      if (lebenslauf.value.value.template) {
        setTemplateState(lebenslauf.value.value.template);
      }
    }
  }, [lebenslauf.value]);

  const unterlagenListe: UnterlageV1[] =
    meineUnterlagen.value?.ok ? meineUnterlagen.value.value : [];
  const cvUnterlagen = unterlagenListe.filter((doc) => doc.kind === "lebenslauf");
  const beilagen = unterlagenListe.filter((doc) => doc.kind !== "lebenslauf");

  useEffect(() => {
    if (weg !== "wahl") return;
    if (cvUnterlagen.length > 0) {
      setWeg("datei");
      return;
    }
    if (lebenslauf.value?.ok && lebenslauf.value.value !== null) {
      setWeg("manuell");
    }
  }, [weg, cvUnterlagen.length, lebenslauf.value]);

  useEffect(() => {
    if (weg !== "manuell") return;
    setZeilen((bisher) => (bisher.length === 0 ? [{ ...LEERE_STATION }] : bisher));
  }, [weg]);

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("resume.title")}
        satz="resume.signInRequired"
      />
    );
  }

  function change(index: number, teil: Partial<Station>) {
    setZeilen((before) =>
      before.map((row, i) => (i === index ? { ...row, ...teil } : row)),
    );
    setGespeichert(false);
  }

  function aendereBildung(
    setter: typeof setLehren,
    index: number,
    teil: Partial<Ausbildung>,
  ) {
    setter((before) => before.map((row, i) => (i === index ? { ...row, ...teil } : row)));
    setGespeichert(false);
  }

  async function speichern() {
    setLaeuft(true);
    const result = await speichereMeinen({
      positions: rows.filter(
        (row) =>
          row.employer.trim() !== "" && row.title.trim() !== "" && row.started_on.trim() !== "",
      ),
      education: [
        ...lehren
          .filter((row) => row.institution.trim() !== "" && row.started_on.trim() !== "")
          .map((row) => ({ ...row, kind: "ausbildung" as const })),
        ...schulen
          .filter((row) => row.institution.trim() !== "" && row.started_on.trim() !== "")
          .map((row) => ({ ...row, kind: "schule" as const })),
      ],
    });
    setLaeuft(false);

    if (result.ok) {
      setFehler(null);
      setGespeichert(true);
      lebenslauf.setze({ ok: true, value: result.value });
    } else {
      setFehler(result.error.detail);
      setGespeichert(false);
    }
  }

  async function beantworteAnfrage(id: string, grant: boolean) {
    setLaeuft(true);
    const result = await beantworten(id, grant);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    anfragen.again();
  }

  async function nimmZurueck(id: string) {
    setLaeuft(true);
    const result = await zuruecknehmen(id);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    anfragen.again();
  }

  async function handleTemplateChange(newTemplate: "schlicht" | "klassisch" | "modern") {
    setLaeuft(true);
    const result = await setTemplate(newTemplate);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    else setTemplateState(newTemplate);
  }

  async function handleUpload(kind: Unterlagenart, file: File, name: string) {
    const genannt = name.trim() || file.name.replace(/\.[^/.]+$/, "");
    setUploading(genannt);
    const result = await uploadDocument(file, genannt, kind);
    setUploading(null);

    if (!result.ok) setFehler(result.error.detail);
    else {
      setFehler(null);
      setDatei(null);
      setDateiname("");
      setCvDatei(null);
      setCvName("");
      meineUnterlagen.again();
    }
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

  async function handleAlsLebenslauf(id: string) {
    const result = await alsLebenslauf(id);
    if (!result.ok) setFehler(result.error.detail);
    else {
      setFehler(null);
      setWeg("datei");
      meineUnterlagen.again();
    }
  }

  async function handleDeleteDocument() {
    if (zuLoeschen === null) return;
    const id = zuLoeschen;
    setZuLoeschen(null);
    const result = await deleteDocument(id);
    if (!result.ok) setFehler(result.error.detail);
    else {
      setFehler(null);
      meineUnterlagen.again();
    }
  }

  return (
    <PageShell
      title={t("resume.title")}
      narrow
      lead={t("resume.lead")}
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setFehler(null)}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("resume.requests")}
          </Typography>

          {anfragen.laedt ? (
            <LoadingBlock label={t("resume.requestsLoading")} />
          ) : null}

          {anfragen.value && !anfragen.value.ok ? (
            <ErrorBlock error={anfragen.value.error} />
          ) : null}

          {anfragen.value?.ok && anfragen.value.value.length === 0 ? (
            <EmptyBlock title={t("resume.requestsEmpty")} />
          ) : null}

          {anfragen.value?.ok && anfragen.value.value.length > 0 ? (
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
              {anfragen.value.value.map((anfrage) => (
                <Anfragezeile
                  key={anfrage.id}
                  anfrage={anfrage}
                  locked={running}
                  onAntwort={(grant) =>
                    void beantworteAnfrage(anfrage.id, grant)
                  }
                  onZurueck={() => void nimmZurueck(anfrage.id)}
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      {weg === "wahl" ? (
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
                    setWeg("manuell");
                    setZeilen((bisher) => (bisher.length === 0 ? [{ ...LEERE_STATION }] : bisher));
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
                <CardActionArea onClick={() => setWeg("datei")}>
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
              const next = weg === "datei" ? "manuell" : "datei";
              setWeg(next);
              if (next === "manuell") {
                setZeilen((bisher) => (bisher.length === 0 ? [{ ...LEERE_STATION }] : bisher));
              }
            }}
          >
            {weg === "datei"
              ? t("resume.switchToManual")
              : t("resume.switchToFile")}
          </Button>
        </Box>
      )}

      {weg === "datei" ? (
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
                    setCvDatei(file);
                    if (file) {
                      setCvName(file.name.replace(/\.[^/.]+$/, ""));
                      setFehler(null);
                    }
                  }}
                />
              </Button>
              {cvDatei ? (
                <>
                  <Typography variant="body2" color="text.secondary">
                    {cvDatei.name}
                  </Typography>
                  <TextField
                    label={t("resume.documentName")}
                    value={cvName}
                    onChange={(event) => setCvName(event.target.value)}
                  />
                  <Button
                    variant="contained"
                    onClick={() => {
                      if (cvDatei) void handleUpload("lebenslauf", cvDatei, cvName);
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
            {cvUnterlagen.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                {t("resume.noDocuments")}
              </Typography>
            ) : (
              <Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
                {cvUnterlagen.map((doc) => (
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
                          onClick={() => setZuLoeschen(doc.id)}
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

      {weg === "datei" ? (
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
                  // Sonst feuert onChange nicht, wenn dieselbe Datei nochmal
                  // gewählt wird — nach Löschen und erneut Hochladen wirkt
                  // der Knopf dann tot, ohne Fehler.
                  event.target.value = "";
                  setDatei(file);
                  if (file) {
                    setDateiname(file.name.replace(/\.[^/.]+$/, ""));
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
                  label={t("resume.documentName")}
                  value={dateiname}
                  onChange={(event) => setDateiname(event.target.value)}
                />
                <TextField
                  select
                  label={t("resume.documentKind")}
                  value={dateiart}
                  onChange={(event) =>
                    setDateiart(event.target.value as "zeugnis" | "zertifikat" | "sonstiges")
                  }
                >
                  <MenuItem value="zeugnis">{t("resume.documentKindReference")}</MenuItem>
                  <MenuItem value="zertifikat">{t("resume.documentKindCertificate")}</MenuItem>
                  <MenuItem value="sonstiges">{t("resume.documentKindOther")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (datei) void handleUpload(dateiart, datei, dateiname);
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

          {meineUnterlagen.laedt ? (
            <LoadingBlock label={t("resume.documentsLoading")} />
          ) : null}

          {meineUnterlagen.value && !meineUnterlagen.value.ok ? (
            <ErrorBlock error={meineUnterlagen.value.error} />
          ) : null}

          {meineUnterlagen.value?.ok && beilagen.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("resume.noDocuments")}</Typography>
          ) : null}

          {beilagen.length > 0 && (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1, mt: 1 }}>
              {beilagen.map((doc) => (
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
                        <Button size="small" onClick={() => void handleAlsLebenslauf(doc.id)}>
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
                        onClick={() => setZuLoeschen(doc.id)}
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

      {weg === "manuell" ? (
      <>
      <Box
        component="form"
        onSubmit={(event) => {
          event.preventDefault();
          void speichern();
        }}
      >
      {lebenslauf.laedt ? (
        <LoadingBlock label={t("resume.loading")} />
      ) : null}

      {lebenslauf.value && !lebenslauf.value.ok ? (
        <ErrorBlock error={lebenslauf.value.error} />
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
                    setZeilen((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
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
            onClick={() => setZeilen((before) => [...before, { ...LEERE_STATION }])}
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
          {lehren.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("resume.trainingNumber", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setLehren((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
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
                  onChange={(e) => aendereBildung(setLehren, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("resume.qualification")}
                  value={row.qualification}
                  onChange={(e) => aendereBildung(setLehren, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("resume.from")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => aendereBildung(setLehren, index, { started_on: e.target.value })}
                  helperText={t("resume.fromHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.to")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => aendereBildung(setLehren, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("resume.toHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
              </Box>
              <Divider sx={{ mt: 3 }} />
            </Box>
          ))}
          <Button
            variant="outlined"
            onClick={() => setLehren((before) => [...before, { ...LEERE_BILDUNG }])}
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
          {schulen.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("resume.schoolNumber", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setSchulen((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
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
                  onChange={(e) => aendereBildung(setSchulen, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("resume.schoolQualification")}
                  value={row.qualification}
                  onChange={(e) => aendereBildung(setSchulen, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("resume.from")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => aendereBildung(setSchulen, index, { started_on: e.target.value })}
                  helperText={t("resume.fromHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("resume.to")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => aendereBildung(setSchulen, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("resume.toHint")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
              </Box>
              <Divider sx={{ mt: 3 }} />
            </Box>
          ))}
          <Button
            variant="outlined"
            onClick={() => setSchulen((before) => [...before, { ...LEERE_BILDUNG }])}
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
          <Lebenslaufblatt
            // The preview IS the promise (ADR-0035: the same rendering the
            // person sees, nothing rendered differently for the recipient), so
            // it is the SAME component as in the application folder — two
            // renderings would be two truths. Fed from the RUNNING form and not
            // from the saved state, so a typed position shows before saving.
            template={template}
            daten={{
              headline: meinProfil.value?.ok
                ? (meinProfil.value.profile?.headline ?? "")
                : "",
              bio: meinProfil.value?.ok ? (meinProfil.value.profile?.bio ?? "") : "",
              location: meinProfil.value?.ok
                ? (meinProfil.value.profile?.location ?? "")
                : "",
              skills: meinProfil.value?.ok ? (meinProfil.value.profile?.skills ?? []) : [],
              positions: rows.filter(
                (row) =>
                  row.employer.trim() !== ""
                  && row.title.trim() !== ""
                  && row.started_on.trim() !== "",
              ),
              education: [
                ...lehren
                  .filter(
                    (row) => row.institution.trim() !== "" && row.started_on.trim() !== "",
                  )
                  .map((row) => ({ ...row, kind: "ausbildung" as const })),
                ...schulen
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
                  setDatei(file);
                  if (file) {
                    setDateiname(file.name.replace(/\.[^/.]+$/, ""));
                    setFehler(null);
                  }
                }}
              />
            </Button>
            {datei ? (
              <>
                <Typography variant="body2" color="text.secondary">{datei.name}</Typography>
                <TextField
                  label={t("resume.documentName")}
                  value={dateiname}
                  onChange={(event) => setDateiname(event.target.value)}
                />
                <TextField
                  select
                  label={t("resume.documentKind")}
                  value={dateiart}
                  onChange={(event) =>
                    setDateiart(event.target.value as "zeugnis" | "zertifikat" | "sonstiges")
                  }
                >
                  <MenuItem value="zeugnis">{t("resume.documentKindReference")}</MenuItem>
                  <MenuItem value="zertifikat">{t("resume.documentKindCertificate")}</MenuItem>
                  <MenuItem value="sonstiges">{t("resume.documentKindOther")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (datei) void handleUpload(dateiart, datei, dateiname);
                  }}
                  disabled={uploading !== null}
                >
                  {t("resume.documentUpload")}
                </Button>
              </>
            ) : null}
          </Box>
          {beilagen.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("resume.noDocuments")}</Typography>
          ) : (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
              {beilagen.map((doc) => (
                <Card key={doc.id} variant="outlined">
                  <CardContent sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                    <Typography variant="body1">{doc.name}</Typography>
                    <Box>
                      <IconButton size="small" onClick={() => void handlePreview(doc.id)} aria-label={t("resume.viewFile")}>
                        <ViewIcon />
                      </IconButton>
                      <IconButton size="small" color="error" onClick={() => setZuLoeschen(doc.id)} aria-label={t("resume.documentDelete")}>
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
        open={zuLoeschen !== null}
        onClose={() => setZuLoeschen(null)}
        aria-labelledby="unterlage-loeschen-titel"
        aria-describedby="unterlage-loeschen-text"
      >
        <DialogTitle id="unterlage-loeschen-titel">
          {t("resume.documentDeleteTitle")}
        </DialogTitle>
        <DialogContent>
          <DialogContentText id="unterlage-loeschen-text">
            {t("resume.documentDeleteConfirm")}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setZuLoeschen(null)}>
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
 * Eine Anfrage mit ihrem Stand.
 *
 * <strong>„Freigegeben" und „hält gerade Zugriff" sind zwei Dinge.</strong>
 * `GRANTED` heisst „wurde einmal erteilt"; ob die Freigabe JETZT gilt, sagt
 * `active`. Nur wer sie hält, bekommt „Zurückziehen" angeboten — sonst böte die
 * Seite eine Handlung an, die nichts mehr ändert.
 */
function Anfragezeile({
  anfrage,
  locked,
  onAntwort,
  onZurueck,
}: {
  anfrage: Lebenslaufanfrage;
  locked: boolean;
  onAntwort: (grant: boolean) => void;
  onZurueck: () => void;
}) {
  const { t } = useTranslation();
  const open = anfrage.status === "PENDING";
  const haeltZugriff = anfrage.status === "GRANTED" && anfrage.active === true;

  const current = open
    ? t("resume.statusOpen")
    : anfrage.status === "DECLINED"
      ? t("resume.statusDeclined")
      : t(
          haeltZugriff
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
                onClick={() => onAntwort(true)}
                disabled={locked}
              >
                {t("common.release")}
              </Button>
              <Button
                variant="text"
                size="small"
                onClick={() => onAntwort(false)}
                disabled={locked}
              >
                {t("common.decline")}
              </Button>
            </>
          ) : null}
          {haeltZugriff ? (
            <Button
              variant="text"
              size="small"
              onClick={onZurueck}
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