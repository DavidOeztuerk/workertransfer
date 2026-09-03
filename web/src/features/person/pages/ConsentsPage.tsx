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
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import type { ApiError } from "../../../core/store/thunkHelpers";
import {
  type GrantedConsent,
  listMyConsents,
  parseCapability,
  setGranted,
} from "../api/consent";
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
        t("freigaben.widerrufsgrund"),
      );
      setzeBeschaeftigt(false);
      setzeFehler(result.ok ? null : result.error);
      freigaben.again();
    },
    [subjectId, freigaben, t],
  );

  if (unbekannt) {
    return (
      <PageShell title={t("freigaben.titel")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return (
      <AnmeldungNoetig
        titel={t("freigaben.titel")}
        satz="freigaben.anmelden"
      />
    );
  }

  return (
    <PageShell
      title={t("freigaben.titel")}
      narrow
      lead={t("freigaben.lead")}
    >
      {fehler !== null ? <ErrorBlock error={fehler} /> : null}

      <Card>
        <CardContent>
          {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
              Inhalt. */}
          {freigaben.laedt ? (
            <LoadingBlock label={t("freigaben.laden")} />
          ) : null}

          {/* Kein leerer Zustand bei einem Fehler: „du hast nichts freigegeben"
              wäre hier die beruhigendste falsche Antwort, die es gibt. */}
          {result !== undefined && !result.ok ? (
            <ErrorBlock error={result.error} />
          ) : null}

          {result?.ok && consents.length === 0 ? (
            <EmptyBlock
              title={t("freigaben.leerTitel")}
              hint={t("freigaben.leerHinweis")}
            />
          ) : null}

          {consents.length > 0 ? (
            <List disablePadding>
              {consents.map((consent) => (
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
  // Eine unbekannte Form wird gezeigt, nicht verschluckt — und lässt sich
  // trotzdem zurückziehen.
  // Übersetzt wird NUR die erkannte Form. Die rohe Zeichenkette durch `t()` zu
  // schicken zerlegt sie: i18next liest ein `:` als Namensraumtrenner, und aus
  // `something.entirely:new` wird `new`. Eine unbekannte Freigabe muss aber
  // wörtlich dastehen — sonst weiss niemand, was er da zurückzieht.
  const was = parts.area === null ? consent.capability : t(parts.area);
  const wer = parts.public
    ? t("freigaben.alleUnternehmen")
    : parts.tenantId !== null
      ? (firmenname ?? t("freigaben.einUnternehmen"))
      : t("freigaben.empfaengerUnbekannt");

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
          {/* Das Datum folgt der GEWÄHLTEN Sprache, nicht dem Gerät: sonst
              stünde ein deutscher Satz neben einem amerikanischen Datum. */}
          {t("freigaben.freigegebenAm", {
            datum: new Date(consent.granted_at).toLocaleDateString(
              i18n.language,
            ),
          })}
        </Typography>
      </Box>
      <Button variant="text" onClick={onWithdraw} disabled={busy}>
        {t("allgemein.zurueckziehen")}
      </Button>
    </ListItem>
  );
}
