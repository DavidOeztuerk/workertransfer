import { useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  ConsentSwitch,
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import { PORTFOLIO_VISIBILITY, isGranted, setGranted } from "../api/consent";
import { type Arbeit, anhangUrl, ladeMeines } from "../api/portfolio";

/** Rolle und Jahr, soweit angegeben — und ob eine Datei hängt. */
function summaryLine(arbeit: Arbeit, mitDatei: string): string {
  const parts: string[] = [];
  if (arbeit.role !== "") parts.push(arbeit.role);
  if (arbeit.year !== null) parts.push(String(arbeit.year));
  if (arbeit.attachment !== null) parts.push(mitDatei);
  return parts.join(" · ");
}

/**
 * <c>/portfolio</c> — das Schaufenster und seine eigene Freigabe.
 *
 * <strong>Die Freigabe ist getrennt vom Profil</strong>, und das ist keine
 * Verdopplung: jemand kann ansprechbar sein, ohne seine Arbeiten zu zeigen.
 *
 * <strong>Drei Zustände am Schalter, nicht zwei.</strong> Schweigt der Ledger,
 * ist der Schalter <em>gesperrt</em> und sagt es — sonst gäbe der nächste Klick
 * eine Freigabe für etwas, dessen Stand niemand kennt. Und ohne eine einzige
 * Arbeit lässt sich nichts freigeben: was es nicht gibt, kann man nicht zeigen.
 */
export function PortfolioPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);
  const subjectId = session?.userId ?? null;

  const [fehler, setFehler] = useState<string | null>(null);
  const [toggling, setSchaltet] = useState(false);

  const showcase = useAsync(
    (signal) => ladeMeines(signal),
    [subjectId],
    subjectId !== null,
  );

  const freigabe = useAsync(
    () => isGranted(subjectId as string, PORTFOLIO_VISIBILITY),
    [subjectId],
    subjectId !== null,
  );

  if (status === "anonymous" || subjectId === null) {
    return (
      <AnmeldungNoetig
        titel={t("arbeiten.titel")}
        satz="arbeiten.anmelden"
      />
    );
  }

  const arbeiten = showcase.value?.ok
    ? (showcase.value.value?.items ?? [])
    : [];
  const hatArbeiten = arbeiten.length > 0;

  // `undefined` heisst „wird noch geladen", `null` heisst „der Ledger schweigt".
  const ledgerSchweigt = freigabe.value === null;
  const ledgerUnbekannt = freigabe.laedt || ledgerSchweigt;

  async function umschalten(neu: boolean) {
    setSchaltet(true);
    const result = await setGranted(
      subjectId as string,
      PORTFOLIO_VISIBILITY,
      neu,
      t("arbeiten.widerrufsgrund"),
    );
    setSchaltet(false);

    if (result.ok) {
      setFehler(null);
      freigabe.setze(result.granted);
    } else {
      setFehler(result.error.detail);
      freigabe.again();
    }
  }

  const hint = ledgerSchweigt
    ? t("arbeiten.freigabeSchweigt")
    : freigabe.laedt
      ? t("arbeiten.freigabePruefung")
      : t(
          hatArbeiten
            ? "arbeiten.freigabeWirkt"
            : "arbeiten.freigabeOhneArbeit",
        );

  return (
    <PageShell
      title={t("arbeiten.titel")}
      narrow
      lead={t("arbeiten.lead")}
    >
      {fehler !== null ? (
        <ErrorBlock
          error={{
            status: 0,
            title: t("arbeiten.freigabeFehlschlag"),
            detail: fehler,
          }}
          title={t("arbeiten.freigabeFehlschlag")}
        />
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <ConsentSwitch
            label={t("arbeiten.freigabeLabel")}
            hint={hint}
            checked={freigabe.value === true}
            disabled={!hatArbeiten || toggling || ledgerUnbekannt}
            onChange={(neu) => void umschalten(neu)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          {showcase.laedt ? (
            <LoadingBlock label={t("arbeiten.laden")} />
          ) : null}

          {showcase.value && !showcase.value.ok ? (
            <ErrorBlock error={showcase.value.error} />
          ) : null}

          {!showcase.laedt && showcase.value?.ok && !hatArbeiten ? (
            <EmptyBlock
              title={t("arbeiten.leerTitel")}
              hint={t("arbeiten.leerHinweis")}
              action={
                <Button
                  component={RouterLink}
                  to="/portfolio/new"
                  variant="contained"
                >
                  {t("arbeiten.hinzufuegen")}
                </Button>
              }
            />
          ) : null}

          {hatArbeiten ? (
            <>
              <Box
                sx={{
                  display: "flex",
                  flexDirection: "column",
                  gap: 1.5,
                  mb: 3,
                }}
              >
                {/* Die Reihenfolge ist die des gespeicherten Feldes, und die
                    Adresse einer Arbeit ist ihre Stelle darin: eine Arbeit hat
                    keine eigene Kennung. */}
                {arbeiten.map((arbeit, index) => (
                  <Card key={index} variant="outlined">
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
                        <Typography variant="h4">{arbeit.title}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {summaryLine(arbeit, t("arbeiten.mitDatei"))}
                        </Typography>
                      </Box>
                      <Box
                        sx={{
                          display: "flex",
                          gap: 1.5,
                          alignItems: "center",
                          flexShrink: 0,
                        }}
                      >
                        <Button
                          component={RouterLink}
                          to={`/portfolio/${index}`}
                          size="small"
                          variant="text"
                        >
                          {t("arbeiten.bearbeiten")}
                        </Button>
                        {arbeit.attachment !== null ? (
                          <Link
                            href={anhangUrl(subjectId, arbeit.attachment)}
                            target="_blank"
                            rel="noreferrer noopener"
                            variant="body2"
                          >
                            {t("arbeiten.dateiAnsehen")}
                          </Link>
                        ) : null}
                      </Box>
                    </CardContent>
                  </Card>
                ))}
              </Box>
              <Button
                component={RouterLink}
                to="/portfolio/new"
                variant="contained"
              >
                {t("arbeiten.hinzufuegen")}
              </Button>
            </>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}
