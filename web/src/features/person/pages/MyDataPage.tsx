import Alert from "@mui/material/Alert";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import {
  type Abschnitt,
  abschnitt,
  baueAuskunft,
  dateiname,
} from "../lib/export";
import { listMyConsentHistory, listMyConsents } from "../api/consent";
import { getMyProfile } from "../api/profile";
import { ladeMeinen, ladeMeineAnfragen } from "../api/resume";
import { ladeMeines } from "../api/portfolio";
import { ladeMeine as ladeGitHub } from "../api/github";
import { ladeWahl } from "../api/settings";
import { ladeAnschrift } from "../api/zivilidentitaet";
import { getMyMarketStatus, listMyMarketRequests } from "../../work/api/market";
import { listMyTransfers } from "../../work/api/transfers";
import { listMyApplications } from "../../work/api/applications";

/**
 * <c>/my-data</c> — alles über einen selbst, in einer Datei.
 *
 * <strong>Sie entsteht im Browser und wird nirgends abgelegt.</strong> Es gibt
 * keinen Endpunkt, der sie erzeugt — sonst hätte eine Auskunft über die eigenen
 * Daten selbst wieder Daten hinterlassen.
 *
 * <strong>Zwölf Quellen, parallel, und keine bricht die Seite.</strong> Was
 * nicht antwortet, wird als <c>nicht_abrufbar</c> vermerkt — auf der Seite UND
 * in der Datei. Eine Auskunft, die stillschweigend die Hälfte weglässt, ist
 * schlimmer als eine, die fehlt: die eine merkt man, die andere nicht.
 *
 * <strong>Kein Löschen-Knopf neben dem Herunterladen-Knopf.</strong> Hier lässt
 * sich nichts falsch anklicken, was sich nicht rückgängig machen liesse. Der
 * Verweis geht in beide Richtungen, damit niemand glaubt, es gäbe eine
 * Pflichtreihenfolge.
 */
