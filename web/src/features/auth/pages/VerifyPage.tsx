import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { AuthCard } from "../components/AuthCard";
import {
  type BestaetigungsErgebnis,
  bestaetigeEmail,
  sendeBestaetigungErneut,
} from "../api/registrierung";

const LEAD = "Die Bestätigung stellt sicher, dass niemand deine Adresse für sich benutzt.";

type Zustand =
  | { phase: "laeuft" }
  | { phase: "fertig"; unternehmen?: string; unternehmenFehler?: string }
  | { phase: "gescheitert"; abgelaufen: boolean; meldung: string };

/**
 * Je Token genau ein Aufruf — modulweit, nicht je Aufbau der Komponente.
 *
 * Der Bestätigungstoken ist einmalig: der zweite Aufruf verbraucht ihn nicht,
 * er scheitert mit „ungültig". Und weil beide Antworten in denselben Zustand
 * schreiben, entscheidet die zuletzt eintreffende, was die Person sieht — im
 * schlechten Fall „Bestätigung fehlgeschlagen", während ihr Konto gerade
 * freigeschaltet wurde. Im E2E-Lauf waren das 2 von 120 Aufrufen.
 *
 * Ein `useRef` reicht dafür NICHT: er stirbt mit der Komponente, und genau ein
 * zweiter Aufbau ist der Fall (HMR, StrictMode, Reload, ein neu einhängender
 * Router). Gemerkt wird deshalb die <strong>Zusage</strong>, nicht nur die
 * Tatsache — so bekommt der zweite Aufbau dasselbe Ergebnis wie der erste,
 * statt gar keines.
 */
const versuche = new Map<string, Promise<BestaetigungsErgebnis>>();

function bestaetigeEinmal(token: string): Promise<BestaetigungsErgebnis> {
  const laufend = versuche.get(token);
  if (laufend !== undefined) return laufend;
  const gestartet = bestaetigeEmail(token);
  versuche.set(token, gestartet);
  return gestartet;
}

export function VerifyPage() {
  const [suchparameter] = useSearchParams();
  const token = suchparameter.get("token") ?? "";

  const [zustand, setZustand] = useState<Zustand>({ phase: "laeuft" });

  useEffect(() => {
    if (token === "") {
      setZustand({
        phase: "gescheitert",
        abgelaufen: false,
        meldung: "Es fehlt ein Bestätigungslink.",
      });
      return;
    }
    void bestaetigeEinmal(token).then((ergebnis) => {
      setZustand(
        ergebnis.ok
          ? { phase: "fertig", ...ergebnis }
          : { phase: "gescheitert", abgelaufen: ergebnis.abgelaufen, meldung: ergebnis.meldung }
      );
    });
  }, [token]);

  if (zustand.phase === "laeuft") {
    return (
      <AuthCard title="Wird bestätigt…" lead={LEAD}>
        <Typography role="status" color="text.secondary">
          Einen Moment bitte.
        </Typography>
      </AuthCard>
    );
  }

  if (zustand.phase === "fertig") {
    return (
      // Die Überschrift ist für ALLE drei Erfolge dieselbe, und sie ist in allen
      // dreien wahr: bestätigt ist die E-MAIL. Was aus dem Unternehmen wurde,
      // ist eine zweite Aussage darunter.
      //
      // Sie bleibt ausserdem buchstabengetreu „E-Mail bestätigt", weil die
      // E2E-Hilfe genau darauf prüft (`exact: true`) — ein weiches Muster traf
      // sonst auch „Wird bestätigt…", und der Test war zufrieden, während die
      // Bestätigung noch lief.
      <AuthCard
        title="E-Mail bestätigt"
        lead={
          zustand.unternehmen !== undefined
            ? `Dein Konto ist freigeschaltet, und ${zustand.unternehmen} ist angelegt — du bist dort Administrator.`
            : "Dein Konto ist freigeschaltet."
        }
      >
        <Box sx={{ display: "grid", gap: 2 }}>
          {/* Der dritte Ausgang: Konto bestätigt, Unternehmen abgelehnt. Ohne
              diesen Zweig wäre die Seite grün, während die halbe Absicht
              verpufft ist — und niemand wüsste, warum später kein Unternehmen
              da ist.

              Es gibt keinen zweiten Versuch: der Token ist verbraucht, und
              einen Knopf „Unternehmen anlegen" gibt es nicht mehr. Der richtige
              Weg ist ohnehin ein anderer — wer eine Adresse auf dieser Domain
              hat, hat dort Kollegen. */}
          {zustand.unternehmenFehler === "domain_already_claimed" ? (
            <Alert severity="warning">
              Dein Konto ist da, das Unternehmen nicht: für deine Domain gibt es hier schon
              eines. Bitte jemanden aus deinem Unternehmen, dich einzuladen — dann handelst du
              unter demselben Dach.
            </Alert>
          ) : zustand.unternehmenFehler !== undefined ? (
            <Alert severity="warning">
              Dein Konto ist da, das Unternehmen konnte nicht angelegt werden. Bitte jemanden
              aus deinem Unternehmen, dich einzuladen.
            </Alert>
          ) : null}

          <Typography variant="body2">
            <Link component={RouterLink} to="/login">
              Zur Anmeldung
            </Link>
          </Typography>
        </Box>
      </AuthCard>
    );
  }

  return <Gescheitert abgelaufen={zustand.abgelaufen} meldung={zustand.meldung} />;
}

/**
 * Der Fehlschlag — und nur bei einem ABGELAUFENEN Link ein neuer.
 *
 * Ein ungültiger Link wird auch beim zweiten Versuch nicht gültig; dort gäbe es
 * nichts anzubieten ausser einer Sackgasse mit Knopf.
 */
function Gescheitert({ abgelaufen, meldung }: { abgelaufen: boolean; meldung: string }) {
  const [email, setEmail] = useState("");
  const [laeuft, setLaeuft] = useState(false);
  const [gesendet, setGesendet] = useState(false);
  const [gescheitert, setGescheitert] = useState(false);

  async function absenden(ereignis: React.FormEvent) {
    ereignis.preventDefault();
    setLaeuft(true);
    setGescheitert(false);
    const ergebnis = await sendeBestaetigungErneut(email);
    setLaeuft(false);
    if (ergebnis.ok) setGesendet(true);
    else setGescheitert(true);
  }

  return (
    <AuthCard title="Bestätigung fehlgeschlagen" lead={LEAD}>
      <Box sx={{ display: "grid", gap: 2 }}>
        <Alert severity="error">{meldung}</Alert>

        {abgelaufen ? (
          <Box component="form" onSubmit={absenden} noValidate sx={{ display: "grid", gap: 2 }}>
            <TextField
              label="E-Mail"
              type="email"
              autoComplete="username"
              helperText="An diese Adresse schicken wir einen neuen Link."
              value={email}
              onChange={(ereignis) => setEmail(ereignis.target.value)}
              required
            />
            <Button type="submit" variant="contained" disabled={laeuft}>
              {laeuft ? "Wird gesendet…" : "Neuen Link senden"}
            </Button>

            {gescheitert ? (
              <Alert severity="error">
                Die E-Mail konnte gerade nicht angefordert werden. Versuch es später noch
                einmal.
              </Alert>
            ) : null}
            {/* Eine Bestätigung unterbricht nicht — deshalb `role="status"`. */}
            {gesendet ? (
              <Alert role="status" severity="success">
                Falls nötig, ist die E-Mail unterwegs.
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </Box>
    </AuthCard>
  );
}
