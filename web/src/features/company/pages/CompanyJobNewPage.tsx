import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { type ServerCase, failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  type Job,
  REMOTE_MODES,
  type RemoteMode,
  createJob,
  draftJobText,
  employmentLabel,
  listOwnJobs,
  remoteLabel,
  type SalaryPeriod,
  updateJob,
} from "../../work/api/jobs";

interface JobDraft {
  title: string;
  description: string;
  location: string;
  postalCode: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string;
  salaryMin: string;
  salaryMax: string;
  salaryPeriod: SalaryPeriod;
}

const EMPTY: JobDraft = {
  title: "",
  description: "",
  location: "",
  postalCode: "",
  remote: "none",
  employment: "full_time",
  skills: "",
  salaryMin: "",
  salaryMax: "",
  salaryPeriod: "year",
};

/**
 * Both fields empty is "not stated" — a statement the advert then shows. One
 * end alone goes out as it is and the service refuses it: "from 40,000"
 * reads like a range and hides its ceiling.
 */
function salaryOf(draft: JobDraft): { min: number; max: number; period: SalaryPeriod } | null {
  if (draft.salaryMin.trim() === "" && draft.salaryMax.trim() === "") return null;
  return {
    min: Number(draft.salaryMin),
    max: Number(draft.salaryMax),
    period: draft.salaryPeriod,
  };
}

/** A stored advert as the form holds it. */
function draftOf(job: Job): JobDraft {
  return {
    title: job.title,
    description: job.description,
    location: job.location,
    postalCode: job.postal_code,
    remote: job.remote,
    employment: job.employment,
    skills: job.skills.join(", "),
    salaryMin: job.salary ? String(job.salary.min) : "",
    salaryMax: job.salary ? String(job.salary.max) : "",
    salaryPeriod: job.salary?.period ?? "year",
  };
}

const skillsOf = (raw: string): string[] =>
  raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");

/**
 * The rules an advert can break, as the company reads them — never
 * jobs-service's sentence, which is English on the wire. Each pattern matches
 * a sentence in jobs-service (`Job.cs`, `SkillList.cs`) that points back here.
 */
const CREATE_CASES: readonly ServerCase[] = [
  // jobs-service `JobErrors.cs`: a fetched advert's text belongs to its source.
  { status: 409, pattern: /mirrors a source/i, key: "companyJobs.mirroredConflict" },
  { status: 409, key: "companyJobs.stateMovedOn" },
  { status: 422, pattern: /must not be empty/i, key: "companyJobs.fieldsRequired" },
  { status: 422, pattern: /is longer than \d+ characters/i, key: "companyJobs.fieldTooLong" },
  { status: 422, pattern: /skill may have at most/i, key: "companyJobs.skillTooLong" },
  { status: 422, pattern: /at most \d+ skills/i, key: "companyJobs.tooManySkills" },
  { status: 422, pattern: /salary range/i, key: "companyJobs.salaryInvalid" },
];

/**
 * The drafting help names its CAUSE where somebody can change it: "no provider
 * is set up" is a configuration, "not available" a refusal (jobs-journey pins
 * the first). Every other silence reads as the general sentence. The pattern
 * matches `HttpDrafter.cs` in jobs-service, which points back here.
 */
const HELP_CASES: readonly ServerCase[] = [
  { status: 503, pattern: /no draft provider is set up/i, key: "companyJobs.helpNoProvider" },
];

/**
 * <c>/company/jobs/new</c> — create an advert.
 *
 * <strong>It comes into being as a draft.</strong> It is only published in the
 * list, with a click of its own — what comes into being here is seen by nobody
 * but the company.
 */
export function CompanyJobNewPage() {
  return <JobEditor />;
}

/**
 * <c>/company/jobs/:id/edit</c> — change an advert, with the same form.
 *
 * <strong>A fetched advert keeps its text</strong> (ADR-0052): title, text,
 * place, work mode and employment come from the company's ATS and are changed
 * there; here the company adds what an ATS does not carry — skills, salary,
 * postal code. Without skills the advert never gets a tick.
 */
export function CompanyJobEditPage() {
  const { id } = useParams<{ id: string }>();
  return <JobEditor jobId={id} />;
}

