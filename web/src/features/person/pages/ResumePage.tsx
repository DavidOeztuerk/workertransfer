import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
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
import { parseSkills } from "../lib/skills";
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
  technologies: [],
};

/** Leer heisst „läuft noch", nicht „unbekannt" — deshalb `null` und nicht `""`. */
function normaliseEnd(value: string): string | null {
  const getrimmt = value.trim();
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
  const { t } = useTranslation();
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

  const [rows, setZeilen] = useState<Station[]>([]);
  const [saved, setGespeichert] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  // Das Formular folgt dem geladenen Stand — aber nur, wenn wirklich einer da
  // ist. Bei einem Ladefehler bleibt es leer UND die Seite sagt warum; ein
  // stilles leeres Formular waere die Einladung, den Lebenslauf zu ueberschreiben.
  useEffect(() => {
    if (lebenslauf.value?.ok && lebenslauf.value.value !== null) {
      setZeilen(
        lebenslauf.value.value.positions.map((station) => ({ ...station })),
      );
    }
  }, [lebenslauf.value]);

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

  async function speichern() {
    setLaeuft(true);
    const result = await speichereMeinen({
      positions: rows,
      education: [],
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

  return (
    <PageShell
      title={t("lebenslauf.titel")}
      narrow
      lead={t("lebenslauf.lead")}
    >
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

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("lebenslauf.stationen")}
          </Typography>

          {lebenslauf.laedt ? (
            <LoadingBlock label={t("lebenslauf.laden")} />
          ) : null}

          {lebenslauf.value && !lebenslauf.value.ok ? (
            <ErrorBlock error={lebenslauf.value.error} />
          ) : null}

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {saved ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              {t("lebenslauf.gespeichert")}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void speichern();
            }}
          >
            {rows.map((row, index) => (
              <Box key={index} sx={{ mb: 3 }}>
                <Typography variant="h3" sx={{ mb: 1.5 }}>
                  {t("lebenslauf.station", { nummer: index + 1 })}
                </Typography>
                <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
                  <TextField
                    label={t("lebenslauf.arbeitgeber")}
                    value={row.employer}
                    onChange={(e) =>
                      change(index, { employer: e.target.value })
                    }
                    required
                  />
                  <TextField
                    label={t("lebenslauf.position")}
                    value={row.title}
                    onChange={(e) => change(index, { title: e.target.value })}
                    required
                  />
                  <TextField
                    label={t("lebenslauf.von")}
                    value={row.started_on}
                    onChange={(e) =>
                      change(index, { started_on: e.target.value })
                    }
                    helperText={t("lebenslauf.vonHinweis")}
                    required
                  />
                  <TextField
                    label={t("lebenslauf.bis")}
                    value={row.ended_on ?? ""}
                    onChange={(e) =>
                      change(index, {
                        ended_on: normaliseEnd(e.target.value),
                      })
                    }
                    helperText={t("lebenslauf.bisHinweis")}
                  />

                  {/*
                    WOMIT — der zweite Ort, an dem jemand sagen kann, was er
                    kann. Er steht hier und nicht nur im Profil, weil eine
                    Fähigkeit an einer Station etwas anderes aussagt als eine im
                    Profil: nicht „ich kann das", sondern „damit habe ich dort
                    gearbeitet". Ein Unternehmen, das den Lebenslauf lesen darf,
                    sieht den Unterschied.

                    Durchsuchbar macht das die Station nicht — dafür muss die
                    Fähigkeit ins Profil, und dorthin kommt sie mit einem Klick.
                  */}
                  <TextField
                    label={t("lebenslauf.technologien")}
                    helperText={t("lebenslauf.technologienHinweis")}
                    value={row.technologies.join(", ")}
                    onChange={(e) =>
                      change(index, {
                        technologies: parseSkills(e.target.value),
                      })
                    }
                  />
                </Box>
                <Divider sx={{ mt: 3 }} />
              </Box>
            ))}

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button
                variant="outlined"
                onClick={() =>
                  setZeilen((before) => [...before, { ...LEERE_STATION }])
                }
              >
                {t("lebenslauf.stationHinzufuegen")}
              </Button>
              <Button type="submit" variant="contained" disabled={running}>
                {running
                  ? t("allgemein.speichernLaeuft")
                  : t("allgemein.speichern")}
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
