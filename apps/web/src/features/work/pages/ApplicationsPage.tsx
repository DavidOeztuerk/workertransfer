import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type Application,
  type ApplicationStatus,
  listMyApplications,
  withdrawApplication,
} from "../api/applications";

const STAND: Record<ApplicationStatus, string> = {
  submitted: "Abgeschickt",
  reviewing: "Wird gelesen",
  rejected: "Abgelehnt",
  withdrawn: "Zurückgezogen — deine Daten sind wieder zu",
  hired: "Zusage",
};

/** Läuft die Bewerbung noch — also sieht das Unternehmen gerade etwas? */
const laeuft = (stand: ApplicationStatus) => stand === "submitted" || stand === "reviewing";

/**
 * Was mit dieser Bewerbung geöffnet wurde.
 *
 * Das Profil ist immer dabei — ohne es gäbe es nichts zu lesen. Lebenslauf und
 * Arbeiten nur, wenn die Person sie beim Bewerben ausdrücklich mitgegeben hat.
 */
function freigegeben(bewerbung: Application): string {
  const teile = ["Profil"];
  if (bewerbung.shares_resume) teile.push("Lebenslauf");
  if (bewerbung.shares_portfolio) teile.push("Arbeiten");
  return teile.join(", ");
}

/**
 * <c>/applications</c> — die eigenen Bewerbungen.
 *
 * <strong>Eine Bewerbung öffnet Daten, und die Seite sagt welche.</strong>
 * Solange sie läuft, sieht das Unternehmen das Profil und was sonst freigegeben
 * wurde; wird sie zurückgezogen, ist der Zugriff sofort zu. Der Vorgang bleibt
 * beim Unternehmen als das stehen, was er war — zurückziehen löscht keine
 * Geschichte, es schliesst nur den Zugang.
 *
 * <strong>Nur laufende bekommen „Zurückziehen".</strong> Bei einer abgelehnten
 * oder bereits zurückgezogenen wäre der Knopf eine Handlung ohne Wirkung.
 */
export function ApplicationsPage() {
  const { angemeldet, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuftGerade, setLaeuftGerade] = useState(false);

  const bewerbungen = useAsync(
    (signal) => listMyApplications(signal),
    [subjectId],
    angemeldet
  );

  if (!angemeldet) {
    return (
      <PageShell title="Meine Bewerbungen" narrow>
        <Card>
          <CardContent>
            <Typography>
              Bitte{" "}
              <Button component={RouterLink} to="/login" variant="text" size="small">
                anmelden
              </Button>
              , um deine Bewerbungen zu sehen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function zurueckziehen(id: string) {
    setLaeuftGerade(true);
    const ergebnis = await withdrawApplication(id);
    setLaeuftGerade(false);

    setFehler(ergebnis.ok ? null : ergebnis.error.detail);
    bewerbungen.reload();
  }

  const ergebnis = bewerbungen.data;
  const liste = ergebnis?.ok ? ergebnis.applications : [];

  return (
    <PageShell
      title="Meine Bewerbungen"
      narrow
      lead={
        "Solange eine Bewerbung läuft, sieht das Unternehmen dein Profil — und was du sonst "
        + "freigegeben hast. Ziehst du sie zurück, ist der Zugriff sofort zu; der Vorgang bleibt "
        + "beim Unternehmen als das stehen, was er war."
      }
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card>
        <CardContent>
          {/* Reihenfolge: lädt, dann Fehler, dann leer, dann Inhalt. Der
              Ladezustand FEHLTE im alten Code — solange die Liste unterwegs
              war, griff keiner der Zweige und man sah eine leere Karte. */}
          {bewerbungen.pending ? <LoadingBlock label="Bewerbungen werden geladen…" /> : null}

          {ergebnis !== null && !ergebnis.ok ? (
            <Alert severity="error">{ergebnis.error.detail}</Alert>
          ) : null}

          {ergebnis?.ok && liste.length === 0 ? (
            <EmptyBlock
              title="Noch keine Bewerbung."
              action={
                <Button component={RouterLink} to="/jobs" variant="contained">
                  Offene Stellen ansehen
                </Button>
              }
            />
          ) : null}

          {liste.length > 0 ? (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
              {liste.map((bewerbung: Application) => (
                <Card key={bewerbung.id} variant="outlined">
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
                      <Typography variant="h4">{STAND[bewerbung.status]}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        {laeuft(bewerbung.status)
                          ? `Freigegeben: ${freigegeben(bewerbung)}`
                          : "Das Unternehmen sieht deine Daten nicht mehr."}
                      </Typography>
                    </Box>

                    {laeuft(bewerbung.status) ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void zurueckziehen(bewerbung.id)}
                        disabled={laeuftGerade}
                        sx={{ flexShrink: 0 }}
                      >
                        Zurückziehen
                      </Button>
                    ) : null}
                  </CardContent>
                </Card>
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}
