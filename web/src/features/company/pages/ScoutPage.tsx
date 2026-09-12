import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { TrefferKarte } from "../components/TrefferKarte";
import { useHandelnder } from "../lib/session";
import { useAsync, useSeiten } from "../lib/useAsync";
import {
  OHNE_FILTER,
  type Suchfilter,
  type Treffer,
  sucheKandidaten,
} from "../api/scout";
import {
  type MarketRequest,
  listCompanyMarketRequests,
} from "../../work/api/market";
import { listOwnJobs } from "../../work/api/jobs";

/**
 * <c>/scout</c> — wer sein Profil freigegeben hat, mit Häkchen und Belegen.
 *
 * <strong>Nachfolger von <c>/candidates</c></strong> (ADR-0036). Der alte Pfad
 * ist am 11.09.2026 gefallen; zwei Suchen nebeneinander wären zwei Wahrheiten
 * gewesen.
 *
 * <strong>Die Worte sind ein ODER.</strong> Wer drei eingibt, sucht nicht
 * jemanden, der alle drei kann — sondern sieht an den Häkchen, wer welches
 * genannt hat. Unter UND wäre jedes Häkchen gesetzt und die Liste nutzlos.
 *
 * <strong>Hier steht ausschliesslich, wer freigegeben hat.</strong> Wer die
 * Freigabe zurückzieht, verschwindet beim nächsten Laden — ohne Umweg über uns,
 * weil die Liste den Ledger bei jeder Anfrage neu fragt.
 *
 * <strong>Keine Gesamtzahl.</strong> Sie verriete, wie viele Profile es gibt,
 * die gerade NICHT freigegeben sind — eine Auskunft über Menschen, die
 * ausdrücklich nichts gesagt haben.
 *
 * <strong>„Leer" heisst nicht dasselbe wie „schweigt".</strong> Antwortet der
 * Ledger nicht, steht hier ein Fehler und keine leere Liste: eine leere Liste
 * wäre die Behauptung, niemand habe freigegeben, und das weiss in dem Moment
 * niemand.
 */
