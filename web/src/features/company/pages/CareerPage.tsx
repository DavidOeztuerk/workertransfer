import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useParams } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAsync } from "../lib/useAsync";
import { getCompanyBySlug } from "../api/companies";
import { REMOTE_LABEL, searchJobs } from "../../work/api/jobs";

/**
 * <c>/careers/&lt;kürzel&gt;</c> — die öffentliche Seite eines Unternehmens.
 *
 * <strong>Ohne Konto lesbar</strong>, wie die Stellenliste selbst: eine
 * Ausschreibung, die man nur nach Anmeldung sieht, erreicht genau die nicht, für
 * die sie gedacht ist.
 *
 * <strong>„Gerade nicht abrufbar" ist nicht „gibt es nicht".</strong> Beide
 * Fälle stehen hier getrennt und mit eigenem Satz — sonst liest jemand, sein
 * Unternehmen existiere nicht, weil ein Dienst gerade schweigt.
 */
export function CareerPage() {
  const { slug } = useParams();
  const gesucht = slug ?? "";

  const unternehmen = useAsync(
    (signal) => getCompanyBySlug(gesucht, signal),
    [gesucht],
    gesucht !== "",
  );

  const profil = unternehmen.data?.ok ? unternehmen.data.profile : undefined;
  const tenantId = profil?.tenant_id;

  const stellen = useAsync(
    (signal) =>
      searchJobs({ company: tenantId as string, limit: 50 }, undefined, signal),
    [tenantId],
    tenantId !== undefined,
  );

  if (unternehmen.pending) {
    return (
      <PageShell title="Karriere" narrow>
        <Card>
          <CardContent>
            <LoadingBlock label="Unternehmen wird geladen…" />
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (
    unternehmen.data?.ok === false &&
    unternehmen.data.reason === "unavailable"
  ) {
    return (
      <PageShell title="Karriere" narrow>
        <Alert severity="warning">
          {unternehmen.data.error.detail} Das heißt nicht, dass es dieses
          Unternehmen nicht gibt — versuch es später noch einmal.
        </Alert>
      </PageShell>
    );
  }

  if (profil === undefined) {
    return (
      <PageShell title="Diese Seite gibt es nicht" narrow>
        <Card>
          <CardContent>
            <Typography>
              Unter dieser Adresse ist kein Unternehmen hinterlegt.{" "}
              <Link component={RouterLink} to="/jobs">
                Alle offenen Stellen
              </Link>
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const ergebnis = stellen.data;
  const liste = ergebnis?.ok ? ergebnis.items : [];
  const abrufGescheitert =
    ergebnis !== null && ergebnis !== undefined && !ergebnis.ok;

  return (
    <PageShell title={profil.display_name} narrow>
      {profil.website !== null ? (
        <Box sx={{ mb: 3 }}>
          <Link href={profil.website} target="_blank" rel="noreferrer noopener">
            {profil.website}
          </Link>
        </Box>
      ) : null}

      {profil.about !== "" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              Über uns
            </Typography>
            <Typography>{profil.about}</Typography>
          </CardContent>
        </Card>
      ) : null}

      {profil.locations.length > 0 || profil.benefits.length > 0 ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            {profil.locations.length > 0 ? (
              <Typography sx={{ mb: profil.benefits.length > 0 ? 1.5 : 0 }}>
                Standorte: {profil.locations.join(", ")}
              </Typography>
            ) : null}
            {profil.benefits.length > 0 ? (
              <Box component="ul" sx={{ pl: 2.5, m: 0 }}>
                {profil.benefits.map((vorteil: string) => (
                  <Typography component="li" key={vorteil}>
                    {vorteil}
                  </Typography>
                ))}
              </Box>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Offene Stellen
          </Typography>

          {/* Reihenfolge: lädt, dann Fehler, dann leer, dann Inhalt. */}
          {stellen.pending ? (
            <LoadingBlock label="Stellen werden geladen…" />
          ) : null}

          {abrufGescheitert ? (
            <Alert severity="warning">
              Die offenen Stellen sind gerade nicht abrufbar. Das heißt nicht,
              dass es keine gibt — versuch es später noch einmal.
            </Alert>
          ) : null}

          {!stellen.pending && !abrufGescheitert && liste.length === 0 ? (
            <EmptyBlock title="Zurzeit ist nichts ausgeschrieben." />
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
              {liste.map((stelle) => (
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
                        {stelle.location !== ""
                          ? stelle.location
                          : "Ort nicht angegeben"}{" "}
                        · {REMOTE_LABEL[stelle.remote] ?? stelle.remote}
                      </Typography>
                    </Box>
                    <Button
                      component={RouterLink}
                      to={`/jobs/${stelle.id}/apply`}
                      variant="contained"
                      size="small"
                      sx={{ flexShrink: 0 }}
                    >
                      Bewerben
                    </Button>
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
