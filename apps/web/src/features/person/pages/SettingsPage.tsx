import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

import { ConsentSwitch, LoadingBlock, PageShell } from "../../../shared/components/ui";
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
const SCHALTER: { schluessel: keyof Benachrichtigungswahl; label: string; hinweis: string }[] = [
  {
    schluessel: "market_request",
    label: "Wenn ein Unternehmen deinen Marktstatus sehen möchte",
    hinweis: "Ohne diese Nachricht erfährst du davon nur, wenn du zufällig vorbeischaust.",
  },
  {
    schluessel: "resume_request",
    label: "Wenn ein Unternehmen nach deinem Lebenslauf fragt",
    hinweis: "Auch hier entscheidest du — aber nur, wenn du von der Frage weißt.",
  },
  {
    schluessel: "transfer_update",
    label: "Wenn sich bei einem Gespräch etwas tut",
    hinweis: "Interesse, Angebot, Rückzug.",
  },
  {
    schluessel: "application_update",
    label: "Wenn sich bei einer Bewerbung etwas tut",
    hinweis: "Nur Züge des Unternehmens — deine eigenen kennst du.",
  },
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
  const sitzung = useAppSelector((state) => state.auth.session);
  const status = useAppSelector((state) => state.auth.status);
  const [fehler, setFehler] = useState<string | null>(null);
  const [speichert, setSpeichert] = useState(false);

  const wahl = useAsync(
    (signal) => ladeWahl(signal),
    [sitzung?.userId],
    sitzung !== null
  );

  if (status === "anonymous") {
    return <AnmeldungNoetig titel="Einstellungen" zweck="deine Einstellungen zu ändern" />;
  }

  const unbekannt = wahl.laedt || wahl.wert === null;
  const werte = wahl.wert ?? ALLES_AN;

  async function umschalten(schluessel: keyof Benachrichtigungswahl, neu: boolean) {
    const naechste = { ...werte, [schluessel]: neu };
    setSpeichert(true);
    wahl.setze(naechste);

    const ergebnis = await speichereWahl(naechste);
    setSpeichert(false);

    if (ergebnis.ok) {
      setFehler(null);
      wahl.setze(ergebnis.wahl);
    } else {
      setFehler(ergebnis.error.detail);
      // Zurueck auf das, was der Dienst wirklich hat — die Anzeige darf keine
      // Einstellung behaupten, die nicht gespeichert wurde.
      wahl.erneut();
    }
  }

  return (
    <PageShell
      title="Einstellungen"
      narrow
      lead={
        "Was in einer Mail steht, ist bewusst wenig: „Es gibt etwas Neues für dich.“ "
        + "Kein Firmenname, kein Vorgang, keine Anzahl. Eine Mail kann in einem Postfach "
        + "landen, das nicht nur dir gehört — und dann wäre der Satz, der sie nützlicher "
        + "machte, genau der, der dich den Arbeitsplatz kostet. Was es ist, steht hinter "
        + "der Anmeldung."
      }
    >
      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Benachrichtigungen
          </Typography>

          {fehler !== null ? <Alert severity="error" sx={{ mb: 2 }}>{fehler}</Alert> : null}

          {wahl.laedt ? <LoadingBlock label="Einstellungen werden geladen…" /> : null}

          {wahl.wert === null && !wahl.laedt ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              Deine Einstellungen sind gerade nicht abrufbar. Solange das so ist, ändern die
              Schalter nichts — sonst würdest du etwas speichern, das du nie eingestellt hast.
            </Alert>
          ) : null}

          <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
            {SCHALTER.map((eintrag) => (
              <ConsentSwitch
                key={eintrag.schluessel}
                label={eintrag.label}
                hint={eintrag.hinweis}
                checked={werte[eintrag.schluessel]}
                disabled={speichert || unbekannt}
                onChange={(neu) => void umschalten(eintrag.schluessel, neu)}
              />
            ))}
          </Box>

          <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
            Höchstens eine Mail pro Stunde, egal wie viel passiert — auch der Zeitpunkt einer
            Mail verrät etwas.
          </Typography>
        </CardContent>
      </Card>
    </PageShell>
  );
}
