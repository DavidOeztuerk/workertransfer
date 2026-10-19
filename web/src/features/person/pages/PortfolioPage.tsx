import { useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  ConsentSwitch,
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { useAppSelector } from "../../../core/store/hooks";
import { SignInRequired } from "../components/SignInRequired";
import { useAsync } from "../lib/useAsync";
import { PORTFOLIO_VISIBILITY, isGranted, setGranted } from "../api/consent";
import { type PortfolioItem, attachmentUrl, getMyPortfolio } from "../api/portfolio";

/** Role and year, as far as stated — and whether a file is attached. */
function summaryLine(item: PortfolioItem, withFile: string): string {
  const parts: string[] = [];
  if (item.role !== "") parts.push(item.role);
  if (item.year !== null) parts.push(String(item.year));
  if (item.attachment !== null) parts.push(withFile);
  return parts.join(" · ");
}

/**
 * <c>/portfolio</c> — the showcase and its own release.
 *
 * <strong>The release is separate from the profile</strong>, and that is no
 * duplication: somebody can be approachable without showing their work.
 *
 * <strong>Three states at the switch, not two.</strong> If the ledger is
 * silent, the switch is <em>locked</em> and says so — otherwise the next click
 * would grant a release for something whose state nobody knows. And without a
 * single item nothing can be released: what does not exist cannot be shown.
 */
export function PortfolioPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);
  const subjectId = session?.userId ?? null;

  const [error, setError] = useState<ApiError | null>(null);
  const [toggling, setToggling] = useState(false);

  const showcase = useAsync(
    (signal) => getMyPortfolio(signal),
    [subjectId],
    subjectId !== null,
  );

  const release = useAsync(
    () => isGranted(subjectId as string, PORTFOLIO_VISIBILITY),
    [subjectId],
    subjectId !== null,
  );

  if (status === "anonymous" || subjectId === null) {
    return (
      <SignInRequired
        title={t("portfolio.title")}
        sentenceKey="portfolio.signInRequired"
      />
    );
  }

  const items = showcase.value?.ok
    ? (showcase.value.value?.items ?? [])
    : [];
  const hasItems = items.length > 0;

  // `undefined` means "still loading", `null` means "the ledger is silent".
  const ledgerSilent = release.value === null;
  const ledgerUnknown = release.loading || ledgerSilent;

  async function toggle(next: boolean) {
    setToggling(true);
    const result = await setGranted(
      subjectId as string,
      PORTFOLIO_VISIBILITY,
      next,
      t("portfolio.revokeReason"),
    );
    setToggling(false);

    if (result.ok) {
      setError(null);
      release.set(result.granted);
    } else {
      setError(result.error);
      release.again();
    }
  }

  const hint = ledgerSilent
    ? t("portfolio.releaseSilent")
    : release.loading
      ? t("portfolio.releaseChecking")
      : t(
          hasItems
            ? "portfolio.releaseEffective"
            : "portfolio.releaseWithoutItem",
        );

  return (
    <PageShell
      title={t("portfolio.title")}
      narrow
      lead={t("portfolio.lead")}
    >
      {error !== null ? (
        <ErrorBlock
          error={error}
          title={t("portfolio.releaseFailed")}
          fallback="errors.consentNotChanged"
        />
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <ConsentSwitch
            label={t("portfolio.releaseLabel")}
            hint={hint}
            checked={release.value === true}
            disabled={!hasItems || toggling || ledgerUnknown}
            onChange={(next) => void toggle(next)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          {showcase.loading ? (
            <LoadingBlock label={t("portfolio.loading")} />
          ) : null}

          {showcase.value && !showcase.value.ok ? (
            <ErrorBlock error={showcase.value.error} fallback="errors.portfolioUnavailable" />
          ) : null}

          {!showcase.loading && showcase.value?.ok && !hasItems ? (
            <EmptyBlock
              title={t("portfolio.emptyTitle")}
              hint={t("portfolio.emptyHint")}
              action={
                <Button
                  component={RouterLink}
                  to="/portfolio/new"
                  variant="contained"
                >
                  {t("portfolio.add")}
                </Button>
              }
            />
          ) : null}

          {hasItems ? (
            <>
              <Box
                sx={{
                  display: "flex",
                  flexDirection: "column",
                  gap: 1.5,
                  mb: 3,
                }}
              >
                {items.map((item, index) => (
                  <Card
                    // The order is the stored field's, and a work sample's
                    // address is its position in it: a work sample has no id of
                    // its own.
                    key={index}
                    variant="outlined"
                  >
                    <CardContent
                      sx={{
                        display: "flex",
                        flexDirection: { xs: "column", sm: "row" },
                        justifyContent: "space-between",
                        alignItems: { xs: "flex-start", sm: "center" },
                        gap: 2,
                      }}
                    >
                      <Box>
                        <Typography variant="h4">{item.title}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {summaryLine(item, t("portfolio.withFile"))}
                        </Typography>
                      </Box>
                      <Box
                        sx={{
                          display: "flex",
                          gap: 1.5,
                          alignItems: "center",
                          flexShrink: 0,
                        }}
                      >
                        <Button
                          component={RouterLink}
                          to={`/portfolio/${index}`}
                          size="small"
                          variant="text"
                        >
                          {t("portfolio.edit")}
                        </Button>
                        {item.attachment !== null ? (
                          <Link
                            href={attachmentUrl(subjectId, item.attachment)}
                            target="_blank"
                            rel="noreferrer noopener"
                            variant="body2"
                          >
                            {t("portfolio.viewFile")}
                          </Link>
                        ) : null}
                      </Box>
                    </CardContent>
                  </Card>
                ))}
              </Box>
              <Button
                component={RouterLink}
                to="/portfolio/new"
                variant="contained"
              >
                {t("portfolio.add")}
              </Button>
            </>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}
