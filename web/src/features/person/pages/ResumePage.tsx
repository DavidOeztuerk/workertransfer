import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Divider from "@mui/material/Divider";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import {
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import {
  type Lebenslaufanfrage,
  type Station,
  beantworten,
  ladeMeinen,
  ladeMeineAnfragen,
  speichereMeinen,
  zuruecknehmen,
} from "../api/resume";

const LEERE_STATION: Station = {
  employer: "",
  title: "",
  started_on: "",
  ended_on: null,
  description: "",
};

/** Leer heisst „läuft noch", nicht „unbekannt" — deshalb `null` und nicht `""`. */
function endeNormalisieren(wert: string): string | null {
  const getrimmt = wert.trim();
  return getrimmt === "" ? null : getrimmt;
}

/**
 * <c>/resume</c> — der Lebenslauf und die Anfragen darauf.
 *
 * <strong>Kein öffentlicher Schalter, und das ist der ganze Entwurf.</strong>
 * Ein Profil ist ein Aushang; ein Lebenslauf nennt echte Arbeitgeber mit Daten
 * — genau das, was ein <em>jetziger</em> Arbeitgeber nicht sehen darf. Eine
 * Firma fragt, die Person antwortet, und die Freigabe gilt für diese eine Firma.
 *
 * <strong>Die Anfragen stehen OBEN, vor dem Formular.</strong> Wer eine Anfrage
 * erwartet, soll sie sehen, ohne an seinem eigenen Lebenslauf vorbeizuscrollen.
 */
export function ResumePage() {
  const status = useAppSelector((state) => state.auth.status);
  const sitzung = useAppSelector((state) => state.auth.session);

  const lebenslauf = useAsync(
    (signal) => ladeMeinen(signal),
    [sitzung?.userId],
    sitzung !== null,
  );
  const anfragen = useAsync(
    (signal) => ladeMeineAnfragen(signal),
    [sitzung?.userId],
    sitzung !== null,
  );

  const [zeilen, setZeilen] = useState<Station[]>([]);
  const [gespeichert, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  // Das Formular folgt dem geladenen Stand — aber nur, wenn wirklich einer da
  // ist. Bei einem Ladefehler bleibt es leer UND die Seite sagt warum; ein
  // stilles leeres Formular waere die Einladung, den Lebenslauf zu ueberschreiben.
  useEffect(() => {
    if (lebenslauf.wert?.ok && lebenslauf.wert.wert !== null) {
      setZeilen(
        lebenslauf.wert.wert.positions.map((station) => ({ ...station })),
      );
    }
  }, [lebenslauf.wert]);

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel="Mein Lebenslauf"
        zweck="deinen Lebenslauf zu bearbeiten"
      />
    );
  }

  function aendere(index: number, teil: Partial<Station>) {
    setZeilen((vorher) =>
      vorher.map((zeile, i) => (i === index ? { ...zeile, ...teil } : zeile)),
    );
    setGespeichert(false);
  }

  async function speichern() {
    setLaeuft(true);
    const ergebnis = await speichereMeinen({
      positions: zeilen,
      education: [],
    });
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setGespeichert(true);
      lebenslauf.setze({ ok: true, wert: ergebnis.wert });
    } else {
      setFehler(ergebnis.error.detail);
      setGespeichert(false);
    }
  }

  async function beantworteAnfrage(id: string, erteilen: boolean) {
    setLaeuft(true);
    const ergebnis = await beantworten(id, erteilen);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    anfragen.erneut();
  }

  async function nimmZurueck(id: string) {
    setLaeuft(true);
    const ergebnis = await zuruecknehmen(id);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    anfragen.erneut();
  }

  return (
    <PageShell
      title="Mein Lebenslauf"
      narrow
      lead={
        "Diesen Lebenslauf sieht niemand, bis du ihn einem Unternehmen freigibst — Unternehmen " +
        "für Unternehmen, jedes einzeln. Eine Freigabe kannst du jederzeit zurückziehen; sie " +
        "wirkt sofort."
      }
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Anfragen
          </Typography>

          {anfragen.laedt ? (
            <LoadingBlock label="Anfragen werden geladen…" />
          ) : null}

          {anfragen.wert && !anfragen.wert.ok ? (
            <ErrorBlock error={anfragen.wert.error} />
          ) : null}

          {anfragen.wert?.ok && anfragen.wert.wert.length === 0 ? (
            <EmptyBlock title="Bislang hat niemand nach deinem Lebenslauf gefragt." />
          ) : null}

          {anfragen.wert?.ok && anfragen.wert.wert.length > 0 ? (
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
              {anfragen.wert.wert.map((anfrage) => (
                <Anfragezeile
                  key={anfrage.id}
                  anfrage={anfrage}
                  gesperrt={laeuft}
                  onAntwort={(erteilen) =>
                    void beantworteAnfrage(anfrage.id, erteilen)
                  }
                  onZurueck={() => void nimmZurueck(anfrage.id)}
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Stationen
          </Typography>

          {lebenslauf.laedt ? (
            <LoadingBlock label="Lebenslauf wird geladen…" />
          ) : null}

          {lebenslauf.wert && !lebenslauf.wert.ok ? (
            <ErrorBlock error={lebenslauf.wert.error} />
          ) : null}

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {gespeichert ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              Lebenslauf gespeichert.
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void speichern();
            }}
          >
            {zeilen.map((zeile, index) => (
              <Box key={index} sx={{ mb: 3 }}>
                <Typography variant="h3" sx={{ mb: 1.5 }}>
                  Station {index + 1}
                </Typography>
                <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                  <TextField
                    label="Arbeitgeber"
                    value={zeile.employer}
                    onChange={(e) =>
                      aendere(index, { employer: e.target.value })
                    }
                    required
                  />
                  <TextField
                    label="Position"
                    value={zeile.title}
                    onChange={(e) => aendere(index, { title: e.target.value })}
                    required
                  />
                  <TextField
                    label="Von"
                    value={zeile.started_on}
                    onChange={(e) =>
                      aendere(index, { started_on: e.target.value })
                    }
                    helperText="Monat, zum Beispiel 2020-01"
                    required
                  />
                  <TextField
                    label="Bis"
                    value={zeile.ended_on ?? ""}
                    onChange={(e) =>
                      aendere(index, {
                        ended_on: endeNormalisieren(e.target.value),
                      })
                    }
                    helperText="Leer lassen, wenn du noch dort bist."
                  />
                </Box>
                <Divider sx={{ mt: 3 }} />
              </Box>
            ))}

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button
                variant="outlined"
                onClick={() =>
                  setZeilen((vorher) => [...vorher, { ...LEERE_STATION }])
                }
              >
                Station hinzufügen
              </Button>
              <Button type="submit" variant="contained" disabled={laeuft}>
                {laeuft ? "Wird gespeichert…" : "Speichern"}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
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
  gesperrt,
  onAntwort,
  onZurueck,
}: {
  anfrage: Lebenslaufanfrage;
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
        ? "Freigegeben — das Unternehmen sieht deinen Lebenslauf"
        : "Freigabe zurückgezogen";

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
            Ein Unternehmen fragt nach deinem Lebenslauf
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {stand}
          </Typography>
        </Box>

        <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
          {offen ? (
            <>
              <Button
                variant="contained"
                size="small"
                onClick={() => onAntwort(true)}
                disabled={gesperrt}
              >
                Freigeben
              </Button>
              <Button
                variant="text"
                size="small"
                onClick={() => onAntwort(false)}
                disabled={gesperrt}
              >
                Ablehnen
              </Button>
            </>
          ) : null}
          {haeltZugriff ? (
            <Button
              variant="text"
              size="small"
              onClick={onZurueck}
              disabled={gesperrt}
            >
              Zurückziehen
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
