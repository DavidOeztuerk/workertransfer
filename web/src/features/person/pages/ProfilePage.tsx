import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import AddIcon from "@mui/icons-material/AddOutlined";
import Chip from "@mui/material/Chip";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";

import {
  OccupationalFieldSelect,
  ConsentSwitch,
  ErrorBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { type MessageKey, failureKey } from "../../../shared/api/errors";
import {
  type CommuteReadiness,
  PROFILE_CASES,
  type Profile,
  type RelocationReadiness,
  getMyProfile,
  getVisibility,
  saveMyProfile,
  setVisibility,
} from "../api/profile";
import { parseSkills } from "../lib/skills";
import {
  fromDocuments,
  fromPortfolioItems,
  fromPositions,
  fromRepositories,
  suggestionsFrom,
} from "../lib/suggestions";
import { getMyConnection } from "../api/github";
import {
  type DocumentTerms,
  getDocumentTerms,
  getMyCurriculumVitae,
  readDocuments,
} from "../api/resume";
import { getMyPortfolio } from "../api/portfolio";
import { useAsync } from "../lib/useAsync";
import { usePerson } from "../lib/session";
import { SignInRequired } from "../components/SignInRequired";
import { EvidenceKinds } from "../components/EvidenceKinds";
import { FromDocuments } from "../components/FromDocuments";
import { offersGitHub } from "../../../shared/lib/occupationalFields";
import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";
import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { occupationalFieldSet } from "../../auth/store/authSlice";
import { saveOccupationalField } from "../api/occupationalField";
import { DraftHelp } from "../components/DraftHelp";
import { CivilIdentity } from "../components/CivilIdentity";

interface FormState {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string;
  /** Empty text means "said nothing" — in the form, not on the wire. */
  commute_km: string;
  relocation: string;
  xing: string;
}

const EMPTY: FormState = {
  headline: "",
  bio: "",
  location: "",
  remote_ok: false,
  skills: "",
  commute_km: "",
  relocation: "",
  xing: "",
};

/**
 * The five steps — as values, not as numbers (ADR-0041). The members are wire
 * words.
 *
 * The thresholds are measured: the median commute is 17.2 km, 4.1 million
 * people commute more than 50 km, still 2.4 million more than 100 km. "Egal"
 * (any distance) must therefore exist — so that nobody has to make themselves
 * smaller than they are.
 */
const COMMUTE_READINESS = ["bis_10", "bis_25", "bis_50", "bis_100", "egal"] as const;

/** Three values, and "offen" (open) is not one too many. */
const RELOCATION_READINESS = ["ja", "nein", "offen"] as const;

function toForm(profile: Profile | null): FormState {
  if (profile === null) return EMPTY;
  return {
    headline: profile.headline,
    bio: profile.bio,
    location: profile.location,
    remote_ok: profile.remote_ok,
    skills: profile.skills.join(", "),
    commute_km: profile.commute_km ?? "",
    relocation: profile.relocation ?? "",
    xing: profile.xing_url ?? "",
  };
}

/**
 * My profile — the content and, beside it, the release.
 *
 * The two stand apart because they are apart: the profile belongs to
 * profile-service, the visibility to the ledger. The switch therefore writes
 * <strong>only</strong> into the ledger, and saving the profile changes
 * nothing about the release.
 */
export function ProfilePage() {
  const { t } = useTranslation();
  const { subjectId, sessionUnknown } = usePerson();

  const profile = useAsync(
    (signal) => getMyProfile(signal),
    [subjectId],
    subjectId !== null,
  );
  const release = useAsync(
    (signal) => getVisibility(subjectId ?? "", signal),
    [subjectId],
    subjectId !== null,
  );

  const [form, setForm] = useState<FormState>(EMPTY);
  // The failure and the step's own sentence for it: what the person reads is
  // a catalogue text chosen by status, never profile-service's sentence.
  const [error, setError] = useState<{ error: ApiError; fallback: MessageKey } | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [toggling, setToggling] = useState(false);

  // The form follows the server as long as nobody types. After that the state
  // belongs to the person — otherwise they lose their input the moment the
  // answer arrives.
  const loaded = profile.value;
  useEffect(() => {
    if (loaded?.ok === true) setForm(toForm(loaded.profile));
  }, [loaded]);

  function change<K extends keyof FormState>(
    key: K,
    value: FormState[K],
  ) {
    setSaved(false);
    setForm((now) => ({ ...now, [key]: value }));
  }

  /*
   * The evidence is only READ, to form suggestions. Without a connected
   * account the list is empty and the section does not appear at all — a
   * greyed-out box for somebody without GitHub would be the silent claim that
   * something is missing there (ADR-0022 §3).
   */
  // The occupational field decides what is offered — never what is allowed
  // (ADR-0039).
  const occupationalField = useAppSelector(
    (state) => state.auth.session?.occupationalField ?? null,
  );
  const github = offersGitHub(occupationalField);
  const dispatch = useAppDispatch();

  /**
   * The occupational field stands on the profile, where everything stands that
   * somebody says about their work — not in the settings, which carry the
   * account.
   *
   * It holds at once, not only with "Save": the evidence box and the
   * navigation follow it immediately.
   */
  async function chooseOccupationalField(field: OccupationalFieldChoice) {
    if (field === occupationalField) return;

    const result = await saveOccupationalField(field);

    if (result.ok) {
      dispatch(occupationalFieldSet(field));
    } else {
      // The display must not claim a choice the server refused.
      setError({ error: result.error, fallback: "errors.occupationNotSaved" });
    }
  }

  // Whoever is not offered GitHub is not asked about it either.
  const evidence = useAsync(
    (signal) => getMyConnection(signal),
    [subjectId, github],
    subjectId !== null && github,
  );

  /*
   * Filtered against the form AND against the loaded profile.
   *
   * Filtering against the form alone had a visible defect: it follows the
   * server through an effect, so it runs one pass behind. In exactly that pass
   * "C#" already stood in the profile and was suggested anyway — a suggestion
   * the person had long since followed. Measured in the test, not made up.
   */
  const alreadyNamed = [
    ...parseSkills(form.skills),
    ...(loaded?.ok === true ? (loaded.profile?.skills ?? []) : []),
  ];

  /*
   * THREE SOURCES: the repositories, the technologies of the CV positions and
   * those of one's own portfolio. All words a human wrote themselves — and all
   * become a statement about them only through a click up here.
   */
  const curriculumVitae = useAsync(
    (signal) => getMyCurriculumVitae(signal),
    [subjectId],
    subjectId !== null,
  );

  const portfolio = useAsync(
    (signal) => getMyPortfolio(signal),
    [subjectId],
    subjectId !== null,
  );

  /*
   * THE FOURTH SOURCE: one's own uploaded documents (PBI-7).
   *
   * This query READS NO FILE. It fetches what an earlier click yielded — the
   * service opens a reference only when somebody presses the button below.
   * Reading while a page loads would be exactly the background run that
   * ADR-0004 rules out.
   */
  const documentTerms = useAsync(
    (signal) => getDocumentTerms(signal),
    [subjectId],
    subjectId !== null,
  );

  const [reading, setReading] = useState(false);
  const [readError, setReadError] = useState<string | null>(null);

  const findings: DocumentTerms[] =
    documentTerms.value?.ok === true ? documentTerms.value.value : [];

  async function readDocumentsNow() {
    setReading(true);
    setReadError(null);
    const result = await readDocuments();
    setReading(false);

    if (result.ok) {
      documentTerms.set({ ok: true, value: result.value });
    } else {
      // The display must not claim a finding that does not exist — it keeps
      // the old state and says beside it that the attempt failed.
      setReadError(t(failureKey(result.error, "errors.documentsNotRead")));
    }
  }

  const sources = [
    ...(evidence.value?.ok === true && evidence.value.value !== null
      ? fromRepositories(evidence.value.value.repositories)
      : []),
    ...(curriculumVitae.value?.ok === true
      ? fromPositions(curriculumVitae.value.value?.positions ?? [])
      : []),
    ...(portfolio.value?.ok === true
      ? fromPortfolioItems(portfolio.value.value?.items ?? [])
      : []),
    ...fromDocuments(findings),
  ];

  // EVIDENCE BECOMES A SUGGESTION, NEVER A NAMING. What occurs in a repository
  // is a fact about a repository; that a person can do something is a statement
  // about a human, and only they may make it (ADR-0022). Taking a word over is
  // therefore TWO acts: a click fills the field, and only Save makes it a
  // naming. A search knows only what was named — evidence stands beside it and
  // never finds.
  const suggestions = suggestionsFrom(sources, alreadyNamed);

  function adopt(word: string) {
    // At the end of the line, with a comma — the same spelling somebody would
    // type by hand. Nothing is saved by this.
    const present = form.skills.trim();
    change(
      "skills",
      present === "" ? word : `${present.replace(/,\s*$/, "")}, ${word}`,
    );
  }

  async function save() {
    setSaving(true);
    const result = await saveMyProfile({
      headline: form.headline,
      bio: form.bio,
      location: form.location,
      remote_ok: form.remote_ok,
      // Empty means `null` and thus "said nothing": the statement is taken
      // back, not kept.
      commute_km: (form.commute_km as CommuteReadiness | "") === "" ? null : (form.commute_km as CommuteReadiness),
      relocation:
        (form.relocation as RelocationReadiness | "") === ""
          ? null
          : (form.relocation as RelocationReadiness),
      skills: parseSkills(form.skills),
      xing_url: form.xing.trim() === "" ? null : form.xing.trim(),
    });
    setSaving(false);
    if (result.ok) {
      setError(null);
      setSaved(true);
      profile.set({ ok: true, profile: result.profile });
    } else {
      setSaved(false);
      setError({ error: result.error, fallback: "errors.profileNotSaved" });
    }
  }

  async function toggle(next: boolean) {
    if (subjectId === null) return;
    setToggling(true);
    const result = await setVisibility(subjectId, next);
    setToggling(false);
    if (result.ok) {
      setError(null);
      // The ledger says what holds — not the wish of the click.
      release.set(result.granted);
    } else {
      setError({ error: result.error, fallback: "errors.consentNotChanged" });
      // Reset: a switch that shows "visible" although nothing was released
      // would be the more dangerous lie.
      release.set(!next);
    }
  }

  if (sessionUnknown) {
    return (
      <PageShell title={t("profile.title")} narrow>
        <LoadingBlock />
      </PageShell>
    );
  }

  if (subjectId === null) {
    return (
      <SignInRequired title={t("profile.title")} sentenceKey="profile.signInRequired" />
    );
  }

  if (profile.loading || loaded === undefined) {
    // No empty form that fills afterwards: whoever starts typing in the
    // meantime loses their input the moment the answer arrives.
    return (
      <PageShell title={t("profile.title")} narrow>
        <LoadingBlock label={t("profile.loading")} />
      </PageShell>
    );
  }

  if (!loaded.ok) {
    // And certainly no empty form over a failed request: whoever types
    // something into it and saves overwrites everything that stood there with
    // empty. The loading state is over by then, and the page would look like
    // "nothing entered yet".
    return (
      <PageShell title={t("profile.title")} narrow>
        <ErrorBlock error={loaded.error} fallback="errors.ownProfileNotLoaded" />
      </PageShell>
    );
  }

  const hasProfile = loaded.profile !== null;
  // The DISPLAY stays off when nothing is known — a switch that accidentally
  // claims "released" is the more dangerous lie.
  const released = release.value === true;
  // ... but "I do not know" is not "no". As long as the answer is pending or
  // absent the switch must not be OPERABLE: otherwise the next click sends a
  // `grant` for a consent whose state nobody knows.
  const ledgerSilent = !release.loading && release.value === null;
  const ledgerUnknown = release.loading || ledgerSilent;

  return (
    <PageShell
      title={t("profile.title")}
      narrow
      lead={t("profile.lead")}
    >
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <ConsentSwitch
            label={t("profile.releaseLabel")}
            checked={released}
            disabled={!hasProfile || toggling || ledgerUnknown}
            hint={
              ledgerSilent
                ? t("profile.releaseSilent")
                : release.loading
                  ? t("profile.releaseChecking")
                  : t(
                      hasProfile
                        ? "profile.releaseEffective"
                        : "profile.releaseWithoutProfile",
                    )
            }
            onChange={(next) => void toggle(next)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("profile.headline")}
              helperText={t("profile.headlineHint")}
              value={form.headline}
              onChange={(event) =>
                change("headline", event.target.value)
              }
              slotProps={{ htmlInput: { maxLength: 120 } }}
              required
              fullWidth
            />
            <TextField
              label={t("profile.aboutMe")}
              helperText={t("profile.aboutMeHint")}
              multiline
              rows={6}
              value={form.bio}
              onChange={(event) => change("bio", event.target.value)}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
              fullWidth
            />
            <DraftHelp
              onDraft={(draft) => change("bio", draft)}
              hasText={form.bio.trim().length > 0}
            />
            <TextField
              label={t("profile.location")}
              helperText={t("profile.locationHint")}
              value={form.location}
              onChange={(event) =>
                change("location", event.target.value)
              }
              slotProps={{ htmlInput: { maxLength: 120 } }}
              fullWidth
            />
            <OccupationalFieldSelect
              value={occupationalField}
              onChange={(field) => void chooseOccupationalField(field)}
            />

            <TextField
              label={t("profile.skills")}
              // The hint explains why "postgres" becomes "PostgreSQL" after
              // saving. Without it, it would look as if the page had changed
              // the input without asking.
              helperText={t("profile.skillsHint")}
              value={form.skills}
              onChange={(event) => change("skills", event.target.value)}
              fullWidth
            />

            {suggestions.length > 0 || findings.length > 0 ? (
              <Box>
                {suggestions.length > 0 ? (
                  <>
                    <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
                      {t(github ? "profile.suggestionsTitle" : "profile.suggestionsTitleOwn")}
                    </Typography>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      sx={{ display: "block", mb: 1 }}
                    >
                      {t(github ? "profile.suggestionsHint" : "profile.suggestionsHintOwn")}
                    </Typography>
                    <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.75 }}>
                      {suggestions.map((word) => (
                        <Chip
                          key={word}
                          label={word}
                          size="small"
                          variant="outlined"
                          icon={<AddIcon />}
                          onClick={() => adopt(word)}
                          aria-label={t("profile.suggestionAdopt", { wort: word })}
                        />
                      ))}
                    </Box>
                  </>
                ) : null}
                <FromDocuments
                  findings={findings}
                  reading={reading}
                  error={readError}
                  onRead={() => void readDocumentsNow()}
                />
              </Box>
            ) : null}
            <FormControlLabel
              control={
                <Checkbox
                  // A checkbox and NOT a switch: this is a profile field and it
                  // only holds once saved. The opposite of the consent switch
                  // above, where a box would be the wrong promise.
                  checked={form.remote_ok}
                  onChange={(event) =>
                    change("remote_ok", event.target.checked)
                  }
                />
              }
              label={t("profile.remote")}
            />

            <TextField
              // ADR-0041: a STEP and no kilometre figure. A number field would
              // invite arithmetic ("37 ≤ 50, so 74 % fitting"); a step is a
              // statement somebody made. The empty choice stands at the top on
              // purpose — "said nothing" is a state, not an omission, and it
              // must be revocable.
              select
              label={t("profile.commute")}
              helperText={t("profile.commuteHint")}
              value={form.commute_km}
              onChange={(event) => change("commute_km", event.target.value)}
            >
              <MenuItem value="">{t("profile.notStated")}</MenuItem>
              {COMMUTE_READINESS.map((readiness) => (
                <MenuItem key={readiness} value={readiness}>
                  {t(`profile.commute_${readiness}`)}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              label={t("profile.relocate")}
              helperText={t("profile.relocateHint")}
              value={form.relocation}
              onChange={(event) => change("relocation", event.target.value)}
            >
              <MenuItem value="">{t("profile.notStated")}</MenuItem>
              {RELOCATION_READINESS.map((value) => (
                <MenuItem key={value} value={value}>
                  {t(`profile.relocate_${value}`)}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              // It names her: a XING profile shows the civil name that the
              // platform otherwise releases last (ADR-0037 stage 3). The hint
              // says so before saving — the link is her choice, not a default.
              label={t("profile.xing")}
              helperText={t("profile.xingHint")}
              placeholder="https://www.xing.com/profile/…"
              value={form.xing}
              onChange={(event) => change("xing", event.target.value)}
              slotProps={{ htmlInput: { maxLength: 200, inputMode: "url" } }}
            />

            {error !== null ? (
              <ErrorBlock error={error.error} fallback={error.fallback} cases={PROFILE_CASES} />
            ) : null}
            {saved && error === null ? (
              <Alert
                severity="success"
                // A confirmation that interrupts the screen reader is noise;
                // MUI's Alert would otherwise set role="alert".
                role="status"
              >
                {t("profile.saved")}
              </Alert>
            ) : null}

            <Box>
              <Button type="submit" variant="contained" disabled={saving}>
                {saving
                  ? t("common.saving")
                  : t("common.save")}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>

      <EvidenceKinds field={occupationalField} />
      <CivilIdentity />
    </PageShell>
  );
}
