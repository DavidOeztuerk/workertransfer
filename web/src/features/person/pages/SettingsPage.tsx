import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

import {
  ConsentSwitch,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { useAsync } from "../lib/useAsync";
import {
  ALLES_AN,
  type Benachrichtigungswahl,
  ladeWahl,
  speichereWahl,
} from "../api/settings";

/**
 * Die vier Schalter, wörtlich aus dem alten Code übernommen.
 *
 * Der Hinweis unter jedem sagt, was das Abschalten <em>kostet</em>, nicht was
 * der Schalter tut. Wer „Marktstatus-Anfragen" abschaltet, soll wissen, dass er
 * von der Anfrage dann nur erfährt, wenn er zufällig vorbeischaut.
 */
const SCHALTER: {
  key: keyof Benachrichtigungswahl;
  name: string;
}[] = [
  { key: "market_request", name: "markt" },
  { key: "resume_request", name: "lebenslauf" },
  { key: "transfer_update", name: "transfer" },
  { key: "application_update", name: "bewerbung" },
];

/**
 * <c>/settings</c> — was in einer Mail stehen darf.
 *
 * <strong>Der Text oben ist die eigentliche Aussage der Seite</strong> und
 * wurde wörtlich übernommen: eine Mail sagt nur, <em>dass</em> es etwas Neues
 * gibt. Kein Firmenname, kein Vorgang, keine Anzahl. Eine Mail kann in einem
 * Postfach landen, das nicht nur der Person gehört — und dann wäre der Satz,
 * der sie nützlicher machte, genau der, der jemanden den Arbeitsplatz kostet.
 *
 * <strong>Unbekannt ist nicht aus.</strong> Antwortet der Dienst nicht, stehen
 * die Schalter zwar auf ihrer Voreinstellung, sind aber <em>gesperrt</em>. Wer
 * sie in diesem Zustand bedienen könnte, speicherte eine Einstellung, die er
 * nie getroffen hat — und überschriebe womöglich eine, die er einmal getroffen
 * hat.
 */
export function SettingsPage() {
  const { t } = useTranslation();
  const session = useAppSelector((state) => state.auth.session);
  const status = useAppSelector((state) => state.auth.status);
  const [fehler, setFehler] = useState<string | null>(null);
  const [saving, setSpeichert] = useState(false);

  const choice = useAsync(
    (signal) => ladeWahl(signal),
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <AnmeldungNoetig
        titel={t("einstellungen.titel")}
        satz="einstellungen.anmelden"
      />
    );
  }

  const unbekannt = choice.laedt || choice.value === null;
  const werte = choice.value ?? ALLES_AN;

  async function umschalten(
    key: keyof Benachrichtigungswahl,
    neu: boolean,
  ) {
    const nextValue = { ...werte, [key]: neu };
    setSpeichert(true);
    choice.setze(nextValue);

    const result = await speichereWahl(nextValue);
    setSpeichert(false);

    if (result.ok) {
      setFehler(null);
      choice.setze(result.choice);
    } else {
      setFehler(result.error.detail);
      // Zurueck auf das, was der Dienst wirklich hat — die Anzeige darf keine
      // Einstellung behaupten, die nicht gespeichert wurde.
      choice.again();
    }
  }

  return (
    <PageShell
      title={t("einstellungen.titel")}
      narrow
      lead={t("einstellungen.lead")}
    >
      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("einstellungen.benachrichtigungen")}
          </Typography>

          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {choice.laedt ? (
            <LoadingBlock label={t("einstellungen.laden")} />
          ) : null}

          {choice.value === null && !choice.laedt ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {t("einstellungen.nichtAbrufbar")}
            </Alert>
          ) : null}

          <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
            {SCHALTER.map((entry) => (
              <ConsentSwitch
                key={entry.key}
                label={t(`einstellungen.${entry.name}Label`)}
                hint={t(`einstellungen.${entry.name}Hinweis`)}
                checked={werte[entry.key]}
                disabled={saving || unbekannt}
                onChange={(neu) => void umschalten(entry.key, neu)}
              />
            ))}
          </Box>

          <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
            {t("einstellungen.takt")}
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
