import { useId, useMemo, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import {
  Link as RouterLink,
  Navigate,
  useSearchParams,
} from "react-router-dom";
import Link from "@mui/material/Link";
import { Trans, useTranslation } from "react-i18next";

import { useAppSelector } from "../../../core/store/hooks";
import { BerufsfeldSelect } from "../../../shared/components/ui";
import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";
import { AuthCard } from "../components/AuthCard";
import { AuthModeTabs } from "../components/AuthModeTabs";
import {
  isPublicEmailDomain,
  registriere,
  sendeBestaetigungErneut,
} from "../api/registrierung";

type Art = "person" | "company";

/**
 * Ein Konto anlegen — für sich selbst oder für ein Unternehmen.
 *
 * <strong>Kein Mandantenfeld.</strong> Der Client nennt hier höchstens einen
 * NAMEN, nie eine Zugehörigkeit. Die Domain leitet der Server aus der
 * bestätigten Adresse ab — was der Client nicht senden kann, kann er nicht
 * fälschen (ADR-0017/0018/0019).
 */
export function RegisterPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const [searchParams] = useSearchParams();

  // Die Voreinstellung ist „person", und zwar ausdrücklich: registrieren ist
  // der Akt einer natürlichen Person (ADR-0017), und der Normalfall auf einem
  // Transfermarkt ist jemand ohne Unternehmen. Die Hero-Knöpfe der Startseite
  // tragen die andere Absicht als `?as=company` mit — ohne diese Vorauswahl
  // landet jemand, der „Als Unternehmen entdecken" klickt, im Personenformular
  // und merkt es erst nach der Bestätigungsmail.
  const [art, setArt] = useState<Art>(
    searchParams.get("as") === "company" ? "company" : "person",
  );

  const [unternehmensname, setUnternehmensname] = useState("");
  const [email, setEmail] = useState("");
  const [passwort, setPasswort] = useState("");
  const [anzeigename, setAnzeigename] = useState("");
  const [vorname, setVorname] = useState("");
  const [nachname, setNachname] = useState("");

  // Kein Pflichtfeld, und die Vorauswahl ist leer (ADR-0039). Wer nichts
  // wählt, bekommt die heutige, neutrale Ansicht — es wird niemandem etwas
  // weggenommen, der sich hier nicht festlegen will.
  const [berufsfeld, setBerufsfeld] = useState<BerufsfeldWahl>(null);

  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);
  const [abgeschickt, setAbgeschickt] = useState(false);

  const personHinweis = useId();
  const firmaHinweis = useId();

  // Nur wenn überhaupt eine Adresse dasteht: `isPublicEmailDomain("")` urteilte
  // über einen leeren Domainteil und blitzte beim ersten getippten Zeichen auf.
  const freemail = useMemo(
    () =>
      art === "company" && email.includes("@") && isPublicEmailDomain(email),
    [art, email],
  );

  if (status === "authenticated") return <Navigate to="/overview" replace />;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setFehler(null);
    setLaeuft(true);
    const result = await registriere({
      email,
      password: passwort,
      anzeigename,
      ...(vorname.trim() ? { vorname: vorname.trim() } : {}),
      ...(nachname.trim() ? { nachname: nachname.trim() } : {}),
      ...(art === "company" ? { unternehmensname } : {}),
      ...(berufsfeld !== null ? { berufsfeld } : {}),
    });
    setLaeuft(false);
    if (result.ok) setAbgeschickt(true);
    else setFehler(result.meldung);
  }

  if (abgeschickt) return <FastGeschafft email={email} />;

  return (
    <AuthCard title={t("registrierung.titel")} lead={t("registrierung.lead")}>
      <AuthModeTabs current="register" />

      <Box
        component="form"
        onSubmit={submit}
        noValidate
        sx={{ display: "grid", gap: 2.5 }}
      >
        <FormControl component="fieldset" sx={{ display: "grid", gap: 1 }}>
          <FormLabel component="legend" sx={{ mb: 0.5 }}>
            {t("registrierung.wofuer")}
          </FormLabel>
          {/* Der Hinweis steht NEBEN der Beschriftung, nicht darin: sonst hiesse
              die Auswahl für einen Screenreader „Für ein Unternehmen Braucht
              deine Arbeitsadresse — …", und ein Test, der sie beim Namen nennt,
              fände sie nicht mehr. */}
          <RadioGroup
            name="art"
            value={art}
            onChange={(event) =>
              setArt(event.target.value === "company" ? "company" : "person")
            }
          >
            <Box>
              <FormControlLabel
                value="person"
                control={
                  <Radio
                    slotProps={{ input: { "aria-describedby": personHinweis } }}
                  />
                }
                label={t("registrierung.fuerMich")}
              />
              <Typography
                id={personHinweis}
                variant="body2"
                color="text.secondary"
                sx={{ ml: 4 }}
              >
                {t("registrierung.fuerMichHinweis")}
              </Typography>
            </Box>
            <Box sx={{ mt: 1 }}>
              <FormControlLabel
                value="company"
                control={
                  <Radio
                    slotProps={{ input: { "aria-describedby": firmaHinweis } }}
                  />
                }
                label={t("registrierung.fuerFirma")}
              />
              <Typography
                id={firmaHinweis}
                variant="body2"
                color="text.secondary"
                sx={{ ml: 4 }}
              >
                {t("registrierung.fuerFirmaHinweis")}
              </Typography>
            </Box>
          </RadioGroup>
        </FormControl>

        <TextField
          label={t("registrierung.email")}
          type="email"
          autoComplete="username"
          helperText={
            art === "company"
              ? t("registrierung.emailHinweisFirma")
              : t("registrierung.emailHinweisPerson")
          }
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
        />

        {art === "company" ? (
          <TextField
            label={t("registrierung.firmenname")}
            autoComplete="organization"
            helperText={t("registrierung.firmennameHinweis")}
            value={unternehmensname}
            onChange={(event) => setUnternehmensname(event.target.value)}
            required
          />
        ) : null}

        {/* Sofort, nicht erst nach der Bestätigungsmail: sonst erfährt jemand
            erst zwei Schritte später, dass sein Weg nicht geht. Die Absage
            spricht weiterhin der SERVER aus (422) — hier wird nur sichtbar
            gemacht, was ohnehin gilt. */}
        {freemail ? (
          <Alert severity="warning">
            {t("registrierung.freemail")}
          </Alert>
        ) : null}

        <TextField
          label={t("registrierung.passwort")}
          type="password"
          autoComplete="new-password"
          helperText={t("registrierung.passwortHinweis")}
          value={passwort}
          onChange={(event) => setPasswort(event.target.value)}
          required
        />
        <TextField
          label={t("registrierung.anzeigename")}
          autoComplete="nickname"
          helperText={t("registrierung.anzeigenameHinweis")}
          value={anzeigename}
          onChange={(event) => setAnzeigename(event.target.value)}
          required
        />
        <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 1fr" } }}>
          <TextField
            label={t("zivil.vorname")}
            autoComplete="given-name"
            value={vorname}
            onChange={(event) => setVorname(event.target.value)}
          />
          <TextField
            label={t("zivil.nachname")}
            autoComplete="family-name"
            value={nachname}
            onChange={(event) => setNachname(event.target.value)}
          />
        </Box>
        <Typography variant="body2" color="text.secondary">
          {t("zivil.klarnameHinweis")}
        </Typography>

        {/* Am Ende und nicht oben: die Angabe ist freiwillig, und was freiwillig
            ist, gehört nicht vor die Felder, ohne die es nicht weitergeht. */}
        <BerufsfeldSelect wert={berufsfeld} onChange={setBerufsfeld} />

        {fehler !== null ? <Alert severity="error">{fehler}</Alert> : null}

        <Button
          type="submit"
          variant="contained"
          size="large"
          disabled={running || freemail}
        >
          {running ? t("registrierung.laeuft") : t("registrierung.knopf")}
        </Button>
      </Box>
    </AuthCard>
  );
}

