import { useState } from "react";
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
import { Link as RouterLink, useNavigate } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  createJob,
  draftJobText,
  employmentLabel,
  remoteLabel,
} from "../../work/api/jobs";

interface JobDraft {
  title: string;
  description: string;
  location: string;
  postalCode: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string;
}

const EMPTY: JobDraft = {
  title: "",
  description: "",
  location: "",
  postalCode: "",
  remote: "none",
  employment: "full_time",
  skills: "",
};

const skillsOf = (raw: string): string[] =>
  raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");

/**
 * <c>/company/jobs/new</c> — create an advert.
 *
 * <strong>It comes into being as a draft.</strong> It is only published in the
 * list, with a click of its own — what comes into being here is seen by nobody
 * but the company.
 */
export function CompanyJobNewPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { forCompany } = useActor();

  const [draft, setDraft] = useState<JobDraft>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const back = (
    <Link component={RouterLink} to="/company/jobs" variant="body2">
      {t("companyJobs.back")}
    </Link>
  );

  if (!forCompany) {
    return (
      <PageShell title={t("companyJobs.new")} narrow>
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

  async function create() {
    setRunning(true);
    const result = await createJob({
      title: draft.title,
      description: draft.description,
      location: draft.location,
      postal_code: draft.postalCode,
      remote: draft.remote,
      employment: draft.employment,
      skills: skillsOf(draft.skills),
    });
    setRunning(false);

    if (result.ok) void navigate("/company/jobs");
    else setError(result.error.detail);
  }

  return (
    <PageShell title={t("companyJobs.new")} narrow>
      <Box sx={{ mb: 2 }}>{back}</Box>

      <Card>
        <CardContent>
          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void create();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("companyJobs.fieldTitle")}
              value={draft.title}
              onChange={(e) =>
                setDraft({ ...draft, title: e.target.value })
              }
              required
            />
            <TextField
              label={t("companyJobs.description")}
              value={draft.description}
              onChange={(e) =>
                setDraft({ ...draft, description: e.target.value })
              }
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
              />
            </Box>
            <TextField
              select
              label={t("companyJobs.workMode")}
              value={draft.remote}
              onChange={(e) =>
                setDraft({ ...draft, remote: e.target.value as RemoteMode })
              }
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
            <TextField
              label={t("companyJobs.skills")}
              helperText={t("companyJobs.skillsHint")}
              value={draft.skills}
              onChange={(e) =>
                setDraft({ ...draft, skills: e.target.value })
              }
            />

            <DraftingHelp
              draft={draft}
              onSuggestion={(text) =>
                setDraft({ ...draft, description: text })
              }
            />

            <Box>
              <Button type="submit" variant="contained" disabled={running}>
                {running
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
              setProblem(result.error.detail);
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
