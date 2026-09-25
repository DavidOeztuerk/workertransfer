import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { type Vorgang, ladeMeineVorgaenge, reicheEin } from "../api/arbeitsproben";

/**
 * `/assessments` — die Arbeitsproben, die einem Menschen gestellt wurden.
 *
 * <strong>Der Umfang steht vorne, und er steht gross.</strong> Eine
 * Arbeitsprobe ohne genannten Umfang ist eine Aufgabe, deren Preis man erst
 * kennt, wenn man ihn bezahlt hat (ADR-0042 §3). Deshalb ist „4 Stunden" auf
 * dieser Seite keine Randnotiz, sondern steht neben der Überschrift.
 *
 * <strong>Es gibt keinen Ablehnen-Knopf, und das ist Absicht.</strong> Wer
 * nicht will, tut nichts; die Frist läuft ab. Ein höflicher Absageknopf wäre
 * freundlicher zum Unternehmen und erzeugte den Vermerk, den ADR-0042 §3
 * verbietet — „hat dreimal abgelehnt" ist eine Tatsache über einen Menschen,
 * und sie entstünde aus lauter einzelnen berechtigten Klicks. Der Satz im Kopf
 * sagt das ausdrücklich, damit niemand rät, ob ein fehlender Knopf ein Versehen
 * ist.
 *
 * <strong>Und die Bewertung steht hier, immer.</strong> Auch bei einer Absage,
 * auch nachdem jemand seine Sichtbarkeit widerrufen hat: diese Liste fragt den
 * Ledger nicht. Eine Beurteilung, die der Beurteilte nie liest, ist genau das,
 * was diese Plattform nicht baut.
 */
export function AssessmentsPage() {
  const { t } = useTranslation();
  const { signedIn, laedt, subjectId } = useHandelnder();

  const vorgaenge = useAsync((signal) => ladeMeineVorgaenge(signal), [subjectId], signedIn);

  if (laedt) {
    return (
      <PageShell title={t("assessments.title")} narrow>
        <LoadingBlock label={t("assessments.loading")} />
      </PageShell>
    );
  }

  if (!signedIn) {
    return (
      <PageShell title={t("assessments.title")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("errors.signIn")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("assessments.title")} narrow>
      <Typography sx={{ mb: 1 }}>{t("assessments.lead")}</Typography>

      <Alert
        // Not dismissible and no tooltip: whoever does not want to do a task
        // should read this without having to look for it.
        severity="info"
        sx={{ mb: 3 }}
      >
        {t("assessments.noDecline")}
      </Alert>

      {vorgaenge.pending && <LoadingBlock label={t("assessments.loading")} />}

      {vorgaenge.data?.ok === false && (
        <Alert severity="warning">{t("errors.ledgerSilent")}</Alert>
      )}

      {vorgaenge.data?.ok === true && vorgaenge.data.items.length === 0 && (
        <EmptyBlock title={t("assessments.empty")} hint={t("assessments.emptyHint")} />
      )}

      <Stack spacing={2}>
        {vorgaenge.data?.ok === true &&
          vorgaenge.data.items.map((vorgang) => (
            <MeineKarte
              key={vorgang.id}
              vorgang={vorgang}
              fertig={() => vorgaenge.reload()}
            />
          ))}
      </Stack>
    </PageShell>
  );
}

/** Eine Karte je Vorgang — Aufgabe, Umfang, eigene Lösung, Rückmeldung. */
function MeineKarte({ vorgang, fertig }: { vorgang: Vorgang; fertig: () => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState("");
  const [adresse, setAdresse] = useState("");
  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);

  async function abgeben() {
    setLaeuft(true);
    const ergebnis = await reicheEin(vorgang.id, text, adresse.trim() || null);
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      fertig();
      return;
    }

    setFehler(ergebnis.error.detail || ergebnis.error.title);
  }

  const offen = vorgang.state === "set";
  const leer = text.trim().length === 0 && adresse.trim().length === 0;

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap", gap: 1 }}>
          <Chip label={t(`assessments.state_${vorgang.state}`)} size="small" />
          <Chip
            // The effort, BESIDE the state and not in the small print.
            label={t("assessments.effort", { stunden: vorgang.hours })}
            size="small"
            color="primary"
            variant="outlined"
          />
          <Chip
            label={t("assessments.deadline", { datum: datum(vorgang.due_at) })}
            size="small"
            variant="outlined"
          />
        </Stack>

        <Typography variant="h3" sx={{ mb: 1 }}>
          {vorgang.title}
        </Typography>

        <Typography sx={{ mb: 2, whiteSpace: "pre-wrap" }}>{vorgang.task}</Typography>

        {vorgang.submission !== undefined && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2">{t("assessments.yourSolution")}</Typography>
            {vorgang.submission.text.length > 0 && (
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>
                {vorgang.submission.text}
              </Typography>
            )}
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
            <Alert
              // THE EVALUATION. It stands here as soon as it exists — on an
              // acceptance as on a rejection, and regardless of what is released
              // right now (ADR-0042 §2).
              severity={vorgang.evaluation.outcome === "accepted" ? "success" : "info"}
              sx={{ mb: 1 }}
            >
              {t(`assessments.outcome_${vorgang.evaluation.outcome}`)}
            </Alert>
            <Typography variant="subtitle2">{t("assessments.feedback")}</Typography>
            <Typography sx={{ whiteSpace: "pre-wrap" }}>{vorgang.evaluation.text}</Typography>
          </>
        )}

        {offen && (
          <>
            <Divider sx={{ my: 2 }} />

            {fehler !== null && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {fehler}
              </Alert>
            )}

            <Stack spacing={2}>
              <TextField
                label={t("assessments.solutionText")}
                helperText={t("assessments.solutionHint")}
                value={text}
                onChange={(event) => setText(event.target.value)}
                multiline
                minRows={3}
                fullWidth
              />
              <TextField
                label={t("assessments.solutionLink")}
                helperText={t("assessments.solutionLinkHint")}
                value={adresse}
                onChange={(event) => setAdresse(event.target.value)}
                fullWidth
              />
              <Stack direction="row">
                <Button
                  variant="contained"
                  disabled={laeuft || leer}
                  onClick={() => void abgeben()}
                >
                  {t("assessments.submit")}
                </Button>
              </Stack>
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** Ein Datum, wie es dasteht. Die Sprache kommt aus der Sitzung, nicht von hier. */
function datum(roh: string): string {
  return new Date(roh).toLocaleDateString();
}

export default AssessmentsPage;
