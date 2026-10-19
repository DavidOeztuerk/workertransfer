import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import CircularProgress from "@mui/material/CircularProgress";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate } from "react-router-dom";

import {
  EmptyBlock,
  ErrorBlock,
  LoadingBlock,
  PageShell,
  PaginationControls,
} from "../../../shared/components/ui";
import type { CompanyProfile } from "../../company/api/companies";
import {
  employmentLabel,
  type Job,
  remoteLabel,
  type SearchFilters,
  type SearchFailure,
  placeNear,
  searchJobs,
  salaryText,
  PERIOD_KEY,
} from "../api/jobs";
import { getDrafts, listMyApplications } from "../api/applications";
import { getMyProfile } from "../api/profile";
import { remember } from "../lib/letterContext";
import { Requirements } from "../components/Requirements";
import { type ProgressReporter, startDrafts } from "../lib/startDrafts";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { useCompanyProfiles } from "../lib/useCompanyProfiles";
import {
  type PageResult,
  usePagination,
} from "../../../shared/hooks/usePagination";
import { size } from "../../../styles/tokens/spacing";
import {
  EMPTY_FILTER,
  JobFilterSidebar,
  type JobFilter,
} from "../components/JobFilterSidebar";
import { useGeolocation } from "../../../shared/hooks/useGeolocation";
import type { DraftsRouteState } from "./DraftsPage";

/**
 * What was searched for: the form's filters AND the point known at submit.
 *
 * The location stands apart because it is no form field: it comes from the
 * browser, not from an input. It belongs in the same state as the filters,
 * though, so that a change of location has the same effect as a change of
 * filter — namely a new search from page one.
 */
interface AppliedFilter extends JobFilter {
  lat: number | null;
  lon: number | null;
}

const NOTHING_SEARCHED: AppliedFilter = { ...EMPTY_FILTER, lat: null, lon: null };

/**
 * The form state becomes the request.
 *
 * The skills stand in the form as ONE string, because one types them that way,
 * and go out as a list, because the server expects them that way. The splitting
 * happens exactly here and nowhere else.
 */
function toSearchFilters(filter: AppliedFilter): SearchFilters {
  const radiusKm = Number(filter.radius);

  /*
   * The radius goes along as soon as one was chosen. The coordinates only when
   * there are any — otherwise the PLACE is the centre, and the service resolves
   * it.
   *
   * The UI allows a distance only once there is a centre anyway: a location or
   * a typed place.
   */
  const radiusFilter =
    Number.isFinite(radiusKm) && radiusKm > 0
      ? {
          radiusKm,
          ...(filter.lat !== null && filter.lon !== null
            ? { lat: filter.lat, lon: filter.lon }
            : {}),
        }
      : {};

  return {
    q: filter.q,
    location: filter.location,
    remote: filter.remote,
    employment: filter.employment,
    skills: filter.skills
      .split(",")
      .map((part) => part.trim())
      .filter((part) => part !== ""),
    ...radiusFilter,
  };
}

/**
 * The open jobs — the only list in this application that is complete without
 * an account.
 *
 * Two filter states on purpose: what stands in the form and what was searched
 * for. Otherwise a query would run on every keystroke, and the list would
 * flicker while somebody types a word.
 */
