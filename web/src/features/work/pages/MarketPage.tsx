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
const WAHLEN: { value: Availability; label: string; hint: string }[] = [
  { value: "open", label: "market.choiceOpen", hint: "market.choiceOpenHint" },
  { value: "listening", label: "market.choiceListening", hint: "market.choiceListeningHint" },
  { value: "unavailable", label: "market.choiceUnavailable", hint: "market.choiceUnavailableHint" },
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
      <PageShell title={t("market.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="market.signInRequired"
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
      title={t("market.title")}
      narrow
      lead={t("market.lead")}
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("market.requests")}
          </Typography>

          {anfragen.pending ? (
            <LoadingBlock label={t("market.requestsLoading")} />
          ) : null}

          {anfragen.data !== null && !anfragen.data.ok ? (
            <Alert severity="error">{anfragen.data.error.detail}</Alert>
          ) : null}

          {anfragen.data?.ok && list.length === 0 ? (
            <EmptyBlock title={t("market.requestsEmpty")} />
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
            {t("market.approachable")}
          </Typography>

          {current.pending ? (
            <LoadingBlock label={t("market.statusLoading")} />
          ) : null}

          {saved ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              {t("market.saved")}
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
              <FormLabel id="markt-status">{t("market.status")}</FormLabel>
              <RadioGroup
                aria-labelledby="markt-status"
                // A shared `name` is what makes the arrow keys move the focus
                // inside the group.
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
                      label={t(choice.label)}
                    />
                    <Typography
                      variant="body2"
                      color="text.secondary"
                      sx={{ ml: 4 }}
                    >
                      {t(choice.hint)}
                    </Typography>
                  </Box>
                ))}
              </RadioGroup>
            </FormControl>

            <FormControlLabel
              control={
                <Checkbox
                  // A box and NOT a switch — the consent-switch rule read
                  // backwards. A switch says: it holds at once. This field sits
                  // in a form with "Save", so flipping it and leaving the page
                  // changes nothing; a switch would promise an immediacy that
                  // does not exist, at the platform's most delicate field.
                  checked={busy}
                  onChange={(event) => {
                    setGespeichert(false);
                    setBeschaeftigt(event.target.checked);
                  }}
                />
              }
              label={t("market.employed")}
              sx={{ mb: 2, display: "block" }}
            />

            <TextField
              label={t("market.note")}
              value={notiz}
              onChange={(event) => {
                setGespeichert(false);
                setNotiz(event.target.value);
              }}
              helperText={t("market.noteHint")}
              multiline
              minRows={3}
              sx={{ mb: 2 }}
            />

            <Button type="submit" variant="contained" disabled={running}>
              {running ? t("market.saving") : t("market.save")}
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
    ? t("market.statusOpen")
    : anfrage.status === "DECLINED"
      ? t("market.statusDeclined")
      : t(
          haeltZugriff
            ? "market.statusReleased"
            : "market.statusWithdrawn",
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
            {t("market.requestTitle")}
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
