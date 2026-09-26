import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { type MessageKey, type ServerCase, failureKey } from "../../../shared/api/errors";
import { useAppSelector } from "../../../core/store/hooks";
import { SignInRequired } from "../components/SignInRequired";
import { useAsync } from "../lib/useAsync";
import {
  type Connection,
  disconnect,
  finishOAuth,
  getMyConnection,
  isOAuthAvailable,
  nameAccount,
  refreshRepositories,
  startOAuth,
  verifyConnection,
} from "../api/github";
import { getMyProfile, saveMyProfile } from "../api/profile";
import { fromRepositories, suggestionsFrom } from "../lib/suggestions";


/**
 * What github-service refuses with, as the person reads it. Each pattern
 * matches `ConnectionEndpoints.cs`, which points back here: the gist was not
 * found, GitHub is silent, the ledger is silent.
 */
const STEP_CASES: readonly ServerCase[] = [
  { status: 422, pattern: /account was not proven/i, key: "github.notProven" },
  { status: 503, pattern: /github unavailable/i, key: "github.unavailable" },
  { status: 503, pattern: /consent ledger did not answer/i, key: "errors.ledgerSilent" },
];

/**
 * <c>/github</c> — one's own, verified connection.
 *
 * <strong>Evidence, not marks.</strong> What stands here are public
 * repositories with a link. This platform computes no score and no ranking
 * from them — whoever wants to know whether the code is good looks at it. That
 * is not restraint but the line: requirement in, evidence out; never person
 * in, number out.
 *
 * <strong>And no silent incompleteness.</strong> No repositories are no defect
 * but a piece of information — whoever has nothing on GitHub is not worse, but
 * elsewhere. The page says so explicitly.
 *
 * <strong>Fetched only when triggered.</strong> No reconciliation in the
 * background.
 *
 * <strong>The fast path comes first and asks for no name.</strong> Typing the
 * account was the GIST's requirement — a gist search must know whose gists to
 * look in. OAuth does not need it: GitHub reports only the account that really
 * consented, so there is nothing to compare. The manual path stays complete and
 * is the only one when this server has no OAuth configured.
 */
