import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { HitCard } from "../components/HitCard";
import { useActor } from "../lib/session";
import { useAsync, usePages } from "../lib/useAsync";
import {
  NO_FILTER,
  type SearchFilter,
  type Hit,
  searchCandidates,
} from "../api/scout";
import {
  type MarketRequest,
  listCompanyMarketRequests,
} from "../../work/api/market";
import { listOwnJobs } from "../../work/api/jobs";

/**
 * <c>/scout</c> — who released their profile, with ticks and evidence.
 *
 * <strong>Successor of <c>/candidates</c></strong> (ADR-0036). The old path
 * fell on 11.09.2026; two searches side by side would have been two truths.
 *
 * <strong>The words are an OR.</strong> Whoever enters three is not looking
 * for someone who knows all three — but sees from the ticks who named which.
 * Under AND every tick would be set and the list useless.
 *
 * <strong>Only who released stands here.</strong> Whoever withdraws the release
 * disappears at the next load — without a detour through us, because the list
 * asks the ledger anew on every request.
 *
 * <strong>No total.</strong> It would reveal how many profiles exist that are
 * NOT released right now — a statement about people who explicitly said
 * nothing.
 *
 * <strong>"Empty" does not mean the same as "silent".</strong> If the ledger
 * does not answer, an error stands here and not an empty list: an empty list
 * would be the claim that nobody released anything, and at that moment nobody
 * knows that.
 */
export function ScoutPage() {
  const { t } = useTranslation();
  const { forCompany } = useActor();

  const [draft, setDraft] = useState({
    skills: "",
    location: "",
    remoteOnly: false,
  });
  const [filter, setFilter] = useState<SearchFilter>(NO_FILTER);

  // The job stands BESIDE the filters, not among them: it takes no hit away, it
  // explains each one (ADR-0041). That is why it takes effect at once and does
  // not wait for "search" — it does not change the set after all.
  const [jobId, setJobId] = useState("");

  const hasFilter =
    filter.skills.length > 0 || filter.location !== "" || filter.remoteOnly;

  const ownJobs = useAsync(
    (signal) => listOwnJobs(signal),
    [forCompany],
    forCompany,
  );

  const jobs =
    ownJobs.data?.ok === true ? ownJobs.data.jobs : [];

  const pages = usePages<Hit, string>(
    (cursor, signal) =>
      searchCandidates(cursor, filter, signal, jobId).then((result) =>
        result.ok
          ? {
              ok: true as const,
              items: result.items,
              nextCursor: result.nextCursor,
            }
          : { ok: false as const, error: result.error.detail },
      ),
    JSON.stringify({ filter, jobId }),
    forCompany,
  );

  const requests = useAsync(
    (signal) => listCompanyMarketRequests(signal),
    [forCompany],
    forCompany,
  );

  const marketRequests = new Map<string, MarketRequest>(
    requests.data?.ok === true
      ? requests.data.requests.map((requestRecord: MarketRequest) => [
          requestRecord.subject_id,
          requestRecord,
        ])
      : [],
  );

  // Without an active company nothing is asked at all. The server would answer
  // 403 — but a request whose result is certain is only noise in the ledger's
  // logs.
  if (!forCompany) {
    return (
      <PageShell title={t("scout.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("scout.companyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell
      title={t("scout.title")}
      lead={t("scout.lead")}
    >
      {pages.error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {pages.error}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              setFilter({
                skills: draft.skills
                  .split(",")
                  .map((entry) => entry.trim())
                  .filter((entry) => entry !== ""),
                location: draft.location,
                remoteOnly: draft.remoteOnly,
              });
            }}
            sx={{
              display: "flex",
              flexDirection: "column",
              gap: 2,
              alignItems: "flex-start",
            }}
          >
            <TextField
              label={t("scout.skills")}
              helperText={t("scout.skillsHint")}
              placeholder={t("scout.skillsExample")}
              value={draft.skills}
              onChange={(e) =>
                setDraft((now) => ({ ...now, skills: e.target.value }))
              }
            />
            <TextField
              label={t("scout.location")}
              helperText={t("scout.locationHint")}
              placeholder={t("scout.locationExample")}
              value={draft.location}
              onChange={(e) =>
                setDraft((now) => ({ ...now, location: e.target.value }))
              }
            />

            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    // A box, not a switch: the filter takes effect on submit,
                    // and the two elements promise that differently.
                    checked={draft.remoteOnly}
                    onChange={(e) =>
                      setDraft((now) => ({
                        ...now,
                        remoteOnly: e.target.checked,
                      }))
                    }
                  />
                }
                label={t("scout.remoteOnly")}
              />
              <Typography variant="body2" color="text.secondary">
                {t("scout.remoteOnlyHint")}
              </Typography>
            </Box>

            {jobs.length > 0 ? (
              <TextField
                select
                // ADR-0041: the advert's attendance and place meet the person's
                // willingness to commute, and that becomes a TICK per hit. NOT a
                // filter — the list never gets shorter from it.
                label={t("scout.againstJob")}
                helperText={t("scout.againstJobHint")}
                value={jobId}
                onChange={(event) => setJobId(event.target.value)}
                >
                <MenuItem value="">{t("scout.noJob")}</MenuItem>
                {jobs.map((advert) => (
                  <MenuItem key={advert.id} value={advert.id}>
                    {advert.title}
                  </MenuItem>
                ))}
              </TextField>
            ) : null}

            <Box sx={{ display: "flex", gap: 1.5 }}>
              <Button type="submit" variant="contained">
                {t("scout.search")}
              </Button>
              {hasFilter ? (
                <Button
                  variant="text"
                  onClick={() => {
                    setDraft({ skills: "", location: "", remoteOnly: false });
                    setFilter(NO_FILTER);
                  }}
                >
                  {t("scout.filterReset")}
                </Button>
              ) : null}
            </Box>
          </Box>
        </CardContent>
      </Card>

      {pages.pending && pages.items.length === 0 ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("scout.loading")} />
          </CardContent>
        </Card>
      ) : null}

      {pages.items.length > 0 ? (
        <Box
          component="ul"
          sx={{
            display: "flex",
            flexDirection: "column",
            gap: 2,
            listStyle: "none",
            p: 0,
            m: 0,
          }}
        >
          {pages.items.map((hit: Hit) => (
            <HitCard
              key={hit.subject_id}
              hit={hit}
              marketRequest={marketRequests.get(hit.subject_id)}
              onChanged={() => requests.reload()}
            />
          ))}
        </Box>
      ) : null}

      {!pages.pending &&
      pages.error === null &&
      pages.items.length === 0 ? (
        hasFilter ? (
          <EmptyBlock title={t("scout.emptyWithFilter")} />
        ) : (
          <EmptyBlock
            title={t("scout.emptyTitle")}
            hint={t("scout.emptyHint")}
          />
        )
      ) : null}

      {pages.hasMore ? (
        <Box sx={{ mt: 3 }}>
          <Button
            variant="outlined"
            onClick={pages.loadMore}
            disabled={pages.pending}
          >
            {pages.pending ? t("common.loading") : t("scout.loadMore")}
          </Button>
        </Box>
      ) : null}
    </PageShell>
  );
}
