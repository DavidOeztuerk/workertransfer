import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Link from "@mui/material/Link";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Checkbox from "@mui/material/Checkbox";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Availability,
  type MarketRequest,
  answerMarketRequest,
  getMyMarketStatus,
  listMyMarketRequests,
  revokeMarketAccess,
  saveMyMarketStatus,
} from "../api/market";

/** Nur die Werte — Beschriftung und Hinweis stehen in den Katalogen. */
const WAHLEN: { value: Availability; name: string }[] = [
  { value: "open", name: "Open" },
  { value: "listening", name: "Listening" },
  { value: "unavailable", name: "Unavailable" },
];

/**
 * <c>/market</c> — ansprechbar sein, und für wen.
 *
 * <strong>Es gibt hier bewusst kein „für alle".</strong> Dass jemand wechseln
 * will, ist die heikelste Angabe auf dieser Plattform: sie kann den
 * Arbeitsplatz kosten, den die Person noch hat. Deshalb fragt ein Unternehmen
 * einzeln, und die Freigabe gilt für dieses eine.
 *
 * <strong>Die Vorgabe ist „gerade nicht".</strong> Wer die Seite öffnet und
 * speichert, ohne etwas zu wählen, darf nicht versehentlich ansprechbar werden.
 */
export function MarketPage() {
  const { t } = useTranslation();
  const { signedIn, subjectId } = useHandelnder();

  const current = useAsync(
    (signal) => getMyMarketStatus(signal),
    [subjectId],
    signedIn,
  );
  const anfragen = useAsync(
    (signal) => listMyMarketRequests(signal),
    [subjectId],
    signedIn,
  );

  const [verfuegbarkeit, setVerfuegbarkeit] =
    useState<Availability>("unavailable");
  const [busy, setBeschaeftigt] = useState(false);
  const [notiz, setNotiz] = useState("");
  const [saved, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);
  const [adopted, setUebernommen] = useState(false);

  // Der geladene Stand fuellt das Formular GENAU EINMAL. Liefe das bei jeder
  // Antwort, ueberschriebe ein Neuladen die gerade getippte Notiz — und ein
  // Speichern schriebe sie zurueck, ohne dass jemand es merkt.
  useEffect(() => {
    if (adopted || !current.data?.ok) return;
    setVerfuegbarkeit(current.data.status.availability);
    setBeschaeftigt(current.data.status.employed);
    setNotiz(current.data.status.note);
    setUebernommen(true);
  }, [adopted, current.data]);

  if (!signedIn) {
    return (
      <PageShell title={t("markt.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="markt.anmelden"
                components={{
                  1: <Link component={RouterLink} to="/login" />,
                }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function speichern() {
    setLaeuft(true);
    const result = await saveMyMarketStatus({
      availability: verfuegbarkeit,
      employed: busy,
      note: notiz,
    });
    setLaeuft(false);

    if (result.ok) {
      setFehler(null);
      setGespeichert(true);
      current.reload();
    } else {
      setFehler(result.error.detail);
      setGespeichert(false);
    }
  }

  async function beantworten(id: string, grant: boolean) {
    setLaeuft(true);
    const result = await answerMarketRequest(id, grant);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    anfragen.reload();
  }

  async function zurueckziehen(id: string) {
    setLaeuft(true);
    const result = await revokeMarketAccess(id);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    anfragen.reload();
  }

  const list = anfragen.data?.ok ? anfragen.data.requests : [];

  return (
    <PageShell
      title={t("markt.titel")}
      narrow
      lead={t("markt.lead")}
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("markt.anfragen")}
          </Typography>

          {anfragen.pending ? (
            <LoadingBlock label={t("markt.anfragenLaden")} />
          ) : null}

          {anfragen.data !== null && !anfragen.data.ok ? (
            <Alert severity="error">{anfragen.data.error.detail}</Alert>
          ) : null}

          {anfragen.data?.ok && list.length === 0 ? (
            <EmptyBlock title={t("markt.anfragenLeer")} />
          ) : null}

          {list.length > 0 ? (
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
              {list.map((anfrage: MarketRequest) => (
                <Anfragezeile
                  key={anfrage.id}
                  anfrage={anfrage}
                  locked={running}
                  onAntwort={(grant) =>
                    void beantworten(anfrage.id, grant)
                  }
                  onZurueck={() => void zurueckziehen(anfrage.id)}
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("markt.ansprechbar")}
          </Typography>

          {current.pending ? (
            <LoadingBlock label={t("markt.standLaden")} />
          ) : null}

          {saved ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              {t("markt.gespeichert")}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void speichern();
            }}
          >
            <FormControl sx={{ mb: 2 }}>
              <FormLabel id="markt-status">{t("markt.status")}</FormLabel>
              {/* Ein RadioGroup mit gemeinsamem `name`: erst dadurch bewegen die
                  Pfeiltasten den Fokus innerhalb der Gruppe. */}
              <RadioGroup
                aria-labelledby="markt-status"
                name="availability"
                value={verfuegbarkeit}
                onChange={(event) => {
                  setGespeichert(false);
                  setVerfuegbarkeit(event.target.value as Availability);
                }}
              >
                {WAHLEN.map((choice) => (
                  <Box key={choice.value} sx={{ mb: 0.5 }}>
                    <FormControlLabel
                      value={choice.value}
                      control={<Radio />}
                      label={t(`markt.wahl${choice.name}`)}
                    />
                    <Typography
                      variant="body2"
                      color="text.secondary"
                      sx={{ ml: 4 }}
                    >
                      {t(`markt.wahl${choice.name}Hinweis`)}
                    </Typography>
                  </Box>
                ))}
              </RadioGroup>
            </FormControl>

            {/*
              Ein Kasten und KEIN Schalter, und das ist dieselbe Regel wie beim
              Einwilligungsschalter — nur andersherum gelesen.

              Ein Schalter sagt: es gilt sofort. Das trifft auf den
              Einwilligungsschalter zu, und deshalb ist er dort einer. Diese
              Angabe steht in einem Formular mit „Speichern": wer sie umlegt und
              die Seite verlaesst, hat nichts geaendert. Ein Schalter versprach
              hier eine Unmittelbarkeit, die es nicht gibt — und ausgerechnet bei
              der heikelsten Angabe der Plattform.
            */}
            <FormControlLabel
              control={
                <Checkbox
                  checked={busy}
                  onChange={(event) => {
                    setGespeichert(false);
                    setBeschaeftigt(event.target.checked);
                  }}
                />
              }
              label={t("markt.beschaeftigt")}
              sx={{ mb: 2, display: "block" }}
            />

            <TextField
              label={t("markt.notiz")}
              value={notiz}
              onChange={(event) => {
                setGespeichert(false);
                setNotiz(event.target.value);
              }}
              helperText={t("markt.notizHinweis")}
              multiline
              minRows={3}
              sx={{ mb: 2 }}
            />

            <Button type="submit" variant="contained" disabled={running}>
              {running ? t("markt.speichernLaeuft") : t("markt.speichern")}
            </Button>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}

/**
 * Eine Anfrage mit ihrem Stand.
 *
 * Wie beim Lebenslauf: „freigegeben" und „hält gerade Zugriff" sind zwei Dinge.
 * Nur wer hält, bekommt „Zurückziehen" angeboten.
 */
function Anfragezeile({
  anfrage,
  locked,
  onAntwort,
  onZurueck,
}: {
  anfrage: MarketRequest;
  locked: boolean;
  onAntwort: (grant: boolean) => void;
  onZurueck: () => void;
}) {
  const { t } = useTranslation();
  const open = anfrage.status === "PENDING";
  const haeltZugriff = anfrage.status === "GRANTED" && anfrage.active === true;

  const current = open
    ? t("markt.standOffen")
    : anfrage.status === "DECLINED"
      ? t("markt.standAbgelehnt")
      : t(
          haeltZugriff
            ? "markt.standFreigegeben"
            : "markt.standZurueckgezogen",
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
            {t("markt.anfrageTitel")}
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
