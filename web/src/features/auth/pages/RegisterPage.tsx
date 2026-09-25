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
import { OccupationalFieldSelect } from "../../../shared/components/ui";
import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";
import { AuthCard } from "../components/AuthCard";
import { AuthModeTabs } from "../components/AuthModeTabs";
import {
  isPublicEmailDomain,
  registriere,
  sendeBestaetigungErneut,
} from "../api/registrierung";
import {
  PASSWORT_MINDESTLAENGE,
  emailFehler,
  passwortFehler,
} from "../lib/formpruefung";

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

  // Kein Pflichtfeld, Vorauswahl leer (ADR-0039).
  const [berufsfeld, setBerufsfeld] = useState<OccupationalFieldChoice>(null);

  // Geurteilt wird erst, wenn ein Feld verlassen wurde: während des Tippens
  // ist jede Adresse unfertig.
  const [emailBeruehrt, setEmailBeruehrt] = useState(false);
  const [passwortBeruehrt, setPasswortBeruehrt] = useState(false);

  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);
  const [abgeschickt, setAbgeschickt] = useState(false);

  // Referenced through `aria-describedby`, so the hint sits BESIDE the label and
  // not inside it; otherwise a screen reader announces the option as "Für ein
  // Unternehmen Braucht deine Arbeitsadresse — …" and a test naming it fails.
  const personHinweis = useId();
  const firmaHinweis = useId();

  // Nur wenn überhaupt eine Adresse dasteht: `isPublicEmailDomain("")` urteilte
  // über einen leeren Domainteil und blitzte beim ersten getippten Zeichen auf.
  //
  // Shown at once rather than after the confirmation mail, so nobody learns two
  // steps later that their path is closed. The refusal is still the SERVER's
  // (422); this only makes visible what already holds.
  const freemail = useMemo(
    () =>
      art === "company" && email.includes("@") && isPublicEmailDomain(email),
    [art, email],
  );

  // Sichtbar vor dem Abschicken; die Absage spricht weiterhin der Server (422).
  const emailProblem = emailBeruehrt ? emailFehler(email) : null;
  const passwortProblem = passwortBeruehrt ? passwortFehler(passwort) : null;
  const unvollstaendig =
    email.trim() === "" || passwort === "" || anzeigename.trim() === "";

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
    <AuthCard title={t("register.title")} lead={t("register.lead")}>
      <AuthModeTabs current="register" />

      <Box
        component="form"
        onSubmit={submit}
        noValidate
        sx={{ display: "grid", gap: 2.5 }}
      >
        <FormControl component="fieldset" sx={{ display: "grid", gap: 1 }}>
          <FormLabel component="legend" sx={{ mb: 0.5 }}>
            {t("register.purpose")}
          </FormLabel>
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
                label={t("register.forMe")}
              />
              <Typography
                id={personHinweis}
                variant="body2"
                color="text.secondary"
                sx={{ ml: 4 }}
              >
                {t("register.forMeHint")}
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
                label={t("register.forCompany")}
              />
              <Typography
                id={firmaHinweis}
                variant="body2"
                color="text.secondary"
                sx={{ ml: 4 }}
              >
                {t("register.forCompanyHint")}
              </Typography>
            </Box>
          </RadioGroup>
        </FormControl>

        <TextField
          label={t("register.email")}
          type="email"
          autoComplete="username"
          error={emailProblem !== null}
          helperText={
            emailProblem !== null
              ? t(emailProblem)
              : art === "company"
                ? t("register.emailHintCompany")
                : t("register.emailHintPerson")
          }
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          onBlur={() => setEmailBeruehrt(true)}
          required
        />

        {art === "company" ? (
          <TextField
            label={t("register.companyName")}
            autoComplete="organization"
            helperText={t("register.companyNameHint")}
            value={unternehmensname}
            onChange={(event) => setUnternehmensname(event.target.value)}
            required
          />
        ) : null}

        {freemail ? (
          <Alert severity="warning">
            {t("register.freemail")}
          </Alert>
        ) : null}

        <TextField
          label={t("register.password")}
          type="password"
          autoComplete="new-password"
          error={passwortProblem !== null}
          helperText={
            passwortProblem !== null
              ? t(passwortProblem, { mindestens: PASSWORT_MINDESTLAENGE })
              : t("register.passwordHint")
          }
          value={passwort}
          onChange={(event) => setPasswort(event.target.value)}
          onBlur={() => setPasswortBeruehrt(true)}
          required
        />
        <TextField
          label={t("register.displayName")}
          autoComplete="nickname"
          helperText={t("register.displayNameHint")}
          value={anzeigename}
          onChange={(event) => setAnzeigename(event.target.value)}
          required
        />
        <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 1fr" } }}>
          <TextField
            label={t("civilIdentity.firstName")}
            autoComplete="given-name"
            value={vorname}
            onChange={(event) => setVorname(event.target.value)}
          />
          <TextField
            label={t("civilIdentity.lastName")}
            autoComplete="family-name"
            value={nachname}
            onChange={(event) => setNachname(event.target.value)}
          />
        </Box>
        <Typography variant="body2" color="text.secondary">
          {t("civilIdentity.legalNameHint")}
        </Typography>

        <OccupationalFieldSelect value={berufsfeld} onChange={setBerufsfeld} />

        {fehler !== null ? <Alert severity="error">{fehler}</Alert> : null}

        <Button
          type="submit"
          variant="contained"
          size="large"
          // Der Knopf urteilt immer; rot gefärbt wird erst nach dem Verlassen.
          disabled={
            running
            || freemail
            || unvollstaendig
            || emailFehler(email) !== null
            || passwortFehler(passwort) !== null
          }
        >
          {running ? t("register.inProgress") : t("register.button")}
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

  // The failure branch is load-bearing: without it the button visibly did
  // NOTHING on a network error, and a 429 from the rate limit (3/min) produced
  // the promise "on its way" while nothing was on its way.
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
      title={t("register.almostDone")}
      lead={t("register.almostDoneLead")}
    >
      <Box sx={{ display: "grid", gap: 2 }}>
        <Button variant="outlined" onClick={erneutSenden} disabled={running}>
          {running ? t("register.resending") : t("register.resend")}
        </Button>

        {gescheitert ? (
          <Alert severity="error">
            {t("register.resendFailed")}
          </Alert>
        ) : null}

        {gesendet ? (
          <Alert
            // A confirmation must not interrupt; MUI's Alert would set role="alert".
            role="status"
            severity="success"
          >
            {t("register.resent")}
          </Alert>
        ) : null}

        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="register.alreadyVerified"
            components={{ 1: <Link component={RouterLink} to="/login" /> }}
          />
        </Typography>
      </Box>
    </AuthCard>
  );
}