export function JobsPage() {
  // WITHOUT a lead. "Was hier steht, haben Unternehmen selbst veröffentlicht.
  // Zum Lesen brauchst du kein Konto — erst zum Bewerben." stood here. Both
  // sentences explain what the page already shows itself: that it stands there
  // without sign-in, and that a button "Bewerben" sits under every card. Two
  // lines that push the first hit down.
  const { t, i18n } = useTranslation();
  const { signedIn, loading } = useActor();
  const myApplications = useAsync(
    (signal) => listMyApplications(signal),
    [],
    signedIn,
  );
  const myDrafts = useAsync((signal) => getDrafts(signal), [], signedIn);
  const alreadySent = new Set(
    (myApplications.data?.ok ? myApplications.data.applications : [])
      .filter((a) => a.status !== "withdrawn")
      .map((a) => a.job_id),
  );
  const openDrafts = new Map(
    (myDrafts.data?.ok ? myDrafts.data.drafts : [])
      .filter((d) => d.status !== "sent")
      .map((d) => [d.job_id, d.id]),
  );
  const [form, setForm] = useState<JobFilter>(EMPTY_FILTER);
  const [applied, setApplied] = useState<AppliedFilter>(NOTHING_SEARCHED);
  const [selectedJobs, setSelectedJobs] = useState<Set<string>>(new Set());
  const [creatingDrafts, setCreatingDrafts] = useState(false);
  const [runningJob, setRunningJob] = useState<string | null>(null);
  /*
   * How far the writing is — `null` as long as nothing runs.
   *
   * It is set only on the bulk path: there it takes long enough that somebody
   * wants to know whether something is still happening. For a single job the
   * spinner in the button suffices, and the bar below only stands there with a
   * selection anyway.
   */
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null);
  const [bulkError, setBulkError] = useState<string | null>(null);
  const navigate = useNavigate();

  // The location is never asked for on load — only at the press of a button in
  // the sidebar, and it is stored nowhere. See `useGeolocation`.
  const geolocation = useGeolocation();

  /*
   * THE FOUND LOCATION BECOMES VISIBLE — it lands in the place field.
   *
   * A radius around an invisible point is an imposition: the list would change,
   * and nobody could say from where the distance was measured. The name comes
   * from the same table as the search itself, not from a third party.
   *
   * The marker prevents the loop: setting changes the form, but the form is no
   * dependency of this effect — fetched exactly once per position.
   */
  const fetchedFor = useRef<string | null>(null);

  useEffect(() => {
    const position = geolocation.position;

    if (position === null) {
      fetchedFor.current = null;
      return;
    }

    const key = `${position.lat},${position.lon}`;

    if (fetchedFor.current === key) return;
    fetchedFor.current = key;

    const abort = new AbortController();

    void placeNear(position.lat, position.lon, abort.signal).then((name) => {
      // `null` means "nothing nearby" — then the field stays as it was, instead
      // of claiming a place.
      if (name !== null) {
        setForm((previous) => ({ ...previous, location: name }));
      }
    });

    return () => abort.abort();
  }, [geolocation.position]);

  const load = useCallback(
    (page: number, pageSize: number, signal: AbortSignal) =>
      searchJobs(toSearchFilters(applied), page, pageSize, signal).then(
        (result): PageResult<Job, SearchFailure> =>
          result.ok
            ? { ok: true, page: result }
            : { ok: false, error: result },
      ),
    [applied],
  );

  const list = usePagination<Job, SearchFailure>(load, JSON.stringify(applied));

  // Once for the whole page, not per job. As long as the session is unknown it
  // does NOT ask — otherwise a fetch would run on a cold start whose answer is
  // settled (401), and the fit would flicker briefly.
  const profile = useAsync(
    () => remember("profile:me", () => getMyProfile()),
    [signedIn],
    signedIn,
  );

  /*
   * Three states, and the distinction carries:
   *   `null`   — nothing to compare: not signed in, or the answer is still
   *              pending. Then the page stays silent about it instead of
   *              claiming a gap it does not know.
   *   `[]`     — signed in, answer there, but nothing entered. The page MUST
   *              speak about that, otherwise it would stay mute where one
   *              sentence explains the whole function — and never as "0 of 3".
   *   a list   — compare.
   */
  const mySkills: string[] | null =
    !signedIn || profile.pending ? null : (profile.data?.skills ?? []);

  const companies = useCompanyProfiles(list.items.map((job) => job.tenant_id));

  const toggleJob = useCallback((jobId: string) => {
    setSelectedJobs((prev) => {
      const next = new Set(prev);
      if (next.has(jobId)) {
        next.delete(jobId);
      } else {
        next.add(jobId);
      }
      return next;
    });
  }, []);

  async function create(jobIds: string[], report?: ProgressReporter) {
    const start = await startDrafts(jobIds, report);
    if (!start.ok) {
      setBulkError(start.error === null ? null : t(start.error));
      return null;
    }
    return start.drafts;
  }

  async function applyToOne(jobId: string) {
    setRunningJob(jobId);
    setBulkError(null);
    try {
      const drafts = await create([jobId]);
      const first = drafts?.[0];
      if (first) navigate(`/applications/drafts/${first.id}`);
    } finally {
      setRunningJob(null);
    }
  }

  const clearSelection = useCallback(() => {
    setSelectedJobs(new Set());
  }, []);

  const handleBulkApply = useCallback(async () => {
    if (!signedIn || selectedJobs.size === 0) return;

    setCreatingDrafts(true);
    setBulkError(null);
    try {
      const jobIds = Array.from(selectedJobs).filter((id) => !openDrafts.has(id));
      if (jobIds.length === 0) {
        clearSelection();
        return;
      }
      const drafts = await create(jobIds, (done, total) => setProgress({ done, total }));
      if (drafts === null) return;

      clearSelection();
      const state: DraftsRouteState = { writing: "ok" };
      navigate("/applications/drafts", { state });
    } finally {
      setCreatingDrafts(false);
      setProgress(null);
    }
  }, [signedIn, selectedJobs, navigate, clearSelection]);

  return (
    <PageShell title={t("jobs.title")}>
      {bulkError ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {bulkError}
        </Alert>
      ) : null}
      <Box
        sx={{
          display: "grid",
          gap: { xs: 3, md: 4 },
          gridTemplateColumns: { xs: "1fr", md: `${size.filterSidebar}px 1fr` },
          alignItems: "start",
        }}
      >
        <JobFilterSidebar
          filter={form}
          onChange={setForm}
          geolocation={geolocation}
          onSubmit={() =>
            setApplied({
              ...form,
              lat: geolocation.position?.lat ?? null,
              lon: geolocation.position?.lon ?? null,
            })
          }
          onReset={() => {
            setForm(EMPTY_FILTER);
            setApplied(NOTHING_SEARCHED);
            geolocation.forget();
          }}
        />

        <Box>
      {list.pending && list.items.length === 0 ? (
        <LoadingBlock label={t("jobs.searching")} />
      ) : null}

      {list.error !== null ? (
        <ErrorBlock error={list.error.error} fallback="errors.searchFailed" />
      ) : null}

      {!list.pending && list.error === null && list.items.length === 0 ? (
        <EmptyBlock
          title={t("jobs.emptyTitle")}
          hint={t("jobs.emptyHint")}
        />
      ) : null}

      {list.omitted !== null && list.omitted > 0 ? (
        <Alert
          // WHAT THE FILTER COULD NOT JUDGE stands above the list, not small
          // below it: a radius search can say nothing about an advert whose
          // place the location table does not know, and dropping those silently
          // would give a result that looks complete and is not (ADR-0022 §3).
          severity="info"
          icon={false}
          sx={{ mb: 2 }}
        >
          {t("jobs.withoutLocation", { count: list.omitted })}
        </Alert>
      ) : null}

      {list.items.length > 0 ? (
        <>
          <Box
            component="ul"
            sx={{
              listStyle: "none",
              p: 0,
              m: 0,
              display: "grid",
              gap: 2,
              pb: signedIn && selectedJobs.size > 0 ? 10 : 0,
              // ONE column, on every width. Two side by side were shorter and
              // worse: jobs differ in length, so uneven cards with holes between
              // them came about, and the eye had to jump back to the left after
              // every card. A list is read from top to bottom.
            }}
          >
            {list.items.map((job) => (
              <Box component="li" key={job.id}>
                <Card>
                  <CardContent>
                    <Box sx={{ display: "flex", alignItems: "flex-start", gap: 2, mb: 1 }}>
                      {!loading && signedIn && !alreadySent.has(job.id) && (
                        <Checkbox
                          checked={selectedJobs.has(job.id)}
                          disabled={openDrafts.has(job.id) || creatingDrafts || runningJob !== null}
                          onChange={() => toggleJob(job.id)}
                          slotProps={{ input: { "aria-label": t("jobs.select") } }}
                          sx={{ mt: 0.5 }}
                        />
                      )}
                      <Box sx={{ flex: 1 }}>
                        <Typography variant="h3" sx={{ mb: 0.5 }}>
                          {job.title}
                        </Typography>
                        <Hiring profile={companies[job.tenant_id]} />
                        <Typography
                          variant="body2"
                          color="text.secondary"
                          sx={{ mb: 1.5 }}
                        >
                          {job.location !== ""
                            ? job.location
                            : t("jobs.locationMissing")}{" "}
                          · {remoteLabel(job.remote)} · {employmentLabel(job.employment)}
                          · {job.salary !== null
                            ? salaryText(job.salary, i18n.language, (period) => t(PERIOD_KEY[period]))
                            : t("jobs.salaryMissing")}
                        </Typography>
                        <Typography sx={{ whiteSpace: "pre-line" }}>
                          {job.description}
                        </Typography>
                        <Requirements skills={job.skills} mine={mySkills} />
                      </Box>
                    </Box>

                    {loading ? null : signedIn ? (
                      alreadySent.has(job.id) ? (
                        <Button variant="contained" disabled sx={{ mt: 2 }}>
                          {t("jobs.alreadyApplied")}
                        </Button>
                      ) : openDrafts.has(job.id) ? (
                        <Button
                          component={RouterLink}
                          to={`/applications/drafts/${openDrafts.get(job.id)}`}
                          variant="contained"
                          sx={{ mt: 2 }}
                        >
                          {t("jobs.openDraft")}
                        </Button>
                      ) : (
                      <Button
                        variant="contained"
                        sx={{ mt: 2 }}
                        disabled={creatingDrafts || runningJob !== null}
                        onClick={() => void applyToOne(job.id)}
                        startIcon={
                          runningJob === job.id ? (
                            <CircularProgress color="inherit" size={16} />
                          ) : null
                        }
                      >
                        {t("jobs.apply")}
                      </Button>
                      )
                    ) : (
                      /*
                        The same button as for signed-in people, and WITHOUT the
                        sentence "you need an account for that". It stood under
                        every card and explained something that is on the screen
                        a moment later anyway: whoever clicks lands at the sign-in
                        and sees there why. Twelve cards with twelve hints at the
                        same thing are noise.

                        A BUTTON and not a word in a sentence: whoever wants to
                        apply looks for a button, and it must carry the same
                        weight as the one for signed-in people.
                      */
                      <Button
                        type="button"
                        variant="contained"
                        sx={{ mt: 2 }}
                        onClick={() => void navigate("/login")}
                      >
                        {t("jobs.apply")}
                      </Button>
                    )}
                  </CardContent>
                </Card>
              </Box>
            ))}
          </Box>

          {signedIn && selectedJobs.size > 0 && (
            <Box
              sx={{
                position: "fixed",
                bottom: 0,
                left: 0,
                right: 0,
                p: 2,
                bgcolor: "background.paper",
                borderTop: "1px solid",
                borderColor: "divider",
                boxShadow: 3,
                zIndex: 1100,
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                flexWrap: "wrap",
                gap: 2,
              }}
            >
              <Typography variant="body1">
                {progress
                  ? t("jobs.progress", {
                      fertig: progress.done,
                      gesamt: progress.total,
                    })
                  : t("jobs.selected", { count: selectedJobs.size })}
              </Typography>
              <Box sx={{ display: "flex", gap: 1 }}>
                <Button
                  variant="outlined"
                  onClick={clearSelection}
                  disabled={creatingDrafts}
                >
                  {t("jobs.clearSelection")}
                </Button>
                <Button
                  variant="contained"
                  onClick={handleBulkApply}
                  disabled={creatingDrafts}
                  startIcon={creatingDrafts ? <Box sx={{ width: 16, height: 16 }} /> : null}
                >
                  {creatingDrafts
                    ? t("jobs.createDrafts")
                    : t("jobs.applyToAll")}
                </Button>
              </Box>
            </Box>
          )}
        </>
      ) : null}

          <PaginationControls
            page={list.page}
            pageSize={list.pageSize}
            totalItems={list.totalItems}
            totalPages={list.totalPages}
            onPage={list.goTo}
            onPageSize={list.setPageSize}
          />
        </Box>
      </Box>
    </PageShell>
  );
}

/**
 * Who is hiring.
 *
 * Without a profile the card shows nothing here — a job then stays anonymous.
 * That is a state the company brought about itself, and filling it with a
 * placeholder like "Unknown company" would be a statement nobody made.
 */
function Hiring({ profile }: { profile: CompanyProfile | null | undefined }) {
  const { t } = useTranslation();
  if (profile === undefined || profile === null) return null;

  return (
    <Typography variant="body2" sx={{ mb: 0.5 }}>
      <Box component="strong">{profile.display_name}</Box>
      {profile.website !== null ? (
        <>
          {" · "}
          <Link href={profile.website} target="_blank" rel="noreferrer noopener">
            {t("jobs.website")}
          </Link>
        </>
      ) : null}
      {profile.benefits.length > 0 ? <> · {profile.benefits.join(", ")}</> : null}
    </Typography>
  );
}
