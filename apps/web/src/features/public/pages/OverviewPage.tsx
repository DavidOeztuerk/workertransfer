import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, Navigate } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { type Aufgabenstand, ladeAufgaben } from "../api/aufgaben";

interface Eintrag {
  text: string;
  ziel: string;
}

/** Ein Satz je Anzahl, ausformuliert — „1 Gespräche" liest sich wie ein Fehler. */
function eintraegeFuerMich(stand: Aufgabenstand): Eintrag[] {
  const eintraege: Eintrag[] = [];

  if (stand.marktanfragen > 0) {
    eintraege.push({
      text:
        stand.marktanfragen === 1
          ? "1 Unternehmen möchte sehen, ob du ansprechbar bist"
          : `${stand.marktanfragen} Unternehmen möchten sehen, ob du ansprechbar bist`,
      ziel: "/market",
    });
  }

  if (stand.lebenslaufanfragen > 0) {
    eintraege.push({
      text:
        stand.lebenslaufanfragen === 1
          ? "1 Anfrage nach deinem Lebenslauf"
          : `${stand.lebenslaufanfragen} Anfragen nach deinem Lebenslauf`,
      ziel: "/resume",
    });
  }

  if (stand.eigeneGespraeche > 0) {
    eintraege.push({
      text:
        stand.eigeneGespraeche === 1
          ? "1 Gespräch wartet auf dich"
          : `${stand.eigeneGespraeche} Gespräche warten auf dich`,
      ziel: "/transfers",
    });
  }

  return eintraege;
}

function eintraegeFuerDieFirma(stand: Aufgabenstand): Eintrag[] {
  if (stand.firmenvorgaenge === 0) return [];
  return [
    {
      text:
        stand.firmenvorgaenge === 1
          ? "1 Transfer wartet auf euch"
          : `${stand.firmenvorgaenge} Transfers warten auf euch`,
      ziel: "/company/transfers",
    },
  ];
}

function Liste({ titel, eintraege }: { titel: string; eintraege: Eintrag[] }) {
  return (
    <Card sx={{ mb: 2 }}>
      <CardContent sx={{ p: { xs: 2.5, md: 3 } }}>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {titel}
        </Typography>
        {/* Gezählt werden VORGÄNGE, nie Personen (ADR-0022/0026). */}
        <Box component="ul" sx={{ listStyle: "none", m: 0, p: 0, display: "grid", gap: 1 }}>
          {eintraege.map((eintrag) => (
            <Box component="li" key={eintrag.ziel}>
              <Link component={RouterLink} to={eintrag.ziel}>
                {eintrag.text}
              </Link>
            </Box>
          ))}
        </Box>
      </CardContent>
    </Card>
  );
}

/**
 * Die Übersicht auf `/overview` — „Was liegt an".
 *
 * <strong>Sie hat eine eigene Adresse</strong>, und das ist der ganze Grund
 * ihrer Existenz: vorher bediente `/` beides, was sie unverlinkbar machte.
 *
 * <strong>Ohne Sitzung wird sie nicht gezeichnet, sondern verlassen.</strong>
 * „Gerade wartet nichts auf dich" wäre für eine abgemeldete Besucherin keine
 * leere Übersicht, sondern eine falsche Auskunft — sie sagt nichts über die
 * Person, sondern über das fehlende Token.
 *
 * Der Zustand steht lokal und nicht im Store: `core/store/store.ts` meldet nur
 * `auth` und `preferences` an, und diese Seite darf dort nichts ergänzen. Wer
 * später einen `overview`-Slice will, ändert zuerst dort.
 */
export function OverviewPage() {
  const status = useAppSelector((zustand) => zustand.auth.status);
  const session = useAppSelector((zustand) => zustand.auth.session);
  // `tenantId === null` heisst „handelt als Person" (ADR-0017) — kein Fehler,
  // sondern der Normalfall auf einem Transfermarkt.
  const mitFirma = session?.tenantId != null;

  const [stand, setStand] = useState<Aufgabenstand | null>(null);

  useEffect(() => {
    if (status !== "authenticated") return;

    const abbruch = new AbortController();
    setStand(null);
    void ladeAufgaben(mitFirma, abbruch.signal).then((ergebnis) => {
      // Nach einem Abbruch kommt hier die abgebrochene Anfrage als Netzfehler
      // an. Sie darf den Stand nicht mehr anfassen — sonst zeigt die Seite die
      // Unvollständigkeit einer Abfrage, die niemand mehr wollte.
      if (abbruch.signal.aborted) return;
      setStand(ergebnis);
    });

    return () => abbruch.abort();
  }, [status, mitFirma]);

  if (status === "unknown") {
    return (
      <PageShell title="Was liegt an" narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (status === "anonymous") return <Navigate to="/login" replace />;

  const meine = stand === null ? [] : eintraegeFuerMich(stand);
  const firmen = stand === null ? [] : eintraegeFuerDieFirma(stand);
  const nichts = meine.length === 0 && firmen.length === 0;

  return (
    <PageShell
      title="Was liegt an"
      narrow
      lead="Nur Dinge, die auf eine Entscheidung von dir warten. Was von selbst läuft, steht hier nicht — sonst wäre es eine Liste, und Listen übersieht man."
    >
      {stand === null ? <LoadingBlock /> : null}

      {stand?.unvollstaendig === true ? (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Ein Teil konnte nicht geladen werden. Was hier steht, ist deshalb womöglich
          unvollständig.
        </Alert>
      ) : null}

      {stand !== null && nichts && !stand.unvollstaendig ? (
        <EmptyBlock
          title="Gerade wartet nichts auf dich."
          hint="Du entscheidest, was von dir sichtbar ist — nachsehen kannst du das jederzeit."
          action={
            <Button component={RouterLink} to="/consents" variant="outlined">
              Meine Freigaben
            </Button>
          }
        />
      ) : null}

      {meine.length > 0 ? <Liste titel="Für dich" eintraege={meine} /> : null}
      {firmen.length > 0 ? <Liste titel="Für dein Unternehmen" eintraege={firmen} /> : null}
    </PageShell>
  );
}
