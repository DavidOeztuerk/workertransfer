import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Link from "@mui/material/Link";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../../work/lib/session";
import { useAsync } from "../../work/lib/useAsync";
import {
  type Ausgang,
  HOECHSTER_UMFANG,
  KLEINSTER_UMFANG,
  MINDESTFRIST_TAGE,
  type Vorgang,
  bewerte,
  ladeFirmenvorgaenge,
  stelleAufgabe,
} from "../../work/api/arbeitsproben";

/**
 * `/company/assessments` — Arbeitsproben stellen und beantworten.
 *
 * <strong>Drei Zusagen, und alle drei sind hier sichtbar</strong> (ADR-0042):
 *
 * 1. Die Rückmeldung hat ein Textfeld und <em>keine Note</em>. Es gibt keinen
 *    Sternebalken, keine Skala und kein zweites, internes Feld — wo es zwei
 *    gäbe, stünde im zweiten die Wahrheit.
 * 2. <strong>Der Absageknopf ist gesperrt, solange kein Text dasteht.</strong>
 *    Der Server weist eine Absage ohne Begründung ohnehin ab; dieses Formular
 *    sagt es vorher, statt es jemanden ausprobieren zu lassen. Ablehnen und
 *    Begründen sind ein Schritt.
 * 3. Der Umfang ist ein Pflichtfeld mit einer Obergrenze von acht Stunden, und
 *    der Hinweis darunter sagt, warum.
 *
 * <strong>Diese Liste zeigt nur, wer gerade freigegeben hat.</strong> Wer
 * widerruft, fällt heraus — beim nächsten Aufruf, nicht beim übernächsten. Eine
 * Gesamtzahl gibt es deshalb nicht: sie verriete über die Differenz zur Länge,
 * wie viele Menschen die Sicht entzogen haben (ADR-0026).
 */
