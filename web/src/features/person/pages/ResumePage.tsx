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
        titel={t("lebenslauf.titel")}
        satz="lebenslauf.anmelden"
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
      title={t("lebenslauf.titel")}
      narrow
      lead={t("lebenslauf.lead")}
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setFehler(null)}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("lebenslauf.anfragen")}
          </Typography>

          {anfragen.laedt ? (
            <LoadingBlock label={t("lebenslauf.anfragenLaden")} />
          ) : null}

          {anfragen.value && !anfragen.value.ok ? (
            <ErrorBlock error={anfragen.value.error} />
          ) : null}

          {anfragen.value?.ok && anfragen.value.value.length === 0 ? (
            <EmptyBlock title={t("lebenslauf.anfragenLeer")} />
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
            <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.wegwahlTitel")}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              {t("lebenslauf.wegwahlLead")}
            </Typography>
            <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" } }}>
              {/*
                EINE KLICKBARE KARTE IST KEIN KNOPF.
                
                Hier stand `<Card onClick>` — ohne Rolle, ohne `tabIndex`, ohne
                Tastaturweg. Wer mit der Tastatur oder einem Vorleser arbeitet,
                konnte nicht wählen, wie sein Lebenslauf entsteht: die Wahl war
                sichtbar und unerreichbar. `CardActionArea` rendert einen echten
                `button` und nimmt Fokus, Eingabetaste und Leertaste mit.
                
                Gefunden hat es die E2E-Reise, die einen Knopf suchte und keinen
                fand — sie hatte recht, und der Fehler lag nicht bei ihr.
              */}
              <Card variant="outlined">
                <CardActionArea
                  onClick={() => {
                    setWeg("manuell");
                    setZeilen((bisher) => (bisher.length === 0 ? [{ ...LEERE_STATION }] : bisher));
                  }}
                >
                <CardContent>
                  <Typography variant="h4">{t("lebenslauf.wegManuell")}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    {t("lebenslauf.wegManuellHinweis")}
                  </Typography>
                </CardContent>
                </CardActionArea>
              </Card>
              <Card variant="outlined">
                <CardActionArea onClick={() => setWeg("datei")}>
                  <CardContent>
                    <Typography variant="h4">{t("lebenslauf.wegDatei")}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t("lebenslauf.wegDateiHinweis")}
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
              ? t("lebenslauf.wegWechselnZuManuell")
              : t("lebenslauf.wegWechselnZuDatei")}
          </Button>
        </Box>
      )}

      {weg === "datei" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.cvDatei")}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              {t("lebenslauf.dateiLead")}
            </Typography>
            <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
              <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
                {t("lebenslauf.unterlageHinzufuegen")}
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
                    label={t("lebenslauf.unterlageName")}
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
            {cvUnterlagen.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                {t("lebenslauf.keineUnterlagen")}
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
                            {t("lebenslauf.unterlageArtLebenslauf")} · {Math.round(doc.size_bytes / 1024)} KB
                          </Typography>
                        </Box>
                      </Box>
                      <Box sx={{ display: "flex", gap: 0.5 }}>
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
                          disabled={running}
                          aria-label={t("lebenslauf.unterlageLoeschen")}
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
          <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.unterlagen")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.unterlagenLead")}
          </Typography>
          <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
            <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
              {t("lebenslauf.unterlageHinzufuegen")}
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
                  label={t("lebenslauf.unterlageName")}
                  value={dateiname}
                  onChange={(event) => setDateiname(event.target.value)}
                />
                <TextField
                  select
                  label={t("lebenslauf.unterlageArt")}
                  value={dateiart}
                  onChange={(event) =>
                    setDateiart(event.target.value as "zeugnis" | "zertifikat" | "sonstiges")
                  }
                >
                  <MenuItem value="zeugnis">{t("lebenslauf.unterlageArtZeugnis")}</MenuItem>
                  <MenuItem value="zertifikat">{t("lebenslauf.unterlageArtZertifikat")}</MenuItem>
                  <MenuItem value="sonstiges">{t("lebenslauf.unterlageArtSonstiges")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (datei) void handleUpload(dateiart, datei, dateiname);
                  }}
                  disabled={uploading !== null}
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

          {meineUnterlagen.laedt ? (
            <LoadingBlock label={t("lebenslauf.unterlagenLaden")} />
          ) : null}

          {meineUnterlagen.value && !meineUnterlagen.value.ok ? (
            <ErrorBlock error={meineUnterlagen.value.error} />
          ) : null}

          {meineUnterlagen.value?.ok && beilagen.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("lebenslauf.keineUnterlagen")}</Typography>
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
                              ? "lebenslauf.unterlageArtZeugnis"
                              : doc.kind === "zertifikat"
                                ? "lebenslauf.unterlageArtZertifikat"
                                : "lebenslauf.unterlageArtSonstiges",
                          )}{" "}
                          · {Math.round(doc.size_bytes / 1024)} KB · {t("lebenslauf.hochgeladenAm", { datum: new Date(doc.uploaded_at).toLocaleDateString("de-DE") })}
                        </Typography>
                      </Box>
                    </Box>
                    <Box sx={{ display: "flex", gap: 0.5, alignItems: "center" }}>
                      {doc.kind !== "lebenslauf" ? (
                        <Button size="small" onClick={() => void handleAlsLebenslauf(doc.id)}>
                          {t("entwurfs.alsLebenslauf")}
                        </Button>
                      ) : null}
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
                        disabled={running}
                        aria-label={t("lebenslauf.unterlageLoeschen")}
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
        <LoadingBlock label={t("lebenslauf.laden")} />
      ) : null}

      {lebenslauf.value && !lebenslauf.value.ok ? (
        <ErrorBlock error={lebenslauf.value.error} />
      ) : null}

      {saved ? (
        <Alert severity="success" sx={{ mb: 2 }} role="status">
          {t("lebenslauf.gespeichert")}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.stationen")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.berufeLead")}
          </Typography>
          {rows.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("lebenslauf.station", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setZeilen((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
                  }}
                  aria-label={t("lebenslauf.eintragEntfernen")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("lebenslauf.arbeitgeber")}
                  value={row.employer}
                  onChange={(e) => change(index, { employer: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.position")}
                  value={row.title}
                  onChange={(e) => change(index, { title: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.von")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => change(index, { started_on: e.target.value })}
                  helperText={t("lebenslauf.vonHinweis")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("lebenslauf.bis")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => change(index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("lebenslauf.bisHinweis")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("lebenslauf.beschreibung")}
                  value={row.description}
                  onChange={(e) => change(index, { description: e.target.value })}
                  multiline
                  minRows={2}
                />
                <TextField
                  label={t("lebenslauf.technologien")}
                  helperText={t("lebenslauf.technologienHinweis")}
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
            {t("lebenslauf.stationHinzufuegen")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.ausbildung")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.ausbildungLead")}
          </Typography>
          {lehren.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("lebenslauf.ausbildungNummer", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setLehren((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
                  }}
                  aria-label={t("lebenslauf.eintragEntfernen")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("lebenslauf.einrichtung")}
                  value={row.institution}
                  onChange={(e) => aendereBildung(setLehren, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.abschluss")}
                  value={row.qualification}
                  onChange={(e) => aendereBildung(setLehren, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.von")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => aendereBildung(setLehren, index, { started_on: e.target.value })}
                  helperText={t("lebenslauf.vonHinweis")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("lebenslauf.bis")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => aendereBildung(setLehren, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("lebenslauf.bisHinweis")}
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
            {t("lebenslauf.ausbildungHinzufuegen")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.schule")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.schuleLead")}
          </Typography>
          {schulen.map((row, index) => (
            <Box key={index} sx={{ mb: 3 }}>
              <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 1.5 }}>
                <Typography variant="h3">
                  {t("lebenslauf.schuleNummer", { nummer: index + 1 })}
                </Typography>
                <IconButton
                  size="small"
                  onClick={() => {
                    setSchulen((before) => before.filter((_, i) => i !== index));
                    setGespeichert(false);
                  }}
                  aria-label={t("lebenslauf.eintragEntfernen")}
                >
                  <DeleteIcon />
                </IconButton>
              </Box>
              <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <TextField
                  label={t("lebenslauf.schuleName")}
                  value={row.institution}
                  onChange={(e) => aendereBildung(setSchulen, index, { institution: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.schuleAbschluss")}
                  value={row.qualification}
                  onChange={(e) => aendereBildung(setSchulen, index, { qualification: e.target.value })}
                />
                <TextField
                  label={t("lebenslauf.von")}
                  type="month"
                  value={row.started_on}
                  onChange={(e) => aendereBildung(setSchulen, index, { started_on: e.target.value })}
                  helperText={t("lebenslauf.vonHinweis")}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  label={t("lebenslauf.bis")}
                  type="month"
                  value={row.ended_on ?? ""}
                  onChange={(e) => aendereBildung(setSchulen, index, { ended_on: normaliseEnd(e.target.value) })}
                  helperText={t("lebenslauf.bisHinweis")}
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
            {t("lebenslauf.schuleHinzufuegen")}
          </Button>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>{t("lebenslauf.vorlage")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{t("lebenslauf.vorlageLead")}</Typography>
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
                {/* Auch hier ein echter Knopf, aus demselben Grund wie oben. */}
                <CardActionArea
                  onClick={() => void handleTemplateChange(templ)}
                  aria-pressed={template === templ}
                >
                  <CardContent>
                    <Typography variant="h4">
                      {t(`lebenslauf.vorlage${templ.charAt(0).toUpperCase()}${templ.slice(1)}`)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t(`lebenslauf.vorlage${templ.charAt(0).toUpperCase()}${templ.slice(1)}Hinweis`)}
                    </Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            ))}
          </Box>

          {/*
            DIE VORSCHAU IST NICHT SCHMUCK, SONDERN DIE ZUSAGE.

            ADR-0035: „dieselbe Darstellung, die sie selbst sieht; nichts wird
            für den Empfänger anders gerendert." Ohne sie wählt die Person eine
            Vorlage blind und sieht das Ergebnis nie — nur das Unternehmen sieht
            es. Es ist DIESELBE Komponente wie in der Bewerbungsmappe; zwei
            Darstellungen wären zwei Wahrheiten.

            Gespeist aus dem LAUFENDEN Formular und nicht aus dem gespeicherten
            Stand: wer eine Station tippt, soll sie im Blatt sehen, bevor er
            speichert.
          */}
          <Divider sx={{ my: 3 }} />
          <Typography variant="h4" sx={{ mb: 0.5 }}>
            {t("lebenslauf.vorschau")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.vorschauLead")}
          </Typography>
          <Lebenslaufblatt
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
          {running ? t("allgemein.speichernLaeuft") : t("allgemein.speichern")}
        </Button>
      </Box>
      </Box>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>{t("lebenslauf.unterlagen")}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("lebenslauf.unterlagenLead")}
          </Typography>
          <Box sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}>
            <Button variant="outlined" component="label" startIcon={<DocIcon />} disabled={uploading !== null}>
              {t("lebenslauf.unterlageHinzufuegen")}
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
                  label={t("lebenslauf.unterlageName")}
                  value={dateiname}
                  onChange={(event) => setDateiname(event.target.value)}
                />
                <TextField
                  select
                  label={t("lebenslauf.unterlageArt")}
                  value={dateiart}
                  onChange={(event) =>
                    setDateiart(event.target.value as "zeugnis" | "zertifikat" | "sonstiges")
                  }
                >
                  <MenuItem value="zeugnis">{t("lebenslauf.unterlageArtZeugnis")}</MenuItem>
                  <MenuItem value="zertifikat">{t("lebenslauf.unterlageArtZertifikat")}</MenuItem>
                  <MenuItem value="sonstiges">{t("lebenslauf.unterlageArtSonstiges")}</MenuItem>
                </TextField>
                <Button
                  variant="contained"
                  onClick={() => {
                    if (datei) void handleUpload(dateiart, datei, dateiname);
                  }}
                  disabled={uploading !== null}
                >
                  {t("lebenslauf.unterlageHochladen")}
                </Button>
              </>
            ) : null}
          </Box>
          {beilagen.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t("lebenslauf.keineUnterlagen")}</Typography>
          ) : (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
              {beilagen.map((doc) => (
                <Card key={doc.id} variant="outlined">
                  <CardContent sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 1 }}>
                    <Typography variant="body1">{doc.name}</Typography>
                    <Box>
                      <IconButton size="small" onClick={() => void handlePreview(doc.id)} aria-label={t("lebenslauf.dateiAnsehen")}>
                        <ViewIcon />
                      </IconButton>
                      <IconButton size="small" color="error" onClick={() => setZuLoeschen(doc.id)} aria-label={t("lebenslauf.unterlageLoeschen")}>
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
          {t("lebenslauf.unterlageLoeschenTitel")}
        </DialogTitle>
        <DialogContent>
          <DialogContentText id="unterlage-loeschen-text">
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
    ? t("lebenslauf.standOffen")
    : anfrage.status === "DECLINED"
      ? t("lebenslauf.standAbgelehnt")
      : t(
          haeltZugriff
            ? "lebenslauf.standFreigegeben"
            : "lebenslauf.standZurueckgezogen",
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
            {t("lebenslauf.anfrageTitel")}
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
                {t("allgemein.freigeben")}
              </Button>
              <Button
                variant="text"
                size="small"
                onClick={() => onAntwort(false)}
                disabled={locked}
              >
                {t("allgemein.ablehnen")}
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
              {t("allgemein.zurueckziehen")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}