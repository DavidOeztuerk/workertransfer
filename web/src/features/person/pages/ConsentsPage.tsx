import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Typography from "@mui/material/Typography";

import {
  ConsentSwitch,
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { Link as RouterLink } from "react-router-dom";
import Link from "@mui/material/Link";
import type { ApiError } from "../../../core/store/thunkHelpers";
import {
  GITHUB_VISIBILITY,
  type GrantedConsent,
  PORTFOLIO_VISIBILITY,
  PROFILE_VISIBILITY,
  listMyConsents,
  parseCapability,
  setGranted,
} from "../api/consent";
import { getMyConnection } from "../api/github";
import { getCompanyName } from "../api/companies";
import { useAsync } from "../lib/useAsync";
import { usePerson } from "../lib/session";
import { SignInRequired } from "../components/SignInRequired";

/**
 * The page a consent platform was missing.
 *
 * Before, the releases were scattered over four pages, and those from an
 * application stood on none of them. A consent one cannot overview is no
 * informed consent, and a withdrawal one cannot find is none.
 *
 * <strong>And it was a one-way street.</strong> It showed what holds and
 * offered "Withdraw" — GRANTING worked only on the page the release belongs
 * to: the profile switch in the profile, the portfolio switch at the
 * portfolio, the market status in the market. Whoever wanted to know how to
 * become visible had to guess which of five pages held the switch. A page
 * called "My releases" that cannot grant a release does not answer the
 * question it is opened for.
 *
 * <strong>For GitHub the switch did not exist at all.</strong> The service has
 * always checked `github.visibility:public`, the UI could only display it — so
 * after connecting, the evidence was visible to nobody, and there was no way to
 * change that.
 *
 * The switches still stand on their own pages TOO. Two places for the same
 * switch are no contradiction: both read and write the ledger, there is only
 * one truth. What no longer exists is exactly one place that nobody finds.
 *
 * <strong>Granting sits above withdrawing</strong>, in the order the questions
 * are asked: whoever comes here mostly wants to know how they become visible;
 * whoever wants to withdraw already knows what to look for.
 *
 * <strong>The two outlined cards at the bottom say what has NO switch.</strong>
 * Without them the page looks complete and is not: the CV is missing, and
 * whoever looks for it takes that for an oversight. It is none — see ADR-0020.
 * Both routes to the CV stand side by side, because the difference is not in
 * the document but in WHO starts: a company asking, or an application sending
 * it along.
 */
export function ConsentsPage() {
  const { t } = useTranslation();
  const { subjectId, sessionUnknown } = usePerson();
  const [error, setError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);

  const releases = useAsync(
    (signal) => listMyConsents(signal),
    [subjectId],
    subjectId !== null,
  );

  const result = releases.value;
  const consents: GrantedConsent[] =
    result?.ok === true ? result.consents : [];

  /*
   * THE THREE GLOBAL VISIBILITIES become switches and drop out of the list
   * below. Otherwise the same release would stand twice on one page — once as
   * a switch, once as a row with "Withdraw" — and nobody would know which of
   * the two holds.
   */
  const global = [PROFILE_VISIBILITY, PORTFOLIO_VISIBILITY, GITHUB_VISIBILITY];
  const holds = (capability: string) =>
    consents.some((entry) => entry.capability === capability);
  const individual = consents.filter(
    (entry) => !global.includes(entry.capability),
  );

  // No empty state on an error: "you have released nothing" would be the most
  // reassuring wrong answer there is, so the empty state needs a successful read.
  const nothingReleased = result?.ok === true && individual.length === 0;

  /*
   * Without a VERIFIED account there is nothing to release. The switch stays
   * visible and says what is missing — hiding it would mean keeping the
   * possibility quiet.
   *
   * <strong>Named is not enough.</strong> Measured on the running system: the
   * connection stood there as `verified: false`, and the switch was operable.
   * That would have released the repositories of an account nobody had
   * attributed to the person — exactly what the proof exists against. Evidence
   * that belongs to somebody else is worse than no evidence.
   */
  const github = useAsync(
    (signal) => getMyConnection(signal),
    [subjectId],
    subjectId !== null,
  );
  const connection =
    github.value?.ok === true ? github.value.value : null;
  const githubVerified = connection?.verified === true;

  const toggle = useCallback(
    async (capability: string, on: boolean) => {
      if (subjectId === null) return;
      setBusy(true);
      const answer = await setGranted(
        subjectId,
        capability,
        on,
        // The reason is read only on revocation — nobody asks for it when
        // granting (ADR-0027: a withdrawal must be explainable, a grant need
        // not be).
        t("consents.revokeReason"),
      );
      setBusy(false);
      setError(answer.ok ? null : answer.error);
      releases.again();
    },
    [subjectId, releases, t],
  );

  // The names come fresh from companies-service. Keeping them in the ledger
  // would mean holding a copy that goes stale the moment a company renames
  // itself — and the ledger manages capabilities, not companies.
  const tenantIds = [
    ...new Set(
      consents
        .map((consent) => parseCapability(consent.capability).tenantId)
        .filter((id): id is string => id !== null),
    ),
  ].sort();

  const names = useAsync(
    async (signal) => {
      const pairs = await Promise.all(
        tenantIds.map(
          async (id) => [id, await getCompanyName(id, signal)] as const,
        ),
      );
      return new Map(
        pairs.filter((pair): pair is [string, string] => pair[1] !== null),
      );
    },
    [tenantIds.join(",")],
    tenantIds.length > 0,
  );
  const nameFor = names.value ?? new Map<string, string>();

  const withdraw = useCallback(
    async (capability: string) => {
      if (subjectId === null) return;
      setBusy(true);
      const result = await setGranted(
        subjectId,
        capability,
        false,
        t("consents.revokeReason"),
      );
      setBusy(false);
      setError(result.ok ? null : result.error);
      releases.again();
    },
    [subjectId, releases, t],
  );

  if (sessionUnknown) {
    return (
      <PageShell title={t("consents.title")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return (
      <SignInRequired
        title={t("consents.title")}
        sentenceKey="consents.signInRequired"
      />
    );
  }

  return (
    <PageShell
      title={t("consents.title")}
      narrow
      lead={t("consents.lead")}
    >
      {error !== null ? <ErrorBlock error={error} fallback="errors.consentNotChanged" /> : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 0.5 }}>
            {t("consents.visibleTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
            {t("consents.visibleLead")}
          </Typography>

          <Box sx={{ display: "grid", gap: 2.5 }}>
            <ConsentSwitch
              label={t("consents.switchProfile")}
              hint={t("consents.switchProfileHint")}
              checked={holds(PROFILE_VISIBILITY)}
              disabled={busy || releases.loading}
              onChange={(on) => void toggle(PROFILE_VISIBILITY, on)}
            />

            <ConsentSwitch
              label={t("consents.switchPortfolio")}
              hint={t("consents.switchPortfolioHint")}
              checked={holds(PORTFOLIO_VISIBILITY)}
              disabled={busy || releases.loading}
              onChange={(on) => void toggle(PORTFOLIO_VISIBILITY, on)}
            />

            <Box>
              <ConsentSwitch
                label={t("consents.switchGithub")}
                hint={
                  githubVerified
                    ? t("consents.switchGithubHint")
                    : connection === null
                      ? t("consents.githubMissing")
                      : t("consents.githubUnverified")
                }
                checked={holds(GITHUB_VISIBILITY)}
                disabled={busy || releases.loading || !githubVerified}
                onChange={(on) => void toggle(GITHUB_VISIBILITY, on)}
              />
              {githubVerified ? null : (
                <Link
                  component={RouterLink}
                  to="/github"
                  variant="body2"
                  sx={{ display: "inline-block", mt: 0.5 }}
                >
                  {connection === null
                    ? t("consents.toGithub")
                    : t("consents.toGithubProof")}
                </Link>
              )}
            </Box>
          </Box>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 0.5 }}>
            {t("consents.individualTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.individualLead")}
          </Typography>
          {releases.loading ? (
            <LoadingBlock label={t("consents.loading")} />
          ) : null}

          {result !== undefined && !result.ok ? (
            <ErrorBlock error={result.error} fallback="errors.consentsNotLoaded" />
          ) : null}

          {nothingReleased ? (
            <EmptyBlock
              title={t("consents.emptyTitle")}
              hint={t("consents.emptyHint")}
            />
          ) : null}

          {individual.length > 0 ? (
            <List disablePadding>
              {individual.map((consent) => (
                <ConsentRow
                  key={consent.capability}
                  consent={consent}
                  companyName={
                    nameFor.get(
                      parseCapability(consent.capability).tenantId ?? "",
                    ) ?? null
                  }
                  busy={busy}
                  onWithdraw={() => void withdraw(consent.capability)}
                />
              ))}
            </List>
          ) : null}
        </CardContent>
      </Card>

      <Card variant="outlined">
        <CardContent>
          <Typography variant="h3" sx={{ mb: 1 }}>
            {t("consents.noSwitchTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.noSwitchText")}
          </Typography>
          <Link component={RouterLink} to="/resume" variant="body2">
            {t("consents.toResume")}
          </Link>
        </CardContent>
      </Card>

      <Card variant="outlined" sx={{ mt: 2 }}>
        <CardContent>
          <Typography variant="h3" sx={{ mb: 1 }}>
            {t("consents.applicationTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {t("consents.applicationText")}
          </Typography>
          <Link component={RouterLink} to="/applications" variant="body2">
            {t("consents.toApplications")}
          </Link>
        </CardContent>
      </Card>
    </PageShell>
  );
}

function ConsentRow({
  consent,
  companyName,
  busy,
  onWithdraw,
}: {
  consent: GrantedConsent;
  companyName: string | null;
  busy: boolean;
  onWithdraw: () => void;
}) {
  const { t, i18n } = useTranslation();
  const parts = parseCapability(consent.capability);
  // The date follows the CHOSEN language, not the device: otherwise a German
  // sentence would stand next to an American date.
  const releasedOn = new Date(consent.granted_at).toLocaleDateString(
    i18n.language,
  );
  // An unknown shape is shown, not swallowed — and can still be withdrawn.
  // ONLY the recognised shape is translated. Sending the raw string through
  // `t()` takes it apart: i18next reads a `:` as the namespace separator, and
  // `something.entirely:new` becomes `new`. An unknown release must stand
  // verbatim, though — otherwise nobody knows what they are withdrawing.
  const what = parts.area === null ? consent.capability : t(parts.area);
  const who = parts.public
    ? t("consents.allCompanies")
    : parts.tenantId !== null
      ? (companyName ?? t("consents.oneCompany"))
      : t("consents.recipientUnknown");

  return (
    <ListItem
      divider
      disableGutters
      sx={{
        display: "flex",
        flexDirection: { xs: "column", sm: "row" },
        alignItems: { xs: "flex-start", sm: "center" },
        justifyContent: "space-between",
        gap: 1.5,
      }}
    >
      <Box>
        <Typography component="p" sx={{ fontWeight: 600 }}>
          {what} · {who}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {t("consents.releasedOn", { datum: releasedOn })}
        </Typography>
      </Box>
      <Button variant="text" onClick={onWithdraw} disabled={busy}>
        {t("common.withdraw")}
      </Button>
    </ListItem>
  );
}
