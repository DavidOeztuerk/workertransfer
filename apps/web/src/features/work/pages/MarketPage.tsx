import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
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

const WAHLEN: { wert: Availability; label: string; hinweis: string }[] = [
  { wert: "open", label: "Ich suche aktiv", hinweis: "Unternehmen mit Freigabe dürfen zugehen." },
  {
    wert: "listening",
    label: "Ich höre zu",
    hinweis: "Ich suche nicht, bin aber für ein gutes Angebot ansprechbar.",
  },
  {
    wert: "unavailable",
    label: "Gerade nicht",
    hinweis: "Auch mit Freigabe darf mich niemand ansprechen.",
  },
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
  const { angemeldet, subjectId } = useHandelnder();

  const stand = useAsync((signal) => getMyMarketStatus(signal), [subjectId], angemeldet);
  const anfragen = useAsync((signal) => listMyMarketRequests(signal), [subjectId], angemeldet);

  const [verfuegbarkeit, setVerfuegbarkeit] = useState<Availability>("unavailable");
  const [beschaeftigt, setBeschaeftigt] = useState(false);
  const [notiz, setNotiz] = useState("");
  const [gespeichert, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);
  const [uebernommen, setUebernommen] = useState(false);

  // Der geladene Stand fuellt das Formular GENAU EINMAL. Liefe das bei jeder
  // Antwort, ueberschriebe ein Neuladen die gerade getippte Notiz — und ein
  // Speichern schriebe sie zurueck, ohne dass jemand es merkt.
  useEffect(() => {
    if (uebernommen || !stand.data?.ok) return;
    setVerfuegbarkeit(stand.data.status.availability);
    setBeschaeftigt(stand.data.status.employed);
    setNotiz(stand.data.status.note);
    setUebernommen(true);
  }, [uebernommen, stand.data]);

  if (!angemeldet) {
    return (
      <PageShell title="Mein Marktstatus" narrow>
        <Card>
          <CardContent>
            <Typography>
              Bitte{" "}
              <Button component={RouterLink} to="/login" variant="text" size="small">
                anmelden
              </Button>
              , um deinen Marktstatus zu setzen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function speichern() {
    setLaeuft(true);
    const ergebnis = await saveMyMarketStatus({
      availability: verfuegbarkeit,
      employed: beschaeftigt,
      note: notiz,
    });
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setGespeichert(true);
      stand.reload();
    } else {
      setFehler(ergebnis.error.detail);
      setGespeichert(false);
    }
  }

  async function beantworten(id: string, erteilen: boolean) {
    setLaeuft(true);
    const ergebnis = await answerMarketRequest(id, erteilen);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    anfragen.reload();
  }

  async function zurueckziehen(id: string) {
    setLaeuft(true);
    const ergebnis = await revokeMarketAccess(id);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    anfragen.reload();
  }

  const liste = anfragen.data?.ok ? anfragen.data.requests : [];

  return (
    <PageShell
      title="Mein Marktstatus"
      narrow
      lead={
        "Ob du ansprechbar bist, sieht nur, wem du es freigegeben hast — Unternehmen für "
        + "Unternehmen, jedes einzeln. Es gibt hier bewusst kein „für alle“: dass jemand wechseln "
        + "will, ist die heikelste Angabe auf dieser Plattform."
      }
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Anfragen
          </Typography>

          {anfragen.pending ? <LoadingBlock label="Anfragen werden geladen…" /> : null}

          {anfragen.data !== null && !anfragen.data.ok ? (
            <Alert severity="error">{anfragen.data.error.detail}</Alert>
          ) : null}

          {anfragen.data?.ok && liste.length === 0 ? (
            <EmptyBlock title="Bislang hat niemand gefragt." />
          ) : null}

          {liste.length > 0 ? (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
              {liste.map((anfrage: MarketRequest) => (
                <Anfragezeile
                  key={anfrage.id}
                  anfrage={anfrage}
                  gesperrt={laeuft}
                  onAntwort={(erteilen) => void beantworten(anfrage.id, erteilen)}
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
            Bin ich ansprechbar?
          </Typography>

          {stand.pending ? <LoadingBlock label="Marktstatus wird geladen…" /> : null}

          {gespeichert ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              Marktstatus gespeichert.
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void speichern();
            }}
          >
            <FormControl sx={{ mb: 2 }}>
              <FormLabel id="markt-status">Status</FormLabel>
              {/* Ein RadioGroup mit gemeinsamem `name`: erst dadurch bewegen die
                  Pfeiltasten den Fokus innerhalb der Gruppe. */}
              <RadioGroup
                aria-labelledby="markt-status"
                name="availability"
                value={verfuegbarkeit}
                onChange={(ereignis) => {
                  setGespeichert(false);
                  setVerfuegbarkeit(ereignis.target.value as Availability);
                }}
              >
                {WAHLEN.map((wahl) => (
                  <Box key={wahl.wert} sx={{ mb: 0.5 }}>
                    <FormControlLabel value={wahl.wert} control={<Radio />} label={wahl.label} />
                    <Typography variant="body2" color="text.secondary" sx={{ ml: 4 }}>
                      {wahl.hinweis}
                    </Typography>
                  </Box>
                ))}
              </RadioGroup>
            </FormControl>

            <FormControlLabel
              control={
                <Switch
                  checked={beschaeftigt}
                  onChange={(ereignis) => {
                    setGespeichert(false);
                    setBeschaeftigt(ereignis.target.checked);
                  }}
                  slotProps={{ input: { role: "switch" } }}
                />
              }
              label="Ich arbeite gerade irgendwo"
              sx={{ mb: 2, display: "block" }}
            />

            <TextField
              label="Notiz"
              value={notiz}
              onChange={(ereignis) => {
                setGespeichert(false);
                setNotiz(ereignis.target.value);
              }}
              helperText="Was du suchst, in eigenen Worten. Sieht nur, wer freigeschaltet ist."
              multiline
              minRows={3}
              sx={{ mb: 2 }}
            />

            <Button type="submit" variant="contained" disabled={laeuft}>
              {laeuft ? "Wird gespeichert…" : "Speichern"}
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
  gesperrt,
  onAntwort,
  onZurueck,
}: {
  anfrage: MarketRequest;
  gesperrt: boolean;
  onAntwort: (erteilen: boolean) => void;
  onZurueck: () => void;
}) {
  const offen = anfrage.status === "PENDING";
  const haeltZugriff = anfrage.status === "GRANTED" && anfrage.active === true;

  const stand = offen
    ? "Noch nicht beantwortet"
    : anfrage.status === "DECLINED"
      ? "Abgelehnt — dieses Unternehmen kann nicht erneut fragen"
      : haeltZugriff
        ? "Freigegeben — das Unternehmen sieht deinen Marktstatus"
        : "Freigabe zurückgezogen";

  return (
    <Card variant="outlined">
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
            Ein Unternehmen möchte sehen, ob du ansprechbar bist
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {stand}
          </Typography>
        </Box>

        <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
          {offen ? (
            <>
              <Button variant="contained" size="small" onClick={() => onAntwort(true)} disabled={gesperrt}>
                Freigeben
              </Button>
              <Button variant="text" size="small" onClick={() => onAntwort(false)} disabled={gesperrt}>
                Ablehnen
              </Button>
            </>
          ) : null}
          {haeltZugriff ? (
            <Button variant="text" size="small" onClick={onZurueck} disabled={gesperrt}>
              Zurückziehen
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
