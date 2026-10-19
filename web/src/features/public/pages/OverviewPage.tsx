import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, Navigate } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { type PendingTasks, getPendingTasks } from "../api/pendingTasks";
import { hasOwnProfile } from "../api/ownProfile";

interface Entry {
  key: string;
  count: number;
  target: string;
}

/**
 * Only key and count — i18next does the inflection.
 *
 * Before, a spelled-out sentence stood here for "1" and one for "more",
 * because "1 Gespräche" reads like a defect. That branch is a statement about
 * the GERMAN numeral; French counts differently, and a third language perhaps
 * differently again. The catalogues carry `_one`/`_other`, and the rule comes
 * from the language instead of from this file.
 */
function entriesForMe(current: PendingTasks): Entry[] {
  const entries: Entry[] = [];

  if (current.marketRequests > 0) {
    entries.push({
      key: "overview.marketRequests",
      count: current.marketRequests,
      target: "/market",
    });
  }

  if (current.resumeRequests > 0) {
    entries.push({
      key: "overview.resumeRequests",
      count: current.resumeRequests,
      target: "/resume",
    });
  }

  if (current.ownTransfers > 0) {
    entries.push({
      key: "overview.conversations",
      count: current.ownTransfers,
      target: "/transfers",
    });
  }

  return entries;
}

function entriesForTheCompany(current: PendingTasks): Entry[] {
  if (current.companyTransfers === 0) return [];
  return [
    {
      key: "overview.companyTransfers",
      count: current.companyTransfers,
      target: "/company/transfers",
    },
  ];
}

/** What is counted are CASES, never people (ADR-0022/0026). */
function EntryList({ title, entries }: { title: string; entries: Entry[] }) {
  const { t } = useTranslation();

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent sx={{ p: { xs: 2.5, md: 3 } }}>
        <Typography variant="h2" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        <Box
          component="ul"
          sx={{ listStyle: "none", m: 0, p: 0, display: "grid", gap: 1 }}
        >
          {entries.map((entry) => (
            <Box component="li" key={entry.target}>
              <Link component={RouterLink} to={entry.target}>
                {t(entry.key, { count: entry.count })}
              </Link>
            </Box>
          ))}
        </Box>
      </CardContent>
    </Card>
  );
}

/**
 * The overview at `/overview` — "Was liegt an".
 *
 * <strong>It has an address of its own</strong>, and that is the whole reason
 * it exists: before, `/` served both, which made it unlinkable.
 *
 * <strong>Without a session it is not drawn but left.</strong> "Gerade wartet
 * nichts auf dich" would be no empty overview for a signed-out visitor but a
 * false statement — it says nothing about the person, only about the missing
 * token.
 *
 * The state is local and not in the store: `core/store/store.ts` registers only
 * `auth` and `preferences`, and this page may add nothing there. Whoever wants
 * an `overview` slice later changes it there first.
 */
export function OverviewPage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);
  // `tenantId === null` means "acting as a person" (ADR-0017) — no error but the
  // normal case on a transfer market.
  const forCompany = session?.tenantId != null;

  const [current, setCurrent] = useState<PendingTasks | null>(null);
  const [profile, setProfile] = useState<boolean | null>(null);

  // The first step of a new account. Asked only of a person: a company has
  // no profile of this kind, and its own start lies at the company profile.
  useEffect(() => {
    if (status !== "authenticated" || forCompany) return;

    const abort = new AbortController();
    void hasOwnProfile(abort.signal).then((exists) => {
      if (!abort.signal.aborted) setProfile(exists);
    });
    return () => abort.abort();
  }, [status, forCompany]);

  useEffect(() => {
    if (status !== "authenticated") return;

    const abort = new AbortController();
    setCurrent(null);
    void getPendingTasks(forCompany, abort.signal).then((result) => {
      // After an abort the aborted request arrives here as a network error. It
      // must not touch the state any more — otherwise the page shows the
      // incompleteness of a read nobody wanted any more.
      if (abort.signal.aborted) return;
      setCurrent(result);
    });

    return () => abort.abort();
  }, [status, forCompany]);

  if (status === "unknown") {
    return (
      <PageShell title={t("overview.title")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (status === "anonymous") return <Navigate to="/login" replace />;

  const mine = current === null ? [] : entriesForMe(current);
  const company = current === null ? [] : entriesForTheCompany(current);
  const nothing = mine.length === 0 && company.length === 0;

  return (
    <PageShell
      title={t("overview.title")}
      narrow
      lead={t("overview.lead")}
    >
      {!forCompany && profile === false ? (
        <Card sx={{ mb: 3 }} component="section" aria-label={t("overview.startTitle")}>
          <CardContent sx={{ display: "grid", gap: 1.5, justifyItems: "start" }}>
            <Typography variant="h3" component="h2">
              {t("overview.startTitle")}
            </Typography>
            <Typography color="text.secondary">{t("overview.startText")}</Typography>
            <Button component={RouterLink} to="/profile" variant="contained">
              {t("overview.startButton")}
            </Button>
          </CardContent>
        </Card>
      ) : null}

      {current === null ? <LoadingBlock /> : null}

      {current?.incomplete === true ? (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {t("overview.incomplete")}
        </Alert>
      ) : null}

      {current !== null && nothing && !current.incomplete ? (
        <EmptyBlock
          title={t("overview.emptyTitle")}
          hint={t("overview.emptyHint")}
          action={
            <Button component={RouterLink} to="/consents" variant="outlined">
              {t("overview.emptyButton")}
            </Button>
          }
        />
      ) : null}

      {mine.length > 0 ? (
        <EntryList title={t("overview.forYou")} entries={mine} />
      ) : null}
      {company.length > 0 ? (
        <EntryList title={t("overview.forCompany")} entries={company} />
      ) : null}
    </PageShell>
  );
}