/**
 * Nach dem Absenden.
 *
 * <strong>Dieselbe Nachricht, ob die Adresse neu war oder schon existierte.</strong>
 * Der Server verrät es nicht (er antwortet auch bekannten Adressen 201 und
 * schickt der echten Inhaberin eine Warnung), und beides ist wahr: es wurde
 * eine E-Mail geschickt. Ein „gibt es schon" hier wäre der Aufzählungskanal,
 * den `/auth/register` gerade schliesst.
 */
function FastGeschafft({ email }: { email: string }) {
  const { t } = useTranslation();
  const [running, setLaeuft] = useState(false);
  const [gesendet, setGesendet] = useState(false);
  const [gescheitert, setGescheitert] = useState(false);

  async function erneutSenden() {
    setLaeuft(true);
    setGescheitert(false);
    const result = await sendeBestaetigungErneut(email);
    setLaeuft(false);
    if (result.ok) setGesendet(true);
    else setGescheitert(true);
  }

  return (
    <AuthCard
      title={t("registrierung.fastGeschafft")}
      lead={t("registrierung.fastGeschafftLead")}
    >
      <Box sx={{ display: "grid", gap: 2 }}>
        {/* Ohne den Fehlerzweig tat dieser Knopf bei einem Netzfehler sichtbar
            NICHTS: der Aufruf warf, die Zusage wurde nie gesetzt. Und ein 429
            von der Bremse (3/min) erzeugte die Zusage „ist unterwegs", obwohl
            nichts unterwegs war. */}
        <Button variant="outlined" onClick={erneutSenden} disabled={running}>
          {running ? t("registrierung.erneutLaeuft") : t("registrierung.erneut")}
        </Button>

        {gescheitert ? (
          <Alert severity="error">
            {t("registrierung.erneutGescheitert")}
          </Alert>
        ) : null}

        {/* `role="status"` und NICHT „alert": eine Bestätigung unterbricht
            nicht. MUIs `Alert` trägt sonst immer `role="alert"`. */}
        {gesendet ? (
          <Alert role="status" severity="success">
            {t("registrierung.erneutGesendet")}
          </Alert>
        ) : null}

        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="registrierung.schonBestaetigt"
            components={{ 1: <Link component={RouterLink} to="/login" /> }}
          />
        </Typography>
      </Box>
    </AuthCard>
  );
}
