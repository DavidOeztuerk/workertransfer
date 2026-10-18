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
import { ExternalSignIn } from "../components/ExternalSignIn";
import { AuthModeTabs } from "../components/AuthModeTabs";
import {
  isPublicEmailDomain,
  register,
  resendVerification,
} from "../api/registration";
import {
  PASSWORD_MIN_LENGTH,
  emailError,
  passwordError,
} from "../lib/formValidation";

type AccountKind = "person" | "company";

/**
 * Create an account — for oneself or for a company.
 *
 * <strong>No tenant field.</strong> The client names at most a NAME here, never
 * an affiliation. The server derives the domain from the confirmed address —
 * what the client cannot send, it cannot forge (ADR-0017/0018/0019).
 */
export function RegisterPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const [searchParams] = useSearchParams();

  // The default is "person", and explicitly so: registering is the act of a
  // natural person (ADR-0017), and the normal case on a transfer market is
  // someone without a company. The hero buttons of the home page carry the
  // other intent along as `?as=company` — without this preselection someone who
  // clicks "Discover as a company" lands in the person form and only notices
  // after the confirmation mail.
  const [kind, setKind] = useState<AccountKind>(
    searchParams.get("as") === "company" ? "company" : "person",
  );

  const [companyName, setCompanyName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [givenName, setGivenName] = useState("");
  const [familyName, setFamilyName] = useState("");

  // Not required, preselection empty (ADR-0039).
  const [occupationalField, setOccupationalField] = useState<OccupationalFieldChoice>(null);

  // Judged only once a field was left: while typing every address is
  // unfinished.
  const [emailTouched, setEmailTouched] = useState(false);
  const [passwordTouched, setPasswordTouched] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  // Referenced through `aria-describedby`, so the hint sits BESIDE the label and
  // not inside it; otherwise a screen reader announces the option as "Für ein
  // Unternehmen Braucht deine Arbeitsadresse — …" and a test naming it fails.
  const personHint = useId();
  const companyHint = useId();

  // Only when an address stands there at all: `isPublicEmailDomain("")` judged
  // an empty domain part and flashed at the first typed character.
  //
  // Shown at once rather than after the confirmation mail, so nobody learns two
  // steps later that their path is closed. The refusal is still the SERVER's
  // (422); this only makes visible what already holds.
  const freemail = useMemo(
    () =>
      kind === "company" && email.includes("@") && isPublicEmailDomain(email),
    [kind, email],
  );

  // Visible before submitting; the refusal is still spoken by the server (422).
  const emailProblem = emailTouched ? emailError(email) : null;
  const passwordProblem = passwordTouched ? passwordError(password) : null;
  const incomplete =
    email.trim() === "" || password === "" || displayName.trim() === "";

  if (status === "authenticated") return <Navigate to="/overview" replace />;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setRunning(true);
    const result = await register({
      email,
      password,
      displayName,
      ...(givenName.trim() ? { givenName: givenName.trim() } : {}),
      ...(familyName.trim() ? { familyName: familyName.trim() } : {}),
      ...(kind === "company" ? { companyName } : {}),
      ...(occupationalField !== null ? { occupationalField } : {}),
    });
    setRunning(false);
    if (result.ok) setSubmitted(true);
    else setError(result.message);
  }

  if (submitted) return <AlmostDone email={email} />;

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
            name="kind"
            value={kind}
            onChange={(event) =>
              setKind(event.target.value === "company" ? "company" : "person")
            }
          >
            <Box>
              <FormControlLabel
                value="person"
                control={
                  <Radio
                    slotProps={{ input: { "aria-describedby": personHint } }}
                  />
                }
                label={t("register.forMe")}
              />
              <Typography
                id={personHint}
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
                    slotProps={{ input: { "aria-describedby": companyHint } }}
                  />
                }
                label={t("register.forCompany")}
              />
              <Typography
                id={companyHint}
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
              : kind === "company"
                ? t("register.emailHintCompany")
                : t("register.emailHintPerson")
          }
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          onBlur={() => setEmailTouched(true)}
          required
        />

        {kind === "company" ? (
          <TextField
            label={t("register.companyName")}
            autoComplete="organization"
            helperText={t("register.companyNameHint")}
            value={companyName}
            onChange={(event) => setCompanyName(event.target.value)}
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
          error={passwordProblem !== null}
          helperText={
            passwordProblem !== null
              ? t(passwordProblem, { mindestens: PASSWORD_MIN_LENGTH })
              : t("register.passwordHint")
          }
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          onBlur={() => setPasswordTouched(true)}
          required
        />
        <TextField
          label={t("register.displayName")}
          autoComplete="nickname"
          helperText={t("register.displayNameHint")}
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          required
        />
        <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 1fr" } }}>
          <TextField
            label={t("civilIdentity.firstName")}
            autoComplete="given-name"
            value={givenName}
            onChange={(event) => setGivenName(event.target.value)}
          />
          <TextField
            label={t("civilIdentity.lastName")}
            autoComplete="family-name"
            value={familyName}
            onChange={(event) => setFamilyName(event.target.value)}
          />
        </Box>
        <Typography variant="body2" color="text.secondary">
          {t("civilIdentity.legalNameHint")}
        </Typography>

        <OccupationalFieldSelect value={occupationalField} onChange={setOccupationalField} />

        {error !== null ? <Alert severity="error">{error}</Alert> : null}

        <Button
          type="submit"
          variant="contained"
          size="large"
          // The button always judges; coloured red only after leaving the field.
          disabled={
            running
            || freemail
            || incomplete
            || emailError(email) !== null
            || passwordError(password) !== null
          }
        >
          {running ? t("register.inProgress") : t("register.button")}
        </Button>
      </Box>

      <ExternalSignIn register />
    </AuthCard>
  );
}

/**
 * After submitting.
 *
 * <strong>The same message whether the address was new or already
 * existed.</strong> The server does not reveal it (it answers known addresses
 * 201 as well and sends the real owner a warning), and both are true: a mail
 * was sent. An "already exists" here would be the enumeration channel that
 * `/auth/register` just closes.
 */
function AlmostDone({ email }: { email: string }) {
  const { t } = useTranslation();
  const [running, setRunning] = useState(false);
  const [sent, setSent] = useState(false);
  const [failed, setFailed] = useState(false);

  // The failure branch is load-bearing: without it the button visibly did
  // NOTHING on a network error, and a 429 from the rate limit (3/min) produced
  // the promise "on its way" while nothing was on its way.
  async function resend() {
    setRunning(true);
    setFailed(false);
    const result = await resendVerification(email);
    setRunning(false);
    if (result.ok) setSent(true);
    else setFailed(true);
  }

  return (
    <AuthCard
      title={t("register.almostDone")}
      lead={t("register.almostDoneLead")}
    >
      <Box sx={{ display: "grid", gap: 2 }}>
        <Button variant="outlined" onClick={resend} disabled={running}>
          {running ? t("register.resending") : t("register.resend")}
        </Button>

        {failed ? (
          <Alert severity="error">
            {t("register.resendFailed")}
          </Alert>
        ) : null}

        {sent ? (
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
