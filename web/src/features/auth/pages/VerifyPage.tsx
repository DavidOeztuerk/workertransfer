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
        meldung: t("verify.linkMissing"),
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
        title={t("verify.inProgressTitle")}
        lead={t("verify.lead")}
      >
        <Typography role="status" color="text.secondary">
          {t("verify.inProgressText")}
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
      //
      // The third outcome — account confirmed, company refused — needs its own
      // branch below; without it the page reads green while half the intent was
      // dropped, and nobody learns why no company is there later. There is no
      // second attempt: the token is spent and there is no "create a company"
      // button.
      <AuthCard
        title={t("verify.doneTitle")}
        lead={
          state.unternehmen !== undefined
            ? t("verify.doneWithCompany", { name: state.unternehmen })
            : t("verify.doneWithoutCompany")
        }
      >
        <Box sx={{ display: "grid", gap: 2 }}>
          {state.unternehmenFehler === "domain_already_claimed" ? (
            <Alert severity="warning">
              {t("verify.domainTaken")}
            </Alert>
          ) : state.unternehmenFehler !== undefined ? (
            <Alert severity="warning">
              {t("verify.companyFailed")}
            </Alert>
          ) : null}

          <Typography variant="body2">
            <Link component={RouterLink} to="/login">
              {t("verify.toLogin")}
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
      title={t("verify.failedTitle")}
      lead={t("verify.lead")}
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
              label={t("verify.email")}
              type="email"
              autoComplete="username"
              helperText={t("verify.emailHint")}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            <Button type="submit" variant="contained" disabled={running}>
              {running
                ? t("verify.newLinkSending")
                : t("verify.newLink")}
            </Button>

            {gescheitert ? (
              <Alert severity="error">
                {t("verify.newLinkFailed")}
              </Alert>
            ) : null}
            {gesendet ? (
              <Alert
                // A confirmation must not interrupt; MUI's Alert would set role="alert".
                role="status"
                severity="success"
              >
                {t("verify.newLinkSent")}
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </Box>
    </AuthCard>
  );
}
