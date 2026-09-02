import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { CandidateCard } from "../components/CandidateCard";
import { useHandelnder } from "../lib/session";
import { useAsync, useSeiten } from "../lib/useAsync";
import { type CandidateFilters, NO_FILTERS, type Profile, listCandidates } from "../api/candidates";
import { type MarketRequest, listCompanyMarketRequests } from "../../work/api/market";

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
  const { fuerFirma } = useHandelnder();

  const [entwurf, setEntwurf] = useState({ skills: "", location: "", remoteOnly: false });
  const [filter, setFilter] = useState<CandidateFilters>(NO_FILTERS);

  const hatFilter =
    filter.skills.length > 0 || filter.location !== "" || filter.remoteOnly;

  const seiten = useSeiten<Profile, string>(
    (cursor, signal) =>
      listCandidates(cursor, filter, signal).then((ergebnis) =>
        ergebnis.ok
          ? { ok: true as const, items: ergebnis.items, nextCursor: ergebnis.nextCursor }
          : { ok: false as const, fehler: ergebnis.error.detail }
      ),
    JSON.stringify(filter),
    fuerFirma
  );

  const anfragen = useAsync(
    (signal) => listCompanyMarketRequests(signal),
    [fuerFirma],
    fuerFirma
  );

  const marktanfragen = new Map<string, MarketRequest>(
    anfragen.data?.ok === true
      ? anfragen.data.requests.map((anfrage: MarketRequest) => [anfrage.subject_id, anfrage])
      : []
  );

  // Ohne aktives Unternehmen wird gar nicht erst gefragt. Der Server antwortete
  // 403 — aber eine Anfrage, deren Ergebnis feststeht, ist nur Rauschen in den
  // Protokollen des Ledgers.
  if (!fuerFirma) {
    return (
      <PageShell title="Kandidatinnen und Kandidaten" narrow>
        <Card>
          <CardContent>
            <Typography>
              Profile sehen nur Unternehmen. Wechsle oben auf ein Unternehmen — oder lass dich von
              jemandem aus deinem Unternehmen einladen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell
      title="Kandidatinnen und Kandidaten"
      lead={
        "Hier steht ausschließlich, wer sein Profil freigegeben hat. Wer die Freigabe zurückzieht, "
        + "verschwindet beim nächsten Laden — ohne Umweg über uns."
      }
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
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              setFilter({
                skills: entwurf.skills
                  .split(",")
                  .map((eintrag) => eintrag.trim())
                  .filter((eintrag) => eintrag !== ""),
                location: entwurf.location,
                remoteOnly: entwurf.remoteOnly,
              });
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2, alignItems: "flex-start" }}
          >
            <TextField
              label="Fähigkeiten"
              helperText="Mit Komma trennen. Es zählt, wer ALLE davon kann."
              placeholder="Python, Kubernetes"
              value={entwurf.skills}
              onChange={(e) => setEntwurf((jetzt) => ({ ...jetzt, skills: e.target.value }))}
            />
            <TextField
              label="Ort"
              helperText="Ein Teil genügt."
              placeholder="Berlin"
              value={entwurf.location}
              onChange={(e) => setEntwurf((jetzt) => ({ ...jetzt, location: e.target.value }))}
            />

            {/* Ein Kästchen, kein Schalter: der Filter gilt mit dem Absenden,
                und genau das versprechen die beiden Elemente unterschiedlich. */}
            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={entwurf.remoteOnly}
                    onChange={(e) =>
                      setEntwurf((jetzt) => ({ ...jetzt, remoteOnly: e.target.checked }))
                    }
                  />
                }
                label="Nur wer Remote angegeben hat"
              />
              <Typography variant="body2" color="text.secondary">
                Ohne Haken erscheinen alle. Es gibt keinen Filter für „nur vor Ort" — ein
                fehlender Haken heißt „nicht ja gesagt", nicht „lehnt ab".
              </Typography>
            </Box>

            <Box sx={{ display: "flex", gap: 1.5 }}>
              <Button type="submit" variant="contained">
                Suchen
              </Button>
              {hatFilter ? (
                <Button
                  variant="text"
                  onClick={() => {
                    setEntwurf({ skills: "", location: "", remoteOnly: false });
                    setFilter(NO_FILTERS);
                  }}
                >
                  Filter zurücksetzen
                </Button>
              ) : null}
            </Box>
          </Box>
        </CardContent>
      </Card>

      {seiten.pending && seiten.items.length === 0 ? (
        <Card>
          <CardContent>
            <LoadingBlock label="Profile werden geladen…" />
          </CardContent>
        </Card>
      ) : null}

      {seiten.items.length > 0 ? (
        <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
          {seiten.items.map((profil: Profile) => (
            <CandidateCard
              key={profil.subject_id}
              profile={profil}
              marketRequest={marktanfragen.get(profil.subject_id)}
              onGeaendert={() => anfragen.reload()}
            />
          ))}
        </Box>
      ) : null}

      {!seiten.pending && seiten.fehler === null && seiten.items.length === 0 ? (
        hatFilter ? (
          <EmptyBlock title="Auf diese Suche passt gerade niemand, der sein Profil freigegeben hat." />
        ) : (
          <EmptyBlock
            title="Im Moment hat niemand sein Profil freigegeben."
            hint="Das ist kein Fehler — es ist die Voreinstellung."
          />
        )
      ) : null}

      {/* Bewusst keine Gesamtzahl. */}
      {seiten.mehr ? (
        <Box sx={{ mt: 3 }}>
          <Button variant="outlined" onClick={seiten.weiter} disabled={seiten.pending}>
            {seiten.pending ? "Wird geladen…" : "Mehr laden"}
          </Button>
        </Box>
      ) : null}
    </PageShell>
  );
}
