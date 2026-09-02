import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AnmeldungNoetig } from "../components/AnmeldungNoetig";
import { loeschungVerlangen } from "../api/erasure";

const WAS_VERSCHWINDET = [
  "Dein Konto: E-Mail-Adresse, Passwort, Anzeigename.",
  "Dein Profil mit Überschrift, Text, Ort und Fähigkeiten.",
  "Dein Lebenslauf mit allen Stationen und Ausbildungen.",
  "Deine Arbeiten samt hochgeladener Dateien.",
  "Dein Marktstatus und alle Gespräche über einen Wechsel.",
  "Deine Bewerbungen samt der Anschreiben, die du geschrieben hast.",
  "Deine GitHub-Verbindung.",
  "Deine Benachrichtigungs-Einstellungen.",
];

function Abschnitt({
  titel,
  children,
}: {
  titel: string;
  children: React.ReactNode;
}) {
  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {titel}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

/**
 * <c>/delete-account</c> — wo das Versprechen einem Menschen gegeben wird.
 *
 * <strong>Alles Wichtige steht VOR dem Knopf</strong>, und der unangenehmste
 * Satz steht laut da: auch die Bewerbung, über die jemand <em>eingestellt</em>
 * wurde, verschwindet aus der Liste des Unternehmens. Eine Löschzusage, deren
 * Ausnahmen man erst hinterher erfährt, ist keine.
 *
 * <strong>Zwei Schritte, inline, kein Dialog.</strong> Ein Dialog nähme die
 * Seite weg, auf der gerade steht, was verschwindet — und genau das soll beim
 * zweiten Klick noch lesbar sein. Der zweite Knopf ist anders formuliert als
 * der erste, damit niemand zweimal dasselbe klickt.
 *
 * <strong>Kein getipptes Wort, keine Wartezeit, kein erneutes Passwort.</strong>
 * Wer löschen will, darf. Jede zusätzliche Hürde wäre eine Bremse gegen die
 * Person, nicht für sie.
 *
 * <strong>Die Falle, die hier schon zugeschnappt ist:</strong> bei Erfolg ist
 * die Sitzung weg, die Hülle zeichnet die Seite also ohne Prinzipal neu. Der
 * angenommene Zustand muss deshalb VOR der Anmeldeaufforderung geprüft werden —
 * sonst liest die Person direkt nach dem Löschen „Bitte anmelden“.
 */
export function DeleteAccountPage() {
  const status = useAppSelector((state) => state.auth.status);
  const firmen = useAppSelector((state) => state.auth.memberships);

  const [fragt, setFragt] = useState(false);
  const [laeuft, setLaeuft] = useState(false);
  const [fehler, setFehler] = useState<string | null>(null);
  const [angenommen, setAngenommen] = useState(false);

  // ZUERST. Siehe oben — nach dem Löschen gibt es keinen Prinzipal mehr.
  if (angenommen) {
    return (
      <PageShell title="Konto löschen" narrow>
        <Card>
          <CardContent>
            {/* `info` und nicht `error`: das ist eine Bestätigung. Ein roter
                Kasten hier läse sich, als sei etwas schiefgegangen. */}
            <Alert severity="info" sx={{ mb: 2 }} role="status">
              <strong>Deine Löschung ist angenommen und läuft.</strong> Du bist
              abgemeldet, und unter deinem Namen passiert ab jetzt nichts mehr.
              Wir schicken dir <strong>eine einzige E-Mail</strong>, sobald
              alles gelöscht ist — bis dahin gibt es hier nichts mehr zu sehen.
            </Alert>
            <Typography variant="body2" color="text.secondary">
              Dass es etwas dauert, hat einen einfachen Grund: deine Daten
              liegen bei mehreren Diensten, und jeder muss den Auftrag
              bestätigen. Erreicht er einen davon nicht, gilt die Löschung nicht
              als fertig — und du bekommst keine Nachricht, die nicht stimmt.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (status !== "authenticated") {
    return (
      <AnmeldungNoetig titel="Konto löschen" zweck="dein Konto zu löschen" />
    );
  }

  async function loeschen() {
    setLaeuft(true);
    const ergebnis = await loeschungVerlangen();
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setAngenommen(true);
    } else {
      setFehler(ergebnis.error.detail);
    }
  }

  return (
    <PageShell
      title="Konto löschen"
      narrow
      lead={
        "Hier wird dein Konto gelöscht — unwiderruflich. Es gibt keinen Papierkorb und keine " +
        "Frist, in der du es dir noch anders überlegen kannst. Was hier steht, gilt; deshalb " +
        "steht es hier und nicht im Kleingedruckten."
      }
    >
      <Abschnitt titel="Was gelöscht wird">
        <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
          {WAS_VERSCHWINDET.map((zeile) => (
            <Typography component="li" key={zeile} sx={{ mb: 0.5 }}>
              {zeile}
            </Typography>
          ))}
        </Box>
        <Typography variant="body2" color="text.secondary">
          <strong>Es bleibt nichts davon stehen.</strong> Auch nicht die
          Bewerbung, über die du <strong>eingestellt</strong> wurdest — auch die
          verschwindet aus der Liste des Unternehmens. Das ist so gewollt: die
          Unterlage über ein Arbeitsverhältnis ist dein Vertrag beim
          Arbeitgeber, nicht eine Zeile bei einer Vermittlungsplattform.
        </Typography>
      </Abschnitt>

      <Abschnitt titel="Was bleibt — und warum">
        <Typography variant="body2" color="text.secondary">
          Ein Nachweis darüber, <em>dass</em> gelöscht wurde: welche Freigaben
          unter deiner Kennung einmal erteilt und wann sie zurückgenommen
          wurden. Ohne ihn ließe sich nicht mehr belegen, dass wir deiner
          Löschung nachgekommen sind. Was du selbst hineingeschrieben hast —
          etwa der Grund für eine zurückgenommene Freigabe — wird dabei
          entfernt. Übrig bleiben Kennungen und Zeitpunkte, die auf keinen
          Menschen mehr zeigen.
        </Typography>
      </Abschnitt>

      {firmen.length > 0 ? (
        <Abschnitt titel="Und deine Unternehmen">
          <Box component="ul" sx={{ pl: 2.5, m: 0, mb: 2 }}>
            {firmen.map((firma) => (
              <Typography component="li" key={firma.id} sx={{ mb: 0.5 }}>
                <strong>{firma.name}</strong>{" "}
                {firma.role === "admin" ? (
                  <>
                    — bist du die <em>letzte</em> Person mit Verwaltungsrechten,
                    wird das Unternehmen <strong>stillgelegt</strong> und seine
                    Stellenanzeigen werden zurückgezogen. Eine Anzeige, hinter
                    der niemand mehr steht, ist schlechter als keine:
                    Bewerbungen liefen an niemanden.
                  </>
                ) : (
                  <>
                    — deine Mitgliedschaft endet. Das Unternehmen selbst bleibt,
                    wie es ist.
                  </>
                )}
              </Typography>
            ))}
          </Box>
          <Typography variant="body2" color="text.secondary">
            Das hält deine Löschung <strong>nicht auf</strong>. Du musst
            niemandem vorher etwas übergeben: dein Recht auf Löschung hängt
            nicht daran, ob sich jemand anderes um ein Unternehmen kümmert.
          </Typography>
        </Abschnitt>
      ) : null}

      <Abschnitt titel="Wie es abläuft">
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          Mit dem Klick bist du <strong>sofort abgemeldet</strong> und deine
          Sitzungen sind widerrufen. Die Löschung selbst läuft danach weiter —
          sie geht <strong>nicht sofort</strong> durch, weil deine Daten bei
          mehreren Diensten liegen und jeder einzeln bestätigen muss. Du
          bekommst genau eine E-Mail, wenn alles erledigt ist.
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Du kannst deine Daten vorher{" "}
          <Link component={RouterLink} to="/my-data">
            unter „Meine Daten“
          </Link>{" "}
          herunterladen. Musst du aber nicht — wer löschen will, darf das ohne
          Umweg.
        </Typography>
      </Abschnitt>

      <Card>
        <CardContent>
          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          {fragt ? (
            <>
              <Alert severity="warning" sx={{ mb: 2 }}>
                Letzte Frage: dein Konto und alles oben Genannte werden
                gelöscht. Das lässt sich nicht rückgängig machen.
              </Alert>
              <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
                <Button
                  variant="contained"
                  color="error"
                  onClick={() => void loeschen()}
                  disabled={laeuft}
                >
                  {laeuft ? "Wird angenommen…" : "Ja, endgültig löschen"}
                </Button>
                <Button
                  variant="text"
                  onClick={() => {
                    setFragt(false);
                    setFehler(null);
                  }}
                  disabled={laeuft}
                >
                  Abbrechen
                </Button>
              </Box>
            </>
          ) : (
            <Button
              variant="outlined"
              color="error"
              onClick={() => setFragt(true)}
            >
              Konto löschen
            </Button>
          )}
        </CardContent>
      </Card>
    </PageShell>
  );
}
