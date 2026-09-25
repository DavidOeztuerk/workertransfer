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
      <PageShell title={t("assessments.companyTitle")} narrow>
        <LoadingBlock label={t("assessments.loading")} />
      </PageShell>
    );
  }

  if (!signedIn || !fuerFirma) {
    return (
      <PageShell title={t("assessments.companyTitle")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("assessments.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("assessments.companyTitle")} narrow>
      <Typography sx={{ mb: 3 }}>{t("assessments.companyLead")}</Typography>

      <Stellformular fertig={() => vorgaenge.reload()} />

      {vorgaenge.pending && <LoadingBlock label={t("assessments.loading")} />}

      {vorgaenge.data?.ok === false && (
        <Alert severity="warning" sx={{ mt: 3 }}>
          {vorgaenge.data.reason === "no-company"
            ? t("assessments.companyOnly")
            : t("errors.ledgerSilent")}
        </Alert>
      )}

      {vorgaenge.data?.ok === true && vorgaenge.data.items.length === 0 && (
        <EmptyBlock title={t("assessments.companyEmpty")} hint={t("assessments.companyEmptyHint")} />
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
          {t("assessments.setTask")}
        </Typography>

        {fehler !== null && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {fehler}
          </Alert>
        )}

        <Stack spacing={2}>
          <TextField
            label={t("assessments.recipient")}
            helperText={t("assessments.recipientHint")}
            value={wer}
            onChange={(event) => setWer(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("assessments.headline")}
            value={titel}
            onChange={(event) => setTitel(event.target.value)}
            fullWidth
          />
          <TextField
            label={t("assessments.task")}
            value={aufgabe}
            onChange={(event) => setAufgabe(event.target.value)}
            multiline
            minRows={4}
            fullWidth
          />
          <TextField
            label={t("assessments.effortField")}
            // Required, with an upper bound, and the hint says WHY — not "please
            // fill in" but what eight hours mean.
            helperText={t("assessments.effortHint", { max: HOECHSTER_UMFANG })}
            value={stunden}
            onChange={(event) => setStunden(event.target.value)}
            type="number"
            slotProps={{
              htmlInput: { min: KLEINSTER_UMFANG, max: HOECHSTER_UMFANG, step: 1 },
            }}
            fullWidth
          />
          <TextField
            label={t("assessments.deadlineField")}
            helperText={t("assessments.deadlineHint", { tage: MINDESTFRIST_TAGE })}
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
              {t("assessments.setTaskButton")}
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
          <Chip label={t(`assessments.state_${vorgang.state}`)} size="small" />
          <Chip
            label={t("assessments.effort", { stunden: vorgang.hours })}
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
            <Typography variant="subtitle2">{t("assessments.submitted")}</Typography>
            {vorgang.submission.text.length > 0 && (
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>
                {vorgang.submission.text}
              </Typography>
            )}
            {vorgang.submission.url !== null && (
              <Link
                // A reference, never embedded content: a stranger chose this
                // address, and neither the server nor the page fetches it.
                href={vorgang.submission.url}
                rel="noreferrer noopener"
                target="_blank"
              >
                {vorgang.submission.url}
              </Link>
            )}
          </>
        )}

        {vorgang.evaluation !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">
              {t(`assessments.outcome_${vorgang.evaluation.outcome}`)}
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
                <FormLabel>{t("assessments.outcomeQuestion")}</FormLabel>
                <RadioGroup
                  row
                  value={ausgang}
                  onChange={(event) => setAusgang(event.target.value as Ausgang)}
                >
                  <FormControlLabel
                    value="accepted"
                    control={<Radio />}
                    label={t("assessments.outcome_accepted")}
                  />
                  <FormControlLabel
                    value="rejected"
                    control={<Radio />}
                    label={t("assessments.outcome_rejected")}
                  />
                </RadioGroup>
              </FormControl>

              <TextField
                // ONE field, and it is the same one the person reads. No
                // internal note beside it, no mark, no scale (ADR-0042 §1).
                label={t("assessments.feedbackField")}
                helperText={t("assessments.feedbackHint")}
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
                  {t("assessments.sendFeedback")}
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
