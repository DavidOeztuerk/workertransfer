import { useCallback, useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Typography from "@mui/material/Typography";

import { EmptyBlock, ErrorBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
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

const WITHDRAWAL_REASON = "Auf der Seite Meine Freigaben zurückgezogen";

/**
 * Die Seite, die einer Consent-Plattform gefehlt hat.
 *
 * Vorher waren die Freigaben über vier Seiten verstreut, und die aus einer
 * Bewerbung standen auf keiner davon. Eine Einwilligung, die man nicht
 * überblicken kann, ist keine informierte Einwilligung, und ein Widerruf, den
 * man nicht findet, ist keiner.
 */
export function ConsentsPage() {
  const { subjectId, unbekannt } = usePerson();
  const [fehler, setzeFehler] = useState<ApiError | null>(null);
  const [beschaeftigt, setzeBeschaeftigt] = useState(false);

  const freigaben = useAsync(
    (signal) => listMyConsents(signal),
    [subjectId],
    subjectId !== null
  );

  const ergebnis = freigaben.wert;
  const consents: GrantedConsent[] = ergebnis?.ok === true ? ergebnis.consents : [];

  // Die Namen kommen frisch vom companies-service. Sie im Ledger zu führen
  // hiesse, eine Kopie zu halten, die veraltet, sobald ein Unternehmen sich
  // umbenennt — und der Ledger verwaltet Fähigkeiten, keine Unternehmen.
  const tenantIds = [
    ...new Set(
      consents
        .map((consent) => parseCapability(consent.capability).tenantId)
        .filter((id): id is string => id !== null)
    ),
  ].sort();

  const namen = useAsync(
    async (signal) => {
      const paare = await Promise.all(
        tenantIds.map(async (id) => [id, await getCompanyName(id, signal)] as const)
      );
      return new Map(paare.filter((paar): paar is [string, string] => paar[1] !== null));
    },
    [tenantIds.join(",")],
    tenantIds.length > 0
  );
  const nameFuer = namen.wert ?? new Map<string, string>();

  const zuruecknehmen = useCallback(
    async (capability: string) => {
      if (subjectId === null) return;
      setzeBeschaeftigt(true);
      const ergebnis = await setGranted(subjectId, capability, false, WITHDRAWAL_REASON);
      setzeBeschaeftigt(false);
      setzeFehler(ergebnis.ok ? null : ergebnis.error);
      freigaben.erneut();
    },
    [subjectId, freigaben]
  );

  if (unbekannt) {
    return (
      <PageShell title="Meine Freigaben" narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return <AnmeldungNoetig titel="Meine Freigaben" zweck="deine Freigaben zu sehen" />;
  }

  return (
    <PageShell
      title="Meine Freigaben"
      narrow
      lead="Alles, was gerade gilt — an einer Stelle. Zurückziehen wirkt sofort: der nächste Zugriff läuft ins Leere, ohne Umweg über uns. Was hier nicht steht, sieht niemand."
    >
      {fehler !== null ? <ErrorBlock error={fehler} /> : null}

      <Card>
        <CardContent>
          {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
              Inhalt. */}
          {freigaben.laedt ? <LoadingBlock label="Freigaben werden geladen…" /> : null}

          {/* Kein leerer Zustand bei einem Fehler: „du hast nichts freigegeben"
              wäre hier die beruhigendste falsche Antwort, die es gibt. */}
          {ergebnis !== undefined && !ergebnis.ok ? <ErrorBlock error={ergebnis.error} /> : null}

          {ergebnis?.ok && consents.length === 0 ? (
            <EmptyBlock
              title="Du hast im Moment nichts freigegeben."
              hint="Niemand sieht etwas von dir."
            />
          ) : null}

          {consents.length > 0 ? (
            <List disablePadding>
              {consents.map((consent) => (
                <ConsentZeile
                  key={consent.capability}
                  consent={consent}
                  firmenname={
                    nameFuer.get(parseCapability(consent.capability).tenantId ?? "") ?? null
                  }
                  beschaeftigt={beschaeftigt}
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
  beschaeftigt,
  onWithdraw,
}: {
  consent: GrantedConsent;
  firmenname: string | null;
  beschaeftigt: boolean;
  onWithdraw: () => void;
}) {
  const teile = parseCapability(consent.capability);
  // Eine unbekannte Form wird gezeigt, nicht verschluckt — und lässt sich
  // trotzdem zurückziehen.
  const was = teile.area ?? consent.capability;
  const wer = teile.public
    ? "Alle Unternehmen"
    : teile.tenantId !== null
      ? (firmenname ?? "Ein Unternehmen")
      : "Empfänger unbekannt";

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
          Freigegeben am {new Date(consent.granted_at).toLocaleDateString("de-DE")}
        </Typography>
      </Box>
      <Button variant="text" onClick={onWithdraw} disabled={beschaeftigt}>
        Zurückziehen
      </Button>
    </ListItem>
  );
}