export function CompanyAssessmentsPage() {
  const { t } = useTranslation();
  const { signedIn, laedt, fuerFirma, tenantId } = useHandelnder();

  const vorgaenge = useAsync(
    (signal) => ladeFirmenvorgaenge(signal),
    [tenantId],
    signedIn && fuerFirma
  );

  if (laedt) {
    return (
      <PageShell title={t("probe.firmaTitel")} narrow>
        <LoadingBlock label={t("probe.laden")} />
      </PageShell>
    );
  }

  if (!signedIn || !fuerFirma) {
    return (
      <PageShell title={t("probe.firmaTitel")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("probe.firmaNurFirma")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("probe.firmaTitel")} narrow>
      <Typography sx={{ mb: 3 }}>{t("probe.firmaLead")}</Typography>

      <Stellformular fertig={() => vorgaenge.reload()} />

      {vorgaenge.pending && <LoadingBlock label={t("probe.laden")} />}

      {vorgaenge.data?.ok === false && (
        <Alert severity="warning" sx={{ mt: 3 }}>
          {vorgaenge.data.reason === "no-company"
            ? t("probe.firmaNurFirma")
            : t("fehler.ledgerSchweigt")}
        </Alert>
      )}

      {vorgaenge.data?.ok === true && vorgaenge.data.items.length === 0 && (
        <EmptyBlock title={t("probe.firmaLeer")} hint={t("probe.firmaLeerHinweis")} />
      )}

      <Stack spacing={2} sx={{ mt: 3 }}>
        {vorgaenge.data?.ok === true &&
          vorgaenge.data.items.map((vorgang) => (
            <Firmenkarte
              key={vorgang.id}
              vorgang={vorgang}
              fertig={() => vorgaenge.reload()}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** Eine Aufgabe stellen — Umfang und Frist sind Pflicht. */
function Stellformular({ fertig }: { fertig: () => void }) {
  const { t } = useTranslation();
  const [wer, setWer] = useState("");
  const [titel, setTitel] = useState("");
  const [aufgabe, setAufgabe] = useState("");
  const [stunden, setStunden] = useState("4");
  const [frist, setFrist] = useState("");
  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);

  async function stellen() {
    setLaeuft(true);

    const ergebnis = await stelleAufgabe({
      subject_id: wer.trim(),
      title: titel,
      task: aufgabe,
      hours: Number(stunden),
      // Der Tag endet abends: eine Frist auf Mitternacht naehme der Person
      // den letzten Tag, ohne es zu sagen.
      due_at: new Date(`${frist}T23:59:00`).toISOString(),
    });

    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setWer("");
      setTitel("");
      setAufgabe("");
      setFrist("");
      fertig();
      return;
    }

    setFehler(ergebnis.error.detail || ergebnis.error.title);
  }

  const vollstaendig =
    wer.trim().length > 0 &&
    titel.trim().length > 0 &&
    aufgabe.trim().length > 0 &&
    frist.length > 0;

  return (
    <Card>
      <CardContent>
        <Typography variant="h3" sx={{ mb: 2 }}>
          {t("probe.stellen")}
        </Typography>

        {fehler !== null && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {fehler}
          </Alert>
        )}

        <Stack spacing={2}>
          <TextField
            label={t("probe.wer")}
            helperText={t("probe.werHinweis")}
            value={wer}
            onChange={(event) => setWer(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("probe.ueberschrift")}
            value={titel}
            onChange={(event) => setTitel(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("probe.aufgabe")}
            value={aufgabe}
            onChange={(event) => setAufgabe(event.target.value)}
            multiline
            minRows={4}
            fullWidth
          />
          {/* Pflichtfeld mit Obergrenze, und der Hinweis sagt WARUM — nicht
              „bitte ausfuellen", sondern was acht Stunden bedeuten. */}
          <TextField
            label={t("probe.umfangFeld")}
            helperText={t("probe.umfangHinweis", { max: HOECHSTER_UMFANG })}
            value={stunden}
            onChange={(event) => setStunden(event.target.value)}
            type="number"
            slotProps={{
              htmlInput: { min: KLEINSTER_UMFANG, max: HOECHSTER_UMFANG, step: 1 },
            }}
            fullWidth
          />
          <TextField
            label={t("probe.fristFeld")}
            helperText={t("probe.fristHinweis", { tage: MINDESTFRIST_TAGE })}
            value={frist}
            onChange={(event) => setFrist(event.target.value)}
            type="date"
            slotProps={{ inputLabel: { shrink: true } }}
            fullWidth
          />
          <Stack direction="row">
            <Button
              variant="contained"
              disabled={laeuft || !vollstaendig}
              onClick={() => void stellen()}
            >
              {t("probe.stellenKnopf")}
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}

/** Eine Karte je Vorgang — mit dem Bewertungsfeld, sobald etwas eingereicht ist. */
function Firmenkarte({ vorgang, fertig }: { vorgang: Vorgang; fertig: () => void }) {
  const { t } = useTranslation();
  const [ausgang, setAusgang] = useState<Ausgang>("accepted");
  const [text, setText] = useState("");
  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);

  async function bewerten() {
    setLaeuft(true);
    const ergebnis = await bewerte(vorgang.id, ausgang, text);
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      fertig();
      return;
    }

    setFehler(ergebnis.error.detail || ergebnis.error.title);
  }

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap", gap: 1 }}>
          <Chip label={t(`probe.stand_${vorgang.state}`)} size="small" />
          <Chip
            label={t("probe.umfang", { stunden: vorgang.hours })}
            size="small"
            variant="outlined"
          />
        </Stack>

        <Typography variant="h3" sx={{ mb: 1 }}>
          {vorgang.title}
        </Typography>

        <Typography variant="body2" sx={{ mb: 2 }}>
          {vorgang.subject_id}
        </Typography>

        {vorgang.submission !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">{t("probe.eingereicht")}</Typography>
            {vorgang.submission.text.length > 0 && (
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>
                {vorgang.submission.text}
              </Typography>
            )}
            {/* Ein Verweis, kein eingebetteter Inhalt: die Adresse hat ein
                Fremder gewaehlt, und weder Server noch Seite rufen sie ab. */}
            {vorgang.submission.url !== null && (
              <Link href={vorgang.submission.url} rel="noreferrer noopener" target="_blank">
                {vorgang.submission.url}
              </Link>
            )}
          </>
        )}

        {vorgang.evaluation !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">
              {t(`probe.ausgang_${vorgang.evaluation.outcome}`)}
            </Typography>
            <Typography sx={{ whiteSpace: "pre-wrap" }}>{vorgang.evaluation.text}</Typography>
          </>
        )}

        {vorgang.state === "submitted" && (
          <>
            <Divider sx={{ my: 2 }} />

            {fehler !== null && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {fehler}
              </Alert>
            )}

            <Stack spacing={2}>
              <FormControl>
                <FormLabel>{t("probe.ausgangFrage")}</FormLabel>
                <RadioGroup
                  row
                  value={ausgang}
                  onChange={(event) => setAusgang(event.target.value as Ausgang)}
                >
                  <FormControlLabel
                    value="accepted"
                    control={<Radio />}
                    label={t("probe.ausgang_accepted")}
                  />
                  <FormControlLabel
                    value="rejected"
                    control={<Radio />}
                    label={t("probe.ausgang_rejected")}
                  />
                </RadioGroup>
              </FormControl>

              {/* EIN Feld, und es ist dasselbe, das die Person liest. Kein
                  internes daneben, keine Note, keine Skala. */}
              <TextField
                label={t("probe.rueckmeldungFeld")}
                helperText={t("probe.rueckmeldungHinweis")}
                value={text}
                onChange={(event) => setText(event.target.value)}
                multiline
                minRows={4}
                fullWidth
              />

              <Stack direction="row">
                <Button
                  variant="contained"
                  disabled={laeuft || text.trim().length === 0}
                  onClick={() => void bewerten()}
                >
                  {t("probe.bewerten")}
                </Button>
              </Stack>
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default CompanyAssessmentsPage;
