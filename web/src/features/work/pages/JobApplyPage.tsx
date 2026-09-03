import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { Requirements } from "../components/Requirements";
import { useHandelnder } from "../lib/session";
import { merkeStelle } from "../lib/intent";
import { useAsync } from "../lib/useAsync";
import { apply } from "../api/applications";
import { getJob } from "../api/jobs";
import { getMyProfile } from "../api/profile";

/**
 * <c>/jobs/:id/apply</c> — sich auf eine Stelle bewerben.
 *
 * <strong>Eine Bewerbung öffnet Daten, und die Seite fragt vorher.</strong> Das
 * Profil geht immer mit — ohne es wäre es keine Bewerbung, und ein Kästchen
 * dafür wäre eine Wahl, die niemand ernsthaft trifft. Lebenslauf und Arbeiten
 * sind die echte Entscheidung.
 *
 * <strong>Kästchen und nicht Schalter</strong>, und das ist genau umgekehrt zum
 * Rest der Anwendung: hier gilt die Freigabe mit dem <em>Absenden</em>, nicht
 * sofort. Ein Schalter verspräche das Gegenteil.
 *
 * <strong>Die Passung steht auch hier</strong> — beim Formulieren hilft es zu
 * sehen, welche Fähigkeit fehlt. Weiterhin eine Liste mit Haken, niemals eine
 * Zahl; und wer nichts eingetragen hat, bekommt kein „0 von 3".
 */
export function JobApplyPage() {
  const { t } = useTranslation();
  const { jobId } = useParams();
  const navigate = useNavigate();
  const { angemeldet, subjectId } = useHandelnder();

  const [anschreiben, setAnschreiben] = useState("");
  const [lebenslauf, setLebenslauf] = useState(false);
  const [arbeiten, setArbeiten] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);
  const [gesendet, setGesendet] = useState(false);

  const gueltig = typeof jobId === "string" && jobId !== "";

  const stelle = useAsync(
    (signal) => getJob(jobId as string, signal),
    [jobId],
    gueltig,
  );
  const profil = useAsync(
    (signal) => getMyProfile(signal),
    [subjectId],
    angemeldet,
  );

  const zurueck = (
    <Link component={RouterLink} to="/jobs" variant="body2">
      {t("bewerbung.zurueck")}
    </Link>
  );

  if (!gueltig) {
    return (
      <PageShell title={t("bewerbung.stelleFehltTitel")} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <Typography>{t("bewerbung.adresseUngueltig")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (stelle.pending) {
    return (
      <PageShell title={t("bewerbung.titel")} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <LoadingBlock label={t("bewerbung.laden")} />
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const anzeige = stelle.data;

  if (anzeige === null) {
    return (
      <PageShell title={t("bewerbung.stelleFehltTitel")} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <Typography>
              {t("bewerbung.stelleZurueckgezogen")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (gesendet) {
    return (
      <PageShell title={t("bewerbung.abgeschickt")} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            {/* Wo man es zurücknimmt, steht dort, wo man es getan hat — nicht in
                einer Hilfe, die man erst suchen muss. */}
            <Typography>
              <Trans
                i18nKey="bewerbung.abgeschicktText"
                components={{
                  1: <Link component={RouterLink} to="/applications" />,
                }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (!angemeldet) {
    return (
      <PageShell title={anzeige.title} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <Typography sx={{ mb: 2 }}>
              {t("bewerbung.kontoNoetig")}
            </Typography>
            {/* Erst merken, dann wechseln. Wer über die Kopfzeile zur Anmeldung
                geht, hat keine Absicht geäußert und wird auch nicht
                zurückgeworfen. */}
            <Button
              variant="contained"
              onClick={() => {
                merkeStelle(anzeige.id, anzeige.title);
                void navigate("/login");
              }}
            >
              {t("bewerbung.anmeldenUndBewerben")}
            </Button>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  // `null` heisst „unbekannt" und unterdrückt die Passung; ein leeres Feld
  // heisst „nichts eingetragen" und führt zum Hinweis aufs Profil.
  const meineFaehigkeiten = profil.pending ? null : (profil.data?.skills ?? []);

  const stellenId = anzeige.id;

  async function absenden() {
    setLaeuft(true);
    const ergebnis = await apply({
      job_id: stellenId,
      message: anschreiben,
      shares_resume: lebenslauf,
      shares_portfolio: arbeiten,
    });
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setGesendet(true);
    } else {
      setFehler(ergebnis.error.detail);
    }
  }

  return (
    <PageShell
      title={anzeige.title}
      narrow
      lead={anzeige.location !== "" ? anzeige.location : undefined}
    >
      <Box sx={{ mb: 2 }}>{zurueck}</Box>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Requirements skills={anzeige.skills} mine={meineFaehigkeiten} />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void absenden();
            }}
          >
            <TextField
              label={t("bewerbung.anschreiben")}
              helperText={t("bewerbung.anschreibenHinweis")}
              multiline
              minRows={4}
              value={anschreiben}
              onChange={(e) => setAnschreiben(e.target.value)}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
              sx={{ mb: 2 }}
            />

            {/* Das Profil steht bewusst NICHT zur Wahl. */}
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              {t("bewerbung.profilImmerDabei")}
            </Typography>

            <Box sx={{ display: "flex", flexDirection: "column", mb: 2 }}>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={lebenslauf}
                    onChange={(e) => setLebenslauf(e.target.checked)}
                  />
                }
                label={t("bewerbung.lebenslauf")}
              />
              <FormControlLabel
                control={
                  <Checkbox
                    checked={arbeiten}
                    onChange={(e) => setArbeiten(e.target.checked)}
                  />
                }
                label={t("bewerbung.meineArbeiten")}
              />
            </Box>

            {fehler !== null ? (
              <Alert severity="error" sx={{ mb: 2 }}>
                {fehler}
              </Alert>
            ) : null}

            <Button type="submit" variant="contained" disabled={laeuft}>
              {laeuft ? t("bewerbung.absendenLaeuft") : t("bewerbung.absenden")}
            </Button>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
