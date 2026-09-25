import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Typography from "@mui/material/Typography";

import {
  ConsentSwitch,
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { Link as RouterLink } from "react-router-dom";
import Link from "@mui/material/Link";
import type { ApiError } from "../../../core/store/thunkHelpers";
import {
  GITHUB_VISIBILITY,
  type GrantedConsent,
  PORTFOLIO_VISIBILITY,
  PROFILE_VISIBILITY,
  listMyConsents,
  parseCapability,
  setGranted,
} from "../api/consent";
import { ladeMeine } from "../api/github";
import { getCompanyName } from "../api/companies";
import { useAsync } from "../lib/useAsync";
import { usePerson } from "../lib/session";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";

/**
 * Die Seite, die einer Consent-Plattform gefehlt hat.
 *
 * Vorher waren die Freigaben über vier Seiten verstreut, und die aus einer
 * Bewerbung standen auf keiner davon. Eine Einwilligung, die man nicht
 * überblicken kann, ist keine informierte Einwilligung, und ein Widerruf, den
 * man nicht findet, ist keiner.
 *
 * <strong>Und sie war eine Einbahnstrasse.</strong> Sie zeigte, was gilt, und
 * bot „Zurückziehen" — ERTEILEN ging nur auf der Seite, zu der die Freigabe
 * gehört: der Profilschalter im Profil, der Arbeitenschalter bei den Arbeiten,
 * der Marktstatus im Markt. Wer wissen wollte, wie er sichtbar wird, musste
 * raten, auf welcher von fünf Seiten der Schalter liegt. Eine Seite, die
 * „Meine Freigaben" heisst und keine Freigabe erteilen kann, beantwortet die
 * Frage nicht, für die man sie öffnet.
 *
 * <strong>Für GitHub gab es den Schalter überhaupt nicht.</strong> Der Dienst
 * prüft `github.visibility:public` seit jeher, die Oberfläche konnte sie nur
 * anzeigen — die Belege waren also nach dem Verbinden für niemanden sichtbar,
 * und es gab keinen Weg, das zu ändern.
 *
 * Die Schalter stehen weiterhin AUCH auf ihren Fachseiten. Zwei Orte für
 * denselben Schalter sind kein Widerspruch: beide lesen und schreiben den
 * Ledger, es gibt nur eine Wahrheit. Was es nicht mehr gibt, ist genau ein Ort,
 * und den findet niemand.
 *
 * <strong>Granting sits above withdrawing</strong>, in the order the questions
 * are asked: whoever comes here mostly wants to know how they become visible;
 * whoever wants to withdraw already knows what to look for.
 *
 * <strong>The two outlined cards at the bottom say what has NO switch.</strong>
 * Without them the page looks complete and is not: the CV is missing, and
 * whoever looks for it takes that for an oversight. It is none — see ADR-0020.
 * Both routes to the CV stand side by side, because the difference is not in
 * the document but in WHO starts: a company asking, or an application sending
 * it along.
 */
export function ConsentsPage() {
  const { t } = useTranslation();
  const { subjectId, unbekannt } = usePerson();
  const [fehler, setzeFehler] = useState<ApiError | null>(null);
  const [busy, setzeBeschaeftigt] = useState(false);

  const freigaben = useAsync(
    (signal) => listMyConsents(signal),
    [subjectId],
    subjectId !== null,
  );

  const result = freigaben.value;
  const consents: GrantedConsent[] =
    result?.ok === true ? result.consents : [];

  /*
   * DIE DREI GLOBALEN SICHTBARKEITEN werden zu Schaltern und fallen aus der
   * Liste darunter heraus. Sonst stünde dieselbe Freigabe zweimal auf einer
   * Seite — einmal als Schalter, einmal als Zeile mit „Zurückziehen" — und
   * niemand wüsste, welche der beiden gilt.
   */
  const global = [PROFILE_VISIBILITY, PORTFOLIO_VISIBILITY, GITHUB_VISIBILITY];
  const gilt = (capability: string) =>
    consents.some((eintrag) => eintrag.capability === capability);
  const einzelne = consents.filter(
    (eintrag) => !global.includes(eintrag.capability),
  );

  // No empty state on an error: "you have released nothing" would be the most
  // reassuring wrong answer there is, so the empty state needs a successful read.
  const nichtsFreigegeben = result?.ok === true && einzelne.length === 0;

  /*
   * Ohne NACHGEWIESENES Konto gibt es nichts freizugeben. Der Schalter bleibt
   * sichtbar und sagt, was fehlt — ihn zu verstecken hiesse, die Möglichkeit zu
   * verschweigen.
   *
   * <strong>Genannt genügt nicht.</strong> Gemessen an der laufenden Anlage:
   * die Verbindung stand als `verified: false` da, und der Schalter war
   * bedienbar. Freigegeben wären damit die Repositories eines Kontos, das der
   * Person niemand zugeordnet hat — genau das, wogegen der Nachweis existiert.
   * Ein Beleg, der jemand anderem gehört, ist schlimmer als kein Beleg.
   */
  const github = useAsync(
    (signal) => ladeMeine(signal),
    [subjectId],
    subjectId !== null,
  );
  const verbindung =
    github.value?.ok === true ? github.value.value : null;
  const githubNachgewiesen = verbindung?.verified === true;

  const schalten = useCallback(
    async (capability: string, an: boolean) => {
      if (subjectId === null) return;
      setzeBeschaeftigt(true);
      const antwort = await setGranted(
        subjectId,
        capability,
        an,
        // Der Grund wird nur beim Widerruf gelesen — beim Erteilen fragt
        // niemand danach (ADR-0027: eine Entziehung muss erklärbar sein, eine
        // Erteilung nicht).
        t("consents.revokeReason"),
      );
      setzeBeschaeftigt(false);
      setzeFehler(antwort.ok ? null : antwort.error);
      freigaben.again();
    },
    [subjectId, freigaben, t],
  );

  // Die Namen kommen frisch vom companies-service. Sie im Ledger zu führen
  // hiesse, eine Kopie zu halten, die veraltet, sobald ein Unternehmen sich
  // umbenennt — und der Ledger verwaltet Fähigkeiten, keine Unternehmen.
  const tenantIds = [
    ...new Set(
      consents
        .map((consent) => parseCapability(consent.capability).tenantId)
        .filter((id): id is string => id !== null),
    ),
  ].sort();

  const namen = useAsync(
    async (signal) => {
      const paare = await Promise.all(
        tenantIds.map(
          async (id) => [id, await getCompanyName(id, signal)] as const,
        ),
      );
      return new Map(
        paare.filter((paar): paar is [string, string] => paar[1] !== null),
      );
    },
    [tenantIds.join(",")],
    tenantIds.length > 0,
  );
  const nameFuer = namen.value ?? new Map<string, string>();

  const zuruecknehmen = useCallback(
    async (capability: string) => {
      if (subjectId === null) return;
      setzeBeschaeftigt(true);
      const result = await setGranted(
        subjectId,
        capability,
        false,
        t("consents.revokeReason"),
      );
      setzeBeschaeftigt(false);
      setzeFehler(result.ok ? null : result.error);
      freigaben.again();
    },
    [subjectId, freigaben, t],
  );

  if (unbekannt) {
    return (
      <PageShell title={t("consents.title")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return (
      <AnmeldungNoetig
        titel={t("consents.title")}
        satz="consents.signInRequired"
      />
    );
  }

  return (
    <PageShell
      title={t("consents.title")}
      narrow
      lead={t("consents.lead")}
    >
      {fehler !== null ? <ErrorBlock error={fehler} /> : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 0.5 }}>
            {t("consents.visibleTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
            {t("consents.visibleLead")}
          </Typography>

          <Box sx={{ display: "grid", gap: 2.5 }}>
            <ConsentSwitch
              label={t("consents.switchProfile")}
              hint={t("consents.switchProfileHint")}
              checked={gilt(PROFILE_VISIBILITY)}
              disabled={busy || freigaben.laedt}
              onChange={(an) => void schalten(PROFILE_VISIBILITY, an)}
            />

            <ConsentSwitch
              label={t("consents.switchPortfolio")}
              hint={t("consents.switchPortfolioHint")}
              checked={gilt(PORTFOLIO_VISIBILITY)}
              disabled={busy || freigaben.laedt}
              onChange={(an) => void schalten(PORTFOLIO_VISIBILITY, an)}
            />

            <Box>
              <ConsentSwitch
                label={t("consents.switchGithub")}
                hint={
                  githubNachgewiesen
                    ? t("consents.switchGithubHint")
                    : verbindung === null
                      ? t("consents.githubMissing")
                      : t("consents.githubUnverified")
                }
                checked={gilt(GITHUB_VISIBILITY)}
                disabled={busy || freigaben.laedt || !githubNachgewiesen}
                onChange={(an) => void schalten(GITHUB_VISIBILITY, an)}
              />
              {githubNachgewiesen ? null : (
                <Link
                  component={RouterLink}
                  to="/github"
                  variant="body2"
                  sx={{ display: "inline-block", mt: 0.5 }}
                >
                  {verbindung === null
                    ? t("consents.toGithub")
                    : t("consents.toGithubProof")}
                </Link>
              )}
            </Box>
          </Box>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 0.5 }}>
            {t("consents.individualTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.individualLead")}
          </Typography>
          {freigaben.laedt ? (
            <LoadingBlock label={t("consents.loading")} />
          ) : null}

          {result !== undefined && !result.ok ? (
            <ErrorBlock error={result.error} />
          ) : null}

          {nichtsFreigegeben ? (
            <EmptyBlock
              title={t("consents.emptyTitle")}
              hint={t("consents.emptyHint")}
            />
          ) : null}

          {einzelne.length > 0 ? (
            <List disablePadding>
              {einzelne.map((consent) => (
                <ConsentZeile
                  key={consent.capability}
                  consent={consent}
                  firmenname={
                    nameFuer.get(
                      parseCapability(consent.capability).tenantId ?? "",
                    ) ?? null
                  }
                  busy={busy}
                  onWithdraw={() => void zuruecknehmen(consent.capability)}
                />
              ))}
            </List>
          ) : null}
        </CardContent>
      </Card>

      <Card variant="outlined">
        <CardContent>
          <Typography variant="h3" sx={{ mb: 1 }}>
            {t("consents.noSwitchTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.noSwitchText")}
          </Typography>
          <Link component={RouterLink} to="/resume" variant="body2">
            {t("consents.toResume")}
          </Link>
        </CardContent>
      </Card>

      <Card variant="outlined" sx={{ mt: 2 }}>
        <CardContent>
          <Typography variant="h3" sx={{ mb: 1 }}>
            {t("consents.applicationTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.applicationText")}
          </Typography>
          <Link component={RouterLink} to="/applications" variant="body2">
            {t("consents.toApplications")}
          </Link>
        </CardContent>
      </Card>
    </PageShell>
  );
}

function ConsentZeile({
  consent,
  firmenname,
  busy,
  onWithdraw,
}: {
  consent: GrantedConsent;
  firmenname: string | null;
  busy: boolean;
  onWithdraw: () => void;
}) {
  const { t, i18n } = useTranslation();
  const parts = parseCapability(consent.capability);
  // The date follows the CHOSEN language, not the device: otherwise a German
  // sentence would stand next to an American date.
  const freigegebenAm = new Date(consent.granted_at).toLocaleDateString(
    i18n.language,
  );
  // Eine unbekannte Form wird gezeigt, nicht verschluckt — und lässt sich
  // trotzdem zurückziehen.
  // Übersetzt wird NUR die erkannte Form. Die rohe Zeichenkette durch `t()` zu
  // schicken zerlegt sie: i18next liest ein `:` als Namensraumtrenner, und aus
  // `something.entirely:new` wird `new`. Eine unbekannte Freigabe muss aber
  // wörtlich dastehen — sonst weiss niemand, was er da zurückzieht.
  const was = parts.area === null ? consent.capability : t(parts.area);
  const wer = parts.public
    ? t("consents.allCompanies")
    : parts.tenantId !== null
      ? (firmenname ?? t("consents.oneCompany"))
      : t("consents.recipientUnknown");

  return (
    <ListItem
      divider
      disableGutters
      sx={{
        display: "flex",
        flexDirection: { xs: "column", sm: "row" },
        alignItems: { xs: "flex-start", sm: "center" },
        justifyContent: "space-between",
        gap: 1.5,
      }}
    >
      <Box>
        <Typography component="p" sx={{ fontWeight: 600 }}>
          {was} · {wer}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {t("consents.releasedOn", { datum: freigegebenAm })}
        </Typography>
      </Box>
      <Button variant="text" onClick={onWithdraw} disabled={busy}>
        {t("common.withdraw")}
      </Button>
    </ListItem>
  );
}
