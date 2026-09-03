import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { AuthCard } from "../components/AuthCard";
import { useTranslation } from "react-i18next";
import {
  type BestaetigungsErgebnis,
  bestaetigeEmail,
  sendeBestaetigungErneut,
} from "../api/registrierung";

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
  const started = bestaetigeEmail(token);
  versuche.set(token, started);
  return started;
}

export function VerifyPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";

  const [state, setZustand] = useState<Zustand>({ phase: "laeuft" });

  useEffect(() => {
    if (token === "") {
      setZustand({
        phase: "gescheitert",
        abgelaufen: false,
        meldung: t("bestaetigung.fehltLink"),
      });
      return;
    }
    void bestaetigeEinmal(token).then((result) => {
      setZustand(
        result.ok
          ? { phase: "fertig", ...result }
          : {
              phase: "gescheitert",
              abgelaufen: result.abgelaufen,
              meldung: result.meldung,
            },
      );
    });
  }, [token, t]);

  if (state.phase === "laeuft") {
    return (
      <AuthCard
        title={t("bestaetigung.laeuftTitel")}
        lead={t("bestaetigung.lead")}
      >
        <Typography role="status" color="text.secondary">
          {t("bestaetigung.laeuftText")}
        </Typography>
      </AuthCard>
    );
  }

  if (state.phase === "fertig") {
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
        title={t("bestaetigung.fertigTitel")}
        lead={
          state.unternehmen !== undefined
            ? t("bestaetigung.fertigMitFirma", { name: state.unternehmen })
            : t("bestaetigung.fertigOhneFirma")
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
          {state.unternehmenFehler === "domain_already_claimed" ? (
            <Alert severity="warning">
              {t("bestaetigung.domainVergeben")}
            </Alert>
          ) : state.unternehmenFehler !== undefined ? (
            <Alert severity="warning">
              {t("bestaetigung.firmaGescheitert")}
            </Alert>
          ) : null}

          <Typography variant="body2">
            <Link component={RouterLink} to="/login">
              {t("bestaetigung.zurAnmeldung")}
            </Link>
          </Typography>
        </Box>
      </AuthCard>
    );
  }

  return (
    <Gescheitert abgelaufen={state.abgelaufen} meldung={state.meldung} />
  );
}

/**
 * Der Fehlschlag — und nur bei einem ABGELAUFENEN Link ein neuer.
 *
 * Ein ungültiger Link wird auch beim zweiten Versuch nicht gültig; dort gäbe es
 * nichts anzubieten ausser einer Sackgasse mit Knopf.
 */
function Gescheitert({
  abgelaufen,
  meldung,
}: {
  abgelaufen: boolean;
  meldung: string;
}) {
  const { t } = useTranslation();
  const [email, setEmail] = useState("");
  const [running, setLaeuft] = useState(false);
  const [gesendet, setGesendet] = useState(false);
  const [gescheitert, setGescheitert] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setLaeuft(true);
    setGescheitert(false);
    const result = await sendeBestaetigungErneut(email);
    setLaeuft(false);
    if (result.ok) setGesendet(true);
    else setGescheitert(true);
  }

  return (
    <AuthCard
      title={t("bestaetigung.gescheitertTitel")}
      lead={t("bestaetigung.lead")}
    >
      <Box sx={{ display: "grid", gap: 2 }}>
        <Alert severity="error">{meldung}</Alert>

        {abgelaufen ? (
          <Box
            component="form"
            onSubmit={submit}
            noValidate
            sx={{ display: "grid", gap: 2 }}
          >
            <TextField
              label={t("bestaetigung.email")}
              type="email"
              autoComplete="username"
              helperText={t("bestaetigung.emailHinweis")}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            <Button type="submit" variant="contained" disabled={running}>
              {running
                ? t("bestaetigung.neuerLinkLaeuft")
                : t("bestaetigung.neuerLink")}
            </Button>

            {gescheitert ? (
              <Alert severity="error">
                {t("bestaetigung.neuerLinkGescheitert")}
              </Alert>
            ) : null}
            {/* Eine Bestätigung unterbricht nicht — deshalb `role="status"`. */}
            {gesendet ? (
              <Alert role="status" severity="success">
                {t("bestaetigung.neuerLinkGesendet")}
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </Box>
    </AuthCard>
  );
}
