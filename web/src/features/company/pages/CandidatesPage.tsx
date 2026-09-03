import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { CandidateCard } from "../components/CandidateCard";
import { useHandelnder } from "../lib/session";
import { useAsync, useSeiten } from "../lib/useAsync";
import {
  type CandidateFilters,
  NO_FILTERS,
  type Profile,
  listCandidates,
} from "../api/candidates";
import {
  type MarketRequest,
  listCompanyMarketRequests,
} from "../../work/api/market";

/**
 * <c>/candidates</c> — wer sein Profil freigegeben hat.
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
export function CandidatesPage() {
  const { t } = useTranslation();
  const { fuerFirma } = useHandelnder();

  const [draft, setEntwurf] = useState({
    skills: "",
    location: "",
    remoteOnly: false,
  });
  const [filter, setFilter] = useState<CandidateFilters>(NO_FILTERS);

  const hatFilter =
    filter.skills.length > 0 || filter.location !== "" || filter.remoteOnly;

  const seiten = useSeiten<Profile, string>(
    (cursor, signal) =>
      listCandidates(cursor, filter, signal).then((result) =>
        result.ok
          ? {
              ok: true as const,
              items: result.items,
              nextCursor: result.nextCursor,
            }
          : { ok: false as const, fehler: result.error.detail },
      ),
    JSON.stringify(filter),
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

            <Box sx={{ display: "flex", gap: 1.5 }}>
              <Button type="submit" variant="contained">
                {t("kandidaten.suchen")}
              </Button>
              {hatFilter ? (
                <Button
                  variant="text"
                  onClick={() => {
                    setEntwurf({ skills: "", location: "", remoteOnly: false });
                    setFilter(NO_FILTERS);
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
          {seiten.items.map((profile: Profile) => (
            <CandidateCard
              key={profile.subject_id}
              profile={profile}
              marketRequest={marktanfragen.get(profile.subject_id)}
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
