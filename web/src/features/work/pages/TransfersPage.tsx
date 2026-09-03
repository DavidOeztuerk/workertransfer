import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
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
  type PersonAction,
  RUNNING,
  type Transfer,
  euro,
  listMyTransfers,
  personMove,
} from "../api/transfers";

/** Der Stand als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const TITEL: Record<Transfer["status"], string> = {
  interested: "gespraeche.standInterested",
  talking: "gespraeche.standTalking",
  offered: "gespraeche.standOffered",
  accepted: "gespraeche.standAccepted",
  completed: "gespraeche.standCompleted",
  declined: "gespraeche.standDeclined",
  withdrawn: "gespraeche.standWithdrawn",
};

/**
 * <c>/transfers</c> — die eigenen Gespräche über einen Wechsel.
 *
 * <strong>Ein Unternehmen kann nur zugehen, wenn der Marktstatus freigegeben
 * ist</strong> und die Person gerade ansprechbar. Ablehnen geht jederzeit, in
 * jedem Schritt — das ist keine Höflichkeit, sondern die Bedingung dafür, dass
 * ein Gespräch überhaupt zumutbar ist.
 *
 * <strong>Der letzte Schritt gehört der Person.</strong> Diese Plattform fragt
 * den jetzigen Arbeitgeber nicht: sie weiss nicht, wer er ist, und soll es nicht
 * wissen. Ob jemand gehen darf, bestätigt er selbst.
 */
export function TransfersPage() {
  const { t } = useTranslation();
  const { angemeldet, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const gespraeche = useAsync(
    (signal) => listMyTransfers(signal),
    [subjectId],
    angemeldet,
  );

  if (!angemeldet) {
    return (
      <PageShell title={t("gespraeche.titel")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="gespraeche.anmelden"
                components={{ 1: <Link component={RouterLink} to="/login" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function ziehen(id: string, zug: PersonAction) {
    setLaeuft(true);
    const ergebnis = await personMove(id, zug);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    else setFehler(null);
    gespraeche.reload();
  }

  const liste = gespraeche.data?.ok ? gespraeche.data.transfers : [];

  return (
    <PageShell title={t("gespraeche.titel")} narrow lead="">
      <Typography color="text.secondary" sx={{ mb: 3, maxWidth: "62ch" }}>
        <Trans
          i18nKey="gespraeche.einleitung"
          components={{ 1: <Link component={RouterLink} to="/market" /> }}
        />
      </Typography>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      {gespraeche.pending ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("gespraeche.laden")} />
          </CardContent>
        </Card>
      ) : null}

      {gespraeche.data !== null && !gespraeche.data.ok ? (
        <Alert severity="error">{gespraeche.data.error.detail}</Alert>
      ) : null}

      {gespraeche.data?.ok && liste.length === 0 ? (
        <EmptyBlock title={t("gespraeche.leer")} />
      ) : null}

      <Box
        component="ul"
        sx={{
          display: "flex",
          flexDirection: "column",
          gap: 2,
          listStyle: "none",
          p: 0,
          m: 0,
        }}
      >
        {liste.map((gespraech: Transfer) => (
          <Gespraechskarte
            key={gespraech.id}
            gespraech={gespraech}
            gesperrt={laeuft}
            onZug={(zug) => void ziehen(gespraech.id, zug)}
          />
        ))}
      </Box>
    </PageShell>
  );
}

function Gespraechskarte({
  gespraech,
  gesperrt,
  onZug,
}: {
  gespraech: Transfer;
  gesperrt: boolean;
  onZug: (zug: PersonAction) => void;
}) {
  const { t } = useTranslation();
  const laeuftNoch = RUNNING.includes(gespraech.status);
  const brauchtFreigabe =
    gespraech.requires_release && !gespraech.release_confirmed;
  const zeigtAngebot =
    gespraech.status === "offered" || gespraech.status === "accepted";

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t(TITEL[gespraech.status])}
        </Typography>

        {gespraech.message !== "" ? (
          <Typography sx={{ mb: 2 }}>{gespraech.message}</Typography>
        ) : null}

        {zeigtAngebot ? (
          <Box
            component="dl"
            sx={{
              display: "grid",
              gridTemplateColumns: "auto 1fr",
              columnGap: 2,
              rowGap: 0.5,
              mb: 2,
            }}
          >
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("gespraeche.angebot")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_note === "" ? "—" : gespraech.offer_note}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("gespraeche.start")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_start_on ?? "—"}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("gespraeche.abloese")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {euro(gespraech.offer_fee_cents)}
            </Typography>
          </Box>
        ) : null}

        {gespraech.status === "accepted" && brauchtFreigabe ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            <Trans
              i18nKey="gespraeche.freigabeFehlt"
              components={{ 1: <strong /> }}
            />
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {gespraech.status === "interested" ? (
            <Button
              variant="contained"
              onClick={() => onZug("accept-talk")}
              disabled={gesperrt}
            >
              {t("gespraeche.gespraechAnnehmen")}
            </Button>
          ) : null}
          {gespraech.status === "offered" ? (
            <Button
              variant="contained"
              onClick={() => onZug("accept-offer")}
              disabled={gesperrt}
            >
              {t("gespraeche.angebotAnnehmen")}
            </Button>
          ) : null}
          {gespraech.status === "accepted" && brauchtFreigabe ? (
            <Button
              variant="contained"
              onClick={() => onZug("confirm-release")}
              disabled={gesperrt}
            >
              {t("gespraeche.freigabeBestaetigen")}
            </Button>
          ) : null}
          {laeuftNoch ? (
            <Button
              variant="text"
              onClick={() => onZug("decline")}
              disabled={gesperrt}
            >
              {t("allgemein.ablehnen")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