export function GitHubPage() {
  const { t, i18n } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const [login, setLogin] = useState("");
  /** Is the manual path expanded? Closed as long as signing in suffices. */
  const [manual, setManual] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [adopt, setAdopt] = useState(false);

  const connection = useAsync(
    (signal) => getMyConnection(signal),
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <SignInRequired title={t("github.title")} sentenceKey="github.signInRequired" />
    );
  }

  /**
   * One step on the connection, and what its failure says — a catalogue text
   * chosen by status and sentence (`STEP_CASES`), never github-service's own
   * sentence, which is English on the wire.
   */
  async function run(
    action: () => Promise<{ ok: boolean; error?: ApiError }>,
    fallback: MessageKey = "errors.stepFailed",
  ) {
    setRunning(true);
    const result = await action();
    setRunning(false);

    if (!result.ok && result.error) setError(t(failureKey(result.error, fallback, STEP_CASES)));
    else setError(null);

    connection.again();
  }

  const loaded = !connection.loading && connection.value?.ok === true;
  const current: Connection | null = connection.value?.ok
    ? connection.value.value
    : null;

  /*
   * WHETHER signing in exists is fetched while viewing — WHERE it leads only
   * on click.
   *
   * The difference is not cosmetic: `startOAuth` creates a connection if there
   * is none yet. Prefetched, it would leave a row for everyone who opened the
   * page once and then clicked on.
   */
  const oauth = useAsync(
    (signal) => isOAuthAvailable(signal),
    [session?.userId],
    session !== null,
  );
  const withOAuth = oauth.value === true;

  // Without configured OAuth the manual path is not a side path but THE path —
  // then there is nothing to expand.
  const showManual = !withOAuth || manual;

  /*
   * A NAMED ACCOUNT IS NO REASON TO FORCE THE GIST.
   *
   * The reverse rule stood here — "whoever has named an account is on the
   * manual path" — so that a reload would not hide the string. Measured on the
   * real system the repair was worse than the damage: a name is also given by
   * somebody who then wants to sign in after all, and they got the gist
   * permanently in view. After a reload the string is one click away and does
   * not change.
   *
   * What must NOT collapse is the way out: whoever has a wrong name standing
   * fails at signing in (the comparison refuses) and needs "Other account" —
   * the button therefore stands at the top and not in the section.
   */
  const namedAccount = current?.login != null && !current.verified
    ? current.login
    : null;

  /** Start signing in and go there. */
  async function signIn() {
    setRunning(true);
    const target = await startOAuth();
    setRunning(false);

    if (target === null) {
      setError(t("github.signInFailed"));
      return;
    }

    window.location.assign(target);
  }

  /*
   * THE RETURN FROM GITHUB.
   *
   * The one-time code stands in the address bar. It is redeemed and the
   * address CLEANED afterwards — a code left standing in the browser's history
   * is a secret in a place where nobody suspects one. It is used up anyway, but
   * only the server knows that.
   */
  const [redeemed, setRedeemed] = useState(false);

  useEffect(() => {
    if (redeemed) return;

    const query = new URLSearchParams(window.location.search);
    const code = query.get("code");
    const state = query.get("state");

    if (code === null || state === null) return;

    setRedeemed(true);
    window.history.replaceState({}, "", window.location.pathname);

    void finishOAuth(code, state).then((result) => {
      if (!result.ok) {
        setError(t("github.signInFailed"));
        return;
      }

      setError(null);
      connection.again();
    });
  }, [redeemed, connection, t]);

  return (
    <PageShell
      title={t("github.title")}
      narrow
      lead={t("github.lead")}
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        {t("github.onlyOnRequest")}
      </Typography>

      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      {connection.loading ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("github.loading")} />
          </CardContent>
        </Card>
      ) : null}

      {connection.value && !connection.value.ok ? (
        <ErrorBlock error={connection.value.error} fallback="errors.connectionNotLoaded" />
      ) : null}

      {loaded && (current === null || !current.verified) ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1.5 }}>
              {t("github.proof")}
            </Typography>

            {withOAuth ? (
              <Box sx={{ mb: manual ? 3 : 0 }}>
                <Button
                  variant="contained"
                  onClick={() => void signIn()}
                  disabled={running}
                >
                  {t("github.withGithub")}
                </Button>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                  {t("github.withGithubHint")}
                </Typography>
                {namedAccount !== null ? (
                  <Box
                    sx={{
                      mt: 2,
                      display: "flex",
                      gap: 1.5,
                      alignItems: "center",
                      flexWrap: "wrap",
                    }}
                  >
                    <Typography variant="body2">
                      <Trans
                        i18nKey="github.namedAccount"
                        values={{ login: namedAccount }}
                        components={{ 1: <strong /> }}
                      />
                    </Typography>
                    <Button
                      variant="text"
                      size="small"
                      onClick={() => void run(async () => ({ ok: await disconnect() }))}
                      disabled={running}
                      sx={{ px: 0 }}
                    >
                      {t("github.otherAccount")}
                    </Button>
                  </Box>
                ) : null}

                <Button
                  variant="text"
                  size="small"
                  onClick={() => setManual((open) => !open)}
                  sx={{ mt: 1.5, px: 0, display: "block" }}
                >
                  {manual ? t("github.manualHide") : t("github.manual")}
                </Button>
              </Box>
            ) : null}

            {showManual ? (
              <Box>
                {withOAuth ? <Divider sx={{ mb: 2.5 }} /> : null}

                {current?.login == null ? (
                  <Box
                    component="form"
                    onSubmit={(event) => {
                      event.preventDefault();
                      void run(() => nameAccount(login), "errors.accountNotAccepted");
                    }}
                    sx={{
                      display: "flex",
                      flexDirection: "column",
                      gap: 2,
                      alignItems: "flex-start",
                    }}
                  >
                    <Typography variant="subtitle2">
                      {t("github.nameAccount")}
                    </Typography>
                    <TextField
                      label={t("github.username")}
                      helperText={t("github.usernameHint")}
                      placeholder="anna"
                      value={login}
                      onChange={(e) => setLogin(e.target.value)}
                      slotProps={{ htmlInput: { maxLength: 39 } }}
                      required
                    />
                    <Button type="submit" variant="contained" disabled={running}>
                      {t("github.next")}
                    </Button>
                  </Box>
                ) : (
                  <>
                    <Typography sx={{ mb: 1.5 }}>
                      <Trans
                        i18nKey="github.proofInstructions"
                        components={{ 1: <strong /> }}
                      />
                    </Typography>
                    <Box
                      component="pre"
                      className="github__challenge"
                      sx={{
                        p: 2,
                        borderRadius: 1,
                        bgcolor: "action.hover",
                        fontFamily: "monospace",
                        fontSize: "0.875rem",
                        overflowX: "auto",
                      }}
                    >
                      {current.challenge_description}
                    </Box>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                      <Trans
                        i18nKey="github.proofExplanation"
                        values={{ login: current.login }}
                        components={{ 1: <strong /> }}
                      />
                    </Typography>
                    <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
                      <Button
                        variant="contained"
                        onClick={() => void run(() => verifyConnection(), "errors.proofNotChecked")}
                        disabled={running}
                      >
                        {running ? t("github.proofChecking") : t("github.proofCheck")}
                      </Button>
                      {withOAuth ? null : (
                        <Button
                          variant="text"
                          onClick={() =>
                            void run(async () => ({ ok: await disconnect() }))
                          }
                          disabled={running}
                        >
                          {t("github.otherAccount")}
                        </Button>
                      )}
                    </Box>
                  </>
                )}
              </Box>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      {current !== null && current.verified ? (
        <Card>
          <CardContent>
            <Typography variant="h2" sx={{ mb: 1 }}>
              {current.login}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              {current.fetched_at !== null
                ? t("github.asOf", {
                    zeitpunkt: new Date(current.fetched_at).toLocaleString(
                      i18n.language,
                    ),
                  })
                : t("github.nothingFetched")}{" "}
              <Trans
                i18nKey="github.visibility"
                components={{
                  1: <Link component={RouterLink} to="/consents" />,
                }}
              />
            </Typography>

            {!current.languages_complete && current.repositories.length > 0 ? (
              <Alert
                // ADR-0022 §3: a shortened set says that it is shortened.
                severity="info"
                sx={{ mb: 2 }}
              >
                {t("github.languagesIncomplete")}
              </Alert>
            ) : null}

            {current.repositories.length === 0 ? (
              <Typography sx={{ mb: 2 }}>
                {t("github.noRepos")}
              </Typography>
            ) : (
              <Box component="ul" sx={{ pl: 2.5, mb: 2 }}>
                {current.repositories.map((repo) => {
                  // WHAT THIS REPOSITORY USES — topics first, because they are
                  // a naming: the owner wrote "kubernetes" there themselves.
                  // Languages stand beside them as a SET, without a share: the
                  // deleted package computed its "skill" from the byte ratio
                  // (ADR-0022 §2). Both are statements about a REPOSITORY, never
                  // about a person; one click carries a word into the profile,
                  // and only then has a human said it.
                  const words = [
                    ...new Set([...repo.topics, ...repo.languages]),
                  ];
                  return (
                    <Box component="li" key={repo.name} sx={{ mb: 1 }}>
                      <Link
                        href={repo.url}
                        target="_blank"
                        rel="noreferrer noopener"
                      >
                        {repo.name}
                      </Link>
                      <Typography variant="body2" color="text.secondary">
                        {repo.language ?? t("github.noLanguage")} ·{" "}
                        {repo.stars} ★
                        {repo.description !== "" ? ` · ${repo.description}` : ""}
                      </Typography>

                      {words.length > 0 ? (
                        <Box
                          sx={{
                            display: "flex",
                            flexWrap: "wrap",
                            gap: 0.5,
                            mt: 0.5,
                          }}
                        >
                          {words.map((word) => (
                            <Chip key={word} label={word} size="small" />
                          ))}
                        </Box>
                      ) : null}
                    </Box>
                  );
                })}
              </Box>
            )}

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap", alignItems: "center" }}>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={adopt}
                    onChange={(_, on) => setAdopt(on)}
                  />
                }
                label={t("github.adoptSkills")}
              />
              <Button
                variant="outlined"
                onClick={() =>
                  void run(async () => {
                    const result = await refreshRepositories();
                    if (!result.ok || !adopt || !result.value) return result;
                    const ownProfile = await getMyProfile();
                    if (!ownProfile.ok || ownProfile.profile === null) return result;
                    const extra = suggestionsFrom(
                      fromRepositories(result.value.repositories),
                      ownProfile.profile.skills,
                    );
                    if (extra.length === 0) return result;
                    await saveMyProfile({
                      headline: ownProfile.profile.headline,
                      bio: ownProfile.profile.bio,
                      location: ownProfile.profile.location,
                      remote_ok: ownProfile.profile.remote_ok,
                      skills: [...ownProfile.profile.skills, ...extra].slice(0, 30),
                      // WRITE ALONG, do not leave out. This call writes the
                      // WHOLE profile back, and `null` takes a statement back
                      // (ADR-0041): without these two lines a click on "adopt
                      // suggestions" would delete the commuting readiness
                      // without anybody asking for it. The compiler noticed —
                      // which is why the fields are required and not
                      // optional.
                      commute_km: ownProfile.profile.commute_km,
                      relocation: ownProfile.profile.relocation,
                    });
                    return result;
                  })
                }
                disabled={running}
              >
                {running
                  ? t("github.refreshing")
                  : t("github.refresh")}
              </Button>
              <Button
                variant="text"
                onClick={() =>
                  void run(async () => ({ ok: await disconnect() }))
                }
                disabled={running}
              >
                {t("github.disconnect")}
              </Button>
            </Box>
          </CardContent>
        </Card>
      ) : null}
    </PageShell>
  );
}