export function MyDataPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const auskunft = useAsync(
    async (signal): Promise<Record<string, Abschnitt>> => {
      const [
        benachrichtigungen,
        anschrift,
        profile,
        lebenslauf,
        lebenslaufAnfragen,
        portfolio,
        github,
        marktstatus,
        marktAnfragen,
        transfers,
        bewerbungen,
        freigaben,
        verlauf,
      ] = await Promise.all([
        ladeWahl(signal),
        ladeAnschrift(signal),
        getMyProfile(signal),
        ladeMeinen(signal),
        ladeMeineAnfragen(signal),
        ladeMeines(signal),
        ladeGitHub(signal),
        getMyMarketStatus(signal),
        listMyMarketRequests(signal),
        listMyTransfers(signal),
        listMyApplications(signal),
        listMyConsents(signal),
        listMyConsentHistory(signal),
      ]);

      return {
        konto: abschnitt(session !== null, {
          ...session,
          anschrift: anschrift ?? undefined,
        }),
        benachrichtigungen: abschnitt(
          benachrichtigungen !== null,
          benachrichtigungen ?? undefined,
        ),
        // Die Schlüssel dieses Objekts sind die ABSCHNITTSNAMEN der Datei, die
        // ein Mensch herunterlädt — Datenvertrag, kein beiläufiger Name. Sie
        // heissen deutsch, weil sie es beim ersten Export taten.
        profil: abschnitt(profile.ok, profile.ok ? profile.profile : undefined),
        lebenslauf: abschnitt(
          lebenslauf.ok,
          lebenslauf.ok ? lebenslauf.value : undefined,
        ),
        lebenslauf_anfragen: abschnitt(
          lebenslaufAnfragen.ok,
          lebenslaufAnfragen.ok ? lebenslaufAnfragen.value : undefined,
        ),
        portfolio: abschnitt(
          portfolio.ok,
          portfolio.ok ? portfolio.value : undefined,
        ),
        github: abschnitt(github.ok, github.ok ? github.value : undefined),
        marktstatus: abschnitt(
          marktstatus.ok,
          marktstatus.ok ? marktstatus.status : undefined,
        ),
        markt_anfragen: abschnitt(
          marktAnfragen.ok,
          marktAnfragen.ok ? marktAnfragen.requests : undefined,
        ),
        transfers: abschnitt(
          transfers.ok,
          transfers.ok ? transfers.transfers : undefined,
        ),
        bewerbungen: abschnitt(
          bewerbungen.ok,
          bewerbungen.ok ? bewerbungen.applications : undefined,
        ),
        freigaben: abschnitt(
          freigaben.ok,
          freigaben.ok ? freigaben.consents : undefined,
        ),
        freigaben_verlauf: abschnitt(
          verlauf.ok,
          verlauf.ok ? verlauf.events : undefined,
        ),
      };
    },
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("meineDaten.titel")}
        satz="meineDaten.anmelden"
      />
    );
  }

  const result =
    auskunft.value === undefined ? null : baueAuskunft(auskunft.value);
  const missing = result?.unvollständig ?? [];

  function herunterladen() {
    if (result === null) return;

    const content = new Blob([JSON.stringify(result, null, 2)], {
      type: "application/json",
    });
    const url = URL.createObjectURL(content);
    const verweis = document.createElement("a");
    verweis.href = url;
    verweis.download = dateiname();
    verweis.click();
    // Freigeben, sonst haelt der Browser den Inhalt bis zum Neuladen im
    // Speicher — bei einer Datei ueber die eigenen Daten unnoetig lange.
    URL.revokeObjectURL(url);
  }

  return (
    <PageShell
      title={t("meineDaten.titel")}
      narrow
      lead={t("meineDaten.lead")}
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          {auskunft.laedt ? (
            <LoadingBlock label={t("meineDaten.laden")} />
          ) : null}

          {missing.length > 0 ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {t("meineDaten.unvollstaendig", { teile: missing.join(", ") })}
            </Alert>
          ) : null}

          {result !== null ? (
            <>
              {/* Ein `<dl>`, keine Liste: zu jedem Abschnitt gehört die
                  Auskunft, ob er enthalten ist. Diese Zuordnung IST der Inhalt
                  der Aufstellung und steht damit im Markup statt im Layout. */}
              <Box
                component="dl"
                sx={{
                  display: "grid",
                  gridTemplateColumns: "auto 1fr",
                  columnGap: 3,
                  rowGap: 0.75,
                  mb: 3,
                }}
              >
                {Object.entries(result.abschnitte).map(([name, entry]) => (
                  <Box key={name} sx={{ display: "contents" }}>
                    <Typography component="dt" variant="body2">
                      {name.replace(/_/g, " ")}
                    </Typography>
                    <Typography
                      component="dd"
                      variant="body2"
                      sx={{ m: 0 }}
                      color={
                        entry.status === "ok"
                          ? "text.secondary"
                          : "warning.main"
                      }
                    >
                      {t(
                        entry.status === "ok"
                          ? "meineDaten.enthalten"
                          : "meineDaten.fehlt",
                      )}
                    </Typography>
                  </Box>
                ))}
              </Box>

              <Button variant="contained" onClick={herunterladen}>
                {t("meineDaten.herunterladen")}
              </Button>
            </>
          ) : null}
        </CardContent>
      </Card>

      {/* DER WEG ZUM LÖSCHEN IST EIN KNOPF, KEIN NEBENSATZ.
          Hier stand ein Abschnitt „Was hier nicht steht" mit zwei Absätzen
          Begründung, und der einzige Weg zur Löschung war ein Wort mitten im
          Fliesstext. Das Recht auf Löschung gehört auffindbar an die Stelle, an
          der jemand seine Daten verwaltet — nicht in eine Erklärung darüber,
          warum es woanders steht. Die Begründung selbst war richtig und steht
          jetzt dort, wo sie wirkt: auf der Löschseite, vor dem Klick. */}
      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 1 }}>
            {t("meineDaten.loeschenTitel")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {t("meineDaten.loeschenText")}
          </Typography>
          {/* Umrandet und nicht gefüllt: auffindbar, ohne neben dem
              Herunterladen-Knopf wie die naheliegende Handlung auszusehen. */}
          <Button
            component={RouterLink}
            to="/delete-account"
            variant="outlined"
            color="error"
          >
            {t("kopf.kontoLoeschen")}
          </Button>
        </CardContent>
      </Card>
    </PageShell>
  );
}
