import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, Navigate } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { type Aufgabenstand, ladeAufgaben } from "../api/aufgaben";

interface Eintrag {
  key: string;
  anzahl: number;
  ziel: string;
}

/**
 * Nur Schlüssel und Anzahl — die Beugung macht i18next.
 *
 * Vorher stand hier je ein ausformulierter Satz für „1" und für „mehr", weil
 * „1 Gespräche" wie ein Fehler liest. Diese Verzweigung ist eine Aussage über
 * das DEUTSCHE Zahlwort; Französisch zählt anders, und eine dritte Sprache
 * womöglich noch anders. Die Kataloge tragen `_one`/`_other`, und die Regel
 * kommt aus der Sprache statt aus dieser Datei.
 */
function eintraegeFuerMich(current: Aufgabenstand): Eintrag[] {
  const eintraege: Eintrag[] = [];

  if (current.marktanfragen > 0) {
    eintraege.push({
      key: "uebersicht.marktanfragen",
      anzahl: current.marktanfragen,
      ziel: "/market",
    });
  }

  if (current.lebenslaufanfragen > 0) {
    eintraege.push({
      key: "uebersicht.lebenslaufanfragen",
      anzahl: current.lebenslaufanfragen,
      ziel: "/resume",
    });
  }

  if (current.eigeneGespraeche > 0) {
    eintraege.push({
      key: "uebersicht.gespraeche",
      anzahl: current.eigeneGespraeche,
      ziel: "/transfers",
    });
  }

  return eintraege;
}

function eintraegeFuerDieFirma(current: Aufgabenstand): Eintrag[] {
  if (current.firmenvorgaenge === 0) return [];
  return [
    {
      key: "uebersicht.firmenvorgaenge",
      anzahl: current.firmenvorgaenge,
      ziel: "/company/transfers",
    },
  ];
}

function Liste({ titel, eintraege }: { titel: string; eintraege: Eintrag[] }) {
  const { t } = useTranslation();

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent sx={{ p: { xs: 2.5, md: 3 } }}>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {titel}
        </Typography>
        {/* Gezählt werden VORGÄNGE, nie Personen (ADR-0022/0026). */}
        <Box
          component="ul"
          sx={{ listStyle: "none", m: 0, p: 0, display: "grid", gap: 1 }}
        >
          {eintraege.map((entry) => (
            <Box component="li" key={entry.ziel}>
              <Link component={RouterLink} to={entry.ziel}>
                {t(entry.key, { count: entry.anzahl })}
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
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);
  // `tenantId === null` heisst „handelt als Person" (ADR-0017) — kein Fehler,
  // sondern der Normalfall auf einem Transfermarkt.
  const mitFirma = session?.tenantId != null;

  const [current, setStand] = useState<Aufgabenstand | null>(null);

  useEffect(() => {
    if (status !== "authenticated") return;

    const abort = new AbortController();
    setStand(null);
    void ladeAufgaben(mitFirma, abort.signal).then((result) => {
      // Nach einem Abbruch kommt hier die abgebrochene Anfrage als Netzfehler
      // an. Sie darf den Stand nicht mehr anfassen — sonst zeigt die Seite die
      // Unvollständigkeit einer Abfrage, die niemand mehr wollte.
      if (abort.signal.aborted) return;
      setStand(result);
    });

    return () => abort.abort();
  }, [status, mitFirma]);

  if (status === "unknown") {
    return (
      <PageShell title={t("uebersicht.titel")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (status === "anonymous") return <Navigate to="/login" replace />;

  const meine = current === null ? [] : eintraegeFuerMich(current);
  const firmen = current === null ? [] : eintraegeFuerDieFirma(current);
  const nichts = meine.length === 0 && firmen.length === 0;

  return (
    <PageShell
      title={t("uebersicht.titel")}
      narrow
      lead={t("uebersicht.lead")}
    >
      {current === null ? <LoadingBlock /> : null}

      {current?.unvollstaendig === true ? (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {t("uebersicht.unvollstaendig")}
        </Alert>
      ) : null}

      {current !== null && nichts && !current.unvollstaendig ? (
        <EmptyBlock
          title={t("uebersicht.leerTitel")}
          hint={t("uebersicht.leerHinweis")}
          action={
            <Button component={RouterLink} to="/consents" variant="outlined">
              {t("uebersicht.leerKnopf")}
            </Button>
          }
        />
      ) : null}

      {meine.length > 0 ? (
        <Liste titel={t("uebersicht.fuerDich")} eintraege={meine} />
      ) : null}
      {firmen.length > 0 ? (
        <Liste titel={t("uebersicht.fuerDieFirma")} eintraege={firmen} />
      ) : null}
    </PageShell>
  );
}
