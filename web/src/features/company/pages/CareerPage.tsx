import { useState } from "react";

import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import CircularProgress from "@mui/material/CircularProgress";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAsync } from "../lib/useAsync";
import { getCompanyBySlug } from "../api/companies";
import { remoteLabel, searchJobs } from "../../work/api/jobs";
import { starteEntwuerfe } from "../../work/lib/entwuerfe";
import { useHandelnder } from "../lib/session";
import { Trans, useTranslation } from "react-i18next";

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
 *
 * <strong>„Bewerben" ist ein Knopf und kein Verweis.</strong> Er zeigte auf
 * <c>/jobs/:id/apply</c>, und diese Adresse war zuletzt nur noch eine Weiche:
 * sie legte einen Entwurf an und leitete weiter. Eine Adresse, die keine
 * Ansicht hat, ist ein Zwischenhalt, den niemand sehen soll — und sie trug ein
 * totes Formular mit Freigabekästchen mit sich, das nach ihr niemand mehr
 * erreichte. Der Vorgang steht jetzt in <c>lib/entwuerfe</c>, an EINER Stelle,
 * und dieser Knopf ruft genau denselben wie der auf der Stellenliste.
 */
export function CareerPage() {
  const { slug } = useParams();
  const gesucht = slug ?? "";

  const { t } = useTranslation();
  const navigate = useNavigate();
  const { signedIn } = useHandelnder();
  const [laeuftStelle, setLaeuftStelle] = useState<string | null>(null);
  const [bewerbungsfehler, setBewerbungsfehler] = useState<string | null>(null);

  async function bewerben(stellenId: string) {
    if (!signedIn) {
      void navigate("/login");
      return;
    }
    setLaeuftStelle(stellenId);
    setBewerbungsfehler(null);
    try {
      const start = await starteEntwuerfe([stellenId]);
      if (!start.ok) {
        setBewerbungsfehler(start.fehler);
        return;
      }
      const erster = start.drafts[0];
      if (erster) void navigate(`/applications/drafts/${erster.id}`);
    } finally {
      setLaeuftStelle(null);
    }
  }

  const unternehmen = useAsync(
    (signal) => getCompanyBySlug(gesucht, signal),
    [gesucht],
    gesucht !== "",
  );

  const profile = unternehmen.data?.ok ? unternehmen.data.profile : undefined;
  const tenantId = profile?.tenant_id;

  const stellen = useAsync(
    (signal) =>
      searchJobs({ company: tenantId as string }, 1, 50, signal),
    [tenantId],
    tenantId !== undefined,
  );

  if (unternehmen.pending) {
    return (
      <PageShell title={t("karriere.titel")} narrow>
        <Card>
          <CardContent>
            <LoadingBlock label={t("karriere.laden")} />
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
      <PageShell title={t("karriere.titel")} narrow>
        <Alert severity="warning">
          {unternehmen.data.error.detail} {t("karriere.nichtAbrufbar")}
        </Alert>
      </PageShell>
    );
  }

  if (profile === undefined) {
    return (
      <PageShell title={t("karriere.keineFirmaTitel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="karriere.keineFirma"
                components={{ 1: <Link component={RouterLink} to="/jobs" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const result = stellen.data;
  const list = result?.ok ? result.items : [];
  const abrufGescheitert =
    result !== null && result !== undefined && !result.ok;

  return (
    <PageShell title={profile.display_name} narrow>
      {profile.website !== null ? (
        <Box sx={{ mb: 3 }}>
          <Link href={profile.website} target="_blank" rel="noreferrer noopener">
            {profile.website}
          </Link>
        </Box>
      ) : null}

      {profile.about !== "" ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              {t("karriere.ueberUns")}
            </Typography>
            <Typography>{profile.about}</Typography>
          </CardContent>
        </Card>
      ) : null}

      {profile.locations.length > 0 || profile.benefits.length > 0 ? (
        <Card sx={{ mb: 3 }}>
          <CardContent>
            {profile.locations.length > 0 ? (
              <Typography sx={{ mb: profile.benefits.length > 0 ? 1.5 : 0 }}>
                {t("karriere.standorte", {
                  orte: profile.locations.join(", "),
                })}
              </Typography>
            ) : null}
            {profile.benefits.length > 0 ? (
              <Box component="ul" sx={{ pl: 2.5, m: 0 }}>
                {profile.benefits.map((vorteil: string) => (
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
            {t("karriere.offeneStellen")}
          </Typography>

          {bewerbungsfehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {bewerbungsfehler}
            </Alert>
          ) : null}

          {/* Reihenfolge: lädt, dann Fehler, dann leer, dann Inhalt. */}
          {stellen.pending ? (
            <LoadingBlock label={t("karriere.stellenLaden")} />
          ) : null}

          {abrufGescheitert ? (
            <Alert severity="warning">
              {t("karriere.stellenNichtAbrufbar")}
            </Alert>
          ) : null}

          {!stellen.pending && !abrufGescheitert && list.length === 0 ? (
            <EmptyBlock title={t("karriere.leer")} />
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
              {list.map((stelle) => (
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
                          : t("karriere.ortFehlt")}{" "}
                        · {remoteLabel(stelle.remote)}
                      </Typography>
                    </Box>
                    <Button
                      type="button"
                      variant="contained"
                      size="small"
                      sx={{ flexShrink: 0 }}
                      disabled={laeuftStelle !== null}
                      onClick={() => void bewerben(stelle.id)}
                      startIcon={
                        laeuftStelle === stelle.id ? (
                          <CircularProgress color="inherit" size={16} />
                        ) : null
                      }
                    >
                      {t("karriere.bewerben")}
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