export function ScoutPage() {
  const { t } = useTranslation();
  const { fuerFirma } = useHandelnder();

  const [draft, setEntwurf] = useState({
    skills: "",
    location: "",
    remoteOnly: false,
  });
  const [filter, setFilter] = useState<Suchfilter>(OHNE_FILTER);

  // Die Stelle steht NEBEN den Filtern, nicht unter ihnen: sie nimmt keinen
  // Treffer weg, sie erklaert jeden (ADR-0041). Deshalb wirkt sie auch sofort
  // und wartet nicht auf „Suchen" — sie aendert die Menge ja nicht.
  const [stelle, setStelle] = useState("");

  const hatFilter =
    filter.skills.length > 0 || filter.location !== "" || filter.remoteOnly;

  const eigeneStellen = useAsync(
    (signal) => listOwnJobs(signal),
    [fuerFirma],
    fuerFirma,
  );

  const stellen =
    eigeneStellen.data?.ok === true ? eigeneStellen.data.jobs : [];

  const seiten = useSeiten<Treffer, string>(
    (cursor, signal) =>
      sucheKandidaten(cursor, filter, signal, stelle).then((result) =>
        result.ok
          ? {
              ok: true as const,
              items: result.items,
              nextCursor: result.nextCursor,
            }
          : { ok: false as const, fehler: result.error.detail },
      ),
    JSON.stringify({ filter, stelle }),
    fuerFirma,
  );

  const anfragen = useAsync(
    (signal) => listCompanyMarketRequests(signal),
    [fuerFirma],
    fuerFirma,
  );

  const marktanfragen = new Map<string, MarketRequest>(
    anfragen.data?.ok === true
      ? anfragen.data.requests.map((anfrage: MarketRequest) => [
          anfrage.subject_id,
          anfrage,
        ])
      : [],
  );

  // Ohne aktives Unternehmen wird gar nicht erst gefragt. Der Server antwortete
  // 403 — aber eine Anfrage, deren Ergebnis feststeht, ist nur Rauschen in den
  // Protokollen des Ledgers.
  if (!fuerFirma) {
    return (
      <PageShell title={t("kandidaten.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("kandidaten.nurFirma")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell
      title={t("kandidaten.titel")}
      lead={t("kandidaten.lead")}
    >
      {seiten.fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {seiten.fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              setFilter({
                skills: draft.skills
                  .split(",")
                  .map((entry) => entry.trim())
                  .filter((entry) => entry !== ""),
                location: draft.location,
                remoteOnly: draft.remoteOnly,
              });
            }}
            sx={{
              display: "flex",
              flexDirection: "column",
              gap: 2,
              alignItems: "flex-start",
            }}
          >
            <TextField
              label={t("kandidaten.faehigkeiten")}
              helperText={t("kandidaten.faehigkeitenHinweis")}
              placeholder={t("kandidaten.faehigkeitenBeispiel")}
              value={draft.skills}
              onChange={(e) =>
                setEntwurf((now) => ({ ...now, skills: e.target.value }))
              }
            />
            <TextField
              label={t("kandidaten.ort")}
              helperText={t("kandidaten.ortHinweis")}
              placeholder={t("kandidaten.ortBeispiel")}
              value={draft.location}
              onChange={(e) =>
                setEntwurf((now) => ({ ...now, location: e.target.value }))
              }
            />

            {/* Ein Kästchen, kein Schalter: der Filter gilt mit dem Absenden,
                und genau das versprechen die beiden Elemente unterschiedlich. */}
            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={draft.remoteOnly}
                    onChange={(e) =>
                      setEntwurf((now) => ({
                        ...now,
                        remoteOnly: e.target.checked,
                      }))
                    }
                  />
                }
                label={t("kandidaten.nurRemote")}
              />
              <Typography variant="body2" color="text.secondary">
                {t("kandidaten.nurRemoteHinweis")}
              </Typography>
            </Box>

            {/* ADR-0041: Anwesenheit und Ort der Stelle treffen auf die
                Pendelbereitschaft der Person — daraus wird ein Häkchen je
                Treffer. KEIN Filter: die Liste wird dadurch nicht kürzer. */}
            {stellen.length > 0 ? (
              <TextField
                select
                label={t("kandidaten.gegenStelle")}
                helperText={t("kandidaten.gegenStelleHinweis")}
                value={stelle}
                onChange={(event) => setStelle(event.target.value)}
                >
                <MenuItem value="">{t("kandidaten.ohneStelle")}</MenuItem>
                {stellen.map((anzeige) => (
                  <MenuItem key={anzeige.id} value={anzeige.id}>
                    {anzeige.title}
                  </MenuItem>
                ))}
              </TextField>
            ) : null}

            <Box sx={{ display: "flex", gap: 1.5 }}>
              <Button type="submit" variant="contained">
                {t("kandidaten.suchen")}
              </Button>
              {hatFilter ? (
                <Button
                  variant="text"
                  onClick={() => {
                    setEntwurf({ skills: "", location: "", remoteOnly: false });
                    setFilter(OHNE_FILTER);
                  }}
                >
                  {t("kandidaten.filterZuruecksetzen")}
                </Button>
              ) : null}
            </Box>
          </Box>
        </CardContent>
      </Card>

      {seiten.pending && seiten.items.length === 0 ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("kandidaten.laden")} />
          </CardContent>
        </Card>
      ) : null}

      {seiten.items.length > 0 ? (
        <Box
          component="ul"
          sx={{
            display: "flex",
            flexDirection: "column",
            gap: 2,
            listStyle: "none",
            p: 0,
            m: 0,
          }}
        >
          {seiten.items.map((treffer: Treffer) => (
            <TrefferKarte
              key={treffer.subject_id}
              treffer={treffer}
              marketRequest={marktanfragen.get(treffer.subject_id)}
              onGeaendert={() => anfragen.reload()}
            />
          ))}
        </Box>
      ) : null}

      {!seiten.pending &&
      seiten.fehler === null &&
      seiten.items.length === 0 ? (
        hatFilter ? (
          <EmptyBlock title={t("kandidaten.leerMitFilter")} />
        ) : (
          <EmptyBlock
            title={t("kandidaten.leerTitel")}
            hint={t("kandidaten.leerHinweis")}
          />
        )
      ) : null}

      {/* Bewusst keine Gesamtzahl. */}
      {seiten.mehr ? (
        <Box sx={{ mt: 3 }}>
          <Button
            variant="outlined"
            onClick={seiten.weiter}
            disabled={seiten.pending}
          >
            {seiten.pending ? t("allgemein.laden") : t("kandidaten.mehrLaden")}
          </Button>
        </Box>
      ) : null}
    </PageShell>
  );
}