function JobEditor({ jobId }: { jobId?: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { forCompany } = useActor();

  const [draft, setDraft] = useState<JobDraft>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  // Editing: the stored advert, from the company's own list — a draft is not
  // public, so `GET /jobs/{id}` would not show it.
  const [stored, setStored] = useState<Job | null | "missing">(null);

  useEffect(() => {
    if (jobId === undefined || !forCompany) return;
    const controller = new AbortController();
    void listOwnJobs(controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      const found = result.ok ? result.jobs.find((job) => job.id === jobId) : undefined;
      if (found === undefined) {
        setStored("missing");
        return;
      }
      setStored(found);
      setDraft(draftOf(found));
    });
    return () => controller.abort();
  }, [jobId, forCompany]);

  const editing = jobId !== undefined;
  const job = stored !== null && stored !== "missing" ? stored : null;
  const mirrored = job?.mirrored === true;
  const closed = job?.status === "closed";
  const title = editing ? t("companyJobs.editTitle") : t("companyJobs.new");

  const back = (
    <Link component={RouterLink} to="/company/jobs" variant="body2">
      {t("companyJobs.back")}
    </Link>
  );

  if (!forCompany) {
    return (
      <PageShell title={title} narrow>
        <Box sx={{ mb: 2 }}>{back}</Box>
        <Card>
          <CardContent>
            <Typography>
              {t("companyJobs.newCompanyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  if (editing && (stored === null || stored === "missing")) {
    return (
      <PageShell title={title} narrow>
        <Box sx={{ mb: 2 }}>{back}</Box>
        <Card>
          <CardContent>
            <Typography>
              {stored === "missing" ? t("companyJobs.notFound") : t("companyJobs.loading")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function save() {
    setRunning(true);
    const input = {
      title: draft.title,
      description: draft.description,
      location: draft.location,
      postal_code: draft.postalCode,
      remote: draft.remote,
      employment: draft.employment,
      skills: skillsOf(draft.skills),
      salary: salaryOf(draft),
    };
    const result = jobId === undefined ? await createJob(input) : await updateJob(jobId, input);
    setRunning(false);

    if (result.ok) void navigate("/company/jobs");
    else setError(t(failureKey(result.error, "errors.jobNotSaved", CREATE_CASES)));
  }

  return (
    <PageShell title={title} narrow>
      <Box sx={{ mb: 2 }}>{back}</Box>

      <Card>
        <CardContent>
          {mirrored ? (
            <Alert severity="info" sx={{ mb: 2 }}>
              {t("companyJobs.mirroredFields")}
            </Alert>
          ) : null}
          {closed ? (
            <Alert severity="info" sx={{ mb: 2 }}>
              {t("companyJobs.closedNoEdit")}
            </Alert>
          ) : null}
          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("companyJobs.fieldTitle")}
              value={draft.title}
              onChange={(e) =>
                setDraft({ ...draft, title: e.target.value })
              }
              disabled={mirrored || closed}
              required
            />
            <TextField
              label={t("companyJobs.description")}
              value={draft.description}
              onChange={(e) =>
                setDraft({ ...draft, description: e.target.value })
              }
              disabled={mirrored || closed}
              multiline
              minRows={5}
            />
            <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 2fr" } }}>
              <TextField
                // The postcode stands BEFORE the place and is narrower — that is
                // how an address is written, and it shows at a glance that the
                // two belong together.
                label={t("companyJobs.postalCode")}
                // Optional and still important: without it the advert drops out
                // of every radius search whose place name is ambiguous. The hint
                // says so instead of forcing it as a required field — a company
                // with only "nationwide" to give must still be able to post.
                helperText={t("companyJobs.postalCodeHint")}
                value={draft.postalCode}
                slotProps={{ htmlInput: { maxLength: 10, inputMode: "numeric" } }}
                onChange={(e) =>
                  setDraft({ ...draft, postalCode: e.target.value })
                }
              />
              <TextField
                label={t("companyJobs.location")}
                helperText={t("companyJobs.locationHint")}
                value={draft.location}
                onChange={(e) =>
                  setDraft({ ...draft, location: e.target.value })
                }
                disabled={mirrored || closed}
              />
            </Box>
            <TextField
              select
              label={t("companyJobs.workMode")}
              value={draft.remote}
              onChange={(e) =>
                setDraft({ ...draft, remote: e.target.value as RemoteMode })
              }
              disabled={mirrored || closed}
            >
              {REMOTE_MODES.map((value) => (
                <MenuItem key={value} value={value}>
                  {remoteLabel(value)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label={t("companyJobs.employment")}
              value={draft.employment}
              disabled={mirrored || closed}
              onChange={(e) =>
                setDraft({
                  ...draft,
                  employment: e.target.value as EmploymentType,
                })
              }
            >
              {EMPLOYMENT_TYPES.map((value) => (
                <MenuItem key={value} value={value}>
                  {employmentLabel(value)}
                </MenuItem>
              ))}
            </TextField>
            <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr 1fr" } }}>
              <TextField
                label={t("companyJobs.salaryMin")}
                type="number"
                value={draft.salaryMin}
                onChange={(e) => setDraft({ ...draft, salaryMin: e.target.value })}
                slotProps={{ htmlInput: { min: 1, step: 1, inputMode: "numeric" } }}
              />
              <TextField
                label={t("companyJobs.salaryMax")}
                type="number"
                value={draft.salaryMax}
                onChange={(e) => setDraft({ ...draft, salaryMax: e.target.value })}
                slotProps={{ htmlInput: { min: 1, step: 1, inputMode: "numeric" } }}
              />
              <TextField
                select
                label={t("companyJobs.salaryPeriod")}
                value={draft.salaryPeriod}
                onChange={(e) => setDraft({ ...draft, salaryPeriod: e.target.value as SalaryPeriod })}
              >
                <MenuItem value="year">{t("jobs.periodYear")}</MenuItem>
                <MenuItem value="month">{t("jobs.periodMonth")}</MenuItem>
                <MenuItem value="hour">{t("jobs.periodHour")}</MenuItem>
              </TextField>
            </Box>
            {/* Set like the helper texts of the fields around it — at body
                size it read as part of the form, not as a note to it. */}
            <Typography variant="caption" color="text.secondary" sx={{ display: "block", mx: 0.75, mt: -1 }}>
              {t("companyJobs.salaryHint")}
            </Typography>
            <TextField
              label={t("companyJobs.skills")}
              helperText={t("companyJobs.skillsHint")}
              value={draft.skills}
              onChange={(e) =>
                setDraft({ ...draft, skills: e.target.value })
              }
            />

            {mirrored || closed ? null : (
              <DraftingHelp
                draft={draft}
                onSuggestion={(text) =>
                  setDraft({ ...draft, description: text })
                }
              />
            )}

            <Box>
              <Button type="submit" variant="contained" disabled={running || closed}>
                {editing
                  ? running
                    ? t("companyJobs.saving")
                    : t("companyJobs.save")
                  : running
                    ? t("companyJobs.creating")
                    : t("companyJobs.create")}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}

/**
 * The drafting help.
 *
 * <strong>It helps the company write ITS OWN text</strong> — it never says
 * anything about a person. Exactly that makes it buildable where a candidate
 * ranking would not be.
 *
 * <strong><c>type="button"</c> spelled out</strong>, although MUI defaults to
 * it anyway: this button stands in the same form as "Entwurf anlegen", and
 * whoever reads here should not have to open the library to know which of the
 * two submits. If the default changed, this button would otherwise create the
 * job.
 */
function DraftingHelp({
  draft,
  onSuggestion,
}: {
  draft: JobDraft;
  onSuggestion: (text: string) => void;
}) {
  const { t } = useTranslation();
  const [wish, setWish] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const hasText = draft.description.trim() !== "";

  return (
    <Box sx={{ p: 2, borderRadius: 2, bgcolor: "action.hover" }}>
      <TextField
        label={t(
          hasText ? "companyJobs.helpRephrase" : "companyJobs.helpNew",
        )}
        helperText={t("companyJobs.helpHint")}
        value={wish}
        onChange={(e) => setWish(e.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        sx={{ mb: 1.5 }}
      />

      {problem !== null ? (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {problem}
        </Alert>
      ) : null}

      <Button
        type="button"
        variant="outlined"
        size="small"
        disabled={running}
        onClick={() => {
          setRunning(true);
          void draftJobText({
            title: draft.title,
            description: draft.description,
            location: draft.location,
            skills: skillsOf(draft.skills),
            wish: wish,
          }).then((result) => {
            setRunning(false);
            if (result.ok) {
              setProblem(null);
              onSuggestion(result.draft);
            } else {
              setProblem(t(failureKey(result.error, "errors.draftHelpUnavailableLong", HELP_CASES)));
            }
          });
        }}
      >
        {running
          ? t("companyJobs.helpInProgress")
          : t(
              hasText
                ? "companyJobs.helpButtonReplaces"
                : "companyJobs.helpButton",
            )}
      </Button>
    </Box>
  );
}
