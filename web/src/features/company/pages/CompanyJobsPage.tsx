import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
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
  type Job,
  closeJob,
  listOwnJobs,
  publishJob,
} from "../../work/api/jobs";

/** Der Stand als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const STAND: Record<string, string> = {
  draft: "firmenstellen.standDraft",
  published: "firmenstellen.standPublished",
  closed: "firmenstellen.standClosed",
};

/**
 * <c>/company/jobs</c> — die eigenen Anzeigen.
 *
 * <strong>Drei Zustände, und der mittlere ist öffentlich.</strong> Ein Entwurf
 * sieht niemand ausser dem Unternehmen; veröffentlicht ist er für alle sichtbar,
 * auch ohne Konto; geschlossen bleibt geschlossen. Eine geschlossene Anzeige
 * lässt sich nicht wieder öffnen — wer neu ausschreibt, schreibt neu aus.
 *
 * <strong>Das Formular liegt auf einer eigenen Adresse</strong>
 * (<c>/company/jobs/new</c>), und diese Liste verweist sichtbar darauf. Eine
 * E2E-Reise sucht es heute noch hier; das ist ihr Fehler und nicht der der
 * Oberfläche, aber die Verlinkung muss deshalb auffindbar sein.
 */
export function CompanyJobsPage() {
  const { t } = useTranslation();
  const { fuerFirma } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const stellen = useAsync(
    (signal) => listOwnJobs(signal),
    [fuerFirma],
    fuerFirma,
  );

  if (!fuerFirma) {
    return (
      <PageShell title={t("firmenstellen.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("firmenstellen.nurFirma")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function schalten(id: string, veroeffentlichen: boolean) {
    setLaeuft(true);
    const ergebnis = veroeffentlichen
      ? await publishJob(id)
      : await closeJob(id);
    setLaeuft(false);
    setFehler(ergebnis.ok ? null : ergebnis.error.detail);
    stellen.reload();
  }

  const ergebnis = stellen.data;
  const liste = ergebnis?.ok ? ergebnis.jobs : [];

  return (
    <PageShell
      title={t("firmenstellen.titel")}
      narrow
      lead={t("firmenstellen.lead")}
      actions={
        <Button
          component={RouterLink}
          to="/company/jobs/new"
          variant="contained"
        >
          {t("firmenstellen.neu")}
        </Button>
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
              LADEZUSTAND fehlte im alten Code — und weil auch der Leersatz eine
              geglückte Antwort verlangte, blieb die Karte währenddessen
              vollständig leer. */}
          {stellen.pending ? (
            <LoadingBlock label={t("firmenstellen.laden")} />
          ) : null}

          {ergebnis !== null && !ergebnis.ok ? (
            <Alert severity="error">{ergebnis.error.detail}</Alert>
          ) : null}

          {ergebnis?.ok && liste.length === 0 ? (
            <EmptyBlock
              title={t("firmenstellen.leer")}
              action={
                <Button
                  component={RouterLink}
                  to="/company/jobs/new"
                  variant="contained"
                >
                  {t("firmenstellen.neu")}
                </Button>
              }
            />
          ) : null}

          {liste.length > 0 ? (
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
              {liste.map((stelle: Job) => (
                <Card key={stelle.id} component="li" variant="outlined">
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
                      <Typography variant="h4">{stelle.title}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        {t(STAND[stelle.status] ?? stelle.status)}
                      </Typography>
                    </Box>

                    {stelle.status !== "closed" ? (
                      <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
                        {stelle.status === "draft" ? (
                          <Button
                            variant="contained"
                            size="small"
                            onClick={() => void schalten(stelle.id, true)}
                            disabled={laeuft}
                          >
                            {t("firmenstellen.veroeffentlichen")}
                          </Button>
                        ) : null}
                        <Button
                          variant="text"
                          size="small"
                          onClick={() => void schalten(stelle.id, false)}
                          disabled={laeuft}
                        >
                          {t("firmenstellen.schliessen")}
                        </Button>
                      </Box>
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
